using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
// Um só ficheiro de configuração: só se lê o appsettings.json (nunca o appsettings.Production.json nem o Development).
foreach (var fonte in builder.Configuration.Sources.OfType<Microsoft.Extensions.Configuration.Json.JsonConfigurationSource>()
             .Where(s => !string.Equals(s.Path, "appsettings.json", StringComparison.OrdinalIgnoreCase)).ToList())
    builder.Configuration.Sources.Remove(fonte);
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

// PIN de configuração (opcional): só serve para criar utilizadores. Tem de ser um PIN aceitável de 4 números.
var pinDev = builder.Configuration["Pin:Dev"]?.Trim() ?? "";
if (pinDev.Length > 0 && !RegrasPin.Aceitavel(pinDev)) throw new InvalidOperationException("Pin:Dev tem de ter 4 números e não pode ser uma sequência óbvia.");
builder.Services.Configure<PinOpcoes>(o => o.Dev = pinDev);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignalR();
builder.Services.AddSingleton<EmissorToken>();
builder.Services.AddSingleton<PinHasher>();

// Sem cadeia de ligação só se aceita em desenvolvimento, e com utilizadores em memória.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("PHCConn")))
{
    if (!emDesenvolvimento) throw new InvalidOperationException("ConnectionStrings:PHCConn não está configurada.");
    builder.Services.AddSingleton<IRepositorioUtilizadores>(sp => new RepositorioUtilizadoresMemoria(
    [
        (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), sp.GetRequiredService<PinHasher>().Hash("4829")),
        (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), sp.GetRequiredService<PinHasher>().Hash("7953"))
    ]));
    builder.Services.AddSingleton<IRepositorioIntervencoes, RepositorioIntervencoesMemoria>();
}
else
{
    builder.Services.AddSingleton<IRepositorioUtilizadores, SqlRepositorioUtilizadores>();
    builder.Services.AddSingleton<IRepositorioIntervencoes, SqlRepositorioIntervencoes>();
}
// O mesmo repositório (SQL ou memória) serve a gestão: cancelar planeadas e gerar a seguinte usam a mesma numeração e as mesmas regras.
builder.Services.AddSingleton<IRepositorioGestao>(sp => (IRepositorioGestao)sp.GetRequiredService<IRepositorioIntervencoes>());

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
            // WebSockets não levam cabeçalhos: o token do canal em tempo real vai no endereço (só para essa rota).
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(AvisosHub.Rota)) ctx.Token = token;
                return Task.CompletedTask;
            },
            OnTokenValidated = async ctx =>
            {
                var codigo = ctx.Principal?.FindFirstValue("sub") ?? ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (ctx.Principal?.HasClaim(EmissorToken.ClaimConfiguracao, "1") == true)
                {
                    // Token do PIN de configuração: vale só enquanto esse PIN existir e for o mesmo.
                    var hasher = ctx.HttpContext.RequestServices.GetRequiredService<PinHasher>();
                    if (!hasher.DevAtivo || ctx.Principal.FindFirstValue(EmissorToken.ClaimVersao) != hasher.VersaoDev) ctx.Fail("sessao_revogada");
                    return;
                }
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
    .AddPolicy("Autenticado", p => p.RequireAuthenticatedUser().RequireAssertion(c => !c.User.HasClaim(EmissorToken.ClaimConfiguracao, "1")))
    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser()
        .RequireAssertion(c => !c.User.HasClaim(EmissorToken.ClaimPinProvisorio, "1") && !c.User.HasClaim(EmissorToken.ClaimConfiguracao, "1")).Build())
    .AddPolicy("PinDefinitivo", p => p.RequireAuthenticatedUser().RequireAssertion(c => !c.User.HasClaim(EmissorToken.ClaimPinProvisorio, "1") && !c.User.HasClaim(EmissorToken.ClaimConfiguracao, "1")))
    .AddPolicy("GestaoUtilizadores", p => p.RequireAssertion(c =>
        !c.User.HasClaim(EmissorToken.ClaimPinProvisorio, "1")
        && (c.User.IsInRole(nameof(Perfil.Supervisor)) || c.User.HasClaim(EmissorToken.ClaimConfiguracao, "1"))))
    .AddPolicy("Supervisor", p => p.RequireRole(nameof(Perfil.Supervisor)).RequireAssertion(c => !c.User.HasClaim(EmissorToken.ClaimPinProvisorio, "1") && !c.User.HasClaim(EmissorToken.ClaimConfiguracao, "1")));

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

