using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

// Gestão (só supervisor): equipamentos, histórico de estado, obsoleto, instruções, tarefas e periodicidades, com o repositório em memória.
public class GestaoApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!"; // sigma-ok
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));
    private const string PinAna = "482913", PinChefe = "603918", PinAnaSup = "846213"; // sigma-ok

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
        [
            (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash(PinAna)),
            (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash(PinChefe)),
            (new UtilizadorApp("anasup", "Ana Silva", Perfil.Supervisor, true, false, Tecno: 101), Hasher.Hash(PinAnaSup))
        ])))));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private static async Task<T> Ler<T>(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>())!;

    private static async Task<string> Erro(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("erro", out var e) ? e.GetString() ?? "" : "";
    }

    private static PedidoEquipamento Nova(string refe = "NOV-01") => new(Ref: refe, Design: "Bomba de água", Tipo: "Bomba", Marca: "KSB", Modelo: "B1", Serie: "S-1",
        Ano: 2020, Valor: 1500.5m, NoFornecedor: 7000004072, Data: new DateOnly(2020, 5, 1), Local: "Produção", Estado: "Em Serviço", ManualPdf: "manual.pdf", Email: "alerta@exemplo.pt");

    // ---------------------------------------------------------------- Autorização

    public static IEnumerable<object[]> Rotas() =>
    [
        ["GET", "/gestao/opcoes"], ["GET", "/gestao/equipamentos/E1"], ["POST", "/gestao/equipamentos"], ["PUT", "/gestao/equipamentos/E1"],
        ["GET", "/gestao/equipamentos/E1/planeadas-a-cancelar"], ["GET", "/gestao/equipamentos/E1/historico-estado"],
        ["GET", "/gestao/equipamentos/E1/instrucoes"], ["POST", "/gestao/equipamentos/E1/instrucoes"], ["PUT", "/gestao/instrucoes/I-E1"],
        ["GET", "/gestao/instrucoes/I-E1/tarefas"], ["POST", "/gestao/instrucoes/I-E1/tarefas"], ["PUT", "/gestao/tarefas/TAREFA2"], ["DELETE", "/gestao/tarefas/TAREFA2"],
        ["GET", "/gestao/periodicidades"], ["PUT", "/gestao/periodicidades/P1"]
    ];

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Operador_recebe_403_e_sem_sessao_401_em_toda_a_gestao(string metodo, string url)
    {
        var ana = await ComoAsync(PinAna);

        var pedido = new HttpRequestMessage(new HttpMethod(metodo), url) { Content = metodo is "POST" or "PUT" ? JsonContent.Create(new { }) : null };
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.SendAsync(pedido)).StatusCode);
        var anonimo = new HttpRequestMessage(new HttpMethod(metodo), url) { Content = metodo is "POST" or "PUT" ? JsonContent.Create(new { }) : null };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().SendAsync(anonimo)).StatusCode);
    }

    // ---------------------------------------------------------------- Equipamentos

    [Fact]
    public async Task Criar_equipamento_numera_resolve_o_fornecedor_cria_a_instrucao_e_o_historico()
    {
        var chefe = await ComoAsync(PinChefe);

        var r = await chefe.PostAsJsonAsync("/gestao/equipamentos", Nova());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var e = await Ler<EquipamentoGestaoDto>(r);
        Assert.Equal(10, e.NoInterno);
        Assert.Equal("Frio Industrial, Lda", e.NomeFornecedor);
        Assert.Equal("Em Serviço", e.Estado);
        Assert.Equal(1500.5m, e.Valor);
        Assert.False(e.Obsoleto);
        var instrucoes = await Ler<List<InstrucaoDto>>(await chefe.GetAsync($"/gestao/equipamentos/{e.Id}/instrucoes"));
        var vazia = Assert.Single(instrucoes);
        Assert.Equal("", vazia.Versao);
        Assert.Equal(0, vazia.NumTarefas);
        Assert.Equal("Em Serviço", Assert.Single(await Ler<List<HistoricoEstadoDto>>(await chefe.GetAsync($"/gestao/equipamentos/{e.Id}/historico-estado"))).Estado);
        Assert.Contains(await Ler<List<EquipamentoDto>>(await chefe.GetAsync("/equipamentos?q=bomba")), x => x.Id == e.Id);
        Assert.Equal(11, (await Ler<EquipamentoGestaoDto>(await chefe.PostAsJsonAsync("/gestao/equipamentos", Nova("NOV-02")))).NoInterno);
    }

    [Fact]
    public async Task Ref_unica_ao_criar_e_ao_mudar_mas_a_propria_ref_grava()
    {
        var chefe = await ComoAsync(PinChefe);

        var duplicada = await chefe.PostAsJsonAsync("/gestao/equipamentos", Nova("cmp-02"));
        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);
        Assert.Equal("ref_duplicada", await Erro(duplicada));
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E2", new PedidoEquipamento(Ref: "CMP-02"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E2", new PedidoEquipamento(Ref: "EMP-05", Marca: "Linde"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E2", new PedidoEquipamento(Ref: "EMP-99"))).StatusCode);
    }

    [Fact]
    public async Task Viaturas_sao_recusadas_e_os_dados_sao_validados()
    {
        var chefe = await ComoAsync(PinChefe);

        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/gestao/equipamentos", Nova() with { Tipo = "Veículos" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E1", new PedidoEquipamento(Tipo: " veículos "))).StatusCode);
        foreach (var invalido in new[]
        {
            new PedidoEquipamento(Design: "x"), new PedidoEquipamento(Ref: "x"), Nova(new string('a', 31)), Nova() with { Estado = "Parado" }, Nova() with { Email = "sem-arroba" },
            Nova() with { Ano = 10000 }, Nova() with { Valor = -1 }, Nova() with { Design = new string('d', 46) }, Nova() with { Data = new DateOnly(1800, 1, 1) }
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/gestao/equipamentos", invalido)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/gestao/equipamentos", Nova() with { NoFornecedor = 123 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/equipamentos/NAOEXISTE", new PedidoEquipamento(Marca: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.GetAsync("/gestao/equipamentos/NAOEXISTE")).StatusCode);
    }

    [Fact]
    public async Task Editar_equipamento_round_trip_com_versao_e_o_segundo_editor_recebe_409()
    {
        var chefe = await ComoAsync(PinChefe);
        var anasup = await ComoAsync(PinAnaSup);
        var lido = await Ler<EquipamentoGestaoDto>(await chefe.GetAsync("/gestao/equipamentos/E1"));

        var gravado = await Ler<EquipamentoGestaoDto>(await chefe.PutAsJsonAsync("/gestao/equipamentos/E1", new PedidoEquipamento(Local: "Frio", Marca: "Atlas", Ano: 2018, Versao: lido.Versao)));

        Assert.Equal("Frio", gravado.Local);
        Assert.Equal("Atlas", gravado.Marca);
        Assert.Equal(2018, gravado.Ano);
        Assert.Equal(lido.Design, gravado.Design);
        Assert.NotEqual(lido.Versao, gravado.Versao);
        Assert.Equal(HttpStatusCode.Conflict, (await anasup.PutAsJsonAsync("/gestao/equipamentos/E1", new PedidoEquipamento(Local: "Outro", Versao: lido.Versao))).StatusCode);
        Assert.Equal("Frio", (await Ler<EquipamentoGestaoDto>(await chefe.GetAsync("/gestao/equipamentos/E1"))).Local);
        Assert.Equal("Frio", (await Ler<List<EquipamentoDto>>(await chefe.GetAsync("/equipamentos?q=compressor"))).Single(x => x.Id == "E1").Zona);
        Assert.Equal(HttpStatusCode.OK, (await anasup.PutAsJsonAsync("/gestao/equipamentos/E1", new PedidoEquipamento(Local: "Outro", Versao: gravado.Versao))).StatusCode);
    }

    [Fact]
    public async Task Historico_de_estado_grava_so_quando_o_estado_muda_e_lista_do_mais_recente()
    {
        var chefe = await ComoAsync(PinChefe);

        await chefe.PutAsJsonAsync("/gestao/equipamentos/E1", new PedidoEquipamento(Estado: "Fora de Serviço"));
        await chefe.PutAsJsonAsync("/gestao/equipamentos/E1", new PedidoEquipamento(Estado: "fora de serviço", Marca: "x"));
        await chefe.PutAsJsonAsync("/gestao/equipamentos/E1", new PedidoEquipamento(Estado: "Em Serviço"));

        var historico = await Ler<List<HistoricoEstadoDto>>(await chefe.GetAsync("/gestao/equipamentos/E1/historico-estado"));
        Assert.Equal(["Em Serviço", "Fora de Serviço"], historico.Select(h => h.Estado).ToArray());
        Assert.All(historico, h => Assert.Equal("chefe", h.Quem));
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.GetAsync("/gestao/equipamentos/NAOEXISTE/historico-estado")).StatusCode);
    }

    [Fact]
    public async Task Obsoleto_pede_a_contagem_cancela_so_as_planeadas_e_nunca_as_em_curso()
    {
        var fabricaAna = await ComoAsync(PinAna);
        var chefe = await ComoAsync(PinChefe);
        // E3 tem duas planeadas (F2 e F4); a F4 passa a em curso e não deve ser cancelada.
        await fabricaAna.PostAsync("/intervencoes/F4/iniciar", null);

        var contagem = await Ler<PlaneadasACancelarDto>(await chefe.GetAsync("/gestao/equipamentos/E3/planeadas-a-cancelar"));
        Assert.Equal(1, contagem.Planeadas);

        var sem = await chefe.PutAsJsonAsync("/gestao/equipamentos/E3", new PedidoEquipamento(Obsoleto: true));
        Assert.Equal(HttpStatusCode.Conflict, sem.StatusCode);
        Assert.Equal("planeadas_mudaram", await Erro(sem));
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E3", new PedidoEquipamento(Obsoleto: true, PlaneadasConfirmadas: 5))).StatusCode);
        Assert.Equal(EstadoIntervencao.Planeada, (await Ler<FichaDetalheDto>(await chefe.GetAsync("/intervencoes/F2/detalhe"))).Estado);

        var ok = await chefe.PutAsJsonAsync("/gestao/equipamentos/E3", new PedidoEquipamento(Obsoleto: true, PlaneadasConfirmadas: 1));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True((await Ler<EquipamentoGestaoDto>(ok)).Obsoleto);
        var f2 = await Ler<FichaDetalheDto>(await chefe.GetAsync("/intervencoes/F2/detalhe"));
        Assert.Equal(EstadoIntervencao.Cancelada, f2.Estado);
        Assert.Equal("Equipamento obsoleto", f2.Motivo);
        Assert.Equal(EstadoIntervencao.EmCurso, (await Ler<FichaDetalheDto>(await chefe.GetAsync("/intervencoes/F4/detalhe"))).Estado);
        // Já obsoleto: gravar outra vez não pede confirmação.
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E3", new PedidoEquipamento(Obsoleto: true, Marca: "Outra"))).StatusCode);
    }

    [Fact]
    public async Task Opcoes_trazem_tipos_marcas_locais_e_estados_sem_viaturas()
    {
        var chefe = await ComoAsync(PinChefe);

        var o = await Ler<OpcoesEquipamentoDto>(await chefe.GetAsync("/gestao/opcoes"));

        Assert.Contains("Compressor", o.Tipos);
        Assert.Contains("Toyota", o.Marcas);
        Assert.Contains("Frio", o.Locais);
        Assert.Equal(["Em Serviço", "Fora de Serviço"], o.Estados);
    }

    // ---------------------------------------------------------------- Instruções

    [Fact]
    public async Task Instrucoes_listam_versoes_criam_nova_e_gravam_com_token()
    {
        var chefe = await ComoAsync(PinChefe);

        var lista = await Ler<List<InstrucaoDto>>(await chefe.GetAsync("/gestao/equipamentos/E1/instrucoes"));
        var inicial = Assert.Single(lista);
        Assert.Equal(2, inicial.NumTarefas);

        var nova = await Ler<InstrucaoDto>(await chefe.PostAsJsonAsync("/gestao/equipamentos/E1/instrucoes", new PedidoInstrucao(Versao: "2", Obs: "Revisão")));
        Assert.Equal("2", nova.Versao);
        Assert.Equal(2, (await Ler<List<InstrucaoDto>>(await chefe.GetAsync("/gestao/equipamentos/E1/instrucoes"))).Count);

        var gravada = await Ler<InstrucaoDto>(await chefe.PutAsJsonAsync($"/gestao/instrucoes/{nova.Id}", new PedidoInstrucao(Obs: "Nova nota", Concorrencia: nova.Concorrencia)));
        Assert.Equal("Nova nota", gravada.Obs);
        Assert.Equal("2", gravada.Versao);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync($"/gestao/instrucoes/{nova.Id}", new PedidoInstrucao(Obs: "x", Concorrencia: nova.Concorrencia))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/gestao/equipamentos/E1/instrucoes", new PedidoInstrucao(Versao: new string('v', 11)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsJsonAsync("/gestao/equipamentos/NAOEXISTE/instrucoes", new PedidoInstrucao())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/instrucoes/NAOEXISTE", new PedidoInstrucao())).StatusCode);
    }

    [Fact]
    public async Task Marcar_a_instrucao_obsoleta_cancela_as_planeadas_das_tarefas_mas_nao_as_em_curso()
    {
        var ana = await ComoAsync(PinAna);
        var chefe = await ComoAsync(PinChefe);
        // F1 (planeada da TAREFA1) é a única planeada de E1; com uma segunda ficha da tarefa em curso mostra-se que as em curso ficam.
        var gerada = await Ler<TarefaGestaoDto>(await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA2", new PedidoTarefa(Descricao: "Drenar os condensados")));
        var planeadas = await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas"));
        var daTarefa2 = Assert.Single(planeadas, p => p.Anomalia == "Drenar os condensados");
        await ana.PostAsync($"/intervencoes/{daTarefa2.Id}/iniciar", null);
        Assert.Equal("TAREFA2", gerada.Id);

        var obsoleta = await chefe.PutAsJsonAsync("/gestao/instrucoes/I-E1", new PedidoInstrucao(Obsoleta: true));

        Assert.Equal(HttpStatusCode.OK, obsoleta.StatusCode);
        Assert.Equal(EstadoIntervencao.Cancelada, (await Ler<FichaDetalheDto>(await chefe.GetAsync("/intervencoes/F1/detalhe"))).Estado);
        Assert.Equal(EstadoIntervencao.EmCurso, (await Ler<FichaDetalheDto>(await chefe.GetAsync($"/intervencoes/{daTarefa2.Id}/detalhe"))).Estado);
        // Numa instrução obsoleta já não se juntam tarefas.
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E1/tarefas", new PedidoTarefa(Periodo: "Mensal", Descricao: "x"))).StatusCode);
    }

    // ---------------------------------------------------------------- Tarefas

    [Fact]
    public async Task Tarefa_nova_ativa_gera_a_planeada_e_valida_os_dados()
    {
        var chefe = await ComoAsync(PinChefe);

        var r = await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E3/tarefas", new PedidoTarefa(Periodo: "Mensal", Responsabilidade: "Empresa Externa", Responsavel: "Frio, Lda", Descricao: "Revisão geral do frio", TipoManut: "externa"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var t = await Ler<TarefaGestaoDto>(r);
        Assert.Equal("Externa", t.TipoManut);
        Assert.True(t.Ativa);
        Assert.Equal(2, (await Ler<List<TarefaGestaoDto>>(await chefe.GetAsync("/gestao/instrucoes/I-E3/tarefas"))).Count);
        Assert.Contains(await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas")), p => p.Anomalia == "Revisão geral do frio" && p.Estado == EstadoIntervencao.Planeada);

        var desconhecida = await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E3/tarefas", new PedidoTarefa(Periodo: "Bienal", Descricao: "x"));
        Assert.Equal(HttpStatusCode.BadRequest, desconhecida.StatusCode);
        Assert.Equal("periodicidade_invalida", await Erro(desconhecida));
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E3/tarefas", new PedidoTarefa(Periodo: "Mensal"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E3/tarefas", new PedidoTarefa(Periodo: "Mensal", Descricao: "x", TipoManut: "Mista"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E3/tarefas", new PedidoTarefa(Periodo: new string('p', 21), Descricao: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsJsonAsync("/gestao/instrucoes/NAOEXISTE/tarefas", new PedidoTarefa(Periodo: "Mensal", Descricao: "x"))).StatusCode);
    }

    [Fact]
    public async Task Desligar_a_tarefa_cancela_as_planeadas_e_voltar_a_ligar_gera_outra()
    {
        var chefe = await ComoAsync(PinChefe);
        var antes = (await Ler<List<TarefaGestaoDto>>(await chefe.GetAsync("/gestao/instrucoes/I-E1/tarefas"))).Single(t => t.Id == "TAREFA1");

        var desligada = await Ler<TarefaGestaoDto>(await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA1", new PedidoTarefa(Ativa: false, Concorrencia: antes.Concorrencia)));

        Assert.False(desligada.Ativa);
        var f1 = await Ler<FichaDetalheDto>(await chefe.GetAsync("/intervencoes/F1/detalhe"));
        Assert.Equal(EstadoIntervencao.Cancelada, f1.Estado);
        Assert.Equal("Tarefa desligada", f1.Motivo);
        Assert.DoesNotContain(await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas")), p => p.Equipamento == "Compressor de ar 02" && p.Id != "F1" && p.Anomalia == "Verificar o nível de óleo e os filtros");

        await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA1", new PedidoTarefa(Ativa: true, Concorrencia: desligada.Concorrencia));
        Assert.Contains(await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas")), p => p.Anomalia == "Verificar o nível de óleo e os filtros" && p.Id != "F1");
    }

    [Fact]
    public async Task Apagar_tarefa_so_sem_fichas_feitas_e_cancela_as_planeadas()
    {
        var chefe = await ComoAsync(PinChefe);

        var recusada = await chefe.DeleteAsync("/gestao/tarefas/TAREFA1");
        Assert.Equal(HttpStatusCode.Conflict, recusada.StatusCode);
        Assert.Equal("tarefa_com_fichas", await Erro(recusada));
        Assert.Contains(await Ler<List<TarefaGestaoDto>>(await chefe.GetAsync("/gestao/instrucoes/I-E1/tarefas")), t => t.Id == "TAREFA1");

        // TAREFA2 não tem fichas: gravá-la gera uma planeada, que o apagar cancela.
        await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA2", new PedidoTarefa(Descricao: "Drenar os condensados"));
        var gerada = Assert.Single(await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas")), p => p.Anomalia == "Drenar os condensados");

        Assert.Equal(HttpStatusCode.NoContent, (await chefe.DeleteAsync("/gestao/tarefas/TAREFA2")).StatusCode);

        Assert.DoesNotContain(await Ler<List<TarefaGestaoDto>>(await chefe.GetAsync("/gestao/instrucoes/I-E1/tarefas")), t => t.Id == "TAREFA2");
        var ficha = await Ler<FichaDetalheDto>(await chefe.GetAsync($"/intervencoes/{gerada.Id}/detalhe"));
        Assert.Equal(EstadoIntervencao.Cancelada, ficha.Estado);
        Assert.Equal("Tarefa apagada", ficha.Motivo);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.DeleteAsync("/gestao/tarefas/TAREFA2")).StatusCode);
    }

    [Fact]
    public async Task Tarefa_com_ficha_cancelada_so_apaga_se_nao_houver_feitas()
    {
        var chefe = await ComoAsync(PinChefe);

        // TAREFA3 só tem uma ficha cancelada (H3): pode apagar-se.
        Assert.Equal(HttpStatusCode.NoContent, (await chefe.DeleteAsync("/gestao/tarefas/TAREFA3")).StatusCode);
    }

    [Fact]
    public async Task Editar_tarefa_com_token_antigo_da_409()
    {
        var chefe = await ComoAsync(PinChefe);
        var t = (await Ler<List<TarefaGestaoDto>>(await chefe.GetAsync("/gestao/instrucoes/I-E3/tarefas"))).Single();

        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA3", new PedidoTarefa(Responsavel: "Rui", Concorrencia: t.Concorrencia))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA3", new PedidoTarefa(Responsavel: "Outro", Concorrencia: t.Concorrencia))).StatusCode);
        Assert.Equal("Rui", (await Ler<List<TarefaGestaoDto>>(await chefe.GetAsync("/gestao/instrucoes/I-E3/tarefas"))).Single().Responsavel);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/tarefas/NAOEXISTE", new PedidoTarefa(Responsavel: "x"))).StatusCode);
    }

    // ---------------------------------------------------------------- Viaturas, IDOR, avisos e obsoleto sem planeadas

    [Fact]
    public async Task Viaturas_ficam_fora_da_gestao_em_todos_os_caminhos_com_ou_sem_acentos()
    {
        var chefe = await ComoAsync(PinChefe);

        foreach (var url in new[]
        {
            "/gestao/equipamentos/E8", "/gestao/equipamentos/E8/planeadas-a-cancelar", "/gestao/equipamentos/E8/historico-estado", "/gestao/equipamentos/E8/instrucoes",
            "/gestao/instrucoes/I-E8/tarefas", "/equipamentos/E8"
        })
            Assert.Equal(HttpStatusCode.NotFound, (await chefe.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E8", new PedidoEquipamento(Marca: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsJsonAsync("/gestao/equipamentos/E8/instrucoes", new PedidoInstrucao(Versao: "2"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/instrucoes/I-E8", new PedidoInstrucao(Obs: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E8/tarefas", new PedidoTarefa(Periodo: "Mensal", Descricao: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA9", new PedidoTarefa(Descricao: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.DeleteAsync("/gestao/tarefas/TAREFA9")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E8"))).StatusCode);
        Assert.DoesNotContain(await Ler<List<EquipamentoDto>>(await chefe.GetAsync("/equipamentos")), e => e.Id == "E8");
        Assert.DoesNotContain(await Ler<List<EquipamentoDto>>(await chefe.GetAsync("/equipamentos?leve=true&q=carrinha")), e => e.Id == "E8");
        Assert.DoesNotContain("Veículos", (await Ler<OpcoesEquipamentoDto>(await chefe.GetAsync("/gestao/opcoes"))).Tipos);
        foreach (var tipo in new[] { "Veiculos", "VEÍCULOS", " veículos " })
            Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/gestao/equipamentos", Nova() with { Tipo = tipo })).StatusCode);
    }

    [Fact]
    public async Task Id_de_uma_coisa_usado_como_outra_da_404()
    {
        var chefe = await ComoAsync(PinChefe);

        Assert.Equal(HttpStatusCode.NotFound, (await chefe.GetAsync("/gestao/equipamentos/I-E1/instrucoes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.GetAsync("/gestao/instrucoes/E1/tarefas")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/instrucoes/E1", new PedidoInstrucao(Obs: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/tarefas/I-E1", new PedidoTarefa(Descricao: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.DeleteAsync("/gestao/tarefas/E1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.GetAsync("/gestao/equipamentos/TAREFA1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/periodicidades/E1", new PedidoPeriodicidade("dias", 1))).StatusCode);
    }

    [Fact]
    public async Task Obsoleto_sem_confirmacao_so_passa_se_nao_houver_planeadas_a_cancelar()
    {
        var chefe = await ComoAsync(PinChefe);

        // E2 não tem planeadas: sem PlaneadasConfirmadas conta como 0 e grava.
        Assert.Equal(0, (await Ler<PlaneadasACancelarDto>(await chefe.GetAsync("/gestao/equipamentos/E2/planeadas-a-cancelar"))).Planeadas);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E2", new PedidoEquipamento(Obsoleto: true))).StatusCode);
        // E3 tem planeadas: sem confirmar, não.
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync("/gestao/equipamentos/E3", new PedidoEquipamento(Obsoleto: true))).StatusCode);
        Assert.Equal(EstadoIntervencao.Planeada, (await Ler<FichaDetalheDto>(await chefe.GetAsync("/intervencoes/F2/detalhe"))).Estado);
    }

    [Fact]
    public async Task Tarefa_gravada_com_o_equipamento_ou_a_instrucao_obsoletos_avisa_que_nao_gera()
    {
        var chefe = await ComoAsync(PinChefe);
        await chefe.PutAsJsonAsync("/gestao/equipamentos/E1", new PedidoEquipamento(Obsoleto: true, PlaneadasConfirmadas: 1));

        var porEquipamento = await Ler<TarefaGestaoDto>(await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA2", new PedidoTarefa(Descricao: "Drenar os condensados")));

        Assert.Equal(RegrasGestao.AvisoEquipamentoObsoleto, porEquipamento.Aviso);
        Assert.DoesNotContain(await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas")), p => p.Anomalia == "Drenar os condensados");

        await chefe.PutAsJsonAsync("/gestao/instrucoes/I-E3", new PedidoInstrucao(Obsoleta: true));
        var porInstrucao = await Ler<TarefaGestaoDto>(await chefe.PutAsJsonAsync("/gestao/tarefas/TAREFA3", new PedidoTarefa(Responsavel: "Rui")));
        Assert.Equal(RegrasGestao.AvisoInstrucaoObsoleta, porInstrucao.Aviso);
        Assert.Equal("Rui", porInstrucao.Responsavel);

        var normal = await Ler<TarefaGestaoDto>(await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E2/tarefas", new PedidoTarefa(Periodo: "Mensal", Descricao: "Lubrificar")));
        Assert.Null(normal.Aviso);
    }

    [Fact]
    public async Task Juntar_tarefa_a_instrucao_obsoleta_da_o_codigo_proprio_e_reativar_nao_gera_planeadas()
    {
        var chefe = await ComoAsync(PinChefe);
        var obsoleta = await Ler<InstrucaoDto>(await chefe.PutAsJsonAsync("/gestao/instrucoes/I-E3", new PedidoInstrucao(Obsoleta: true)));

        var r = await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E3/tarefas", new PedidoTarefa(Periodo: "Mensal", Descricao: "x"));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("instrucao_obsoleta", await Erro(r));

        var planeadasAntes = (await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas"))).Count;
        var reativada = await Ler<InstrucaoDto>(await chefe.PutAsJsonAsync("/gestao/instrucoes/I-E3", new PedidoInstrucao(Obsoleta: false, Concorrencia: obsoleta.Concorrencia)));
        Assert.False(reativada.Obsoleta);
        Assert.Equal(planeadasAntes, (await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas"))).Count);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync("/gestao/instrucoes/I-E3/tarefas", new PedidoTarefa(Periodo: "Mensal", Descricao: "x"))).StatusCode);
    }

    // ---------------------------------------------------------------- Periodicidades

    [Fact]
    public async Task Periodicidades_listam_e_editam_so_unidade_intervalo_e_aviso()
    {
        var chefe = await ComoAsync(PinChefe);
        var lista = await Ler<List<PeriodicidadeDto>>(await chefe.GetAsync("/gestao/periodicidades"));
        Assert.Equal(10, lista.Count);
        var mensal = lista.Single(p => p.Periodo == "Mensal");

        var editada = await Ler<PeriodicidadeDto>(await chefe.PutAsJsonAsync($"/gestao/periodicidades/{mensal.Id}", new PedidoPeriodicidade("dias", 30, true, mensal.Concorrencia)));

        Assert.Equal("dias", editada.Unidade);
        Assert.Equal(30, editada.Intervalo);
        Assert.True(editada.Aviso);
        Assert.Equal("Mensal", editada.Periodo);
        Assert.Equal(10, (await Ler<List<PeriodicidadeDto>>(await chefe.GetAsync("/gestao/periodicidades"))).Count);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync($"/gestao/periodicidades/{mensal.Id}", new PedidoPeriodicidade("dias", 31, null, mensal.Concorrencia))).StatusCode);

        var regular = lista.Single(p => p.Periodo == "Regular");
        var nenhum = await Ler<PeriodicidadeDto>(await chefe.PutAsJsonAsync($"/gestao/periodicidades/{regular.Id}", new PedidoPeriodicidade("nenhum", 5, false)));
        Assert.Equal(0, nenhum.Intervalo);
    }

    [Fact]
    public async Task Periodicidade_valida_a_unidade_e_o_intervalo_como_no_landeiro()
    {
        var chefe = await ComoAsync(PinChefe);

        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/gestao/periodicidades/P4", new PedidoPeriodicidade("anos", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/gestao/periodicidades/P4", new PedidoPeriodicidade("dias", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/gestao/periodicidades/P4", new PedidoPeriodicidade("meses", 10000))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/gestao/periodicidades/P4", new PedidoPeriodicidade(null, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/gestao/periodicidades/P4", new PedidoPeriodicidade("meses", 9999))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/gestao/periodicidades/NAOEXISTE", new PedidoPeriodicidade("dias", 1))).StatusCode);
    }

    [Fact]
    public async Task Mudar_a_periodicidade_muda_a_proxima_data_da_tarefa()
    {
        var chefe = await ComoAsync(PinChefe);
        var antes = (await Ler<FichaEquipamentoDto>(await chefe.GetAsync("/equipamentos/E1"))).Tarefas.Single(t => t.Id == "TAREFA1").ProximaData;
        var mensal = (await Ler<List<PeriodicidadeDto>>(await chefe.GetAsync("/gestao/periodicidades"))).Single(p => p.Periodo == "Mensal");

        await chefe.PutAsJsonAsync($"/gestao/periodicidades/{mensal.Id}", new PedidoPeriodicidade("dias", 3));

        var depois = (await Ler<FichaEquipamentoDto>(await chefe.GetAsync("/equipamentos/E1"))).Tarefas.Single(t => t.Id == "TAREFA1").ProximaData;
        Assert.NotEqual(antes, depois);
    }
}
