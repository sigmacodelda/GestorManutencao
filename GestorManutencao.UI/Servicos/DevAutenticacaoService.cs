using System.Reflection;
using System.Text.Json;
using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Só para desenvolvimento e testes: lê utilizadores fictícios de um recurso embebido.
public sealed class DevAutenticacaoService : IAutenticacaoService
{
    private sealed record UtilizadorTeste(string Pin, string Nome, Perfil Perfil, bool PinProvisorio = false);

    private readonly List<UtilizadorTeste> _utilizadores = Carregar();

    public async Task<ResultadoLogin> EntrarAsync(string pin, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        var u = _utilizadores.FirstOrDefault(x => x.Pin == pin);
        return u is null
            ? ResultadoLogin.Falha(ErroLogin.Credenciais)
            : ResultadoLogin.Ok(new Utilizador(u.Nome, u.Perfil, PinProvisorio: u.PinProvisorio));
    }

    public async Task<ResultadoLogin> MudarPinAsync(string? token, string pinAtual, string pinNovo, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        var pos = _utilizadores.FindIndex(x => x.Pin == pinAtual);
        if (pos < 0) return ResultadoLogin.Falha(ErroLogin.Credenciais);
        if (!RegrasPin.Aceitavel(pinNovo) || pinNovo == pinAtual) return ResultadoLogin.Falha(ErroLogin.PinFraco);
        if (_utilizadores.Any(x => x.Pin == pinNovo)) return ResultadoLogin.Falha(ErroLogin.PinEmUso);

        var u = _utilizadores[pos] with { Pin = pinNovo, PinProvisorio = false };
        _utilizadores[pos] = u;
        return ResultadoLogin.Ok(new Utilizador(u.Nome, u.Perfil));
    }

    private static List<UtilizadorTeste> Carregar()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var nome = assembly.GetManifestResourceNames().Single(n => n.EndsWith("utilizadores-teste.json"));
        using var stream = assembly.GetManifestResourceStream(nome)!;
        var opcoes = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        opcoes.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return JsonSerializer.Deserialize<List<UtilizadorTeste>>(stream, opcoes)!;
    }
}
