using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;

namespace GestorManutencao.Api;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum Perfil { Operador, Supervisor }

public enum FalhaValidacao { Credenciais, Indisponivel }

public sealed record UtilizadorValidado(string Codigo, string Nome, IReadOnlyList<string> Perfis);

public sealed record ResultadoValidacao(UtilizadorValidado? Utilizador, FalhaValidacao? Falha);

public sealed record PedidoLogin(string? Utilizador, string? Password);

public sealed record RespostaLogin(string Token, DateTimeOffset ExpiraEm, string Nome, Perfil Perfil);

public sealed class JwtOpcoes
{
    public string Chave { get; set; } = "";
    public string Emissor { get; set; } = "GestorManutencao.Api";
    public string Audiencia { get; set; } = "GestorManutencao.App";
    public int HorasValidade { get; set; } = 10;
}

public sealed class PerfisOpcoes
{
    // Perfil do PHC (pf.resumo) que dá o modo supervisor; quem o tiver passa a poder ver o modo supervisor.
    public string Supervisor { get; set; } = "DES_MANUT_SUPERVISOR";
}

// Valida as credenciais contra o SQL Server do PHC.
public interface IValidadorCredenciais
{
    Task<ResultadoValidacao> ValidarAsync(string utilizador, string password, CancellationToken ct);
}

// Mesmo critério do LandeiroMaui: abre-se uma ligação com o próprio login SQL do utilizador.
// A API não guarda credenciais de SQL nenhumas; a cadeia de ligação base (servidor e base) vem do appsettings.
public sealed class SqlValidadorCredenciais(IConfiguration configuracao, ILogger<SqlValidadorCredenciais> log) : IValidadorCredenciais
{
    // 18456 login falhado, 18486 conta bloqueada, 18487 password expirada, 18488 password tem de mudar.
    private static readonly HashSet<int> ErrosDeCredenciais = [18456, 18486, 18487, 18488];

    private const string SqlUtilizador =
        "select username as nome from us with (nolock) where usercode = @utilizador";

    private const string SqlPerfis =
        """
        select pf.resumo
        from pf (nolock)
        inner join pfu (nolock) on pfu.pfstamp = pf.pfstamp
        where pf.resumo is not null and left(pf.resumo, 4) = 'DES_'
          and pfu.userno = (select userno from us with (nolock) where usercode = @utilizador)
        """;

    // O builder escapa os valores (um ';' não injeta opções). Pooling desligado: uma ligação em pool com a
    // mesma password não reautentica, e um login desativado no SQL continuaria a entrar.
    public static string ConstruirLigacao(string baseLigacao, string utilizador, string password) =>
        new SqlConnectionStringBuilder(baseLigacao)
        {
            UserID = utilizador,
            Password = password,
            Pooling = false,
            IntegratedSecurity = false
        }.ConnectionString;

    public async Task<ResultadoValidacao> ValidarAsync(string utilizador, string password, CancellationToken ct)
    {
        var baseLigacao = configuracao.GetConnectionString("PHCConn");
        if (string.IsNullOrWhiteSpace(baseLigacao))
        {
            log.LogError("ConnectionStrings:PHCConn não está configurada.");
            return new ResultadoValidacao(null, FalhaValidacao.Indisponivel);
        }

        try
        {
            await using var conn = new SqlConnection(ConstruirLigacao(baseLigacao, utilizador, password));
            await conn.OpenAsync(ct);

            string? nome;
            await using (var cmd = new SqlCommand(SqlUtilizador, conn))
            {
                cmd.Parameters.Add("@utilizador", System.Data.SqlDbType.VarChar, 128).Value = utilizador;
                nome = await cmd.ExecuteScalarAsync(ct) as string;
            }

            // Login SQL válido mas sem utilizador PHC (conta de serviço, sa...): não entra na app.
            if (nome is null) return new ResultadoValidacao(null, FalhaValidacao.Credenciais);

            var perfis = new List<string>();
            await using (var cmd = new SqlCommand(SqlPerfis, conn))
            {
                cmd.Parameters.Add("@utilizador", System.Data.SqlDbType.VarChar, 128).Value = utilizador;
                await using var leitor = await cmd.ExecuteReaderAsync(ct);
                while (await leitor.ReadAsync(ct))
                    if (!leitor.IsDBNull(0)) perfis.Add(leitor.GetString(0).Trim());
            }

            return new ResultadoValidacao(new UtilizadorValidado(utilizador, nome.Trim() is { Length: > 0 } n ? n : utilizador, perfis), null);
        }
        catch (SqlException ex) when (ErrosDeCredenciais.Contains(ex.Number))
        {
            return new ResultadoValidacao(null, FalhaValidacao.Credenciais);
        }
        catch (SqlException ex)
        {
            // Só o número do erro (4060 = sem acesso à base, etc.), nunca a mensagem.
            log.LogError("Falha SQL durante o login (erro {Numero}).", ex.Number);
            return new ResultadoValidacao(null, FalhaValidacao.Indisponivel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sem a password nem o utilizador no registo.
            log.LogError(ex, "Falha a ligar ao SQL Server durante o login.");
            return new ResultadoValidacao(null, FalhaValidacao.Indisponivel);
        }
    }
}

public sealed class EmissorToken(Microsoft.Extensions.Options.IOptions<JwtOpcoes> jwt, Microsoft.Extensions.Options.IOptions<PerfisOpcoes> perfis, TimeProvider relogio)
{
    public Perfil CalcularPerfil(UtilizadorValidado u) =>
        u.Perfis.Contains(perfis.Value.Supervisor, StringComparer.OrdinalIgnoreCase) ? Perfil.Supervisor : Perfil.Operador;

    public RespostaLogin Emitir(UtilizadorValidado u)
    {
        var opcoes = jwt.Value;
        var perfil = CalcularPerfil(u);
        var agora = relogio.GetUtcNow();
        var expira = agora.AddHours(opcoes.HorasValidade);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, u.Codigo),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Role, perfil.ToString())
        };

        var token = new JwtSecurityToken(
            opcoes.Emissor, opcoes.Audiencia, claims,
            notBefore: agora.UtcDateTime, expires: expira.UtcDateTime,
            signingCredentials: new SigningCredentials(ChaveDe(opcoes.Chave), SecurityAlgorithms.HmacSha256));

        return new RespostaLogin(new JwtSecurityTokenHandler().WriteToken(token), expira, u.Nome, perfil);
    }

    public static SymmetricSecurityKey ChaveDe(string chave) => new(Encoding.UTF8.GetBytes(chave));
}
