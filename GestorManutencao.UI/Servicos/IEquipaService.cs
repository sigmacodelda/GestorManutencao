using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

public sealed record MembroEquipa(string Codigo, string Nome, Perfil Perfil, bool Ativo, bool PinProvisorio, long Versao = 0, int Tecno = 0);

public enum ResultadoEquipa { Ok, JaExiste, SemPermissao, Rede, TecnoEmUso }

public sealed record RespostaEquipa(ResultadoEquipa Resultado, string? Pin = null);

// Gestão da equipa pelo supervisor. O PIN provisório só aparece uma vez, na resposta.
public interface IEquipaService
{
    Task<IReadOnlyList<MembroEquipa>> ListarAsync(string? token, CancellationToken ct = default);
    // tecno = número de utilizador do PHC (0 = ainda sem número: a pessoa não vê fichas até o supervisor o indicar).
    Task<RespostaEquipa> CriarAsync(string? token, string codigo, string nome, int tecno, CancellationToken ct = default);
    Task<RespostaEquipa> DefinirTecnoAsync(string? token, string codigo, int tecno, CancellationToken ct = default);
    Task<RespostaEquipa> NovoPinAsync(string? token, string codigo, CancellationToken ct = default);
}

public sealed class EquipaApiService(HttpClient http) : IEquipaService
{
    private sealed record RespostaPin(string Pin);

    public async Task<IReadOnlyList<MembroEquipa>> ListarAsync(string? token, CancellationToken ct = default)
    {
        using var pedido = Pedido(HttpMethod.Get, "utilizadores", token);
        using var resposta = await http.SendAsync(pedido, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<List<MembroEquipa>>(ct) ?? [];
    }

    public Task<RespostaEquipa> CriarAsync(string? token, string codigo, string nome, int tecno, CancellationToken ct = default) =>
        Enviar(Pedido(HttpMethod.Post, "utilizadores", token, new { utilizador = codigo, nome, tecno }), ct);

    public Task<RespostaEquipa> DefinirTecnoAsync(string? token, string codigo, int tecno, CancellationToken ct = default) =>
        Enviar(Pedido(HttpMethod.Put, $"utilizadores/{Uri.EscapeDataString(codigo)}/tecno", token, new { tecno }), ct);

    public Task<RespostaEquipa> NovoPinAsync(string? token, string codigo, CancellationToken ct = default) =>
        Enviar(Pedido(HttpMethod.Post, $"utilizadores/{Uri.EscapeDataString(codigo)}/pin", token), ct);

    private static HttpRequestMessage Pedido(HttpMethod metodo, string url, string? token, object? corpo = null) => new(metodo, url)
    {
        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        Content = corpo is null ? null : JsonContent.Create(corpo)
    };

    private async Task<RespostaEquipa> Enviar(HttpRequestMessage pedido, CancellationToken ct)
    {
        try
        {
            using (pedido)
            using (var resposta = await http.SendAsync(pedido, ct))
            {
                if (resposta.StatusCode == HttpStatusCode.Conflict)
                    return new((await resposta.Content.ReadAsStringAsync(ct)).Contains("tecno_em_uso") ? ResultadoEquipa.TecnoEmUso : ResultadoEquipa.JaExiste);
                if (resposta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) return new(ResultadoEquipa.SemPermissao);
                if (!resposta.IsSuccessStatusCode) return new(ResultadoEquipa.Rede);
                if (resposta.StatusCode == HttpStatusCode.NoContent) return new(ResultadoEquipa.Ok);
                var corpo = await resposta.Content.ReadFromJsonAsync<RespostaPin>(ct);
                return corpo is null ? new(ResultadoEquipa.Rede) : new(ResultadoEquipa.Ok, corpo.Pin);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return new(ResultadoEquipa.Rede);
        }
    }
}

// Só para desenvolvimento: equipa em memória.
public sealed class DevEquipaService : IEquipaService
{
    private readonly object _trinco = new();
    private readonly List<MembroEquipa> _equipa =
    [
        new("operador", "Operador de Teste", Perfil.Operador, true, false, 0, 101),
        new("novo", "Novo Operador de Teste", Perfil.Operador, true, true, 0, 102),
        new("supervisor", "Supervisor de Teste", Perfil.Supervisor, true, false, 0, 100)
    ];

    public Task<IReadOnlyList<MembroEquipa>> ListarAsync(string? token, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<MembroEquipa>>(_equipa.ToList());
    }

    public Task<RespostaEquipa> CriarAsync(string? token, string codigo, string nome, int tecno, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            if (_equipa.Any(m => m.Codigo == codigo)) return Task.FromResult(new RespostaEquipa(ResultadoEquipa.JaExiste));
            if (tecno > 0 && _equipa.Any(m => m.Tecno == tecno)) return Task.FromResult(new RespostaEquipa(ResultadoEquipa.TecnoEmUso));
            _equipa.Add(new MembroEquipa(codigo, nome, Perfil.Operador, true, true, 0, tecno));
            return Task.FromResult(new RespostaEquipa(ResultadoEquipa.Ok, "590147"));
        }
    }

    public Task<RespostaEquipa> DefinirTecnoAsync(string? token, string codigo, int tecno, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            var pos = _equipa.FindIndex(m => m.Codigo == codigo);
            if (pos < 0) return Task.FromResult(new RespostaEquipa(ResultadoEquipa.SemPermissao));
            if (tecno > 0 && _equipa.Any(m => m.Codigo != codigo && m.Tecno == tecno)) return Task.FromResult(new RespostaEquipa(ResultadoEquipa.TecnoEmUso));
            _equipa[pos] = _equipa[pos] with { Tecno = tecno };
            return Task.FromResult(new RespostaEquipa(ResultadoEquipa.Ok));
        }
    }

    public Task<RespostaEquipa> NovoPinAsync(string? token, string codigo, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            var pos = _equipa.FindIndex(m => m.Codigo == codigo && m.Perfil == Perfil.Operador);
            if (pos < 0) return Task.FromResult(new RespostaEquipa(ResultadoEquipa.SemPermissao));
            _equipa[pos] = _equipa[pos] with { PinProvisorio = true };
            return Task.FromResult(new RespostaEquipa(ResultadoEquipa.Ok, "831462"));
        }
    }
}
