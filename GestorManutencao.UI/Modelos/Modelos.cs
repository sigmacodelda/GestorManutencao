namespace GestorManutencao.UI.Modelos;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum Perfil { Operador, Supervisor }

public enum ModoTrabalho { Operador, Supervisor }

public enum ErroLogin { Credenciais, Rede, DemasiadasTentativas, PinFraco, PinEmUso }

public sealed record Utilizador(string Nome, Perfil Perfil, string? Token = null, DateTimeOffset? ExpiraEm = null, bool PinProvisorio = false)
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

// Mesmas regras do servidor, só para avisar logo no ecrã (o servidor volta a validar).
public static class RegrasPin
{
    public const int Minimo = 4;
    public const int Maximo = 6;

    public static bool FormatoValido(string? pin) => pin is { Length: >= Minimo and <= Maximo } && pin.All(c => c is >= '0' and <= '9');

    public static bool Aceitavel(string? pin)
    {
        if (!FormatoValido(pin)) return false;
        var passos = pin!.Zip(pin.Skip(1), (a, b) => b - a).ToList();
        return !passos.All(p => p == 0) && !passos.All(p => p == 1) && !passos.All(p => p == -1);
    }

    public static string? ValidarNovo(string? pin, string? repetido) =>
        !FormatoValido(pin) ? "O PIN tem de ter de 4 a 6 números."
        : !Aceitavel(pin) ? "Esse PIN é fácil de adivinhar. Escolha outro."
        : pin != repetido ? "Os dois PINs não são iguais."
        : null;
}

public sealed record ResultadoLogin(Utilizador? Utilizador, ErroLogin? Erro)
{
    public bool Sucesso => Utilizador is not null;
    public static ResultadoLogin Ok(Utilizador u) => new(u, null);
    public static ResultadoLogin Falha(ErroLogin e) => new(null, e);
}
