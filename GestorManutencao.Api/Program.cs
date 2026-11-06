using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOpcoes>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<PerfisOpcoes>(builder.Configuration.GetSection("Perfis"));

// Em produção a chave tem de vir da configuração (mínimo 32 caracteres). Em desenvolvimento gera-se uma por arranque.
var jwtOpcoes = builder.Configuration.GetSection("Jwt").Get<JwtOpcoes>() ?? new JwtOpcoes();
if (string.IsNullOrWhiteSpace(jwtOpcoes.Chave))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Jwt:Chave não está configurada.");
    jwtOpcoes.Chave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    builder.Services.Configure<JwtOpcoes>(o => o.Chave = jwtOpcoes.Chave);
}
else if (jwtOpcoes.Chave.Length < 32)
{
    throw new InvalidOperationException("Jwt:Chave tem de ter pelo menos 32 caracteres.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EmissorToken>();
builder.Services.AddScoped<IValidadorCredenciais, SqlValidadorCredenciais>();

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
    });
builder.Services.AddAuthorization();

// Trava tentativas de adivinhar passwords: 10 por minuto e por endereço.
var limiteLogin = builder.Configuration.GetValue("Limites:LoginPorMinuto", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limiteLogin, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

// Atrás de proxy/IIS o IP real vem em X-Forwarded-For; só se confia nos proxies listados (Proxy:IpsConfiaveis).
var proxies = app.Configuration.GetSection("Proxy:IpsConfiaveis").Get<string[]>() ?? [];
if (proxies.Length > 0)
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownProxies.Clear();
    foreach (var ip in proxies) forwarded.KnownProxies.Add(System.Net.IPAddress.Parse(ip));
    app.UseForwardedHeaders(forwarded);
}

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/saude", () => Results.Ok(new { estado = "ok" }));

app.MapPost("/auth/login", async (PedidoLogin pedido, IValidadorCredenciais validador, EmissorToken emissor, HttpContext http, ILogger<Program> log, CancellationToken ct) =>
{
    http.Response.Headers.CacheControl = "no-store";
    if (string.IsNullOrWhiteSpace(pedido.Utilizador) || string.IsNullOrEmpty(pedido.Password)
        || pedido.Utilizador.Length > 128 || pedido.Password.Length > 128)
        return Results.BadRequest(new { erro = "pedido_invalido" });

    var resultado = await validador.ValidarAsync(pedido.Utilizador.Trim(), pedido.Password, ct);
    return resultado switch
    {
        { Utilizador: { } u } => Results.Ok(emissor.Emitir(u)),
        { Falha: FalhaValidacao.Credenciais } => RegistarFalha(log, http, Results.Json(new { erro = "credenciais" }, statusCode: StatusCodes.Status401Unauthorized)),
        _ => Results.Json(new { erro = "indisponivel" }, statusCode: StatusCodes.Status503ServiceUnavailable)
    };
}).RequireRateLimiting("login").AllowAnonymous();

app.MapGet("/auth/eu", (ClaimsPrincipal utilizador) => Results.Ok(new
{
    utilizador = utilizador.FindFirstValue(ClaimTypes.NameIdentifier) ?? utilizador.FindFirstValue("sub"),
    nome = utilizador.Identity?.Name,
    perfil = utilizador.FindFirstValue(ClaimTypes.Role)
})).RequireAuthorization();

// Sem utilizador nem password no registo: só o IP, para detetar força bruta.
static IResult RegistarFalha(ILogger log, HttpContext http, IResult resposta)
{
    log.LogWarning("Login falhado a partir de {Ip}.", http.Connection.RemoteIpAddress);
    return resposta;
}

app.Run();

public partial class Program;