// Rede interna: com "Seguranca:PermitirHttp": true a API aceita http sem redirecionar para https.
if (!app.Configuration.GetValue<bool>("Seguranca:PermitirHttp"))
{
    if (!emDesenvolvimento) app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

static IResult Erro(int estado, string codigo) => Results.Json(new { erro = codigo }, statusCode: estado);
static string? Codigo(ClaimsPrincipal u) => u.FindFirstValue("sub") ?? u.FindFirstValue(ClaimTypes.NameIdentifier);

// O token dura um turno, mas as ações do supervisor confirmam-se na base: um supervisor desativado perde-as logo.
static async Task<UtilizadorApp?> SupervisorAtual(ClaimsPrincipal u, IRepositorioUtilizadores repo, CancellationToken ct)
{
    if (u.HasClaim(EmissorToken.ClaimConfiguracao, "1")) return EmissorToken.UtilizadorConfiguracao;
    var codigo = Codigo(u);
    var atual = codigo is null ? null : await repo.ObterAsync(codigo, ct);
    return atual is { Ativo: true, Perfil: Perfil.Supervisor, PinProvisorio: false } ? atual : null;
}

// Um PIN gerado nunca pode ser o de configuração (quem o recebesse ficava a conhecê-lo e não conseguia entrar).
static string PinNovo(PinHasher hasher)
{
    string pin;
    do pin = RegrasPin.Gerar(); while (hasher.EhPinDev(pin));
    return pin;
}

static IResult RegistarPinEmUso(ILogger log, HttpContext http)
{
    log.LogWarning("Mudança de PIN para um PIN já em uso, a partir de {Ip}.", http.Connection.RemoteIpAddress);
    return Erro(409, "pin_em_uso");
}

app.MapHub<AvisosHub>(AvisosHub.Rota).RequireAuthorization();
app.MapIntervencoes();
app.MapGestao();

app.MapGet("/saude", () => Results.Ok(new { estado = "ok" }));

app.MapPost("/auth/login", async (PedidoLogin pedido, IRepositorioUtilizadores repo, PinHasher hasher, EmissorToken emissor,
    HttpContext http, ILogger<Program> log, CancellationToken ct) =>
{
    http.Response.Headers.CacheControl = "no-store";
    if (!RegrasPin.FormatoValido(pedido.Pin)) return Erro(400, "pedido_invalido");

    try
    {
        if (hasher.EhPinDev(pedido.Pin))
        {
            log.LogWarning("Entrada com o PIN de configuração a partir de {Ip}.", http.Connection.RemoteIpAddress);
            return Results.Ok(emissor.EmitirConfiguracao(hasher.VersaoDev));
        }
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
    if (hasher.EhPinDev(pedido.PinNovo)) return Erro(409, "pin_em_uso");
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

var equipa = app.MapGroup("/utilizadores").RequireAuthorization("GestaoUtilizadores");

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
    if (!RegrasUtilizador.CodigoOk(codigo) || !RegrasUtilizador.NomeOk(pedido.Nome) || pedido.Perfil is { } pf && !Enum.IsDefined(pf)) return Erro(400, "pedido_invalido");

    try
    {
        if (await SupervisorAtual(eu, repo, ct) is not { } supervisor) return Results.Forbid();

        // As fichas identificam o técnico pelo número do PHC: tem de ser de uma só pessoa.
        var tecno = pedido.Tecno ?? 0;
        if (tecno is < 0 or > 99999) return Erro(400, "pedido_invalido");

        // Só o PIN de configuração cria supervisores (é assim que se cria o primeiro); um supervisor só cria operadores.
        var perfil = pedido.Perfil ?? Perfil.Operador;
        if (perfil == Perfil.Supervisor && !eu.HasClaim(EmissorToken.ClaimConfiguracao, "1")) return Results.Forbid();

        for (var tentativa = 0; tentativa < 10; tentativa++)
        {
            var pin = PinNovo(hasher);
            switch (await repo.CriarUtilizadorAsync(codigo!, pedido.Nome!.Trim(), perfil, tecno, hasher.Hash(pin), supervisor.Codigo, ct))
            {
                case ResultadoPin.Ok: return Results.Json(new RespostaPin(pin), statusCode: StatusCodes.Status201Created);
                case ResultadoPin.JaExiste: return Erro(409, "ja_existe");
                case ResultadoPin.TecnoEmUso: return Erro(409, "tecno_em_uso");
            }
        }
        return Erro(503, "indisponivel");
    }
    catch (RepositorioIndisponivelException) { return Erro(503, "indisponivel"); }
});

equipa.MapPut("/{codigo}/tecno", async (string codigo, PedidoTecno pedido, ClaimsPrincipal eu, IRepositorioUtilizadores repo, CancellationToken ct) =>
{
    if (!RegrasUtilizador.CodigoOk(codigo) || pedido.Tecno is null or < 0 or > 99999) return Erro(400, "pedido_invalido");

    try
    {
        if (await SupervisorAtual(eu, repo, ct) is not { } supervisor) return Results.Forbid();
        return await repo.DefinirTecnoAsync(codigo, pedido.Tecno.Value, supervisor.Codigo, ct) switch
        {
            ResultadoPin.Ok => Results.NoContent(),
            ResultadoPin.TecnoEmUso => Erro(409, "tecno_em_uso"),
            _ => Erro(404, "nao_existe")
        };
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
            var pin = PinNovo(hasher);
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
