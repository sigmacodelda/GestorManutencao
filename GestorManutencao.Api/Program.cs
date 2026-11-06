using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var emDesenvolvimento = builder.Environment.IsDevelopment();

builder.Services.Configure<JwtOpcoes>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<PinOpcoes>(builder.Configuration.GetSection("Pin"));

// Em produção os segredos vêm da configuração (mínimo 32 caracteres). Em desenvolvimento geram-se por arranque.
string SegredoObrigatorio(string chave, Action<string> usar)
{
    var valor = builder.Configuration[chave];
    if (string.IsNullOrWhiteSpace(valor))
    {
        if (!emDesenvolvimento) throw new InvalidOperationException($"{chave} não está configurada.");
        valor = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    }
    else if (valor.Length < 32)
    {
        throw new InvalidOperationException($"{chave} tem de ter pelo menos 32 caracteres.");
    }
    usar(valor);
    return valor;
}

var jwtOpcoes = builder.Configuration.GetSection("Jwt").Get<JwtOpcoes>() ?? new JwtOpcoes();
SegredoObrigatorio("Jwt:Chave", v => jwtOpcoes.Chave = v);
builder.Services.Configure<JwtOpcoes>(o => o.Chave = jwtOpcoes.Chave);
var pepper = SegredoObrigatorio("Pin:Pepper", v => builder.Services.Configure<PinOpcoes>(o => o.Pepper = v));
if (pepper == jwtOpcoes.Chave) throw new InvalidOperationException("Pin:Pepper e Jwt:Chave têm de ser diferentes.");

// Para criar o primeiro supervisor na tabela: dotnet run --project GestorManutencao.Api -- gerar-pin
if (args.Contains("gerar-pin"))
{
    var pinNovo = RegrasPin.Gerar();
    Console.WriteLine($"PIN: {pinNovo}");
    Console.WriteLine($"pinhash: {new PinHasher(Microsoft.Extensions.Options.Options.Create(new PinOpcoes { Pepper = pepper })).Hash(pinNovo)}");
    return;
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EmissorToken>();
builder.Services.AddSingleton<PinHasher>();

// Sem cadeia de ligação só se aceita em desenvolvimento, e com utilizadores em memória.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("PHCConn")))
{
    if (!emDesenvolvimento) throw new InvalidOperationException("ConnectionStrings:PHCConn não está configurada.");
    builder.Services.AddSingleton<IRepositorioUtilizadores>(sp => new RepositorioUtilizadoresMemoria(
    [
        (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false), sp.GetRequiredService<PinHasher>().Hash("482913")),
        (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false), sp.GetRequiredService<PinHasher>().Hash("795316"))
    ]));
    builder.Services.AddSingleton<IRepositorioIntervencoes, RepositorioIntervencoesMemoria>();
}
else
{
    builder.Services.AddSingleton<IRepositorioUtilizadores, SqlRepositorioUtilizadores>();
    builder.Services.AddSingleton<IRepositorioIntervencoes, SqlRepositorioIntervencoes>();
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOpcoes.Emissor,
            ValidAudience = jwtOpcoes.Audiencia,
            IssuerSigningKey = EmissorToken.ChaveDe(jwtOpcoes.Chave),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var codigo = ctx.Principal?.FindFirstValue("sub") ?? ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                try
                {
                    var atual = codigo is null ? null : await ctx.HttpContext.RequestServices.GetRequiredService<IRepositorioUtilizadores>()
                        .ObterAsync(codigo, ctx.HttpContext.RequestAborted);
                    var versao = ctx.Principal?.FindFirstValue(EmissorToken.ClaimVersao);
                    if (atual is not { Ativo: true } || versao != atual.Versao.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        ctx.Fail("sessao_revogada");
                }
                catch (RepositorioIndisponivelException)
                {
                    ctx.Fail("indisponivel");
                }
            }
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Autenticado", p => p.RequireAuthenticatedUser())
    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser()
        .RequireAssertion(c => !c.User.HasClaim(EmissorToken.ClaimPinProvisorio, "1")).Build())
    .AddPolicy("PinDefinitivo", p => p.RequireAuthenticatedUser().RequireAssertion(c => !c.User.HasClaim(EmissorToken.ClaimPinProvisorio, "1")))
    .AddPolicy("Supervisor", p => p.RequireRole(nameof(Perfil.Supervisor)).RequireAssertion(c => !c.User.HasClaim(EmissorToken.ClaimPinProvisorio, "1")));

