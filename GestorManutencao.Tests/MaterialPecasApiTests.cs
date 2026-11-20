using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

// Material da ficha ligado a uma peça: saída automática, anulação ao editar/apagar e repetição da saída.
public class MaterialPecasApiTests : IDisposable
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
                (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918"))
            ])));
            s.Replace(ServiceDescriptor.Singleton<IRepositorioIntervencoes>(sp =>
                new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false))));
            s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(new RepositorioPecasMemoria(
            [
                new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
                new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
            ])));
        }));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    // Ficha F1 em curso da Ana.
    private async Task<HttpClient> ComoAnaAsync()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
        return c;
    }

    private static async Task<decimal> StockAsync(HttpClient c, string codigo = "P001") =>
        (await c.GetFromJsonAsync<PecaDto>($"/pecas/codigo/{codigo}"))!.Disponivel;

    private static async Task<MaterialDto> JuntarAsync(HttpClient c, string design, decimal qtt, decimal epv, string? refPeca = null) =>
        (await (await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial(design, qtt, epv, refPeca))).Content.ReadFromJsonAsync<MaterialDto>())!;

    [Fact]
    public async Task Material_com_peca_da_saida_e_apagar_devolve_o_stock()
    {
        var c = await ComoAnaAsync();
        var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Parafuso M8", 4, 0.12m, "P001"));
        var m = await r.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.True(m!.SaidaDada);
        Assert.Equal("P001", m.Ref);
        Assert.Equal(6, await StockAsync(c));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);
        Assert.Equal(10, await StockAsync(c));
    }

    [Fact]
    public async Task Editar_a_quantidade_desfaz_a_saida_antiga_e_da_a_nova()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        var e = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 7, 0.12m, "P001"));
        Assert.True((await e.Content.ReadFromJsonAsync<MaterialDto>())!.SaidaDada);
        Assert.Equal(3, await StockAsync(c));
    }

    [Fact]
    public async Task Editar_sem_referencia_desliga_a_peca_e_devolve_o_stock()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        var e = await (await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 4, 0.12m))).Content.ReadFromJsonAsync<MaterialDto>();
        Assert.False(e!.SaidaDada);
        Assert.Equal("", e.Ref);
        Assert.Equal(10, await StockAsync(c));
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
        Assert.Equal([false, true], lista!.Select(x => x.SaidaDada).ToArray());
        Assert.Equal(8, await StockAsync(c));
    }

    [Fact]
    public async Task Referencia_inexistente_recusa_com_400_e_nao_cria_material()
    {
        var c = await ComoAnaAsync();
        var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("X", 1, 1m, "NAOEXISTE"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("pedido_invalido", await r.Content.ReadAsStringAsync());
        Assert.Empty((await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais"))!);
    }

    [Fact]
    public async Task Referencia_inexistente_ao_editar_recusa_e_nao_mexe_na_saida()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        var r = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 9, 0.12m, "NAOEXISTE"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(6, await StockAsync(c));
    }

    [Fact]
    public async Task Ficha_concluida_recusa_apagar_e_o_stock_nao_muda()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);

        Assert.Equal(6, await StockAsync(c));
        var lista = await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais");
        Assert.True(Assert.Single(lista!).SaidaDada);
    }

    [Fact]
    public async Task Apagar_material_inexistente_ou_de_ficha_alheia_da_404()
    {
        var c = await ComoAnaAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/intervencoes/F1/materiais/NAOEXISTE")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/intervencoes/F2/materiais/X")).StatusCode);
    }

    [Fact]
    public async Task Supervisor_apaga_material_de_ficha_alheia_e_o_stock_volta()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        var m = await JuntarAsync(ana, "Parafuso M8", 4, 0.12m, "P001");

        Assert.Equal(HttpStatusCode.NoContent, (await chefe.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);
        Assert.Equal(10, await StockAsync(ana));
    }

    [Fact]
    public async Task Repetir_saida_de_material_ja_com_saida_da_409_e_nao_duplica()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/intervencoes/F1/materiais/{m.Id}/saida", null)).StatusCode);
        Assert.Equal(6, await StockAsync(c));
    }

    [Fact]
    public async Task Repetir_saida_sem_peca_ligada_exige_a_referencia()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/intervencoes/F1/materiais/{m.Id}/saida", null)).StatusCode);
        var r = await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida("P001"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var ok = await r.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.True(ok!.SaidaDada);
        Assert.Equal("P001", ok.Ref);
        Assert.Equal(6, await StockAsync(c));
    }

    [Fact]
    public async Task Repetir_saida_segue_o_acesso_de_edicao_da_ficha()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F2/materiais/X/saida", new PedidoSaida("P001"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F1/materiais/NAOEXISTE/saida", new PedidoSaida("P001"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);
        // Ficha já não editável: responde como o POST do material (409).
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida("P001"))).StatusCode);
        Assert.Equal(10, await StockAsync(c));
    }

    // Repositório de peças com falhas ligáveis a meio do teste; o resto é o de memória.
    private sealed class PecasInstaveis(RepositorioPecasMemoria memoria) : IRepositorioPecas
    {
        public bool SaidaFalha, SaidaIndisponivel, AnularFalha, EnriquecerIndisponivel, FalharSaidaAposAnular;

        public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct) => memoria.ListarAsync(texto, maximo, ct);
        public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct) => memoria.ObterPorCodigoAsync(codigo, ct);
        public Task<ResultadoMovimento> DarSaidaAsync(string fichaId, string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct)
        {
            if (SaidaIndisponivel) throw new RepositorioIndisponivelException();
            return SaidaFalha ? Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroOcupado)) : memoria.DarSaidaAsync(fichaId, materialId, refPeca, qtt, iniciais, ct);
        }
        public Task<ResultadoMovimento> AbrirConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => memoria.AbrirConferenciaAsync(fichaId, iniciais, ct);
        public Task<ResultadoMovimento> ConfirmarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => memoria.ConfirmarConferenciaAsync(fichaId, iniciais, ct);
        public Task<ResultadoMovimento> CancelarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => memoria.CancelarConferenciaAsync(fichaId, iniciais, ct);
        public Task<IReadOnlyDictionary<string, decimal>> EmConferenciaPorPecaAsync(IReadOnlyCollection<string> refs, string? excetoFichaId, CancellationToken ct) => memoria.EmConferenciaPorPecaAsync(refs, excetoFichaId, ct);
        public async Task<ResultadoMovimento> AnularSaidaAsync(string materialId, string iniciais, CancellationToken ct)
        {
            if (AnularFalha) return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado);
            var r = await memoria.AnularSaidaAsync(materialId, iniciais, ct);
            if (FalharSaidaAposAnular) SaidaFalha = true;
            return r;
        }
        public Task<bool> LigarPecaAsync(string stampMaterial, string refPeca, string usrinis, CancellationToken ct) => memoria.LigarPecaAsync(stampMaterial, refPeca, usrinis, ct);
        public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct) =>
            EnriquecerIndisponivel ? throw new RepositorioIndisponivelException() : memoria.EnriquecerMateriaisAsync(materiais, ct);
    }

    public sealed class FlagsApagar { public bool Indisponivel; }

    // Repositório de fichas que só falha o apagar do material (indisponível); o resto delega no de memória.
    public class ProxyIntervencoes : DispatchProxy
    {
        public IRepositorioIntervencoes Real = null!;
        public FlagsApagar Flags = null!;

        protected override object? Invoke(MethodInfo? metodo, object?[]? args)
        {
            if (metodo!.Name == nameof(IRepositorioIntervencoes.ApagarMaterialAsync) && Flags.Indisponivel) throw new RepositorioIndisponivelException();
            try { return metodo.Invoke(Real, args); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }
    }

    private async Task<HttpClient> ComoAnaCom(PecasInstaveis pecas, FlagsApagar? flags = null)
    {
        var f = _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(pecas));
            if (flags is not null)
                s.Replace(ServiceDescriptor.Singleton<IRepositorioIntervencoes>(sp =>
                {
                    var proxy = DispatchProxy.Create<IRepositorioIntervencoes, ProxyIntervencoes>();
                    var p = (ProxyIntervencoes)(object)proxy;
                    p.Real = new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false));
                    p.Flags = flags;
                    return proxy;
                }));
        }));
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin("482913"))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
        return c;
    }

    private static async Task<List<MaterialDto>> ListaAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais"))!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Falha_ao_dar_saida_nao_desfaz_o_material_e_diz_saida_por_dar(bool indisponivel)
    {
        var pecas = new PecasInstaveis(new RepositorioPecasMemoria()) { SaidaFalha = !indisponivel, SaidaIndisponivel = indisponivel };
        var c = await ComoAnaCom(pecas);

        var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Parafuso M8", 4, 0.12m, "P001"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var m = await r.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.False(m!.SaidaDada);
        Assert.Equal("P001", m.Ref);
        Assert.Equal(10, await StockAsync(c));
    }

    [Fact]
    public async Task Depois_de_falhar_a_saida_a_lista_mostra_a_ref_ligada_e_saida_por_dar()
    {
        var pecas = new PecasInstaveis(new RepositorioPecasMemoria()) { SaidaFalha = true };
        var c = await ComoAnaCom(pecas);
        await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");

        var m = Assert.Single(await ListaAsync(c));
        Assert.Equal("P001", m.Ref);
        Assert.False(m.SaidaDada);
    }

    [Fact]
    public async Task Repetir_saida_depois_de_falha_real_usa_a_ref_ligada_sem_corpo()
    {
        var pecas = new PecasInstaveis(new RepositorioPecasMemoria()) { SaidaFalha = true };
        var c = await ComoAnaCom(pecas);
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        Assert.False(m.SaidaDada);

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/intervencoes/F1/materiais/{m.Id}/saida", null)).StatusCode);
        pecas.SaidaFalha = false;
        var r = await c.PostAsync($"/intervencoes/F1/materiais/{m.Id}/saida", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var ok = await r.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.True(ok!.SaidaDada);
        Assert.Equal("P001", ok.Ref);
        Assert.Equal(6, await StockAsync(c));
        Assert.True(Assert.Single(await ListaAsync(c)).SaidaDada);
    }

    [Fact]
    public async Task Repetir_saida_com_ref_no_corpo_prevalece_sobre_a_ligada()
    {
        var pecas = new PecasInstaveis(new RepositorioPecasMemoria()) { SaidaFalha = true };
        var c = await ComoAnaCom(pecas);
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        pecas.SaidaFalha = false;

        var r = await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida("P002"));

        Assert.Equal("P002", (await r.Content.ReadFromJsonAsync<MaterialDto>())!.Ref);
        Assert.Equal(10, await StockAsync(c));
        Assert.Equal(-4, await StockAsync(c, "P002"));
    }

    [Fact]
    public async Task Editar_com_falha_na_nova_saida_grava_o_material_e_deixa_a_peca_por_dar()
    {
        var pecas = new PecasInstaveis(new RepositorioPecasMemoria());
        var c = await ComoAnaCom(pecas);
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        pecas.SaidaFalha = true;

        var e = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 7, 0.12m, "P001"));

        Assert.Equal(HttpStatusCode.OK, e.StatusCode);
        Assert.False((await e.Content.ReadFromJsonAsync<MaterialDto>())!.SaidaDada);
        var lido = Assert.Single(await ListaAsync(c));
        Assert.Equal(7, lido.Qtt);
        Assert.Equal("P001", lido.Ref);
        Assert.False(lido.SaidaDada);
        Assert.Equal(10, await StockAsync(c));
    }

    [Fact]
    public async Task Editar_em_ficha_concluida_da_409_e_o_stock_nao_muda()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);

        var r = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 7, 0.12m, "P001"));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(6, await StockAsync(c));
        var lido = Assert.Single(await ListaAsync(c));
        Assert.Equal(4, lido.Qtt);
        Assert.True(lido.SaidaDada);
    }

    [Fact]
    public async Task Editar_com_anulacao_recusada_da_409_e_nao_edita_o_material()
    {
        var pecas = new PecasInstaveis(new RepositorioPecasMemoria());
        var c = await ComoAnaCom(pecas);
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        pecas.AnularFalha = true;

        var r = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 7, 0.12m, "P001"));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("erro", await r.Content.ReadAsStringAsync());
        var lido = Assert.Single(await ListaAsync(c));
        Assert.Equal(4, lido.Qtt);
        Assert.True(lido.SaidaDada);
        Assert.Equal(6, await StockAsync(c));
    }

    [Fact]
    public async Task Apagar_com_o_apagar_a_falhar_repoe_a_saida_e_responde_503()
    {
        var flags = new FlagsApagar();
        var c = await ComoAnaCom(new PecasInstaveis(new RepositorioPecasMemoria()), flags);
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        flags.Indisponivel = true;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);

        Assert.Equal(6, await StockAsync(c));
        var lido = Assert.Single(await ListaAsync(c));
        Assert.True(lido.SaidaDada);
        Assert.Equal("P001", lido.Ref);
    }

    [Fact]
    public async Task Apagar_com_reposicao_a_falhar_da_409_com_aviso_de_stock_desfasado()
    {
        var flags = new FlagsApagar();
        var pecas = new PecasInstaveis(new RepositorioPecasMemoria()) { FalharSaidaAposAnular = true };
        var c = await ComoAnaCom(pecas, flags);
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        flags.Indisponivel = true;

        var r = await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("desfasado", await r.Content.ReadAsStringAsync());
        Assert.Single(await ListaAsync(c));
    }

    [Fact]
    public async Task Material_em_ficha_alheia_da_404_mesmo_com_peca_invalida_e_nao_mexe_no_stock()
    {
        var c = await ComoAnaAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F2/materiais", new PedidoMaterial("X", 1, 1m, "P001"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F2/materiais", new PedidoMaterial("X", 1, 1m, "NAOEXISTE"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/intervencoes/F2/materiais/X", new PedidoMaterial("X", 1, 1m, "P001"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/intervencoes/F2/materiais/X", new PedidoMaterial("X", 1, 1m, "NAOEXISTE"))).StatusCode);
        Assert.Equal(10, await StockAsync(c));
    }

    // Decisão: com as peças indisponíveis a lista de materiais não se degrada em silêncio (Ref/SaidaDada seriam falsos): responde 503.
    [Fact]
    public async Task Listar_materiais_com_pecas_indisponiveis_da_503()
    {
        var pecas = new PecasInstaveis(new RepositorioPecasMemoria());
        var c = await ComoAnaCom(pecas);
        await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        pecas.EnriquecerIndisponivel = true;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync("/intervencoes/F1/materiais")).StatusCode);
    }
}
