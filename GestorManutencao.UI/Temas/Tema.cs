using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GestorManutencao.UI.Temas;

// Identidade visual de um cliente. Um tema novo = um JSON em Temas/ (ver README), sem tocar em código nem CSS.
public sealed partial class Tema
{
    public static readonly string[] ChavesObrigatorias =
    [
        "fundo", "superficie", "superficie-2", "texto", "texto-suave", "borda", "borda-suave",
        "primario", "sobre-primario", "primario-hover", "foco", "erro", "erro-fundo",
        "marca-fundo", "marca-gradiente", "sobre-marca", "avatar-fundo", "sobre-avatar", "sombra"
    ];

    public const string TemaPorDefeito = "sigmacode";

    public required string Id { get; init; }
    public required string Nome { get; init; }
    public string? Logo { get; init; }
    public required IReadOnlyDictionary<string, string> Claro { get; init; }
    public required IReadOnlyDictionary<string, string> Escuro { get; init; }

    // Variáveis CSS do tema; o escuro segue o sistema (prefers-color-scheme).
    public string Css => $":root{{{Bloco(Claro)}}}@media (prefers-color-scheme: dark){{:root{{{Bloco(Escuro)}}}}}";

    public static Tema Carregar(string? id)
    {
        id = string.IsNullOrWhiteSpace(id) ? TemaPorDefeito : id.Trim().ToLowerInvariant();
        var assembly = typeof(Tema).Assembly;
        var recurso = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".Temas.{id}.json", StringComparison.Ordinal));
        if (recurso is null)
            throw new InvalidOperationException($"Tema '{id}' não encontrado. Temas disponíveis: {string.Join(", ", Disponiveis())}.");

        using var stream = assembly.GetManifestResourceStream(recurso)!;
        var dto = JsonSerializer.Deserialize<TemaDto>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? throw new InvalidOperationException($"Tema '{id}' inválido.");
        return Validar(dto);
    }

    public static IEnumerable<string> Disponiveis() =>
        typeof(Tema).Assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Temas.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n[(n.LastIndexOf(".Temas.", StringComparison.Ordinal) + 7)..^5]);

    private static Tema Validar(TemaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Nome))
            throw new InvalidOperationException("Tema sem id ou nome.");
        ValidarModo(dto.Id, "claro", dto.Claro);
        ValidarModo(dto.Id, "escuro", dto.Escuro);
        return new Tema { Id = dto.Id, Nome = dto.Nome, Logo = dto.Logo, Claro = dto.Claro!, Escuro = dto.Escuro! };
    }

    private static void ValidarModo(string id, string modo, Dictionary<string, string>? valores)
    {
        if (valores is null) throw new InvalidOperationException($"Tema '{id}' sem o modo '{modo}'.");
        var falta = ChavesObrigatorias.Where(c => !valores.ContainsKey(c)).ToList();
        if (falta.Count > 0)
            throw new InvalidOperationException($"Tema '{id}' ({modo}) sem as chaves: {string.Join(", ", falta)}.");
        foreach (var (chave, valor) in valores)
            if (!ChaveCss().IsMatch(chave) || !ValorCss().IsMatch(valor))
                throw new InvalidOperationException($"Tema '{id}' ({modo}): valor inválido em '{chave}'.");
    }

    private static string Bloco(IReadOnlyDictionary<string, string> valores)
    {
        var sb = new StringBuilder();
        foreach (var (chave, valor) in valores) sb.Append("--").Append(chave).Append(':').Append(valor).Append(';');
        return sb.ToString();
    }

    // Só caracteres de cor, gradiente e sombra: impede fechar o bloco ou injetar CSS/HTML.
    [GeneratedRegex(@"^[a-z0-9-]+$")] private static partial Regex ChaveCss();
    [GeneratedRegex(@"^[#a-zA-Z0-9(),.%/\s-]+$")] private static partial Regex ValorCss();

    private sealed class TemaDto
    {
        public string? Id { get; set; }
        public string? Nome { get; set; }
        public string? Logo { get; set; }
        public Dictionary<string, string>? Claro { get; set; }
        public Dictionary<string, string>? Escuro { get; set; }
    }
}
