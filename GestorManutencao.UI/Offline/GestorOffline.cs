using GestorManutencao.UI.Servicos;

namespace GestorManutencao.UI.Offline;

// O que os ecrãs precisam do modo offline. Sem offline (modo de demonstração) é a versão "Nenhum": tudo calado.
public interface IGestorOffline : IDisposable
{
    // Liga os gatilhos (arranque, rede, temporizadores). Pode ser chamado várias vezes.
    void Iniciar();
    bool SemLigacao { get; }
    string? TextoDados { get; }
    EstadoSincronizacao Estado { get; }
    int Pendentes { get; }
    int Conflitos { get; }
    event Action? Mudou;

    // Registos por enviar do utilizador atual (lê a fila).
    Task<int> ContarPendentesAsync();
    Task<IReadOnlyList<AvisoConflito>> ListarAvisosAsync();
    Task DescartarConflitoAsync(string avisoId);
    // Uma rota pode ainda ter o id local de uma avaria entretanto criada no servidor.
    Task<string> ResolverIdAsync(string id);
}

public sealed class GestorOfflineNenhum : IGestorOffline
{
    public void Iniciar() { }
    public bool SemLigacao => false;
    public string? TextoDados => null;
    public EstadoSincronizacao Estado => EstadoSincronizacao.Sincronizado;
    public int Pendentes => 0;
    public int Conflitos => 0;
    public event Action? Mudou { add { } remove { } }
    public Task<int> ContarPendentesAsync() => Task.FromResult(0);
    public Task<IReadOnlyList<AvisoConflito>> ListarAvisosAsync() => Task.FromResult<IReadOnlyList<AvisoConflito>>([]);
    public Task DescartarConflitoAsync(string avisoId) => Task.CompletedTask;
    public Task<string> ResolverIdAsync(string id) => Task.FromResult(id);
    public void Dispose() { }
}

// Decide quando se envia a fila e se atualiza a cache: arranque, rede que volta, 60 s com pendentes, 15 min com a app aberta,
// troca de utilizador e depois de cada escrita online aceite. Sem sessão não faz pedidos. As falhas passam em silêncio (a fila e a
// cache não perdem nada e a próxima tentativa corrige).
public sealed class GestorOffline : IGestorOffline
{
    public static readonly TimeSpan IntervaloEnvio = TimeSpan.FromSeconds(60), IntervaloCache = TimeSpan.FromMinutes(15);

    private readonly SessaoService _sessao;
    private readonly SincronizadorFila _sinc;
    private readonly AtualizadorCache _atualizador;
    private readonly ManutencaoCache _mc;
    private readonly IFilaSaida _fila;
    private readonly IDonoDados _dono;
    private readonly IRedeEstado _rede;
    private readonly OfflineIntervencoesService _leitura;
    private readonly SemaphoreSlim _cacheEmCurso;
    private CancellationTokenSource _cts = new();
    private bool _orfasLimpas;
    private readonly object _trinco = new();
    private Timer? _temporizadorEnvio, _temporizadorCache;
    private string? _donoAtual;
    private bool _iniciado, _libertado;

    public GestorOffline(SessaoService sessao, SincronizadorFila sinc, AtualizadorCache atualizador, ManutencaoCache mc, IFilaSaida fila,
        IDonoDados dono, IRedeEstado rede, OfflineIntervencoesService leitura)
    {
        _sessao = sessao; _sinc = sinc; _atualizador = atualizador; _mc = mc; _fila = fila; _dono = dono; _rede = rede; _leitura = leitura;
        _cacheEmCurso = TrincosPartilhados.Obter(mc.Cache, "atualizacao");
    }

    public bool SemLigacao => !_rede.Disponivel;
    public string? TextoDados => _leitura.TextoDados;
    public EstadoSincronizacao Estado => _sinc.Estado;
    public int Pendentes => _sinc.PendentesCount;
    public int Conflitos => _sinc.ConflitosCount;
    public event Action? Mudou;

    public async Task<int> ContarPendentesAsync() => _dono.Dono is { } dono ? await _fila.ContarPendentesAsync(dono) : 0;
    public Task<IReadOnlyList<AvisoConflito>> ListarAvisosAsync() => _sinc.ListarAvisosAsync();
    public Task DescartarConflitoAsync(string avisoId) => _sinc.DescartarConflitoAsync(avisoId);
    public Task<string> ResolverIdAsync(string id) => _mc.ResolverIdAsync(id);

    public void Iniciar()
    {
        lock (_trinco)
        {
            if (_iniciado || _libertado) return;
            _iniciado = true;
        }
        _sessao.Alterada += AoMudarSessao;
        _rede.Alterada += AoMudarRede;
        _sinc.Mudou += Notificar;
        _leitura.Mudou += Notificar;
        _leitura.EscritaOnline += AoEscritaOnline;
        _temporizadorEnvio = new Timer(_ => _ = EnviarSeHouverPendentesAsync(), null, IntervaloEnvio, IntervaloEnvio);
        _temporizadorCache = new Timer(_ => _ = AtualizarCacheSilenciosoAsync(), null, IntervaloCache, IntervaloCache);
        if (_sessao.TemSessao) { _donoAtual = _dono.Dono; _ = ArrancarAsync(TokenAtual()); }
    }

