# Entrada e saída de peças (Receção e Picking) — Plano de implementação

> **Para agentes:** SUB-SKILL obrigatória: `superpowers:subagent-driven-development` (recomendada) ou `superpowers:executing-plans`. Os passos usam caixas `- [ ]`.

**Objetivo:** replicar na GestorManutencao o circuito de peças de manutenção que já existe na LandeiroMaui: **Entrada de peças** (dossier PHC 64, um por dia) e **Saída automática na ficha** (dossier PHC 63, um por intervenção, armazém 104).

**Arquitetura:** a API ganha um repositório novo (`IRepositorioPecas`, SQL + memória) que só trata de peças e movimentos; os endpoints de material da ficha chamam-no depois do repositório de intervenções (que não muda de assinatura). A UI ganha `IPecasService` (padrão `IEquipaService`: API + Dev), um ecrã "Entrada de peças" e a escolha da peça no formulário de material da ficha. Offline: a saída acompanha o material (sincroniza com ele); a entrada exige ligação.

**Tech Stack:** .NET 10, ASP.NET Core minimal API, `Microsoft.Data.SqlClient` (sem Dapper: a API não o usa e não se acrescentam dependências), Blazor Hybrid (MAUI), xUnit + `WebApplicationFactory`.

## Restrições globais

