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

// Ponte entre o ciclo de vida da janela (MAUI) e a UI: avisa quando a app volta ao primeiro plano.
public sealed class CicloVidaApp
{
    public event Action? PrimeiroPlano;
    public void NotificarPrimeiroPlano() => PrimeiroPlano?.Invoke();
}

// Avisos derivados do estado das fichas (nada é gravado no servidor). Só corre com a app aberta ou em segundo plano.
public sealed class NotificacaoService : IDisposable
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    private readonly SessaoService _sessao;
    private readonly IIntervencoesService _intervencoes;
    private readonly IArmazemAvisos _armazem;
    private readonly IAvisoSistema _sistema;
    private readonly CicloVidaApp _ciclo;
    private readonly Func<DateTime> _agora;
    private readonly SemaphoreSlim _trinco = new(1, 1);
    private EstadoAvisos _estado = EstadoAvisos.Vazio;
    private string? _chaveArmazem;
    private readonly SemaphoreSlim _escrita = new(1, 1);
    private CancellationTokenSource? _cts;
    private DateTime _ultimaVerificacao = DateTime.MinValue;

    public NotificacaoService(SessaoService sessao, IIntervencoesService intervencoes, IArmazemAvisos armazem, IAvisoSistema sistema, CicloVidaApp ciclo)
        : this(sessao, intervencoes, armazem, sistema, ciclo, () => DateTime.Now) { }

    public NotificacaoService(SessaoService sessao, IIntervencoesService intervencoes, IArmazemAvisos armazem, IAvisoSistema sistema, CicloVidaApp ciclo, Func<DateTime> agora)
    {
        _sessao = sessao; _intervencoes = intervencoes; _armazem = armazem; _sistema = sistema; _ciclo = ciclo; _agora = agora;
        _sessao.Alterada += AoMudarSessao;
        _ciclo.PrimeiroPlano += AoPrimeiroPlano;
    }

    public IReadOnlyList<EntradaAviso> Lista => _estado.Lista;
    public int NaoLidas => _estado.Lista.Count(e => !e.Lido);

    public event Action? Alterado;
    // Aviso novo que o sistema não mostrou (sem permissão ou sem suporte): a app mostra uma faixa.
    public event Action<Aviso>? AvisoNaApp;

    public void Iniciar()
    {
        // Com PIN provisório a API ainda não deixa consultar fichas.
        if (_cts is not null || !_sessao.TemSessao || _sessao.Utilizador?.PinProvisorio == true) return;
        _cts = new CancellationTokenSource();
        _ = CicloAsync(_cts.Token);
    }

    public void Parar()
    {
        _cts?.Cancel();
        _cts = null;
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
        if (DateTime.UtcNow - _ultimaVerificacao < TimeSpan.FromSeconds(15)) return;
        _ = VerificarSemFalharAsync();
    }

    private async Task VerificarSemFalharAsync()
    {
        try { await VerificarAsync(); } catch (Exception) { }
    }

    private async Task CicloAsync(CancellationToken ct)
    {
        try
        {
            try { await _sistema.PedirPermissaoAsync(); } catch (Exception) { /* sem permissão ficam só as faixas dentro da app */ }
            using var temporizador = new PeriodicTimer(Intervalo);
            do
            {
                // Uma falha numa verificação não pode parar o ciclo: tenta de novo no próximo minuto.
                try { await VerificarAsync(ct); } catch (Exception e) when (e is not OperationCanceledException) { }
            } while (await temporizador.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    // Sem sessão, sem rede ou sem API não faz nada (e não rebenta).
    public async Task VerificarAsync(CancellationToken ct = default)
    {
        if (_sessao.Utilizador is not { } utilizador) return;
        if (!await _trinco.WaitAsync(0, ct)) return;
        try
        {
            IReadOnlyList<Intervencao> fichas;
            try { fichas = await ObterFichasAsync(utilizador.Perfil, ct); }
            catch (Exception) { return; }
            if (!ReferenceEquals(_sessao.Utilizador, utilizador)) return;

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
        finally { _trinco.Release(); }
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
        _cts?.Cancel();
        _trinco.Dispose();
    }
}
