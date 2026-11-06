using System.Reflection;
using System.Text.Json;
using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Só para desenvolvimento e testes: lê utilizadores fictícios de um recurso embebido.
public sealed class DevAutenticacaoService : IAutenticacaoService
{
    private sealed record UtilizadorTeste(string Utilizador, string Password, string Nome, Perfil Perfil);

    private static readonly Lazy<IReadOnlyList<UtilizadorTeste>> Utilizadores = new(Carregar);

    public async Task<ResultadoLogin> EntrarAsync(string utilizador, string password, CancellationToken ct = default)
    {
        await Task.Delay(500, ct);
        var encontrado = Utilizadores.Value.FirstOrDefault(u =>
            string.Equals(u.Utilizador, utilizador.Trim(), StringComparison.OrdinalIgnoreCase) && u.Password == password);
        return encontrado is null
            ? ResultadoLogin.Falha(ErroLogin.Credenciais)
            : ResultadoLogin.Ok(new Utilizador(encontrado.Nome, encontrado.Perfil));
    }

    private static IReadOnlyList<UtilizadorTeste> Carregar()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var nome = assembly.GetManifestResourceNames().Single(n => n.EndsWith("utilizadores-teste.json"));
        using var stream = assembly.GetManifestResourceStream(nome)!;
        var opcoes = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        opcoes.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return JsonSerializer.Deserialize<List<UtilizadorTeste>>(stream, opcoes)!;
    }
}