    private void Notificar() => Mudou?.Invoke();

    // Só reage quando o utilizador muda (a mesma sessão também avisa ao trocar de modo). O trabalho em curso do dono anterior é cancelado.
    private void AoMudarSessao()
    {
        var dono = _sessao.TemSessao ? _dono.Dono : null;
        if (dono == _donoAtual) return;
        _donoAtual = dono;
        _ = ArrancarAsync(NovoDono());
    }

    // Cancela o que o dono anterior tinha em curso e devolve o token do novo.
    private CancellationToken NovoDono()
    {
        CancellationTokenSource anterior, novo = new();
        lock (_trinco)
        {
            anterior = _cts;
            _cts = novo;
            if (_libertado) novo.Cancel();
        }
        anterior.Cancel();
        return novo.Token;
    }

    private CancellationToken TokenAtual() { lock (_trinco) return _cts.Token; }

    private void AoMudarRede(bool disponivel)
    {
        Notificar();
        if (disponivel) _ = RedeVoltouAsync();
    }

    private void AoEscritaOnline() => _ = EnviarSeHouverPendentesAsync();

    private bool PodeTrabalhar => !_libertado && _sessao.TemSessao && _dono.Dono is not null;

    private async Task ArrancarAsync(CancellationToken ct)
    {
        try
        {
            await _sinc.AtualizarContagensAsync();
            if (!PodeTrabalhar) return;
            await _mc.GarantirDonoAsync();
            await _sinc.AtualizarContagensAsync();
            await LimparFotosOrfasUmaVezAsync();
            if (!_rede.Disponivel || ct.IsCancellationRequested) return;
            await _sinc.SincronizarAsync(ct, esperar: true);
            await AtualizarCacheAsync(ct, esperar: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Registar(e); }
    }

    // Uma vez por execução da app: os envios e cancelamentos interrompidos podem ter deixado ficheiros que nenhuma operação referencia.
    private async Task LimparFotosOrfasUmaVezAsync()
    {
        lock (_trinco) { if (_orfasLimpas) return; _orfasLimpas = true; }
        try { await _sinc.LimparFotosOrfasAsync(); }
        catch (Exception e) { Registar(e); }
    }

    private async Task RedeVoltouAsync()
    {
        try
        {
            if (!PodeTrabalhar) return;
            var ct = TokenAtual();
            await _sinc.SincronizarAsync(ct);
            await AtualizarCacheAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Registar(e); }
    }

    private async Task EnviarSeHouverPendentesAsync()
    {
        try
        {
            if (!PodeTrabalhar || !_rede.Disponivel || _sinc.PendentesCount <= 0) return;
            await _sinc.SincronizarAsync(TokenAtual());
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Registar(e); }
    }

    private async Task AtualizarCacheSilenciosoAsync()
    {
        try
        {
            if (!PodeTrabalhar || !_rede.Disponivel) return;
            await AtualizarCacheAsync(TokenAtual());
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Registar(e); }
    }

    // Uma atualização de cada vez; se já há uma a decorrer, esta não faz nada (ao arrancar, espera pela do dono anterior, já cancelada).
    private async Task AtualizarCacheAsync(CancellationToken ct, bool esperar = false)
    {
        if (esperar) await _cacheEmCurso.WaitAsync(ct);
        else if (!await _cacheEmCurso.WaitAsync(0, CancellationToken.None)) return;
        try { await _atualizador.AtualizarCacheAsync(ct); }
        finally { _cacheEmCurso.Release(); }
    }

    // Só o tipo da exceção: nunca dados pessoais.
    private static void Registar(Exception e) => System.Diagnostics.Debug.WriteLine("Offline: " + e.GetType().Name);

    public void Dispose()
    {
        CancellationTokenSource atual;
        lock (_trinco) { if (_libertado) return; _libertado = true; atual = _cts; }
        atual.Cancel();
        _temporizadorEnvio?.Dispose();
        _temporizadorCache?.Dispose();
        _sessao.Alterada -= AoMudarSessao;
        _rede.Alterada -= AoMudarRede;
        _sinc.Mudou -= Notificar;
        _leitura.Mudou -= Notificar;
        _leitura.EscritaOnline -= AoEscritaOnline;
    }
}

// Copiar texto para a área de transferência; a app MAUI regista a versão real.
public interface IAreaTransferencia
{
    // false se não for possível (o ecrã deixa o texto selecionável).
    Task<bool> CopiarAsync(string texto);
}

public sealed class AreaTransferenciaNenhuma : IAreaTransferencia
{
    public Task<bool> CopiarAsync(string texto) => Task.FromResult(false);
}
