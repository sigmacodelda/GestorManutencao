namespace GestorManutencao.UI.Modelos;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum Perfil { Operador, Supervisor }

public enum ModoTrabalho { Operador, Supervisor }

public enum ErroLogin { Credenciais, Rede, DemasiadasTentativas }

public sealed record Utilizador(string Nome, Perfil Perfil, string? Token = null, DateTimeOffset? ExpiraEm = null)
{
    // Nunca imprimir o token (o ToString automático do record iria para logs e páginas de erro).
    private bool PrintMembers(System.Text.StringBuilder sb)
    {
        sb.Append("Nome = ").Append(Nome).Append(", Perfil = ").Append(Perfil);
        return true;
    }

    public bool Expirou => ExpiraEm is { } e && e <= DateTimeOffset.UtcNow;

    public string Iniciais
    {
        get
        {
            var partes = Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return partes.Length switch
            {
                0 => "?",
                1 => partes[0][..1].ToUpperInvariant(),
                _ => $"{partes[0][0]}{partes[^1][0]}".ToUpperInvariant()
            };
        }
    }
}

public sealed record ResultadoLogin(Utilizador? Utilizador, ErroLogin? Erro)
{
    public bool Sucesso => Utilizador is not null;
    public static ResultadoLogin Ok(Utilizador u) => new(u, null);
    public static ResultadoLogin Falha(ErroLogin e) => new(null, e);
}