// Como o PIN identifica a pessoa, adivinhá-lo não se trava por utilizador: trava-se por endereço e no total.
// O limite global é só um teto contra ataques distribuídos; um valor baixo deixava qualquer um bloquear a equipa toda.
var limiteIp = builder.Configuration.GetValue("Limites:LoginPorMinuto", 10);
var limiteGlobal = builder.Configuration.GetValue("Limites:LoginGlobalPorMinuto", 600);
static bool LimitaPin(HttpContext c) => c.GetEndpoint()?.Metadata.GetMetadata<LimitaPin>() is not null;
// Atrás de NAT, todos os aparelhos partilham o endereço; em IPv6 um atacante roda endereços dentro do mesmo /64.
static string ChaveEndereco(HttpContext c)
{
    var ip = c.Connection.RemoteIpAddress;
    if (ip is null) return "desconhecido";
    if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
    return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? Convert.ToHexString(ip.GetAddressBytes().AsSpan(0, 8)) : ip.ToString();
}
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, _) =>
    {
        ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Limites")
            .LogWarning("Pedido travado pelo limite de tentativas ({Endereco}).", ChaveEndereco(ctx.HttpContext));
        return ValueTask.CompletedTask;
    };
    o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(c => !LimitaPin(c)
            ? RateLimitPartition.GetNoLimiter("livre")
            : RateLimitPartition.GetFixedWindowLimiter(ChaveEndereco(c),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limiteIp, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })),
        PartitionedRateLimiter.Create<HttpContext, string>(c => !LimitaPin(c)
            ? RateLimitPartition.GetNoLimiter("livre")
            : RateLimitPartition.GetFixedWindowLimiter("total",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limiteGlobal, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
});

var app = builder.Build();

// Atrás de proxy/IIS o IP real vem em X-Forwarded-For; só se confia nos proxies listados (Proxy:IpsConfiaveis).
var proxies = app.Configuration.GetSection("Proxy:IpsConfiaveis").Get<string[]>() ?? [];
if (proxies.Length > 0)
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownProxies.Clear();
    foreach (var ip in proxies) forwarded.KnownProxies.Add(IPAddress.Parse(ip));
    app.UseForwardedHeaders(forwarded);
}

if (!emDesenvolvimento) app.UseHsts();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

static IResult Erro(int estado, string codigo) => Results.Json(new { erro = codigo }, statusCode: estado);
static string? Codigo(ClaimsPrincipal u) => u.FindFirstValue("sub") ?? u.FindFirstValue(ClaimTypes.NameIdentifier);

// O token dura um turno, mas as ações do supervisor confirmam-se na base: um supervisor desativado perde-as logo.
static async Task<UtilizadorApp?> SupervisorAtual(ClaimsPrincipal u, IRepositorioUtilizadores repo, CancellationToken ct)
{
    var codigo = Codigo(u);
    var atual = codigo is null ? null : await repo.ObterAsync(codigo, ct);
    return atual is { Ativo: true, Perfil: Perfil.Supervisor, PinProvisorio: false } ? atual : null;
}

static IResult RegistarPinEmUso(ILogger log, HttpContext http)
{
    log.LogWarning("Mudança de PIN para um PIN já em uso, a partir de {Ip}.", http.Connection.RemoteIpAddress);
    return Erro(409, "pin_em_uso");
}

app.MapIntervencoes();

app.MapGet("/saude", () => Results.Ok(new { estado = "ok" }));

app.MapPost("/auth/login", async (PedidoLogin pedido, IRepositorioUtilizadores repo, PinHasher hasher, EmissorToken emissor,
    HttpContext http, ILogger<Program> log, CancellationToken ct) =>
{
    http.Response.Headers.CacheControl = "no-store";
    if (!RegrasPin.FormatoValido(pedido.Pin)) return Erro(400, "pedido_invalido");

    try
    {
        var utilizador = await repo.ObterPorPinAsync(hasher.Hash(pedido.Pin!), ct);
        if (utilizador is not null) return Results.Ok(emissor.Emitir(utilizador));

        // Sem o PIN no registo: só o endereço, para detetar tentativas de adivinhar.
        log.LogWarning("Entrada com PIN falhada a partir de {Ip}.", http.Connection.RemoteIpAddress);
        return Erro(401, "credenciais");
    }
    catch (RepositorioIndisponivelException)
    {
        return Erro(503, "indisponivel");
    }
}).WithMetadata(new LimitaPin()).AllowAnonymous();

app.MapPost("/auth/pin", async (PedidoMudarPin pedido, ClaimsPrincipal eu, IRepositorioUtilizadores repo, PinHasher hasher,
    EmissorToken emissor, HttpContext http, ILogger<Program> log, CancellationToken ct) =>
{
    http.Response.Headers.CacheControl = "no-store";
    if (!RegrasPin.FormatoValido(pedido.PinAtual) || !RegrasPin.FormatoValido(pedido.PinNovo)) return Erro(400, "pedido_invalido");
    if (!RegrasPin.Aceitavel(pedido.PinNovo)) return Erro(400, "pin_fraco");
    if (pedido.PinNovo == pedido.PinAtual) return Erro(400, "pin_igual");

    var codigo = Codigo(eu);
    if (codigo is null) return Results.Unauthorized();

    try
    {
        // O PIN atual tem de ser o desta pessoa: um token roubado não chega para trocar o PIN.
        var atual = await repo.ObterPorPinAsync(hasher.Hash(pedido.PinAtual!), ct);
        if (atual is null || atual.Codigo != codigo)
        {
            log.LogWarning("Mudança de PIN recusada a partir de {Ip}.", http.Connection.RemoteIpAddress);
            return Erro(401, "credenciais");
        }

        return await repo.DefinirPinAsync(codigo, hasher.Hash(pedido.PinNovo!), false, codigo, ct, hasher.Hash(pedido.PinAtual!)) switch
        {
            ResultadoPin.Ok => Results.Ok(emissor.Emitir((await repo.ObterAsync(codigo, ct))!)),
            ResultadoPin.PinEmUso => RegistarPinEmUso(log, http),
            _ => Erro(401, "credenciais")
        };
    }
    catch (RepositorioIndisponivelException)
    {
        return Erro(503, "indisponivel");
    }
}).WithMetadata(new LimitaPin()).RequireAuthorization("Autenticado");

