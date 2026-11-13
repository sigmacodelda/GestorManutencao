using System.Text.Json;
using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Onde ficam as notificações no aparelho (ficheiro no MAUI, memória nos testes). A chave já vem segura para nome de ficheiro.
public interface IArmazemAvisos
{
    Task<string?> LerAsync(string chave);
    Task GravarAsync(string chave, string valor);
}

public sealed class ArmazemAvisosMemoria : IArmazemAvisos
{
    private readonly Dictionary<string, string> _dados = [];
    public Task<string?> LerAsync(string chave) => Task.FromResult(_dados.GetValueOrDefault(chave));
    public Task GravarAsync(string chave, string valor) { _dados[chave] = valor; return Task.CompletedTask; }
}

// Aviso do sistema operativo. Devolve true se o sistema o mostrou; com false a app mostra uma faixa por dentro.
public interface IAvisoSistema
{
    Task PedirPermissaoAsync();
    Task<bool> MostrarAsync(Aviso aviso);
}

public sealed class AvisoSistemaNenhum : IAvisoSistema
{
    public Task PedirPermissaoAsync() => Task.CompletedTask;
    public Task<bool> MostrarAsync(Aviso aviso) => Task.FromResult(false);
}

// Mantém a app viva com o ecrã apagado ou fora do primeiro plano (no Android, serviço em primeiro plano). Nos outros sistemas não faz nada.
public interface IServicoSegundoPlano
{
    Task IniciarAsync();
    void Parar();
}

public sealed class ServicoSegundoPlanoNenhum : IServicoSegundoPlano
{
    public Task IniciarAsync() => Task.CompletedTask;
    public void Parar() { }
}

// Ponte entre o ciclo de vida da janela (MAUI) e a UI: avisa quando a app volta ao primeiro plano.
public sealed class CicloVidaApp
{
    public event Action? PrimeiroPlano;
    public void NotificarPrimeiroPlano() => PrimeiroPlano?.Invoke();

    // Toque numa notificação do sistema: guarda o pedido (a UI pode ainda não estar pronta, p. ex. arranque a frio) e avisa quem já estiver a ouvir.
    public sealed record PedidoAbrir(TipoAviso Tipo, string? FichaId);
    private PedidoAbrir? _pedido;
    public event Action? AbrirPedido;
    public void PedirAbrir(TipoAviso tipo, string? fichaId)
    {
        _pedido = new PedidoAbrir(tipo, fichaId);
        AbrirPedido?.Invoke();
    }
    public PedidoAbrir? ConsumirPedido() => Interlocked.Exchange(ref _pedido, null);

    // Botão Pausar/Retomar da notificação fixa: a plataforma só avisa, quem trata é o NotificacaoService.
    public event Action<string>? PausaPedida;
    public void PedirPausa(string fichaId) => PausaPedida?.Invoke(fichaId);

    // Quem estiver a tratar a sessão em memória regista-se aqui; o push usa-o em vez de abrir uma sessão paralela.
    public Func<CancellationToken, Task>? Verificador { get; set; }
}

