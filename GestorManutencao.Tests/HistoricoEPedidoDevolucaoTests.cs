using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.Api;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using EstadoApi = GestorManutencao.Api.EstadoIntervencao;
using EstadoUi = GestorManutencao.UI.Modelos.EstadoIntervencao;
using PerfilApi = GestorManutencao.Api.Perfil;
using PerfilUi = GestorManutencao.UI.Modelos.Perfil;

namespace GestorManutencao.Tests;

// QA J10 (histórico do técnico) e G01 (pedir devolução).
public class PedidoDevolucaoApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
        [
            (new UtilizadorApp("ana", "Ana Silva", PerfilApi.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
            (new UtilizadorApp("chefe", "Chefe de Turno", PerfilApi.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918"))
        ])))));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private static Task<List<IntervencaoDto>> Lista(HttpClient c, string url) => c.GetFromJsonAsync<List<IntervencaoDto>>(url)!;

    [Fact]
    public async Task Tecnico_pede_devolucao_o_estado_nao_muda_e_o_supervisor_ve_o_pedido()
    {
        var ana = await ComoAsync("482913");
        var chefe = await ComoAsync("603918");

        var r = await ana.PostAsJsonAsync("/intervencoes/F3/pedir-devolucao", new PedidoDevolucaoTecnico("  Esqueci-me das horas  "));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var ficha = (await r.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(EstadoApi.Concluida, ficha.Estado);
        Assert.Equal("Esqueci-me das horas", ficha.PedidoDevolucao);
        Assert.Equal("Esqueci-me das horas", (await ana.GetFromJsonAsync<IntervencaoDto>("/intervencoes/F3"))!.PedidoDevolucao);
        Assert.Equal("Esqueci-me das horas", Assert.Single(await Lista(chefe, "/validacao"), i => i.Id == "F3").PedidoDevolucao);
    }

    [Fact]
    public async Task Pedido_exige_motivo_ficha_concluida_e_ser_do_tecnico()
    {
        var ana = await ComoAsync("482913");
        var chefe = await ComoAsync("603918");

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/F3/pedir-devolucao", new PedidoDevolucaoTecnico(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/F3/pedir-devolucao", new PedidoDevolucaoTecnico(new string('x', 255)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/intervencoes/F1/pedir-devolucao", new PedidoDevolucaoTecnico("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PostAsJsonAsync("/intervencoes/nao-existe/pedir-devolucao", new PedidoDevolucaoTecnico("x"))).StatusCode);
        // A ficha F3 é da Ana: o chefe (outro número de técnico) não pede em nome dela.
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsJsonAsync("/intervencoes/F3/pedir-devolucao", new PedidoDevolucaoTecnico("x"))).StatusCode);
        Assert.Null(Assert.Single(await Lista(chefe, "/validacao"), i => i.Id == "F3").PedidoDevolucao);
    }

    [Fact]
    public async Task Devolver_e_validar_limpam_o_pedido()
    {
        var ana = await ComoAsync("482913");
        var chefe = await ComoAsync("603918");
        var repo = _fabrica.Services.GetRequiredService<IRepositorioPedidosDevolucao>();
        await ana.PostAsJsonAsync("/intervencoes/F3/pedir-devolucao", new PedidoDevolucaoTecnico("Falta o tempo"));
        Assert.NotNull(repo.Obter("F3"));

        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync("/validacao/F3/devolver", new PedidoDevolucao("Corrija o tempo"))).StatusCode);
        Assert.Null(repo.Obter("F3"));

        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/intervencoes/F3/concluir", new { feito = "Feito", inicio = "08:00", fim = "09:00" })).StatusCode);
        Assert.Null(Assert.Single(await Lista(chefe, "/validacao"), i => i.Id == "F3").PedidoDevolucao);
        repo.Registar("F3", 101, "outra vez");
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsync("/validacao/F3", null)).StatusCode);
        Assert.Null(repo.Obter("F3"));
    }

    [Fact]
    public void Pedido_persiste_em_ficheiro_e_repetir_substitui_o_motivo()
    {
        var caminho = Path.Combine(Path.GetTempPath(), "pd-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var a = new RepositorioPedidosDevolucao(TimeProvider.System, caminho);
            a.Registar("F3", 101, "um");
            a.Registar("F3", 101, "dois");
            Assert.Equal("dois", new RepositorioPedidosDevolucao(TimeProvider.System, caminho).Obter("F3")!.Motivo);
            a.Remover("F3");
            Assert.Null(new RepositorioPedidosDevolucao(TimeProvider.System, caminho).Obter("F3"));
        }
        finally { File.Delete(caminho); }
    }
}

public class HistoricoEPedidoDevolucaoUiTests
{
    private static Intervencao Ficha(string id, EstadoUi estado, int diasAtras = 0) =>
        RemotoFalso.NovaFicha(id, "Equip " + id, estado, Cenario.Hoje.AddDays(-diasAtras));

    [Fact]
    public async Task Anteriores_com_rede_traz_as_fechadas_mais_recentes_primeiro_e_sem_rede_usa_a_cache()
    {
        var c = new Cenario();
        c.Semear(Ficha("A", EstadoUi.Planeada));
        c.Semear(Ficha("V", EstadoUi.Validada, 5));
        c.Semear(Ficha("C", EstadoUi.Concluida, 1));
        c.Semear(Ficha("X", EstadoUi.Cancelada, 9));

        Assert.Equal(["C", "V", "X"], (await c.Svc.ListarAnterioresAsync()).Select(f => f.Id));

        c.Remoto.Rede = false;
        Assert.Equal(["C", "V", "X"], (await c.Svc.ListarAnterioresAsync()).Select(f => f.Id));
        Assert.True(c.Svc.AMostrarCache);
    }

    [Fact]
    public async Task Pedir_devolucao_guarda_o_pedido_na_ficha_e_sem_rede_recusa()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("C1", "Balança", EstadoUi.Concluida);

        var f = await c.Svc.PedirDevolucaoAsync("C1", " Falta o tempo ");

        Assert.Equal(EstadoUi.Concluida, f.Estado);
        Assert.Equal("Falta o tempo", f.PedidoDevolucao);
        Assert.Equal("Falta o tempo", (await c.Svc.ObterAsync("C1"))!.PedidoDevolucao);

        c.Remoto.Rede = false;
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.PedirDevolucaoAsync("C1", "outra vez"));
        Assert.Equal(MensagensIntervencao.SemRede, e.Message);
    }

    [Fact]
    public void Supervisor_recebe_aviso_do_pedido_de_devolucao_que_abre_a_validacao()
    {
        var ficha = Ficha("D", EstadoUi.Concluida) with { Tecnico = "Ana", PedidoDevolucao = "Falta o tempo" };
        var avisos = CentroAvisos.Gerar(PerfilUi.Supervisor, 0, [ficha], new DateTime(2026, 11, 9, 10, 0, 0));

        var a = Assert.Single(avisos, x => x.Tipo == TipoAviso.PedidoDevolucao);
        Assert.Contains("Falta o tempo", a.Texto);
        Assert.Equal("validacao", a.Destino(PerfilUi.Supervisor));
    }
}