- Fonte de verdade do circuito: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui\Services\Manutencao\PecasMovimentoService.cs` e `PecasService.cs`. O SQL deste plano é o desse ficheiro, adaptado a ADO.NET.
- Peça = artigo `st` com `stobs.u_manut = 1`, `st.inactivo = 0`. Armazém das peças = **104**. Dossiers: **63** saída, **64** entrada.
- Stock sobe/desce pelos triggers do PHC, disparados pelos "toques" (`UPDATE bo SET [no] = [no]`, `UPDATE bi SET [ref] = [ref]`). Anular = apagar a linha (e `bi2`) e, se o dossier ficar vazio, `bo3`/`bo2`/`bo`.
- Ligação material → saída: `bi.lobs` = stamp do `u_matinterv` (25 caracteres). Sem stock **avisa e deixa continuar** (nunca bloqueia).
- **Escritas na base só em teste.** Antes de qualquer escrita: `SELECT DB_NAME()` e confirmação do nome pelo utilizador. Nunca em produção nem clientes. A connection string fica só no `appsettings` (cifrada, `LigacaoPhc`); nunca em código, logs ou documentos.
- Sem tabelas nem campos novos. `stobs.u_manut`, os dossiers 63/64 em `ts` e o armazém 104 têm de existir já (Task 6 verifica-o por leitura).
- Comentários mínimos, em pt-PT, só o porquê. Segredos: correr `C:/_SigmaRepos/Claude-The-ALLKNOWING/tools/verificar-segredos.ps1 -Caminho <pasta>` antes de entregar.
- Git: ramo próprio `feature/pecas-entrada-saida`; commits só pelo agente `sc-git` e com confirmação do utilizador. Os passos "Commit" abaixo são pedidos ao `sc-git`, não comandos diretos.

## Fora de âmbito (decisão a registar)

- Localização no armazém (tabela nova no PHC, a criar por pessoa) e consulta de stock por local: ficam para depois, por decisão do utilizador (2026-11-20).
- Etiquetas QR, localizações, peças necessárias por tarefa, alerta de rutura (também por fazer na LandeiroMaui).
- Leitura de etiquetas: **decidido 2026-11-20** — telemóvel lê pela câmara com `ZXing.Net.Maui`; PDA lê por campo escondido sempre à escuta (leitor tipo teclado, Enter termina); modo Automático/Câmara/Leitor com ecrã de escolha (Task 5).
- Sem perfil "Armazém": como na LandeiroMaui, qualquer utilizador com sessão dá entrada; a saída segue as regras de edição da ficha. (Suposição, a confirmar.)

## Estrutura de ficheiros

| Ficheiro | Responsabilidade |
|---|---|
| `GestorManutencao.Api/Pecas.cs` (criar) | DTOs, `RegrasPecas`, `IRepositorioPecas`, endpoints `/pecas*` |
| `GestorManutencao.Api/SqlRepositorioPecas.cs` (criar) | Consulta de peças e movimentos nos dossiers 63/64 |
| `GestorManutencao.Api/RepositorioPecasMemoria.cs` (criar) | Mesma interface em memória (modo Development e testes) |
| `GestorManutencao.Api/Intervencoes.cs` (alterar) | `MaterialDto`/`PedidoMaterial` ganham `Ref`; endpoints de material chamam a saída |
| `GestorManutencao.Api/Program.cs` (alterar) | Registo do repositório e `app.MapPecas()` |
| `GestorManutencao.UI/Modelos/Pecas.cs` (criar) | Modelos da UI (espelham os DTOs) |
| `GestorManutencao.UI/Servicos/IPecasService.cs`, `PecasApiService.cs`, `PecasDevService.cs` (criar) | Cliente da API e versão Dev |
| `GestorManutencao.UI/Pages/EntradaPecas.razor` (criar) | Ecrã de entrada de peças |
| `GestorManutencao.UI/Componentes/PecaPesquisa.razor` (criar) | Pesquisa/leitura da peça, partilhada |
| `GestorManutencao.UI/Pages/DetalheIntervencao.razor`, `Modelos/Intervencoes.cs`, `Servicos/*Intervencoes*`, `Offline/*` (alterar) | Material com peça |
| `GestorManutencao.Tests/PecasRegrasTests.cs`, `PecasApiTests.cs`, `MaterialPecasApiTests.cs` (criar) | Testes |
| `docs/pecas-armazem.md`, `docs/adr/0002-pecas-por-dossier-phc.md` (criar) | Documentação e decisão |

---

### Task 1: Regras e modelos das peças (sem base de dados)

**Files:**
- Create: `GestorManutencao.Api/Pecas.cs`
- Test: `GestorManutencao.Tests/PecasRegrasTests.cs`

**Interfaces:**
- Produces: `RegrasPecas.Palavras(string?) : string[]`, `RegrasPecas.EscaparLike(string) : string`, `RegrasPecas.AvisoStock(decimal pedida, decimal stock) : string?`, `RegrasPecas.QuantidadeValida(decimal?) : bool`; constantes `RegrasPecas.NdosSaida = 63`, `NdosEntrada = 64`, `Armazem = 104`, `CampoStobs = "u_manut"`; records `PecaDto`, `ResultadoPecasDto`, `EntradaPecaDto`, `PedidoEntrada`; interface `IRepositorioPecas`.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
// GestorManutencao.Tests/PecasRegrasTests.cs
using GestorManutencao.Api;

namespace GestorManutencao.Tests;

public class PecasRegrasTests
{
    [Fact]
    public void Palavras_corta_a_quatro_em_minusculas_e_sem_espacos_a_mais()
        => Assert.Equal(["a", "b", "c", "d"], RegrasPecas.Palavras("  A  B c D e "));

    [Fact]
    public void Palavras_vazio_devolve_nenhuma()
    {
        Assert.Empty(RegrasPecas.Palavras(null));
        Assert.Empty(RegrasPecas.Palavras("   "));
    }

    [Fact]
    public void EscaparLike_escapa_curinga_e_barra()
        => Assert.Equal(@"a\%b\_c\[d\\e", RegrasPecas.EscaparLike(@"a%b_c[d\e"));

    [Theory]
    [InlineData(0, 5, null)]
    [InlineData(2, 0, "Sem stock desta peça.")]
    [InlineData(3, 2, "Stock insuficiente: há 2 e pede 3.")]
    [InlineData(2, 2, null)]
    public void AvisoStock_avisa_mas_nunca_bloqueia(double pedida, double stock, string? esperado)
        => Assert.Equal(esperado, RegrasPecas.AvisoStock((decimal)pedida, (decimal)stock));

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0.5, true)]
    [InlineData(1000000, false)]
    public void QuantidadeValida_positiva_e_abaixo_do_limite(double? q, bool esperado)
        => Assert.Equal(esperado, RegrasPecas.QuantidadeValida(q is null ? null : (decimal)q));
}
```

- [ ] **Step 2: Correr e ver falhar**

Run: `dotnet test GestorManutencao.Tests --filter PecasRegrasTests`
Expected: erro de compilação, `RegrasPecas` não existe.

- [ ] **Step 3: Implementar**

```csharp
// GestorManutencao.Api/Pecas.cs
namespace GestorManutencao.Api;

// Peça = artigo st marcado em stobs.u_manut. Stock e local são os do armazém de manutenção (sa).
public sealed record PecaDto(string Ref, string Design, string Codigo, string Local, decimal Stock, decimal StMin, decimal Preco)
{
    public bool AbaixoDoMinimo => StMin > 0 && Stock <= StMin;
}

// CampoExiste = false: falta criar stobs.u_manut no PHC (o ecrã diz o que falta). HaMais: pedir ao utilizador para escrever mais.
public sealed record ResultadoPecasDto(bool CampoExiste, IReadOnlyList<PecaDto> Pecas, bool HaMais);

public sealed record EntradaPecaDto(string Id, string Ref, string Design, decimal Qtt, string Quem, string Hora);

public sealed record PedidoEntrada(string? Ref, decimal? Qtt);

public static class RegrasPecas
{
    public const int NdosSaida = 63, NdosEntrada = 64, Armazem = 104;
    public const string CampoStobs = "u_manut";
    public const decimal QuantidadeMaxima = 999_999m;

    public static string[] Palavras(string? texto)
        => (texto ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4).ToArray();

    public static string EscaparLike(string palavra)
        => palavra.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public static string? AvisoStock(decimal pedida, decimal stock)
    {
        if (pedida <= 0) return null;
        if (stock <= 0) return "Sem stock desta peça.";
        return pedida > stock ? $"Stock insuficiente: há {stock:0.###} e pede {pedida:0.###}." : null;
    }

    public static bool QuantidadeValida(decimal? q) => q is > 0 and < QuantidadeMaxima + 1;
}

public sealed record ResultadoMovimento(bool Ok, string? Id, string? Erro)
{
    public static ResultadoMovimento Sucesso(string id) => new(true, id, null);
    public static ResultadoMovimento Falha(string erro) => new(false, null, erro);
}

public interface IRepositorioPecas
{
    Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct);
    Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct);

    // Entrada: dossier 64 do dia (um por dia), linha por peça.
    Task<ResultadoMovimento> DarEntradaAsync(string refPeca, decimal qtt, string iniciais, CancellationToken ct);
    Task<IReadOnlyList<EntradaPecaDto>> EntradasDeHojeAsync(CancellationToken ct);
    Task<ResultadoMovimento> AnularEntradaAsync(string id, CancellationToken ct);

    // Saída: dossier 63 da ficha, linha por material (bi.lobs = materialId). Grava também u_matinterv.ref.
    Task<ResultadoMovimento> DarSaidaAsync(string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct);
    Task<ResultadoMovimento> AnularSaidaAsync(string materialId, CancellationToken ct);

    // Preenche Ref e SaidaDada nos materiais da ficha (a ligação é u_matinterv.ref e bi.lobs).
    Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct);
}
```

Nota: `MaterialDto` ganha `Ref`/`SaidaDada` na Task 3; para esta task compilar, acrescentar já ali o fim do record (ver Task 3, Step 3) **ou** deixar `EnriquecerMateriaisAsync` para a Task 3. Fazer o segundo: apagar a última linha da interface agora e acrescentá-la na Task 3.

- [ ] **Step 4: Correr e ver passar**

Run: `dotnet test GestorManutencao.Tests --filter PecasRegrasTests`
Expected: PASS (8 testes).

- [ ] **Step 5: Commit** (pedir ao `sc-git`, com confirmação): `Regras e modelos das peças de manutenção`.

---

### Task 2: Repositório SQL e em memória + endpoints da entrada de peças

**Files:**
- Create: `GestorManutencao.Api/SqlRepositorioPecas.cs`, `GestorManutencao.Api/RepositorioPecasMemoria.cs`
- Modify: `GestorManutencao.Api/Pecas.cs` (endpoints), `GestorManutencao.Api/Program.cs:85-96`
- Test: `GestorManutencao.Tests/PecasApiTests.cs`

**Interfaces:**
- Consumes: `IRepositorioPecas`, `RegrasPecas`, `LigacaoPhc.Obter(IConfiguration)`, `RepositorioIndisponivelException` (existentes), `ComoTecnicoOuSupervisor(eu, utilizadores, ct, (TecnicoAtual t, bool sup) => Task<IResult>)`, `PedidoInvalido(string)` (existentes em `Intervencoes.cs`).
- Produces: `app.MapPecas()`; rotas `GET /pecas?texto=`, `GET /pecas/codigo/{codigo}`, `GET /pecas/entradas/hoje`, `POST /pecas/entradas` (`PedidoEntrada` → `EntradaPecaDto`), `DELETE /pecas/entradas/{id}`.

- [ ] **Step 1: Teste da API (falha)**

```csharp
// GestorManutencao.Tests/PecasApiTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

public class PecasApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
            [
                (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913"))
            ])));
            s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(new RepositorioPecasMemoria(
            [
                new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
                new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
            ])));
        }));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAnaAsync()
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin("482913"))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    [Fact]
    public async Task Sem_sessao_recusa()
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/pecas")).StatusCode);
    }

    [Fact]
    public async Task Pesquisa_por_texto_e_por_codigo_exato()
    {
        var c = await ComoAnaAsync();
        var r = await c.GetFromJsonAsync<ResultadoPecasDto>("/pecas?texto=rolamento");
        Assert.Equal("P002", Assert.Single(r!.Pecas).Ref);
        var p = await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/560001");
        Assert.Equal("P001", p!.Ref);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/pecas/codigo/XXXX")).StatusCode);
    }

    [Fact]
    public async Task Entrada_sobe_o_stock_lista_hoje_e_anula()
    {
        var c = await ComoAnaAsync();
        var r = await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada("P002", 3));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var entrada = await r.Content.ReadFromJsonAsync<EntradaPecaDto>();
        Assert.Equal(3, (await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/P002"))!.Stock);
        Assert.Single((await c.GetFromJsonAsync<List<EntradaPecaDto>>("/pecas/entradas/hoje"))!);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/pecas/entradas/{entrada!.Id}")).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/P002"))!.Stock);
        Assert.Empty((await c.GetFromJsonAsync<List<EntradaPecaDto>>("/pecas/entradas/hoje"))!);
    }

    [Theory]
    [InlineData("P001", 0)]
    [InlineData("P001", -2)]
    [InlineData("", 1)]
    [InlineData("INEXISTENTE", 1)]
    public async Task Entrada_invalida_recusa(string refPeca, int qtt)
    {
        var c = await ComoAnaAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada(refPeca, qtt))).StatusCode);
    }
}
```

- [ ] **Step 2: Correr e ver falhar** — `dotnet test GestorManutencao.Tests --filter PecasApiTests` → não compila (`RepositorioPecasMemoria`, `MapPecas`).

- [ ] **Step 3: Repositório em memória**

```csharp
// GestorManutencao.Api/RepositorioPecasMemoria.cs
namespace GestorManutencao.Api;

// Modo Development e testes: mesmas regras do SQL, sem base. Stock = soma das entradas − saídas.
public sealed class RepositorioPecasMemoria(IEnumerable<PecaDto>? pecas = null) : IRepositorioPecas
{
    private readonly object _trinco = new();
    private readonly Dictionary<string, PecaDto> _pecas = (pecas ?? Amostra()).ToDictionary(p => p.Ref, StringComparer.OrdinalIgnoreCase);
    private readonly List<EntradaPecaDto> _entradas = [];
    private readonly Dictionary<string, (string Ref, decimal Qtt)> _saidas = []; // materialId → saída

    private static IEnumerable<PecaDto> Amostra() =>
    [
        new("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
        new("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
    ];

    private void Mexer(string refPeca, decimal delta)
    {
        var p = _pecas[refPeca];
        _pecas[refPeca] = p with { Stock = p.Stock + delta };
    }

    public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct)
    {
        var palavras = RegrasPecas.Palavras(texto);
        lock (_trinco)
        {
            var todas = _pecas.Values
                .Where(p => palavras.All(w => p.Ref.Contains(w, StringComparison.OrdinalIgnoreCase)
                    || p.Design.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Codigo.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.Ref).ToList();
            return Task.FromResult(new ResultadoPecasDto(true, todas.Take(maximo).ToList(), todas.Count > maximo));
        }
    }

    public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct)
    {
        codigo = codigo.Trim();
        lock (_trinco)
            return Task.FromResult(_pecas.Values.FirstOrDefault(p => p.Codigo == codigo) ?? _pecas.GetValueOrDefault(codigo));
    }

    public Task<ResultadoMovimento> DarEntradaAsync(string refPeca, decimal qtt, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (!_pecas.TryGetValue(refPeca.Trim(), out var p)) return Task.FromResult(ResultadoMovimento.Falha("Referência inexistente."));
            var e = new EntradaPecaDto(Guid.NewGuid().ToString("N")[..25], p.Ref, p.Design, qtt, iniciais, DateTime.Now.ToString("HH:mm:ss"));
            _entradas.Add(e);
            Mexer(p.Ref, qtt);
            return Task.FromResult(ResultadoMovimento.Sucesso(e.Id));
        }
    }

    public Task<IReadOnlyList<EntradaPecaDto>> EntradasDeHojeAsync(CancellationToken ct)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<EntradaPecaDto>>(_entradas.AsEnumerable().Reverse().ToList());
    }

    public Task<ResultadoMovimento> AnularEntradaAsync(string id, CancellationToken ct)
    {
        lock (_trinco)
        {
            var e = _entradas.FirstOrDefault(x => x.Id == id);
            if (e is null) return Task.FromResult(ResultadoMovimento.Falha("A linha já não existe."));
            _entradas.Remove(e);
            Mexer(e.Ref, -e.Qtt);
            return Task.FromResult(ResultadoMovimento.Sucesso(id));
        }
    }

    public Task<ResultadoMovimento> DarSaidaAsync(string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (!_pecas.ContainsKey(refPeca.Trim())) return Task.FromResult(ResultadoMovimento.Falha("Referência inexistente."));
            if (_saidas.ContainsKey(materialId)) return Task.FromResult(ResultadoMovimento.Falha("Já foi dada saída desta peça."));
            _saidas[materialId] = (refPeca.Trim(), qtt);
            Mexer(refPeca.Trim(), -qtt);
            return Task.FromResult(ResultadoMovimento.Sucesso(materialId));
        }
    }

    public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (!_saidas.Remove(materialId, out var s)) return Task.FromResult(ResultadoMovimento.Sucesso(materialId));
            Mexer(s.Ref, s.Qtt);
            return Task.FromResult(ResultadoMovimento.Sucesso(materialId));
        }
    }
}
```

- [ ] **Step 4: Endpoints** — acrescentar a `Pecas.cs`:

```csharp
public static class EndpointsPecas
{
    public static void MapPecas(this WebApplication app)
    {
        var g = app.MapGroup("/pecas").RequireAuthorization();

        g.MapGet("", async (string? texto, IRepositorioPecas repo, CancellationToken ct) =>
            Results.Ok(await repo.ListarAsync(texto, 60, ct)));

        g.MapGet("/codigo/{codigo}", async (string codigo, IRepositorioPecas repo, CancellationToken ct) =>
            await repo.ObterPorCodigoAsync(codigo, ct) is { } p ? Results.Ok(p) : Results.NotFound());

        g.MapGet("/entradas/hoje", async (IRepositorioPecas repo, CancellationToken ct) =>
            Results.Ok(await repo.EntradasDeHojeAsync(ct)));

        g.MapPost("/entradas", (PedidoEntrada p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas repo, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(p.Ref) || !RegrasPecas.QuantidadeValida(p.Qtt)
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
                {
                    var r = await repo.DarEntradaAsync(p.Ref.Trim(), p.Qtt!.Value, t.Codigo, ct);
                    if (!r.Ok) return Results.BadRequest(new { erro = r.Erro });
                    var linha = (await repo.EntradasDeHojeAsync(ct)).First(e => e.Id == r.Id);
                    return Results.Ok(linha);
                }));

        g.MapDelete("/entradas/{id}", async (string id, IRepositorioPecas repo, CancellationToken ct) =>
            (await repo.AnularEntradaAsync(id, ct)).Ok ? Results.NoContent() : Results.NotFound());
    }
}
```

`ComoTecnicoOuSupervisor` e `PedidoInvalido` são `private`/`static` no ficheiro `Intervencoes.cs`: tornar `internal` a visibilidade da(s) função(ões) usadas (ou mover para uma classe `internal static` partilhada) e acrescentar `using System.Security.Claims;` ao topo de `Pecas.cs`. Ler a definição antes de mexer (`Grep "ComoTecnicoOuSupervisor" -n`).

- [ ] **Step 5: Registo** — em `Program.cs`, no ramo Development: `builder.Services.AddSingleton<IRepositorioPecas, RepositorioPecasMemoria>();`; no ramo SQL: `builder.Services.AddSingleton<IRepositorioPecas, SqlRepositorioPecas>();`; e onde se chamam os `MapIntervencoes()`: `app.MapPecas();`.

- [ ] **Step 6: Repositório SQL** (ADO.NET; SQL igual ao da LandeiroMaui)

```csharp
// GestorManutencao.Api/SqlRepositorioPecas.cs
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Peças e movimentos de armazém por dossier PHC (63 saída por ficha, 64 entrada por dia), como a LandeiroMaui.
// Os "toques" (UPDATE x = x) fazem os triggers do PHC moverem o stock. Só na base de testes até estar validado.
public sealed class SqlRepositorioPecas(IConfiguration configuracao, ILogger<SqlRepositorioPecas> log) : IRepositorioPecas
{
    private const string Hoje = "CAST(CONVERT(date, GETDATE()) AS datetime)";
    private const string Hora = "CONVERT(varchar(8), GETDATE(), 108)";
    private static bool _campoExiste;

    private async Task<T> Executar<T>(Func<SqlConnection, Task<T>> trabalho)
    {
        var ligacao = LigacaoPhc.Obter(configuracao);
        if (string.IsNullOrWhiteSpace(ligacao)) throw new RepositorioIndisponivelException();
        try
        {
            await using var conn = new SqlConnection(ligacao);
            await conn.OpenAsync();
            return await trabalho(conn);
        }
        catch (SqlException ex)
        {
            log.LogError("Falha SQL nas peças (erro {Numero}).", ex.Number);
            throw new RepositorioIndisponivelException(ex);
        }
    }

    private static SqlCommand Cmd(SqlConnection conn, SqlTransaction? tx, string sql, params (string Nome, object? Valor)[] ps)
    {
        var cmd = new SqlCommand(sql, conn, tx);
        foreach (var (nome, valor) in ps) cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
        return cmd;
    }

    private static async Task<bool> CampoExisteAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_campoExiste) return true;
        await using var cmd = Cmd(conn, null, $"SELECT COL_LENGTH('stobs', '{RegrasPecas.CampoStobs}')");
        _campoExiste = await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
        return _campoExiste;
    }

    private const string Colunas = """
        LTRIM(RTRIM(s.ref)) AS Ref, LTRIM(RTRIM(s.design)) AS Design, LTRIM(RTRIM(s.codigo)) AS Codigo,
        LTRIM(RTRIM(s.local)) AS LocalArtigo,
        CAST(COALESCE(NULLIF(s.epcpond, 0), NULLIF(s.epcult, 0), NULLIF(s.epcusto, 0), 0) AS decimal(19, 6)) AS Preco,
        ISNULL(a.stock, 0) AS Stock, ISNULL(a.stmin, 0) AS StMin, LTRIM(RTRIM(ISNULL(a.local, ''))) AS LocalArm
        """;

    private const string De = $"""
        FROM st s WITH (NOLOCK)
        JOIN stobs o WITH (NOLOCK) ON o.ref = s.ref
        LEFT JOIN sa a WITH (NOLOCK) ON a.ref = s.ref AND a.armazem = @arm
        """;

    private static PecaDto Ler(SqlDataReader r)
    {
        var localArm = r.GetString(r.GetOrdinal("LocalArm"));
        return new PecaDto(r.GetString(r.GetOrdinal("Ref")), r.GetString(r.GetOrdinal("Design")), r.GetString(r.GetOrdinal("Codigo")),
            localArm != "" ? localArm : r.GetString(r.GetOrdinal("LocalArtigo")),
            r.GetDecimal(r.GetOrdinal("Stock")), r.GetDecimal(r.GetOrdinal("StMin")), r.GetDecimal(r.GetOrdinal("Preco")));
    }

    public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct) => Executar(async conn =>
    {
        if (!await CampoExisteAsync(conn, ct)) return new ResultadoPecasDto(false, [], false);
        var palavras = RegrasPecas.Palavras(texto);
        var filtros = palavras.Select((_, i) => $"(s.ref LIKE @t{i} ESCAPE '\\' OR s.design LIKE @t{i} ESCAPE '\\' OR s.codigo LIKE @t{i} ESCAPE '\\')");
        var onde = palavras.Length == 0 ? "" : " AND " + string.Join(" AND ", filtros);
        await using var cmd = Cmd(conn, null,
            $"SELECT TOP (@max) {Colunas} {De} WHERE o.[{RegrasPecas.CampoStobs}] = 1 AND s.inactivo = 0{onde} ORDER BY s.ref",
            ("@max", maximo + 1), ("@arm", RegrasPecas.Armazem));
        for (var i = 0; i < palavras.Length; i++) cmd.Parameters.AddWithValue($"@t{i}", "%" + RegrasPecas.EscaparLike(palavras[i]) + "%");
        var lista = new List<PecaDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(Ler(r));
        return new ResultadoPecasDto(true, lista.Take(maximo).ToList(), lista.Count > maximo);
    });

    public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct) => Executar<PecaDto?>(async conn =>
    {
        codigo = codigo.Trim();
        if (codigo.Length == 0 || !await CampoExisteAsync(conn, ct)) return null;
        await using var cmd = Cmd(conn, null, $"""
            SELECT TOP 1 {Colunas} {De}
            WHERE o.[{RegrasPecas.CampoStobs}] = 1 AND s.inactivo = 0 AND (LTRIM(RTRIM(s.codigo)) = @c OR LTRIM(RTRIM(s.ref)) = @c)
            ORDER BY CASE WHEN LTRIM(RTRIM(s.codigo)) = @c THEN 0 ELSE 1 END
            """, ("@c", codigo), ("@arm", RegrasPecas.Armazem));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    });

    // ---------- Entrada (dossier 64, um por dia)

    public Task<ResultadoMovimento> DarEntradaAsync(string refPeca, decimal qtt, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        iniciais = string.IsNullOrWhiteSpace(iniciais) ? "XX" : iniciais.Trim();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var proc = Cmd(conn, tx, "SELECT TOP 1 bostamp FROM bo WITH (NOLOCK) WHERE ndos = @ndos AND CAST(dataopen AS date) = CAST(GETDATE() AS date) ORDER BY obrano",
                ("@ndos", RegrasPecas.NdosEntrada));
            var bostamp = await proc.ExecuteScalarAsync(ct) as string
                ?? await CriarDossierAsync(conn, tx, RegrasPecas.NdosEntrada, $"Entradas {DateTime.Today:dd/MM/yyyy}", iniciais, ct);
            var linha = await InserirLinhaAsync(conn, tx, bostamp, RegrasPecas.NdosEntrada, refPeca, qtt, "", iniciais, ct);
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(linha);
        }
        catch (SqlException ex) when (ex.Number == 50000 || ex.Class == 16)
        {
            await tx.RollbackAsync(ct);
            return ResultadoMovimento.Falha(ex.Message);
        }
    });

    public Task<IReadOnlyList<EntradaPecaDto>> EntradasDeHojeAsync(CancellationToken ct) => Executar<IReadOnlyList<EntradaPecaDto>>(async conn =>
    {
        await using var cmd = Cmd(conn, null, """
            SELECT i.bistamp, LTRIM(RTRIM(i.ref)), LTRIM(RTRIM(i.design)), i.qtt, LTRIM(RTRIM(i.ousrinis)), LTRIM(RTRIM(i.ousrhora))
            FROM bi i WITH (NOLOCK) JOIN bo b WITH (NOLOCK) ON b.bostamp = i.bostamp
            WHERE b.ndos = @ndos AND CAST(b.dataopen AS date) = CAST(GETDATE() AS date)
            ORDER BY i.ousrhora DESC, i.lordem DESC
            """, ("@ndos", RegrasPecas.NdosEntrada));
        var lista = new List<EntradaPecaDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(new EntradaPecaDto(r.GetString(0).Trim(), r.GetString(1), r.GetString(2), r.GetDecimal(3), r.GetString(4), r.GetString(5)));
        return lista;
    });

    public Task<ResultadoMovimento> AnularEntradaAsync(string id, CancellationToken ct) => ApagarLinhaAsync(id, RegrasPecas.NdosEntrada, ct);

    // ---------- Saída (dossier 63, um por ficha; ligação = bi.lobs = stamp do material)

    public Task<ResultadoMovimento> DarSaidaAsync(string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        iniciais = string.IsNullOrWhiteSpace(iniciais) ? "XX" : iniciais.Trim();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var ja = Cmd(conn, tx, "SELECT COUNT(*) FROM bi WITH (NOLOCK) WHERE ndos = @ndos AND LTRIM(RTRIM(lobs)) = @m", ("@ndos", RegrasPecas.NdosSaida), ("@m", materialId)))
                if ((int)(await ja.ExecuteScalarAsync(ct))! > 0) { await tx.RollbackAsync(ct); return ResultadoMovimento.Falha("Já foi dada saída desta peça."); }

            await using var lobs = Cmd(conn, tx, "SELECT ISNULL(COL_LENGTH('bi', 'lobs'), 0)");
            if ((int)(await lobs.ExecuteScalarAsync(ct))! < 25) throw new InvalidOperationException("A coluna bi.lobs não comporta o stamp do material (25 caracteres).");

            string? ficha, equip = null; decimal numero = 0;
            await using (var f = Cmd(conn, tx, """
                SELECT mi.u_fichaintstamp, f.no, LTRIM(RTRIM(ISNULL(e.ref, f.ref))) + ' ' + LTRIM(RTRIM(ISNULL(e.design, f.design)))
                FROM u_matinterv mi WITH (NOLOCK)
                JOIN u_fichaint f WITH (NOLOCK) ON f.u_fichaintstamp = mi.u_fichaintstamp
                LEFT JOIN u_equip e WITH (NOLOCK) ON e.u_equipstamp = f.u_equipstamp
                WHERE mi.u_matintervstamp = @m
                """, ("@m", materialId)))
            {
                await using var r = await f.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) { await tx.RollbackAsync(ct); return ResultadoMovimento.Falha("O material já não existe."); }
                ficha = r.GetString(0); numero = r.GetDecimal(1); equip = r.GetString(2);
            }

            // Dossier da intervenção: o que já tem linhas de materiais desta ficha; senão cria-se.
            await using var proc = Cmd(conn, tx, """
                SELECT TOP 1 b.bostamp FROM bo b WITH (NOLOCK) JOIN bi i WITH (NOLOCK) ON i.bostamp = b.bostamp
                WHERE b.ndos = @ndos AND LTRIM(RTRIM(i.lobs)) IN (SELECT mi.u_matintervstamp FROM u_matinterv mi WITH (NOLOCK) WHERE mi.u_fichaintstamp = @ficha)
                ORDER BY b.obrano
                """, ("@ndos", RegrasPecas.NdosSaida), ("@ficha", ficha));
            var bostamp = await proc.ExecuteScalarAsync(ct) as string
                ?? await CriarDossierAsync(conn, tx, RegrasPecas.NdosSaida, $"Ficha {numero:0} {equip}", iniciais, ct);
            await InserirLinhaAsync(conn, tx, bostamp, RegrasPecas.NdosSaida, refPeca, qtt, materialId, iniciais, ct);

            await using var liga = Cmd(conn, tx, "UPDATE u_matinterv SET ref = @ref WHERE u_matintervstamp = @m", ("@ref", refPeca), ("@m", materialId));
            await liga.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(materialId);
        }
        catch (SqlException ex) when (ex.Class == 16)
        {
            await tx.RollbackAsync(ct);
            return ResultadoMovimento.Falha(ex.Message);
        }
    });

    public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, CancellationToken ct) => Executar(async conn =>
    {
        await using var cmd = Cmd(conn, null, "SELECT bistamp FROM bi WITH (NOLOCK) WHERE ndos = @ndos AND LTRIM(RTRIM(lobs)) = @m", ("@ndos", RegrasPecas.NdosSaida), ("@m", materialId));
        if (await cmd.ExecuteScalarAsync(ct) is not string bistamp) return ResultadoMovimento.Sucesso(materialId);
        return await ApagarLinhaAsync(bistamp, RegrasPecas.NdosSaida, ct);
    });

    // ---------- Dossier e linhas (padrão do TransferenciasService da LandeiroMaui)

    private static async Task<string> CriarDossierAsync(SqlConnection conn, SqlTransaction tx, int ndos, string obranome, string iniciais, CancellationToken ct)
    {
        var bostamp = Guid.NewGuid().ToString("N")[..25];
        await using (var t = Cmd(conn, tx, "SELECT ISNULL(COL_LENGTH('bo', 'obranome'), 20)"))
        {
            var tamanho = (int)(await t.ExecuteScalarAsync(ct))!;
            if (tamanho > 0 && obranome.Length > tamanho) obranome = obranome[..tamanho].TrimEnd();
        }
        await using var bo = Cmd(conn, tx, $"""
            DECLARE @obrano numeric(10,0) = (SELECT ISNULL(MAX(obrano), 0) + 1 FROM bo WITH (NOLOCK) WHERE ndos = @ndos AND YEAR(dataobra) = YEAR(GETDATE()));
            INSERT INTO bo (bostamp, boano, obrano, obranome, dataobra, dataopen, memissao, origem, ndos, nmdos, ocupacao, lifref,
                            [no], estab, nome, moeda, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            SELECT @bostamp, YEAR(GETDATE()), @obrano, @obranome, CAST(GETDATE() AS date), GETDATE(), 'EURO', 'Gestor Manutencao',
                   ts.ndos, ts.nmdos, ts.ocupacao, ts.lifref, 1, 0, 'Carnes Landeiro, SA.', 'PTE ou EURO',
                   @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora}
            FROM ts WITH (NOLOCK) WHERE ts.ndos = @ndos;
            """, ("@bostamp", bostamp), ("@ndos", ndos), ("@obranome", obranome), ("@iniciais", iniciais));
        if (await bo.ExecuteNonQueryAsync(ct) == 0) throw new InvalidOperationException($"Tipo de dossier {ndos} inexistente (ts).");
        await using var extra = Cmd(conn, tx, $"""
            INSERT INTO bo2 (bo2stamp, idserie, xpdhora, tiposaft, ncin, ncout, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            SELECT @bostamp, 'BO', CONVERT(varchar(5), GETDATE(), 108), ts.tiposaft, ts.ncin, ts.ncout, @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora}
            FROM ts WITH (NOLOCK) WHERE ts.ndos = @ndos;
            INSERT INTO bo3 (bo3stamp, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            VALUES (@bostamp, @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
            UPDATE bo SET [no] = [no] WHERE bostamp = @bostamp;
            """, ("@bostamp", bostamp), ("@ndos", ndos), ("@iniciais", iniciais));
        await extra.ExecuteNonQueryAsync(ct);
        return bostamp;
    }

    // lobs = stamp do material (saída) ou vazio (entrada). Devolve o bistamp. RAISERROR classe 16 se a referência não existir.
    private static async Task<string> InserirLinhaAsync(SqlConnection conn, SqlTransaction tx, string bostamp, int ndos, string refPeca,
        decimal qtt, string lobs, string iniciais, CancellationToken ct)
    {
        var bistamp = Guid.NewGuid().ToString("N")[..25];
        await using var cmd = Cmd(conn, tx, $"""
            DECLARE @lordem decimal(10,0) = (SELECT ISNULL(MAX(lordem) + 10000, 10000) FROM bi WITH (NOLOCK) WHERE bostamp = @bostamp);
            DECLARE @ordem decimal(10,0) = (SELECT ISNULL(MAX(binum1) + 1, 1) FROM bi WITH (NOLOCK) WHERE bostamp = @bostamp);
            DECLARE @obrano decimal(10,0) = (SELECT obrano FROM bo WITH (NOLOCK) WHERE bostamp = @bostamp);
            DECLARE @nmdos varchar(60) = (SELECT nmdos FROM bo WITH (NOLOCK) WHERE bostamp = @bostamp);
            DECLARE @design varchar(60), @unidade varchar(4), @epcusto decimal(19,6), @pcusto decimal(19,6), @edebito decimal(19,6), @debito decimal(19,6);
            SELECT @design = LTRIM(RTRIM(design)), @unidade = LTRIM(RTRIM(unidade)), @epcusto = epcpond, @pcusto = pcpond, @edebito = epvultimo, @debito = pvultimo
            FROM st WITH (NOLOCK) WHERE ref = @ref;
            IF @design IS NULL
            BEGIN
                RAISERROR(N'Referência inexistente na ST.', 16, 1);
                RETURN;
            END
            INSERT INTO bi (bistamp, ref, design, qtt, unidade, edebito, debito, armazem, iva, tabiva, rdata, lordem, bostamp, obranome,
                            dataobra, obrano, ndos, nmdos, lote, usalote, binum1, lobs, epcusto, pcusto, ttdeb, ettdeb,
                            ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            VALUES (@bistamp, @ref, @design, @qtt, ISNULL(NULLIF(@unidade, ''), 'UN'), @edebito, @debito, @armazem, 0, 0, GETDATE(), @lordem, @bostamp, '',
                    GETDATE(), @obrano, @ndos, @nmdos, '', 0, @ordem, @lobs, @epcusto, @pcusto, @qtt * @debito, @qtt * @edebito,
                    @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
            INSERT INTO bi2 (bi2stamp) VALUES (@bistamp);
            UPDATE bo SET [no] = [no] WHERE bostamp = @bostamp;
            UPDATE bi SET [ref] = [ref] WHERE bostamp = @bostamp;
            """, ("@bistamp", bistamp), ("@bostamp", bostamp), ("@ndos", ndos), ("@ref", refPeca), ("@qtt", qtt),
                 ("@lobs", lobs), ("@iniciais", iniciais), ("@armazem", RegrasPecas.Armazem));
        await cmd.ExecuteNonQueryAsync(ct);
        return bistamp;
    }

    // Apaga a linha (e a bi2) e, se o dossier ficar sem linhas, o dossier (bo/bo2/bo3). Só linhas do ndos indicado.
    private Task<ResultadoMovimento> ApagarLinhaAsync(string bistamp, int ndos, CancellationToken ct) => Executar(async conn =>
    {
        bistamp = bistamp.Trim();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        await using var proc = Cmd(conn, tx, "SELECT bostamp FROM bi WITH (NOLOCK) WHERE bistamp = @b AND ndos = @ndos", ("@b", bistamp), ("@ndos", ndos));
        if (await proc.ExecuteScalarAsync(ct) is not string bostamp) { await tx.RollbackAsync(ct); return ResultadoMovimento.Falha("A linha já não existe."); }
        await using (var del = Cmd(conn, tx, "DELETE FROM bi2 WHERE bi2stamp = @b; DELETE FROM bi WHERE bistamp = @b;", ("@b", bistamp)))
            await del.ExecuteNonQueryAsync(ct);
        await using var restam = Cmd(conn, tx, "SELECT COUNT(*) FROM bi WITH (NOLOCK) WHERE bostamp = @s", ("@s", bostamp));
        if ((int)(await restam.ExecuteScalarAsync(ct))! == 0)
            await using (var d = Cmd(conn, tx, "DELETE FROM bo3 WHERE bo3stamp = @s; DELETE FROM bo2 WHERE bo2stamp = @s; DELETE FROM bo WHERE bostamp = @s;", ("@s", bostamp)))
                await d.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return ResultadoMovimento.Sucesso(bistamp);
    });
}
```

Notas para quem implementa: `ficha`/`equip` declarados com `string?` mas sempre atribuídos no bloco; se o compilador reclamar, inicializar com `""`. `EnriquecerMateriaisAsync` entra na Task 3.

- [ ] **Step 7: Correr os testes** — `dotnet test GestorManutencao.Tests --filter "PecasApiTests|PecasRegrasTests"` → PASS. Depois `dotnet build GestorManutencao.slnx` sem erros (o repositório SQL só compila aqui; corre-se na Task 6).

- [ ] **Step 8: Commit** (pedir ao `sc-git`): `Entrada de peças: repositório SQL e em memória e endpoints /pecas`.

---

### Task 3: Material da ficha com peça e saída automática

**Files:**
- Modify: `GestorManutencao.Api/Intervencoes.cs:53,65,419-432` (DTOs e endpoints de material), `GestorManutencao.Api/Pecas.cs` (interface + `EnriquecerMateriaisAsync`), `RepositorioPecasMemoria.cs`, `SqlRepositorioPecas.cs`
- Test: `GestorManutencao.Tests/MaterialPecasApiTests.cs`

**Interfaces:**
- Consumes: `IRepositorioIntervencoes.AdicionarMaterialAsync/EditarMaterialAsync/ApagarMaterialAsync/ListarMateriaisAsync` (assinaturas inalteradas), `IRepositorioPecas` da Task 1/2, `RepositorioIntervencoesMemoria` (cria fichas de teste; ver `MaterialFotosTests` para o padrão e o id `F1`).
- Produces: `MaterialDto(string Id, string Design, decimal Qtt, decimal Epv, decimal Custo, string Ref = "", bool SaidaDada = false)`; `PedidoMaterial(string? Design, decimal? Qtt, decimal? Epv, string? Ref = null)`; `IRepositorioPecas.EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto>, CancellationToken)`.

Regras do fluxo (iguais à LandeiroMaui):
1. `POST`: o material grava-se (regras da ficha: em curso, do técnico). Se vem `Ref`, dá-se saída depois; se a saída falhar, o material fica **sem** saída e a resposta traz `SaidaDada = false` (o ecrã oferece repetir).
2. `PUT`: anula-se a saída antiga; grava-se; se há `Ref`, dá-se a nova.
3. `DELETE`: anula-se a saída antes de apagar o material (o stock volta).
4. `GET`: a lista vem enriquecida com `Ref` e `SaidaDada`.
5. Mais um endpoint `POST /intervencoes/{id}/materiais/{materialId}/saida` repete uma saída falhada.

- [ ] **Step 1: Teste (falha)** — usar o mesmo arranque que `PecasApiTests` mas com `RepositorioIntervencoesMemoria` (copiar `Criar`/`ComoAsync` de `MaterialFotosTests.cs`, com `IRepositorioPecas` em memória como no Step 1 da Task 2) e a ficha em curso da Ana. Casos:

```csharp
[Fact]
public async Task Material_com_peca_da_saida_e_apagar_devolve_o_stock()
{
    var c = await ComoAnaAsync();                       // ficha F1 em curso da Ana (padrão de MaterialFotosTests)
    var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Parafuso M8", 4, 0.12m, "P001"));
    var m = await r.Content.ReadFromJsonAsync<MaterialDto>();
    Assert.True(m!.SaidaDada);
    Assert.Equal(6, (await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/P001"))!.Stock);

    Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);
    Assert.Equal(10, (await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/P001"))!.Stock);
}

[Fact]
public async Task Editar_a_quantidade_desfaz_a_saida_antiga_e_da_a_nova()
{
    var c = await ComoAnaAsync();
    var m = await (await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Parafuso M8", 4, 0.12m, "P001"))).Content.ReadFromJsonAsync<MaterialDto>();
    var e = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m!.Id}", new PedidoMaterial("Parafuso M8", 7, 0.12m, "P001"));
    Assert.True((await e.Content.ReadFromJsonAsync<MaterialDto>())!.SaidaDada);
    Assert.Equal(3, (await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/P001"))!.Stock);
}

[Fact]
public async Task Sem_stock_avisa_mas_deixa_continuar()
{
    var c = await ComoAnaAsync();
    var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Rolamento 6204", 1, 5.5m, "P002")); // stock 0
    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    Assert.True((await r.Content.ReadFromJsonAsync<MaterialDto>())!.SaidaDada);
}

[Fact]
public async Task Material_sem_peca_nao_mexe_no_stock_e_lista_traz_ref()
{
    var c = await ComoAnaAsync();
    await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Fita isoladora", 1, 1m));
    await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Parafuso M8", 2, 0.12m, "P001"));
    var lista = await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais");
    Assert.Equal(["", "P001"], lista!.Select(x => x.Ref).ToArray());
    Assert.Equal(8, (await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/P001"))!.Stock);
}

[Fact]
public async Task Referencia_inexistente_recusa_com_400_e_nao_cria_material()
{
    var c = await ComoAnaAsync();
    var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("X", 1, 1m, "NAOEXISTE"));
    Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    Assert.Empty((await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais"))!);
}
```

- [ ] **Step 2: Correr e ver falhar** — erro de compilação (`PedidoMaterial` com 4 argumentos).

- [ ] **Step 3: DTOs** — em `Intervencoes.cs`:

```csharp
public sealed record MaterialDto(string Id, string Design, decimal Qtt, decimal Epv, decimal Custo, string Ref = "", bool SaidaDada = false);
// ...
public sealed record PedidoMaterial(string? Design, decimal? Qtt, decimal? Epv, string? Ref = null);
```

E acrescentar à interface em `Pecas.cs`:

```csharp
    Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct);
```

- [ ] **Step 4: Memória** — em `RepositorioPecasMemoria`: guardar também a ref ligada ao material e devolver `Ref`/`SaidaDada`:

```csharp
    public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct)
    {
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<MaterialDto>>(materiais
                .Select(m => _saidas.TryGetValue(m.Id, out var s) ? m with { Ref = s.Ref, SaidaDada = true } : m).ToList());
    }
```

(A ref de um material cuja saída foi anulada perde-se em memória; no SQL fica em `u_matinterv.ref`. Aceitável: a memória é só para dev e testes.)

- [ ] **Step 5: SQL** — em `SqlRepositorioPecas`:

```csharp
    public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct) => Executar<IReadOnlyList<MaterialDto>>(async conn =>
    {
        if (materiais.Count == 0) return materiais;
        var ids = materiais.Select(m => m.Id).ToList();
        var nomes = string.Join(",", ids.Select((_, i) => $"@m{i}"));
        await using var cmd = Cmd(conn, null, $"""
            SELECT LTRIM(RTRIM(mi.u_matintervstamp)), LTRIM(RTRIM(ISNULL(mi.ref, ''))),
                   CASE WHEN EXISTS (SELECT 1 FROM bi i WITH (NOLOCK) WHERE i.ndos = @ndos AND LTRIM(RTRIM(i.lobs)) = LTRIM(RTRIM(mi.u_matintervstamp))) THEN 1 ELSE 0 END
            FROM u_matinterv mi WITH (NOLOCK) WHERE mi.u_matintervstamp IN ({nomes})
            """, ("@ndos", RegrasPecas.NdosSaida));
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue($"@m{i}", ids[i]);
        var porId = new Dictionary<string, (string Ref, bool Saida)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) porId[r.GetString(0)] = (r.GetString(1), r.GetInt32(2) == 1);
        return materiais.Select(m => porId.TryGetValue(m.Id, out var x) ? m with { Ref = x.Ref, SaidaDada = x.Saida } : m).ToList();
    });
```

- [ ] **Step 6: Endpoints de material** — em `Intervencoes.cs`, substituir os quatro endpoints (linhas 416-432) por:

```csharp
        g.MapGet("/intervencoes/{id}/materiais", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, IRepositorioPecas pecas, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ListarMateriaisAsync(id, t, ct) is { } m ? Results.Ok(await pecas.EnriquecerMateriaisAsync(m, ct)) : Results.NotFound()));

        g.MapPost("/intervencoes/{id}/materiais", (string id, PedidoMaterial p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, IRepositorioPecas pecas, CancellationToken ct) =>
            RegrasIntervencao.ValidarMaterial(p.Design, p.Qtt, p.Epv) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) =>
                {
                    var refPeca = (p.Ref ?? "").Trim();
                    if (refPeca != "" && await pecas.ObterPorCodigoAsync(refPeca, ct) is null) return Results.BadRequest(new { erro = "Peça inexistente." });
                    var (res, mat) = await repo.AdicionarMaterialAsync(id, p.Design!.Trim(), p.Qtt!.Value, p.Epv ?? 0, t, ct, sup);
                    if (res == ResultadoIntervencao.Ok && mat is not null && refPeca != "")
                        mat = mat with { Ref = refPeca, SaidaDada = (await pecas.DarSaidaAsync(mat.Id, refPeca, mat.Qtt, t.Codigo, ct)).Ok };
                    return Resposta((res, mat));
                }));

        g.MapPut("/intervencoes/{id}/materiais/{materialId}", (string id, string materialId, PedidoMaterial p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, IRepositorioPecas pecas, CancellationToken ct) =>
            RegrasIntervencao.ValidarMaterial(p.Design, p.Qtt, p.Epv) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) =>
                {
                    var refPeca = (p.Ref ?? "").Trim();
                    if (refPeca != "" && await pecas.ObterPorCodigoAsync(refPeca, ct) is null) return Results.BadRequest(new { erro = "Peça inexistente." });
                    // Só mexe na saída depois de a ficha aceitar a edição (acesso e estado).
                    var (res, mat) = await repo.EditarMaterialAsync(id, materialId, p.Design!.Trim(), p.Qtt!.Value, p.Epv ?? 0, t, sup, ct);
                    if (res != ResultadoIntervencao.Ok || mat is null) return Resposta((res, mat));
                    await pecas.AnularSaidaAsync(materialId, ct);
                    if (refPeca != "")
                        mat = mat with { Ref = refPeca, SaidaDada = (await pecas.DarSaidaAsync(materialId, refPeca, mat.Qtt, t.Codigo, ct)).Ok };
                    return Resposta((res, mat));
                }));

        g.MapPost("/intervencoes/{id}/materiais/{materialId}/saida", (string id, string materialId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, IRepositorioPecas pecas, CancellationToken ct) =>
            ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) =>
            {
                if (await repo.ListarMateriaisAsync(id, t, ct) is not { } lista || lista.FirstOrDefault(m => m.Id == materialId) is not { } mat) return Results.NotFound();
                var m = (await pecas.EnriquecerMateriaisAsync([mat], ct))[0];
                if (m.Ref == "") return Results.BadRequest(new { erro = "Este material não está ligado a uma peça." });
                var r = await pecas.DarSaidaAsync(materialId, m.Ref, m.Qtt, t.Codigo, ct);
                return r.Ok ? Results.Ok(m with { SaidaDada = true }) : Results.Conflict(new { erro = r.Erro });
            }));

        g.MapDelete("/intervencoes/{id}/materiais/{materialId}", (string id, string materialId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, IRepositorioPecas pecas, CancellationToken ct) =>
            ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) =>
            {
                // Anula a saída antes de apagar o material (sem material já não se sabe a ligação); se a anulação falhar, nada se apaga.
                var anul = await pecas.AnularSaidaAsync(materialId, ct);
                if (!anul.Ok) return Results.Conflict(new { erro = anul.Erro });
                var r = await repo.ApagarMaterialAsync(id, materialId, t, ct, sup);
                return r == ResultadoIntervencao.Ok ? Results.NoContent() : Resposta<object>((r, null));
            }));
```

Atenção ao `DELETE`: se a ficha recusar o apagar (estado/acesso) a saída já foi anulada. Para evitar isso, antes de anular, ler o material com `repo.ListarMateriaisAsync(id, t, ct)` (devolve `null` se a ficha não é visível) e só então anular e apagar; se `ApagarMaterialAsync` falhar a seguir, **repor a saída** com `DarSaidaAsync` usando a `Ref`/`Qtt` lidas. Implementar assim e cobrir com um teste ("ficha concluída recusa apagar e o stock não muda").

- [ ] **Step 7: Correr** — `dotnet test GestorManutencao.Tests` completo. Expected: tudo passa, incluindo os testes antigos de material (`IntervencoesApiTests`, `MaterialFotosTests`), porque o novo campo é opcional.

- [ ] **Step 8: Commit** (pedir ao `sc-git`): `Material da ficha com peça e saída automática`.

---

### Task 4: UI — modelos, serviço de peças e ecrã de entrada

**Files:**
- Create: `GestorManutencao.UI/Modelos/Pecas.cs`, `Servicos/IPecasService.cs`, `Servicos/PecasApiService.cs`, `Servicos/PecasDevService.cs`, `Componentes/PecaPesquisa.razor`, `Pages/EntradaPecas.razor`
- Modify: `GestorManutencao.App/MauiProgram.cs:93-99,115-118` (registos), `GestorManutencao.UI/Pages/Inicio.razor` (mosaico)
- Test: `GestorManutencao.Tests/PecasUiTests.cs` (regras de ecrã sem Blazor)

**Interfaces:**
- Consumes: rotas da Task 2; padrão `EquipaApiService(HttpClient)` / `DevEquipaService` (ler `GestorManutencao.UI/Servicos/IEquipaService.cs` e `GestorManutencao.App/EquipaApiService*` para copiar o tratamento de erros e do token).
- Produces: `IPecasService` com `PesquisarAsync(string texto)`, `ObterPorCodigoAsync(string codigo)`, `EntradasDeHojeAsync()`, `DarEntradaAsync(string refPeca, decimal qtt)`, `AnularEntradaAsync(string id)`; modelos `Peca`, `ResultadoPecas`, `EntradaPeca`.

- [ ] **Step 1: Modelos**

```csharp
// GestorManutencao.UI/Modelos/Pecas.cs
namespace GestorManutencao.UI.Modelos;

public sealed record Peca(string Ref, string Design, string Codigo, string Local, decimal Stock, decimal StMin, decimal Preco)
{
    public bool AbaixoDoMinimo => StMin > 0 && Stock <= StMin;
}
public sealed record ResultadoPecas(bool CampoExiste, IReadOnlyList<Peca> Pecas, bool HaMais);
public sealed record EntradaPeca(string Id, string Ref, string Design, decimal Qtt, string Quem, string Hora);
```

- [ ] **Step 2: Interface e Dev** (sem rede, para o modo Dev e para testes)

```csharp
// GestorManutencao.UI/Servicos/IPecasService.cs
using GestorManutencao.UI.Modelos;
namespace GestorManutencao.UI.Servicos;

public interface IPecasService
{
    Task<ResultadoPecas> PesquisarAsync(string texto, CancellationToken ct = default);
    Task<Peca?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<EntradaPeca>> EntradasDeHojeAsync(CancellationToken ct = default);
    Task<EntradaPeca> DarEntradaAsync(string refPeca, decimal qtt, CancellationToken ct = default);
    Task AnularEntradaAsync(string id, CancellationToken ct = default);
}
```

`PecasDevService`: lista fixa de 2 peças (como `RepositorioPecasMemoria.Amostra`), entradas numa lista em memória; mesma lógica de stock. `PecasApiService(HttpClient http)`: chama `GET pecas?texto=`, `GET pecas/codigo/{codigo}` (404 → `null`), `GET pecas/entradas/hoje`, `POST pecas/entradas` (`new { refPeca = ..., qtt }` **não**: o contrato da API é `{ "ref": ..., "qtt": ... }`, usar um record `PedidoEntrada(string Ref, decimal Qtt)`), `DELETE pecas/entradas/{id}`; para o token e erros, replicar exatamente o que `EquipaApiService` faz (cabeçalho Bearer vindo da sessão; `EnsureSuccessStatusCode`; mensagem do campo `erro` quando 400).

- [ ] **Step 3: Teste das regras de ecrã** — extrair para `UI/Modelos/LogicaEcras.cs` (já existe e tem testes em `LogicaEcrasTests.cs`) duas funções puras e testá-las primeiro:

```csharp
// em LogicaEcrasTests.cs
[Theory]
[InlineData("5600012345", true)]
[InlineData("Parafuso", false)]
[InlineData("", false)]
public void PareceCodigo_so_digitos_com_pelo_menos_6(string texto, bool esperado)
    => Assert.Equal(esperado, LogicaEcras.PareceCodigoDeBarras(texto));

[Fact]
public void Avisos_de_stock_usam_a_regra_unica()
    => Assert.Equal("Stock insuficiente: há 2 e pede 3.", LogicaEcras.AvisoStock(3, 2));
```

Implementar em `LogicaEcras`: `PareceCodigoDeBarras(string t) => t.Length >= 6 && t.All(char.IsDigit);` e `AvisoStock` com o mesmo corpo de `RegrasPecas.AvisoStock` (a UI não referencia o projeto da API). Correr `dotnet test --filter LogicaEcrasTests` (falha → implementar → passa).

- [ ] **Step 4: `PecaPesquisa.razor`** — componente com `[Parameter] EventCallback<Peca> OnEscolher`. Campo de texto; **Enter** com texto que `PareceCodigoDeBarras` chama `ObterPorCodigoAsync` e, se existir, escolhe logo (é o que um leitor tipo teclado faz); caso contrário pesquisa por texto (máx. 60; mostra até 6 resultados e "+N, escreva mais" se `HaMais`). Se `CampoExiste = false`, mostra "Falta criar o campo u_manut na stobs do PHC." Estrutura e classes CSS: copiar o padrão de `Pages/Equipamentos.razor` (lista de resultados com `intervencao`/`estado-lista`).

- [ ] **Step 5: `EntradaPecas.razor`** (`@page "/pecas/entrada"`) — `PecaPesquisa`; ao escolher mostra `Ref Design · stock X · local`, quantidade (defeito 1, `<input type="number" min="0.001" step="any">`), botões **Dar entrada** (Enter confirma) e **Cancelar**; depois da entrada limpa a peça, mostra "Entrada dada: …" e relê a lista **Entradas de hoje** (hora, ref, peça, qtd, quem, botão anular com `ConfirmaSaida`/confirmação simples). Offline: se `Rede.Ligada` for falso, o botão fica desativado com o texto "Sem ligação: a entrada de peças precisa de rede." (usar `IRedeEstado`, como `GestaoSoOnlineService`).

- [ ] **Step 6: Registos e menu** — `MauiProgram.cs`: ramo API `builder.Services.AddSingleton<IPecasService>(new PecasApiService(http));` junto da linha 93; ramo Dev `builder.Services.AddSingleton<IPecasService, PecasDevService>();` junto da linha 118. Em `Inicio.razor`, acrescentar ao lado dos mosaicos existentes: `<Mosaico Icone="package" Titulo="Entrada de peças" Href="pecas/entrada" Descricao="Dar entrada de material no armazém" />`.

- [ ] **Step 7: Verificar** — `dotnet build GestorManutencao.slnx` e `dotnet test GestorManutencao.Tests` verdes; arrancar a app em modo Dev (Windows) e confirmar à mão: pesquisar "rol", escolher, dar entrada de 3, ver o stock subir e a linha em "Entradas de hoje", anular. Registar o resultado (não afirmar sem ter visto).

- [ ] **Step 8: Commit** (pedir ao `sc-git`): `Ecrã de entrada de peças`.

---

### Task 5: Leitura de etiquetas (câmara no telemóvel, campo escondido no PDA)

**Files:**
- Create: `GestorManutencao.UI/Servicos/Leitura.cs` (`ModoLeitura`, `LogicaLeitura`, `IPreferenciasLeitura`, `IDetetorLeitor`, `ILeitorCamara`), `GestorManutencao.UI/Componentes/LeitorEtiquetas.razor`, `GestorManutencao.UI/Pages/DefinicaoLeitor.razor` (`@page "/leitor"`), `GestorManutencao.UI/wwwroot/js/leitor.js` (manter o campo escondido focado)
- Create (App): `GestorManutencao.App/Platforms/Android/DetetorLeitorAndroid.cs`, `GestorManutencao.App/PaginaLeituraCamara.xaml(.cs)` (página nativa com `CameraBarcodeReaderView`), `GestorManutencao.App/LeitorCamaraMaui.cs`, `GestorManutencao.App/PreferenciasLeituraMaui.cs`
- Modify: `GestorManutencao.App/GestorManutencao.App.csproj` (PackageReference `ZXing.Net.Maui.Controls`), `GestorManutencao.App/MauiProgram.cs` (`.UseBarcodeReader()` e registos), `GestorManutencao.UI/Componentes/PecaPesquisa.razor` (usa `LeitorEtiquetas`), `GestorManutencao.UI/Pages/Inicio.razor` (mosaico "Leitor"), `docs/DEPENDENCIES.md`
- Test: `GestorManutencao.Tests/LeituraTests.cs`

**Interfaces:**
- Produces: `enum ModoLeitura { Automatico, Camara, Leitor }`; `LogicaLeitura.NormalizarCodigo(string?) : string` (tira CR/LF/espaços e caracteres de controlo); `LogicaLeitura.PareceLeitorIndustrial(string fabricante, string modelo, IEnumerable<string> pacotes) : bool`; `LogicaLeitura.ModoEfetivo(ModoLeitura escolhido, bool pareceLeitor) : ModoLeitura` (nunca devolve `Automatico`: `Automatico` → `Leitor` se `pareceLeitor`, senão `Camara`); `IPreferenciasLeitura { ModoLeitura Modo { get; set; } }`; `IDetetorLeitor { bool PareceLeitorIndustrial { get; } string Descricao { get; } }`; `ILeitorCamara { Task<string?> LerAsync(CancellationToken ct = default); }`; componente `LeitorEtiquetas` com `[Parameter] EventCallback<string> OnLido`.

Regras:
1. **Leitor (PDA)**: `<input>` escondido (`inputmode="none"`, fora do ecrã, não `display:none`) **sempre focado**; volta a focar-se ao perder o foco e ao mudar de página (exceto quando outro campo de texto visível tem o foco por ação do utilizador). Enter termina a leitura: `OnLido(LogicaLeitura.NormalizarCodigo(valor))` e limpa o campo. Serve também o leitor tipo teclado no Windows.
2. **Câmara (telemóvel)**: botão "Ler com a câmara" abre `ILeitorCamara.LerAsync()` (página nativa com ZXing, formatos QR, EAN-13, EAN-8, Code 128, Code 39, DataMatrix; fecha à primeira leitura; botão cancelar) e devolve o texto lido ou `null`.
3. **Automático**: decide por `IDetetorLeitor` (heurística fabricante/modelo/pacotes: Zebra/Symbol/Motorola, Honeywell/Intermec, Datalogic, Urovo, Chainway, Newland, Unitech, Cipherlab, Point Mobile, Sunmi industrial, pacote `com.symbol.datawedge`, `com.honeywell.decode.service`, `com.urovo.*`). Windows: sempre `Leitor`.
4. **Ecrã `/leitor`**: três opções (Automático, Câmara, Leitor), mostra o modo efetivo e o que o detetor viu (`Descricao`: fabricante e modelo), e uma caixa "Teste de leitura" que mostra o último código lido pelo modo ativo. Guarda a escolha em `Preferences`.
5. Os dois modos entregam texto igual ao `ObterPorCodigoAsync` (a ficha e a entrada de peças não sabem qual é o modo).

- [ ] **Step 1: Verificar a biblioteca antes de escrever código** — confirmar no NuGet e na documentação oficial (usar `context7`) a versão estável de `ZXing.Net.Maui.Controls` compatível com .NET 10 / MAUI 10 e o uso de `UseBarcodeReader()` e `CameraBarcodeReaderView`. Se não houver versão compatível, **parar** e reportar `BLOQUEIO` com a alternativa (ZXing em JavaScript); não inventar versão.
- [ ] **Step 2: Testes das regras puras (falham primeiro)** em `LeituraTests.cs`: `NormalizarCodigo` ("  5600
" → "5600", null → ""), `ModoEfetivo` (as 6 combinações), `PareceLeitorIndustrial` (Zebra TC21 → true; "samsung SM-A546B" → false; pacote `com.symbol.datawedge` → true; fabricante em maiúsculas ou minúsculas igual).
- [ ] **Step 3: Implementar** `Leitura.cs`, o componente, o `leitor.js`, a página `/leitor` e o mosaico. A implementação nativa (Android/Windows) segue as interfaces; `DetetorLeitorAndroid` usa `Android.OS.Build.Manufacturer`/`Model` e `PackageManager.GetInstalledPackages` só para as strings listadas.
- [ ] **Step 4: Integrar** no `PecaPesquisa` (e portanto na Entrada de peças e no material da ficha): o `OnLido` chama `ObterPorCodigoAsync`; se não existir, mostra "Código não encontrado: X" e deixa escrever.
- [ ] **Step 5: Correr** `dotnet test GestorManutencao.Tests --filter LeituraTests` e `dotnet build GestorManutencao.slnx`. Verificação em aparelho (telemóvel e PDA) é manual e fica por registar pelo utilizador; **não afirmar que foi visto**.
- [ ] **Step 6: Documentar** a dependência em `docs/DEPENDENCIES.md` (nome, versão, licença, motivo) e commit `Leitura de etiquetas: câmara e leitor`.

Nota de git: `GestorManutencao.App.csproj` já tem alterações do utilizador por commitar (subida de versão para 1.18). Esta task acrescenta a PackageReference ao mesmo ficheiro: **não fazer `git add` desse ficheiro**; deixar a alteração na árvore de trabalho e referi-la no relatório.

### Task 6: UI — peça no material da ficha (online e offline)

**Files:**
- Modify: `GestorManutencao.UI/Modelos/Intervencoes.cs:107` (`Material` + `Ref`, `SaidaDada`), `Servicos/IIntervencoesService.cs:34-37,60` e `IIntervencoesRemoto`, `Servicos/IntervencoesApiService.cs:110-128,267`, `Offline/OfflineIntervencoesService.cs:279-340,663`, `Offline/SincronizadorFila.cs:182-232`, `Offline/` (record `CargaMaterial`), `Pages/DetalheIntervencao.razor:168-235,409-519`, `GestorManutencao.Tests/OfflineFalsos.cs`
- Test: `GestorManutencao.Tests/MaterialPecasOfflineTests.cs`

**Interfaces:**
- Consumes: `IPecasService`, `PecaPesquisa`, `LogicaEcras.AvisoStock` (Task 4); rota `POST /intervencoes/{id}/materiais` com `ref` (Task 3).
- Produces: `Material(string Id, string Design, decimal Qtt, decimal Epv, decimal Custo, string Ref = "", bool SaidaDada = false)`; `AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, string refPeca = "", CancellationToken ct = default)` e `EditarMaterialAsync(..., string refPeca = "", CancellationToken ct = default)`; `CargaMaterial(MaterialId, Design, Qtt, Epv, Ref)` com `Ref` por defeito `""` (compatível com filas já gravadas).

- [ ] **Step 1: Teste offline (falha)** — seguir `OfflineTests.cs` (usa `OfflineFalsos`): adicionar material com `refPeca: "P001"` offline → a operação na fila leva `Ref = "P001"`; ao sincronizar, o remoto falso recebe `refPeca = "P001"`; apagar um material local ainda não sincronizado cancela a operação sem chamar o remoto. Escrever os 3 testes com os mesmos helpers dos testes de material existentes (procurar `AdicionarMaterialAsync` em `OfflineTests.cs` e copiar a montagem).

- [ ] **Step 2: Correr e ver falhar.**

- [ ] **Step 3: Modelo e contratos** — alterar `Material`; nas interfaces e implementações (`IntervencoesApiService`, `OfflineIntervencoesService`, `IntervencoesMemoria` em `IIntervencoesService.cs:264-284,471-480` e os `NotSupportedException` das linhas 566-584, `OfflineFalsos`) acrescentar `string refPeca = ""` antes de `CancellationToken`, e corrigir todas as chamadas posicionais que passavam `ct` na 5.ª posição (procurar `AdicionarMaterialAsync(` e `EditarMaterialAsync(` em `GestorManutencao.UI`, `.App` e `.Tests`). `IntervencoesApiService.AdicionarMaterialAsync` envia `new { design, qtt, epv, @ref = refPeca }`. `CargaMaterial` ganha `string Ref = ""`; `EnviarMaterialAsync` passa `carga.Ref`.

- [ ] **Step 4: Offline** — em `OfflineIntervencoesService.AdicionarMaterialAsync`, o `Material` local e a `CargaMaterial` levam `refPeca`; `SaidaDada = false` até sincronizar (o ecrã mostra "Saída por dar (sem ligação)"). Editar continua **só online** (já é: `SoOnlineAsync`).

- [ ] **Step 5: Formulário da ficha** — em `DetalheIntervencao.razor`, por cima do campo de texto "Material" do formulário de juntar (e do de editar): `PecaPesquisa` com `OnEscolher="EscolherPeca"`. `EscolherPeca(Peca p)` define `_pecaRef = p.Ref; _material = p.Design (cortado a 35); _epv = p.Preco;` e `_avisoStock = LogicaEcras.AvisoStock(_qtt ?? 1, p.Stock)` (recalculado quando muda a quantidade); mostra o aviso em `<p class="campo-aviso" role="status">`, **sem bloquear** o botão. Texto livre continua possível (material sem peça, `_pecaRef = ""`); se o utilizador limpa a peça, `_pecaRef = ""`. `AdicionarMaterialAsync()` e `GuardarMaterialEditadoAsync()` passam `_pecaRef`/`_eRef`. Na lista de materiais, cada linha com `m.Ref != ""` mostra `m.Ref` e "Saída dada" ou "Saída por dar" com botão **Repetir saída** (chama `Intervencoes.RepetirSaidaAsync(Id, m.Id)` → `POST …/saida`; acrescentar o método às interfaces e implementações como `SoOnlineAsync`).

- [ ] **Step 6: Correr** — `dotnet test GestorManutencao.Tests` completo verde; `dotnet build GestorManutencao.slnx`.

- [ ] **Step 7: Verificação manual em Dev** — abrir uma ficha em curso, juntar material com peça de stock 10 e quantidade 4 (stock passa a 6), editar para 7 (3), apagar (10); juntar a peça sem stock e ver o aviso sem bloqueio. Registar o que se viu.

- [ ] **Step 8: Commit** (pedir ao `sc-git`): `Material da ficha escolhe a peça e dá saída`.

---

### Task 7: Base de testes real, documentação e entrega

**Files:**
- Create: `docs/pecas-armazem.md`, `docs/adr/0002-pecas-por-dossier-phc.md`, `docs/sql/pecas-verificacao.sql` (só SELECT)
- Modify: `docs/DEPENDENCIES.md` só se existir e algo tiver mudado (não há dependências novas)

- [ ] **Step 1: Pedir ao utilizador a connection string de TESTES no `appsettings.Development.json`** (ou `appsettings.json` local fora do git) — nunca no chat nem no repositório.

- [ ] **Step 2: Script de verificação só leitura** (`docs/sql/pecas-verificacao.sql`), executado pelo agente `sc-leitor-bd`:

```sql
SELECT DB_NAME() AS base;                                                     -- confirmar que é de TESTES com o utilizador
SELECT ndos, nmdos FROM ts WHERE ndos IN (63, 64);                            -- esperado: 2 linhas
SELECT no, nome FROM sz WHERE no = 104;                                       -- armazém de manutenção
SELECT COL_LENGTH('stobs', 'u_manut') AS campo_u_manut;                       -- esperado: não NULL
SELECT COL_LENGTH('bi', 'lobs') AS lobs_tamanho;                              -- esperado: >= 25
SELECT COL_LENGTH('bo', 'obranome') AS obranome_tamanho;
SELECT COUNT(*) AS pecas FROM stobs WHERE u_manut = 1;                        -- esperado: > 0
SELECT TOP 5 ref, design, codigo FROM st s WHERE EXISTS (SELECT 1 FROM stobs o WHERE o.ref = s.ref AND o.u_manut = 1);
```

Se algo falhar → `BLOQUEIO:` (criar `u_manut`, dossiers ou armazém é no PHC, por pessoa; o Claude Code não cria tabelas nem campos).

- [ ] **Step 3: Teste ponta a ponta na base de testes** (só depois do nome confirmado): 1) dar entrada de 3 unidades de uma peça pelo ecrã; conferir por SELECT que `bo` ndos 64 de hoje tem 1 linha e que `sa.stock` do armazém 104 subiu 3; 2) juntar material com essa peça numa ficha de teste; conferir `bi` ndos 63 com `lobs` = stamp do material e `sa.stock` desceu; 3) apagar o material e a entrada; conferir que os dossiers desapareceram e o stock voltou ao inicial. Registar cada resultado com os números reais.

- [ ] **Step 4: Documentar** — `docs/pecas-armazem.md` (circuito, dossiers, pré-requisitos PHC, limites, o que falta: câmara/QR/localizações) e o ADR 0002 (decisão: dossiers nativos 63/64 em vez de tabelas `u_` novas, por paridade com a LandeiroMaui; consequência: o stock é o do PHC, sem segunda fonte).

- [ ] **Step 5: Verificações finais** — `dotnet test GestorManutencao.Tests` verde; `C:/_SigmaRepos/Claude-The-ALLKNOWING/tools/verificar-segredos.ps1 -Caminho C:\_SigmaRepos\GestorManutencao` sem achados; revisão por `sc-revisor` (envolve SQL e >300 linhas) com o diff.

- [ ] **Step 6: Commit e entrega** (via `sc-git`, com confirmação do utilizador; push só com `GATE:` aprovado).

---

## Autorrevisão

- **Cobertura:** entrada com leitura/escrita de código, quantidade, anulação, lista do dia (Tasks 2, 4); leitura de etiquetas por câmara e por campo escondido (Task 5); saída automática na ficha, editar, apagar, repetir, aviso de stock sem bloqueio (Tasks 3, 6); offline (Task 6); verificação por leitura e escrita só em teste (Task 6). Localizações e consulta ficam fora, declarado.
- **Tipos:** `PecaDto`/`Peca`, `EntradaPecaDto`/`EntradaPeca`, `PedidoEntrada(Ref, Qtt)`, `MaterialDto`/`Material` com `Ref` e `SaidaDada` coincidem entre tarefas; `IRepositorioPecas` tem os mesmos métodos na interface, na memória e no SQL.
- **Pontos de atenção:** (1) `DELETE` de material tem de repor a saída se a ficha recusar (Step 6 da Task 3); (2) `EnriquecerMateriaisAsync` só entra na Task 3; (3) o ecrã Dev e os testes dependem da amostra `P001`/`P002`; (4) a Task 4 e a 5 mexem em Razor que só se confirma correndo a app.