// Avisos derivados do estado das fichas (nada é gravado no servidor). Só corre com a app aberta ou em segundo plano.
public sealed class NotificacaoService : IDisposable
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

    private readonly SessaoService _sessao;
    private readonly IIntervencoesService _intervencoes;
    private readonly IArmazemAvisos _armazem;
    private readonly IAvisoSistema _sistema;
    private readonly CicloVidaApp _ciclo;
    private readonly IServicoSegundoPlano _segundoPlano;
    private readonly ITempoReal _tempoReal;
    private volatile bool _repetir;
    private readonly Func<DateTime> _agora;
    private readonly SemaphoreSlim _trinco = new(1, 1);
    private EstadoAvisos _estado = EstadoAvisos.Vazio;
    private string? _chaveArmazem;
    private readonly SemaphoreSlim _escrita = new(1, 1);
    private CancellationTokenSource? _cts;
    private DateTime _ultimaVerificacao = DateTime.MinValue;

    public NotificacaoService(SessaoService sessao, IIntervencoesService intervencoes, IArmazemAvisos armazem, IAvisoSistema sistema, CicloVidaApp ciclo, IServicoSegundoPlano? segundoPlano = null, ITempoReal? tempoReal = null, IAvisoEmCurso? emCurso = null, IPushToken? push = null, IDispositivosRemoto? dispositivos = null)
        : this(sessao, intervencoes, armazem, sistema, ciclo, () => DateTime.Now, segundoPlano, tempoReal, emCurso, push, dispositivos) { }

    public NotificacaoService(SessaoService sessao, IIntervencoesService intervencoes, IArmazemAvisos armazem, IAvisoSistema sistema, CicloVidaApp ciclo, Func<DateTime> agora, IServicoSegundoPlano? segundoPlano = null, ITempoReal? tempoReal = null, IAvisoEmCurso? emCurso = null, IPushToken? push = null, IDispositivosRemoto? dispositivos = null)
    {
        _sessao = sessao; _intervencoes = intervencoes; _armazem = armazem; _sistema = sistema; _ciclo = ciclo; _agora = agora;
        _push = push; _dispositivos = dispositivos;
        _segundoPlano = segundoPlano ?? new ServicoSegundoPlanoNenhum();
        _tempoReal = tempoReal ?? new TempoRealNenhum();
        _emCurso = emCurso ?? new AvisoEmCursoNenhum();
        _tempoReal.FichasMudaram += AoMudarFichas;
        _sessao.Alterada += AoMudarSessao;
        _ciclo.PrimeiroPlano += AoPrimeiroPlano;
        _ciclo.PausaPedida += AoPedirPausa;
    }

    private readonly IPushToken? _push;
    private readonly IDispositivosRemoto? _dispositivos;
    private string? _tokenRegistado, _jwtRegisto;
    private Func<CancellationToken, Task>? _verificador;

    // Regista o aparelho para o push, sem esperar e sem poder falhar. Se a sessão acabou entretanto, desfaz o registo.
    private async Task RegistarPushAsync(string jwt, CancellationToken ct)
    {
        if (_push is null || _dispositivos is null) return;
        try
        {
            var token = await _push.ObterAsync(ct); // sigma-ok: variável, não é um valor
            if (string.IsNullOrEmpty(token) || ct.IsCancellationRequested) return;
            await _dispositivos.RegistarAsync(jwt, token, ct);
            _tokenRegistado = token; _jwtRegisto = jwt;
            if (ct.IsCancellationRequested) DesregistarPush();
        }
        catch (Exception) { /* sem rede ou sem push: a app continua a verificar pelo ciclo e pelo canal em tempo real */ }
    }

    private void DesregistarPush()
    {
        var (token, jwt, dispositivos) = (_tokenRegistado, _jwtRegisto, _dispositivos);
        _tokenRegistado = _jwtRegisto = null;
        if (token is null || jwt is null || dispositivos is null) return;
        _ = Task.Run(async () =>
        {
            try { await dispositivos.DesregistarAsync(jwt, token); } catch (Exception) { }
        });
    }

    private readonly IAvisoEmCurso _emCurso;
    private PausasEmCurso? _pausas;
    private Intervencao? _emCursoAtual;

    // Botão da notificação fixa: pausa ou retoma a intervenção a contar e atualiza já o aviso.
    private void AoPedirPausa(string fichaId) => _ = AlternarPausaAsync(fichaId);

    public async Task AlternarPausaAsync(string fichaId)
    {
        if (_emCursoAtual is not { } ficha || ficha.Id != fichaId) return;
        try
        {
            if (ficha.EmPausa) await _intervencoes.RetomarAsync(fichaId);
            else await _intervencoes.PausarAsync(fichaId);
        }
        catch (Exception) { /* sem rede ou recusado: o aviso volta ao estado real na verificação seguinte */ }
        await VerificarSemFalharAsync();
    }

    // Só o técnico, e só com uma intervenção em curso: mostra o cronómetro; caso contrário tira a notificação.
    private void AtualizarEmCurso(Utilizador utilizador, IReadOnlyList<Intervencao> fichas)
    {
        var ficha = utilizador.Perfil == Perfil.Supervisor ? null : RelogioEmCurso.Unica(fichas);
        _emCursoAtual = ficha;
        try
        {
            if (ficha is null) { _pausas = null; _emCurso.Esconder(); return; }
            var (aviso, estado) = RelogioEmCurso.Calcular(_pausas, ficha, _agora());
            _pausas = estado;
            _emCurso.Mostrar(aviso);
        }
        catch (Exception) { /* a notificação fixa nunca pode parar as verificações */ }
    }

    public IReadOnlyList<EntradaAviso> Lista => _estado.Lista;
    public int NaoLidas => _estado.Lista.Count(e => !e.Lido);

    public event Action? Alterado;
    // As fichas mudaram no servidor: as páginas com listas voltam a carregar os dados (sem o utilizador ter de atualizar).
    public event Action? DadosMudaram;
    // Aviso novo que o sistema não mostrou (sem permissão ou sem suporte): a app mostra uma faixa.
    public event Action<Aviso>? AvisoNaApp;

    public void Iniciar()
    {
        // Com PIN provisório a API ainda não deixa consultar fichas.
        if (_cts is not null || !_sessao.TemSessao || _sessao.Utilizador?.PinProvisorio == true) return;
        _cts = new CancellationTokenSource();
        _verificador = ct => VerificarAsync(ct);
        _ciclo.Verificador = _verificador;
        _ = CicloAsync(_cts.Token, _sessao.Utilizador?.Token);
        try { _ = _tempoReal.IniciarAsync(() => _sessao.Utilizador?.Token); } catch (Exception) { }
    }

    // Mudança no servidor: verifica já os avisos e manda as páginas recarregar. Sem esperar pelo ciclo.
    private void AoMudarFichas()
    {
        _ = VerificarSemFalharAsync();
        DadosMudaram?.Invoke();
    }

    public void Parar()
    {
        _cts?.Cancel();
        _cts = null;
        LimparVerificador();
        DesregistarPush();
        try { _segundoPlano.Parar(); } catch (Exception) { }
        try { _emCurso.Esconder(); } catch (Exception) { }
        _pausas = null; _emCursoAtual = null;
        _ = _tempoReal.PararAsync();
        _estado = EstadoAvisos.Vazio;
        _chaveArmazem = null;
        Alterado?.Invoke();
    }

    private void AoMudarSessao()
    {
        if (_sessao.TemSessao) Iniciar();
        else Parar();
    }

    // O Windows dispara "ativada" a cada foco da janela: ignora disparos seguidos.
    private void AoPrimeiroPlano()
    {
        if (DateTime.UtcNow - _ultimaVerificacao < TimeSpan.FromSeconds(3)) return;
        // Sem rede no arranque o registo do aparelho falhou: volta a tentar (o pedido é idempotente).
        if (_tokenRegistado is null && _cts is { } ciclo && _sessao.Utilizador?.Token is { Length: > 0 } jwt) _ = RegistarPushAsync(jwt, ciclo.Token);
        _ = VerificarSemFalharAsync();
    }

    private async Task VerificarSemFalharAsync()
    {
        try { await VerificarAsync(); } catch (Exception) { }
    }

    private void LimparVerificador()
    {
        if (_verificador is not null && ReferenceEquals(_ciclo.Verificador, _verificador)) _ciclo.Verificador = null;
        _verificador = null;
    }

    private async Task CicloAsync(CancellationToken ct, string? jwt)
    {
        try
        {
            try { await _sistema.PedirPermissaoAsync(); } catch (Exception) { /* sem permissão ficam só as faixas dentro da app */ }
            if (!string.IsNullOrEmpty(jwt)) _ = RegistarPushAsync(jwt, ct);
            // Depois do pedido de permissão (para os diálogos não se sobreporem). Sem isto o Android suspende a app com o ecrã apagado.
            try { await _segundoPlano.IniciarAsync(); } catch (Exception) { }
            using var temporizador = new PeriodicTimer(Intervalo);
            do
            {
                // Com o canal em tempo real ligado a verificação periódica é só uma rede de segurança (1 por minuto).
                if (_tempoReal.Ligado && DateTime.UtcNow - _ultimaVerificacao < TimeSpan.FromSeconds(60)) continue;
                // Uma falha numa verificação não pode parar o ciclo: tenta de novo no próximo ciclo.
                try { await VerificarAsync(ct); } catch (Exception e) when (e is not OperationCanceledException) { }
            } while (await temporizador.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    // Sem sessão, sem rede ou sem API não faz nada (e não rebenta).
    public async Task VerificarAsync(CancellationToken ct = default)
    {
        if (_sessao.Utilizador is not { } utilizador) return;
        // Uma mudança que chega a meio de uma verificação pede outra logo a seguir (a primeira pode já ter lido dados antigos).
        if (!await _trinco.WaitAsync(0, ct)) { _repetir = true; return; }
        try
        {
            do
            {
                _repetir = false;
                await VerificarUmaVezAsync(utilizador, ct);
            } while (_repetir && !ct.IsCancellationRequested && ReferenceEquals(_sessao.Utilizador, utilizador));
        }
        finally { _trinco.Release(); }
    }

    private async Task VerificarUmaVezAsync(Utilizador utilizador, CancellationToken ct)
    {
        {
            IReadOnlyList<Intervencao> fichas;
            try { fichas = await ObterFichasAsync(utilizador.Perfil, ct); }
            catch (Exception) { return; }
            if (!ReferenceEquals(_sessao.Utilizador, utilizador)) return;

            AtualizarEmCurso(utilizador, fichas);
            await CarregarEstadoAsync(utilizador);
            // A sessão pode ter acabado (ou ter mudado de pessoa) enquanto se lia o ficheiro.
            if (ct.IsCancellationRequested || !ReferenceEquals(_sessao.Utilizador, utilizador)) return;
            _ultimaVerificacao = DateTime.UtcNow;
            var agora = _agora();
            var resultado = CentroAvisos.Processar(_estado, CentroAvisos.Gerar(utilizador.Perfil, 0, fichas, agora));
            if (!resultado.Mudou) return;
            _estado = resultado.Estado;
            await GuardarAsync();
            Alterado?.Invoke();

            foreach (var aviso in resultado.Avisar)
            {
                var mostrou = false;
                try { mostrou = await _sistema.MostrarAsync(aviso); } catch (Exception) { }
                if (!mostrou) AvisoNaApp?.Invoke(aviso);
            }
        }
    }

    private async Task<IReadOnlyList<Intervencao>> ObterFichasAsync(Perfil perfil, CancellationToken ct)
    {
        if (perfil != Perfil.Supervisor) return await _intervencoes.ListarMinhasAsync(ct);
        var porValidar = await _intervencoes.ListarPorValidarAsync(ct);
        var planeadas = await _intervencoes.ListarPlaneadasAsync(ct);
        return porValidar.Concat(planeadas).ToList();
    }

    public async Task MarcarTodasLidasAsync()
    {
        _estado = CentroAvisos.MarcarTodasLidas(_estado);
        await GuardarAsync();
        Alterado?.Invoke();
    }

    public async Task LimparAsync()
    {
        _estado = CentroAvisos.Limpar(_estado);
        await GuardarAsync();
        Alterado?.Invoke();
    }

    public async Task MarcarLidaAsync(string chave)
    {
        _estado = CentroAvisos.MarcarLida(_estado, chave);
        await GuardarAsync();
        Alterado?.Invoke();
    }

    // Uma lista por utilizador neste aparelho; o nome não vai em claro para o ficheiro.
    private static string ChaveDe(Utilizador u) => "avisos-" + CentroAvisos.Hash(u.Nome.Trim().ToLowerInvariant());

    private async Task CarregarEstadoAsync(Utilizador utilizador)
    {
        var chave = ChaveDe(utilizador);
        if (chave == _chaveArmazem) return;
        _chaveArmazem = chave;
        _estado = EstadoAvisos.Vazio;
        try
        {
            if (await _armazem.LerAsync(chave) is { Length: > 0 } json)
                _estado = JsonSerializer.Deserialize<EstadoAvisos>(json) is { Vistas: not null, Lista: not null } lido ? lido : EstadoAvisos.Vazio;
        }
        catch (Exception) { _estado = EstadoAvisos.Vazio; }
    }

    private async Task GuardarAsync()
    {
        if (_chaveArmazem is null) return;
        var chave = _chaveArmazem;
        var json = JsonSerializer.Serialize(_estado);
        await _escrita.WaitAsync();
        try { await _armazem.GravarAsync(chave, json); }
        catch (Exception) { /* sem armazenamento a lista fica só em memória */ }
        finally { _escrita.Release(); }
    }

    public void Dispose()
    {
        _sessao.Alterada -= AoMudarSessao;
        _ciclo.PrimeiroPlano -= AoPrimeiroPlano;
        _ciclo.PausaPedida -= AoPedirPausa;
        _tempoReal.FichasMudaram -= AoMudarFichas;
        LimparVerificador();
        _cts?.Cancel();
        _trinco.Dispose();
    }
}