app.MapGet("/auth/eu", (ClaimsPrincipal utilizador) => Results.Ok(new
{
    utilizador = Codigo(utilizador),
    nome = utilizador.Identity?.Name,
    perfil = utilizador.FindFirstValue(ClaimTypes.Role)
})).RequireAuthorization();

var equipa = app.MapGroup("/utilizadores").RequireAuthorization("Supervisor");

equipa.MapGet("/", async (ClaimsPrincipal eu, IRepositorioUtilizadores repo, CancellationToken ct) =>
{
    try
    {
        if (await SupervisorAtual(eu, repo, ct) is null) return Results.Forbid();
        return Results.Ok(await repo.ListarAsync(ct));
    }
    catch (RepositorioIndisponivelException) { return Erro(503, "indisponivel"); }
});

equipa.MapPost("/", async (PedidoNovoUtilizador pedido, ClaimsPrincipal eu, IRepositorioUtilizadores repo, PinHasher hasher,
    HttpContext http, CancellationToken ct) =>
{
    http.Response.Headers.CacheControl = "no-store";
    var codigo = pedido.Utilizador?.Trim().ToLowerInvariant();
    if (!RegrasUtilizador.CodigoOk(codigo) || !RegrasUtilizador.NomeOk(pedido.Nome)) return Erro(400, "pedido_invalido");

    try
    {
        if (await SupervisorAtual(eu, repo, ct) is not { } supervisor) return Results.Forbid();

        // As fichas identificam o técnico pelo nome (45 letras): dois nomes iguais veriam as fichas um do outro.
        var nomeFicha = EndpointsIntervencoes.Truncar(pedido.Nome!.Trim());
        if ((await repo.ListarAsync(ct)).Any(u => string.Equals(EndpointsIntervencoes.Truncar(u.Nome), nomeFicha, StringComparison.OrdinalIgnoreCase)))
            return Erro(409, "nome_repetido");

        for (var tentativa = 0; tentativa < 10; tentativa++)
        {
            var pin = RegrasPin.Gerar();
            switch (await repo.CriarOperadorAsync(codigo!, pedido.Nome!.Trim(), hasher.Hash(pin), supervisor.Codigo, ct))
            {
                case ResultadoPin.Ok: return Results.Json(new RespostaPin(pin), statusCode: StatusCodes.Status201Created);
                case ResultadoPin.JaExiste: return Erro(409, "ja_existe");
            }
        }
        return Erro(503, "indisponivel");
    }
    catch (RepositorioIndisponivelException) { return Erro(503, "indisponivel"); }
});

equipa.MapPost("/{codigo}/pin", async (string codigo, ClaimsPrincipal eu, IRepositorioUtilizadores repo, PinHasher hasher,
    HttpContext http, CancellationToken ct) =>
{
    http.Response.Headers.CacheControl = "no-store";
    if (!RegrasUtilizador.CodigoOk(codigo)) return Erro(400, "pedido_invalido");

    try
    {
        if (await SupervisorAtual(eu, repo, ct) is not { } supervisor) return Results.Forbid();

        // Só se repõe o PIN a operadores: repor o de outro supervisor permitia fazer-se passar por ele.
        if (await repo.ObterAsync(codigo, ct) is not { } alvo) return Erro(404, "nao_existe");
        if (alvo.Perfil != Perfil.Operador) return Results.Forbid();

        for (var tentativa = 0; tentativa < 10; tentativa++)
        {
            var pin = RegrasPin.Gerar();
            switch (await repo.DefinirPinAsync(codigo, hasher.Hash(pin), true, supervisor.Codigo, ct))
            {
                case ResultadoPin.Ok: return Results.Ok(new RespostaPin(pin));
                case ResultadoPin.NaoExiste: return Erro(404, "nao_existe");
            }
        }
        return Erro(503, "indisponivel");
    }
    catch (RepositorioIndisponivelException) { return Erro(503, "indisponivel"); }
});

app.Run();

public partial class Program;

// Marca os endpoints sujeitos ao limite de tentativas de PIN.
public sealed class LimitaPin;
