using System.Net.Http.Headers;
using RelatorioGeracao = GestorManutencao.UI.Modelos.RelatorioGeracao;
using Perfil = GestorManutencao.Api.Perfil;
using GestorManutencao.UI.Servicos;
using System.Net.Http.Json;
using GestorManutencao.Api;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

// Geração diária das planeadas ao abrir o ecrã (no máximo uma vez por dia, por dispositivo e utilizador).
public class GeracaoDiariaTests
{
    private static GeracaoDiaria Criar(TempoFalso tempo, out ArmazemGeracaoDiariaMemoria armazem)
    {
        armazem = new ArmazemGeracaoDiariaMemoria();
        return new GeracaoDiaria(armazem, tempo);
    }

    private static Func<Task<RelatorioGeracao>> Gera(Action? aoGerar = null, int criadas = 2, int erros = 0) => () =>
    {
        aoGerar?.Invoke();
        return Task.FromResult(new RelatorioGeracao(criadas, 0, 0, erros));
    };

    [Fact]
    public async Task Gera_uma_vez_por_dia_e_volta_a_gerar_no_dia_seguinte()
    {
        var tempo = new TempoFalso();
        var g = Criar(tempo, out _);
        var chamadas = 0;

        var primeira = await g.ExecutarAsync("Chefe", true, Gera(() => chamadas++));
        var segunda = await g.ExecutarAsync("Chefe", true, Gera(() => chamadas++));
        tempo.Agora = tempo.Agora.AddDays(1);
        var amanha = await g.ExecutarAsync("Chefe", true, Gera(() => chamadas++));

        Assert.Equal(EstadoGeracaoDiaria.Gerou, primeira.Estado);
        Assert.Equal(2, primeira.Criadas);
        Assert.Equal(EstadoGeracaoDiaria.JaFeitaHoje, segunda.Estado);
        Assert.Equal(EstadoGeracaoDiaria.Gerou, amanha.Estado);
        Assert.Equal(2, chamadas);
    }

    [Fact]
    public async Task O_dia_e_por_utilizador()
    {
        var g = Criar(new TempoFalso(), out _);

        await g.ExecutarAsync("Chefe", true, Gera());

        Assert.True(await g.JaFoiHojeAsync("chefe "));
        Assert.False(await g.JaFoiHojeAsync("Outro"));
        Assert.Equal(EstadoGeracaoDiaria.Gerou, (await g.ExecutarAsync("Outro", true, Gera())).Estado);
    }

    [Fact]
    public async Task Sem_rede_ou_com_falha_nao_marca_o_dia_e_so_deixa_um_aviso()
    {
        var g = Criar(new TempoFalso(), out _);

        var falhou = await g.ExecutarAsync("Chefe", true, () => throw new HttpRequestException("sem rede"));
        var falhouDepois = await g.ExecutarAsync("Chefe", true, () => Task.FromException<RelatorioGeracao>(new InvalidOperationException(MensagensIntervencao.SemRede)));

        Assert.Equal(EstadoGeracaoDiaria.Falhou, falhou.Estado);
        Assert.NotNull(falhou.Aviso);
        Assert.Equal(EstadoGeracaoDiaria.Falhou, falhouDepois.Estado);
        Assert.False(await g.JaFoiHojeAsync("Chefe"));
        Assert.Equal(EstadoGeracaoDiaria.Gerou, (await g.ExecutarAsync("Chefe", true, Gera())).Estado);
    }

    [Fact]
    public async Task Com_erros_volta_a_tentar_na_proxima_abertura()
    {
        var g = Criar(new TempoFalso(), out _);
        var chamadas = 0;

        var r = await g.ExecutarAsync("Chefe", true, Gera(() => chamadas++, criadas: 1, erros: 2));
        await g.ExecutarAsync("Chefe", true, Gera(() => chamadas++, criadas: 1, erros: 0));

        Assert.Equal(EstadoGeracaoDiaria.ComErros, r.Estado);
        Assert.Contains("2", r.Aviso);
        Assert.Equal(2, chamadas);
    }

    [Fact]
    public async Task Com_a_configuracao_desligada_nao_gera()
    {
        var g = Criar(new TempoFalso(), out _);
        var chamadas = 0;

        var r = await g.ExecutarAsync("Chefe", false, Gera(() => chamadas++));

        Assert.Equal(EstadoGeracaoDiaria.Desligada, r.Estado);
        Assert.Equal(0, chamadas);
        Assert.False(await g.JaFoiHojeAsync("Chefe"));
    }

    [Fact]
    public async Task O_dia_guarda_se_na_cache_local_sem_rede()
    {
        var cache = new CacheLocalMemoria();
        var g = new GeracaoDiaria(new ArmazemGeracaoDiariaCache(cache), new TempoFalso());

        await g.ExecutarAsync("Chefe", true, Gera());

        Assert.Equal("2026-11-09", await cache.LerMetaAsync(GeracaoDiaria.Chave("Chefe")));
        // Outro serviço sobre a mesma cache (a app reaberta) sabe que já gerou hoje, sem chamar a rede.
        var reaberta = new GeracaoDiaria(new ArmazemGeracaoDiariaCache(cache), new TempoFalso());
        Assert.Equal(EstadoGeracaoDiaria.JaFeitaHoje, (await reaberta.ExecutarAsync("Chefe", true, () => throw new HttpRequestException())).Estado);
    }
}

// Os contadores do menu e o KPI "fora de prazo" do ecrã de planeadas usam a mesma regra.
public class ContadoresIgualAoKpiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!"; // sigma-ok
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
        [
            (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918"))
        ])))));

    public void Dispose() => _fabrica.Dispose();

    [Fact]
    public async Task Contador_da_api_e_kpi_da_ui_dao_o_mesmo_nas_planeadas_atrasadas()
    {
        var c = _fabrica.CreateClient();
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin("603918"))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        // Uma planeada com a data de ontem e a F1, que está para hoje mas fora de prazo (última feita + 1 mês já passou).
        await c.PutAsJsonAsync("/intervencoes/F4", new PedidoFicha(Data: DateOnly.FromDateTime(DateTime.Today).AddDays(-1)));

        var contadores = await c.GetFromJsonAsync<ContadoresMenuDto>("/intervencoes/contadores");
        var planeadas = await c.GetFromJsonAsync<List<Intervencao>>("/planeadas");
        var kpi = VistaPlaneadas.Contar(planeadas!, DateOnly.FromDateTime(DateTime.Today), 7);

        Assert.Equal(2, contadores!.PlaneadasAtrasadas);
        Assert.Equal(kpi.ForaDePrazo, contadores.PlaneadasAtrasadas);
        Assert.Equal(1, contadores.PorValidar);
    }
}
