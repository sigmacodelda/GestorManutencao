using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;

namespace GestorManutencao.Api;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum Perfil { Operador, Supervisor }

// Versao muda sempre que o PIN muda; vai no token e permite revogar os tokens antigos.
// Tecno é o número de utilizador do PHC (0 = ainda sem número): é a identidade da pessoa nas fichas de intervenção.
public sealed record UtilizadorApp(string Codigo, string Nome, Perfil Perfil, bool Ativo, bool PinProvisorio, long Versao = 0, int Tecno = 0);

public sealed record PedidoLogin(string? Pin)
{
    private bool PrintMembers(StringBuilder sb) { sb.Append("***"); return true; }
}

public sealed record PedidoMudarPin(string? PinAtual, string? PinNovo)
{
    private bool PrintMembers(StringBuilder sb) { sb.Append("***"); return true; }
}

public sealed record PedidoNovoUtilizador(string? Utilizador, string? Nome, int? Tecno = null);

public sealed record PedidoTecno(int? Tecno);

public sealed record RespostaLogin(string Token, DateTimeOffset ExpiraEm, string Nome, Perfil Perfil, bool PinProvisorio)
{
    private bool PrintMembers(StringBuilder sb) { sb.Append("***"); return true; }
}

public sealed record RespostaPin(string Pin)
{
    private bool PrintMembers(StringBuilder sb) { sb.Append("***"); return true; }
}

public sealed class JwtOpcoes
{
    public string Chave { get; set; } = "";
    public string Emissor { get; set; } = "GestorManutencao.Api";
    public string Audiencia { get; set; } = "GestorManutencao.App";
    public int HorasValidade { get; set; } = 10;
}

public sealed class PinOpcoes
{
    // Segredo do servidor com que se calcula o hash dos PINs; nunca fica na base de dados.
    public string Pepper { get; set; } = "";
}

public static partial class RegrasPin
{
    public const int Minimo = 4;
    public const int Maximo = 6;

    [GeneratedRegex(@"^\d{4,6}$")]
    private static partial Regex QuatroASeisDigitos();

    public static bool FormatoValido(string? pin) => pin is not null && QuatroASeisDigitos().IsMatch(pin);

    // Recusa os PINs que qualquer pessoa tentaria primeiro: 0000, 1234, 4321, 123456...
    public static bool Aceitavel(string? pin)
    {
        if (!FormatoValido(pin)) return false;
        var passos = pin!.Zip(pin.Skip(1), (a, b) => b - a).ToList();
        return !passos.All(p => p == 0) && !passos.All(p => p == 1) && !passos.All(p => p == -1);
    }

    public static string Gerar()
    {
        while (true)
        {
            var pin = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
            if (Aceitavel(pin)) return pin;
        }
    }
}

// O PIN identifica a pessoa, por isso o hash tem de ser reproduzível: HMAC com um segredo do servidor.
// Uma fuga da tabela sozinha não chega para recuperar os PINs.
public sealed class PinHasher(Microsoft.Extensions.Options.IOptions<PinOpcoes> opcoes)
{
    public string Hash(string pin) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(opcoes.Value.Pepper), Encoding.UTF8.GetBytes(pin)));
}

public enum ResultadoPin { Ok, PinEmUso, NaoExiste, JaExiste, TecnoEmUso }

public sealed class RepositorioIndisponivelException(Exception? interna = null)
    : Exception("A base de dados dos utilizadores não está disponível.", interna);

public interface IRepositorioUtilizadores
{
    // Só devolve utilizadores ativos.
    Task<UtilizadorApp?> ObterPorPinAsync(string pinHash, CancellationToken ct);
    Task<UtilizadorApp?> ObterAsync(string codigo, CancellationToken ct);
    Task<IReadOnlyList<UtilizadorApp>> ListarAsync(CancellationToken ct);
    // Cria sempre um operador, já com o seu PIN provisório.
    Task<ResultadoPin> CriarOperadorAsync(string codigo, string nome, int tecno, string pinHash, string porQuem, CancellationToken ct);
    // Cada número do PHC só pode estar numa pessoa (0 = sem número).
    Task<ResultadoPin> DefinirTecnoAsync(string codigo, int tecno, string porQuem, CancellationToken ct);
    // Com pinHashAtual só muda se o PIN ainda for esse (evita que duas alterações em simultâneo se atropelem).
    Task<ResultadoPin> DefinirPinAsync(string codigo, string pinHash, bool provisorio, string porQuem, CancellationToken ct, string? pinHashAtual = null);
}

public sealed class EmissorToken(Microsoft.Extensions.Options.IOptions<JwtOpcoes> jwt, TimeProvider relogio)
{
    public const string ClaimPinProvisorio = "pinprov";
    public const string ClaimVersao = "pv";

    public RespostaLogin Emitir(UtilizadorApp u)
    {
        var opcoes = jwt.Value;
        var agora = relogio.GetUtcNow();
        var expira = agora.AddHours(opcoes.HorasValidade);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, u.Codigo),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Role, u.Perfil.ToString())
        };
        // Com o PIN provisório o token só serve para escolher o PIN definitivo.
        if (u.PinProvisorio) claims.Add(new Claim(ClaimPinProvisorio, "1"));
        claims.Add(new Claim(ClaimVersao, u.Versao.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var token = new JwtSecurityToken(
            opcoes.Emissor, opcoes.Audiencia, claims,
            notBefore: agora.UtcDateTime, expires: expira.UtcDateTime,
            signingCredentials: new SigningCredentials(ChaveDe(opcoes.Chave), SecurityAlgorithms.HmacSha256));

        return new RespostaLogin(new JwtSecurityTokenHandler().WriteToken(token), expira, u.Nome, u.Perfil, u.PinProvisorio);
    }

    public static SymmetricSecurityKey ChaveDe(string chave) => new(Encoding.UTF8.GetBytes(chave));
}

public static partial class RegrasUtilizador
{
    [GeneratedRegex(@"^[a-z0-9._-]{2,30}$")]
    private static partial Regex CodigoValido();

    public static bool CodigoOk(string? codigo) => codigo is not null && CodigoValido().IsMatch(codigo);
    public static bool NomeOk(string? nome) => nome is { } n && n.Trim().Length is >= 2 and <= 60;
}
