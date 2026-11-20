using System.Net;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class MaterialPecasOfflineTests
{
    private static async Task<Cenario> CenarioEmCursoAsync()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        return c;
    }

    [Fact]
    public async Task Material_com_peca_sem_rede_fica_na_fila_com_a_ref_e_por_dar()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);

        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 2, 3, "P001");

        Assert.Equal("P001", local.Ref);
        Assert.False(local.SaidaDada);
        var operacao = Assert.Single(await c.Fila.ListarAsync("ana"));
        Assert.Contains("\"Ref\":\"P001\"", operacao.Payload);
        Assert.Equal("P001", Assert.Single(await c.Svc.ListarMateriaisAsync("T1")).Ref);
    }

    [Fact]
    public async Task Ao_sincronizar_o_remoto_recebe_a_ref_e_o_material_passa_a_ter_saida_dada()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);
        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 2, 3, "P001");
        await c.Svc.AdicionarMaterialAsync("T1", "Massa", 1, 1);
        c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        Assert.Equal(["P001", ""], c.Remoto.RefsMaterial);
        var cache = (await c.Cache.LerMateriaisAsync("T1"))!;
        var comPeca = Assert.Single(cache, m => m.Ref == "P001");
        Assert.True(comPeca.SaidaDada);
        Assert.DoesNotContain(cache, m => m.Id.StartsWith("L-"));
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Apagar_material_local_com_peca_cancela_a_operacao_sem_chamar_o_remoto()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);
        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 1, 5, "P001");

        await c.Svc.ApagarMaterialAsync("T1", local.Id);

        Assert.Empty(await c.Fila.ListarAsync("ana"));
        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();
        Assert.Equal(0, c.Remoto.Contar("AdicionarMaterial"));
        Assert.Equal(0, c.Remoto.Contar("ApagarMaterial"));
        Assert.Empty(c.Remoto.RefsMaterial);
    }

    [Fact]
    public async Task Operacao_gravada_antes_das_pecas_sincroniza_com_ref_vazia()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);
        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 1, 5);
        var operacao = Assert.Single(await c.Fila.ListarAsync("ana"));
        // Fila antiga: a carga não tem o campo Ref.
        var antiga = operacao.Payload.Replace(",\"Ref\":\"\"", "");
        Assert.DoesNotContain("Ref", antiga);
        await c.Fila.AtualizarAsync(operacao with { Payload = antiga });
        c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        Assert.Equal([""], c.Remoto.RefsMaterial);
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Timeout_com_peca_deixa_incerta_e_reenvia_so_uma_vez_com_a_ref()
    {
        var c = await CenarioEmCursoAsync();
        await c.Svc.ListarMateriaisAsync("T1");
        c.Remoto.DepoisFalha["AdicionarMaterial"] = new TaskCanceledException("timeout");

        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 2, 3, "P001");
        Assert.Equal(EstadoOperacao.Incerta, Assert.Single(await c.Fila.ListarAsync("ana")).Estado);

        await c.Sinc.SincronizarAsync();

        // O servidor já tinha o material (e a saída) quando a resposta se perdeu: a fila encontra-o e não repete.
        Assert.Equal(1, c.Remoto.Contar("AdicionarMaterial"));
        Assert.Equal(["P001"], c.Remoto.RefsMaterial);
        Assert.Equal("P001", Assert.Single((await c.Cache.LerMateriaisAsync("T1"))!).Ref);
    }

    [Fact]
    public async Task Repetir_saida_de_material_local_sem_rede_diz_sem_ligacao_e_nao_chama_o_remoto()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);
        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 1, 5, "P001");

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.RepetirSaidaAsync("T1", local.Id));

        Assert.Equal(MensagensIntervencao.SemRede, e.Message);
        Assert.Equal(0, c.Remoto.Contar("RepetirSaida"));
    }

    private sealed class Fixo(HttpStatusCode codigo, string corpo) : HttpMessageHandler
    {
        public string? Corpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            Corpo = pedido.Content is null ? null : await pedido.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(codigo) { Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static IntervencoesApiService Api(Fixo h) =>
        new(new HttpClient(h) { BaseAddress = new Uri("https://exemplo.invalid/") }, new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria()));

    [Fact]
    public async Task Api_envia_a_ref_no_corpo_ao_juntar_e_ao_editar()
    {
        var h = new Fixo(HttpStatusCode.OK, """{"id":"M1","design":"x","qtt":1,"epv":1,"custo":1,"ref":"P001","saidaDada":true}""");
        var api = Api(h);

        await api.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001");
        Assert.Contains("\"ref\":\"P001\"", h.Corpo);

        await api.EditarMaterialAsync("T1", "M1", "Rolamento", 2, 3, "P002");
        Assert.Contains("\"ref\":\"P002\"", h.Corpo);
    }

    [Fact]
    public async Task Api_400_com_peca_mostra_a_mensagem_da_api()
    {
        var api = Api(new Fixo(HttpStatusCode.BadRequest, """{"mensagem":"Essa peça não existe."}"""));

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => api.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P999"));

        Assert.Equal("Essa peça não existe.", e.Message);
    }

    [Fact]
    public async Task Api_400_sem_peca_nao_e_convertido_em_mensagem()
    {
        var api = Api(new Fixo(HttpStatusCode.BadRequest, """{"mensagem":"Essa peça não existe."}"""));

        await Assert.ThrowsAsync<HttpRequestException>(() => api.AdicionarMaterialAsync("T1", "Rolamento", 2, 3));
    }

    private const string DoisLugares = """{"erro":"escolha_o_lugar","lugares":[{"corredor":"A","estante":"A-01","nivel":1,"posicao":1,"qtt":4,"texto":"A-01-1-01"},{"corredor":"B","estante":"B-02","nivel":1,"posicao":3,"qtt":2.5,"texto":"B-02-1-03"}]}""";

    [Fact]
    public async Task Api_envia_o_local_no_corpo_ao_juntar_e_ao_editar_e_omite_o_vazio()
    {
        var h = new Fixo(HttpStatusCode.OK, """{"id":"M1","design":"x","qtt":1,"epv":1,"custo":1,"ref":"P001","saidaDada":true,"local":"A-01-1-01","avisoStock":"Sem stock desta peça."}""");
        var api = Api(h);

        var m = await api.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001", "LOC-A-01-1-01");
        Assert.Contains("\"local\":\"LOC-A-01-1-01\"", h.Corpo);
        Assert.Equal("A-01-1-01", m.Local);
        Assert.Equal("Sem stock desta peça.", m.AvisoStock);

        await api.EditarMaterialAsync("T1", "M1", "Rolamento", 2, 3, "P001", "A-01-1-01");
        Assert.Contains("\"local\":\"A-01-1-01\"", h.Corpo);

        await api.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001");
        Assert.DoesNotContain("local", h.Corpo);
    }

    [Fact]
    public async Task Api_409_escolha_o_lugar_traz_os_lugares_com_a_quantidade_de_cada()
    {
        var api = Api(new Fixo(HttpStatusCode.Conflict, DoisLugares));

        var e = await Assert.ThrowsAsync<EscolhaLugarException>(() => api.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001"));

        Assert.Equal(["A-01-1-01", "B-02-1-03"], e.Lugares.Select(l => l.Texto));
        Assert.Equal(2.5m, e.Lugares[1].Qtt);
        Assert.Equal(MensagensIntervencao.EscolhaLugar, e.Message);
        await Assert.ThrowsAsync<EscolhaLugarException>(() => api.EditarMaterialAsync("T1", "M1", "Rolamento", 2, 3, "P001"));
        await Assert.ThrowsAsync<EscolhaLugarException>(() => api.RepetirSaidaAsync("T1", "M1"));
    }

    [Fact]
    public async Task Api_repetir_saida_leva_o_local_so_quando_ha_e_apagar_devolve_o_aviso_do_200()
    {
        var h = new Fixo(HttpStatusCode.OK, """{"id":"M1","design":"x","qtt":1,"epv":1,"custo":1,"ref":"P001","saidaDada":true}""");
        var api = Api(h);

        await api.RepetirSaidaAsync("T1", "M1");
        Assert.Null(h.Corpo);
        await api.RepetirSaidaAsync("T1", "M1", "LOC-B-02-1-03");
        Assert.Contains("\"local\":\"LOC-B-02-1-03\"", h.Corpo);

        var comAviso = Api(new Fixo(HttpStatusCode.OK, """{"avisoStock":"Material apagado, mas a peça saiu sem repor no lugar: avise o supervisor."}"""));
        Assert.Equal("Material apagado, mas a peça saiu sem repor no lugar: avise o supervisor.", await comAviso.ApagarMaterialAsync("T1", "M1"));
        Assert.Null(await Api(new Fixo(HttpStatusCode.NoContent, "")).ApagarMaterialAsync("T1", "M1"));
    }

    [Fact]
    public async Task Api_validar_e_cancelar_trazem_o_aviso_de_conferencia()
    {
        const string ficha = """{"id":"T1","equipamento":"E","zona":"Z","tipo":"Preventiva","dataPlaneada":"2026-11-20","estado":"Validada","avisoStock":"Conferência por confirmar"}""";
        var api = Api(new Fixo(HttpStatusCode.OK, ficha));

        Assert.Equal("Conferência por confirmar", (await api.ValidarAsync("T1")).AvisoStock);
        Assert.Equal("Conferência por confirmar", (await api.CancelarAsync("T1", "motivo")).AvisoStock);
    }

    [Fact]
    public async Task Material_sem_rede_guarda_ref_e_local_e_o_remoto_recebe_o_local_ao_sincronizar()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);

        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 2, 3, "P001", "LOC-A-01-1-01");

        Assert.Equal("A-01-1-01", LogicaFichaPeca.TextoLugar(local.Local));
        var operacao = Assert.Single(await c.Fila.ListarAsync("ana"));
        Assert.Contains("\"Ref\":\"P001\"", operacao.Payload);
        Assert.Contains("\"Local\":\"LOC-A-01-1-01\"", operacao.Payload);

        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();

        Assert.Equal(["LOC-A-01-1-01"], c.Remoto.LocaisMaterial);
        Assert.Equal("LOC-A-01-1-01", Assert.Single((await c.Cache.LerMateriaisAsync("T1"))!).Local);
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Material_sem_peca_nao_leva_local()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);

        await c.Svc.AdicionarMaterialAsync("T1", "Massa", 1, 1, "", "LOC-A-01-1-01");

        Assert.Contains("\"Local\":\"\"", Assert.Single(await c.Fila.ListarAsync("ana")).Payload);
    }

    [Fact]
    public async Task Fila_antiga_sem_local_desserializa_e_envia_local_vazio()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);
        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 1, 5, "P001");
        var operacao = Assert.Single(await c.Fila.ListarAsync("ana"));
        var antiga = operacao.Payload.Replace(",\"Local\":\"\"", "");
        Assert.DoesNotContain("Local", antiga);
        await c.Fila.AtualizarAsync(operacao with { Payload = antiga });
        c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        Assert.Equal([""], c.Remoto.LocaisMaterial);
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    private static async Task<Cenario> ComPecaEmVariosLugaresAsync()
    {
        var c = await CenarioEmCursoAsync();
        await c.SemearEmCacheAsync("T2", "Câmara de frio 01", EstadoIntervencao.EmCurso);
        c.Remoto.LugaresPorPeca["P001"] = [new("A", "A-01", 1, 1, 4, "A-01-1-01"), new("B", "B-02", 1, 3, 2, "B-02-1-03")];
        return c;
    }

    [Fact]
    public async Task Escolha_o_lugar_ao_sincronizar_deixa_o_material_pendente_e_visivel_sem_bloquear_outras_fichas()
    {
        var c = await ComPecaEmVariosLugaresAsync();
        c.Rede.Definir(false);
        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001");
        await c.Svc.ConcluirAsync("T1", "Feito", new TimeOnly(8, 0), new TimeOnly(9, 0));
        await c.Svc.AdicionarMaterialAsync("T2", "Massa", 1, 1);
        c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        var fila = await c.Fila.ListarAsync("ana");
        Assert.Equal(EstadoOperacao.PorEscolherLugar, fila.Single(o => o.Tipo == TipoOperacao.AdicionarMaterial && o.FichaId == "T1").Estado);
        // Não é conflito: sem aviso de conflito, continua contado como pendente, e a conclusão da mesma ficha espera pela ordem.
        Assert.Empty(await c.Sinc.ListarAvisosAsync());
        Assert.Equal(2, c.Sinc.PendentesCount);
        Assert.Equal(0, c.Remoto.Contar("Concluir"));
        // A outra ficha seguiu.
        Assert.Equal(1, c.Remoto.Contar("AdicionarMaterial:T2"));
        Assert.DoesNotContain(fila, o => o.FichaId == "T2");
        var visivel = Assert.Single(await c.Svc.ListarMateriaisAsync("T1"));
        Assert.Equal(local.Id, visivel.Id);
        Assert.True(visivel.FaltaLugar);

        // Nova passagem não a descarta nem a repete.
        await c.Sinc.SincronizarAsync();
        Assert.Equal(EstadoOperacao.PorEscolherLugar, (await c.Fila.ListarAsync("ana")).Single(o => o.Tipo == TipoOperacao.AdicionarMaterial).Estado);
    }

    [Fact]
    public async Task Escolher_o_lugar_volta_a_pendente_e_a_fila_envia_material_e_depois_a_conclusao()
    {
        var c = await ComPecaEmVariosLugaresAsync();
        c.Rede.Definir(false);
        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001");
        await c.Svc.ConcluirAsync("T1", "Feito", new TimeOnly(8, 0), new TimeOnly(9, 0));
        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();

        await c.Svc.DefinirLugarMaterialAsync("T1", local.Id, "LOC-B-02-1-03");
        var operacao = (await c.Fila.ListarAsync("ana")).Single(o => o.Tipo == TipoOperacao.AdicionarMaterial);
        Assert.Equal(EstadoOperacao.Pendente, operacao.Estado);
        Assert.Contains("\"Local\":\"LOC-B-02-1-03\"", operacao.Payload);
        await c.Sinc.SincronizarAsync();

        Assert.Equal(["LOC-B-02-1-03"], c.Remoto.LocaisMaterial);
        Assert.Equal(1, c.Remoto.Contar("Concluir"));
        Assert.Equal(0, c.Sinc.PendentesCount);
        Assert.DoesNotContain(await c.Svc.ListarMateriaisAsync("T1"), m => m.FaltaLugar);
    }

    [Fact]
    public async Task Escolher_o_lugar_sem_nada_por_escolher_ou_vazio_recusa()
    {
        var c = await CenarioEmCursoAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.DefinirLugarMaterialAsync("T1", "L-nao-existe", "LOC-A-01-1-01"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.DefinirLugarMaterialAsync("T1", "L-x", "  "));
    }

    [Fact]
    public async Task Apagar_o_material_que_espera_pelo_lugar_cancela_a_operacao()
    {
        var c = await ComPecaEmVariosLugaresAsync();
        c.Rede.Definir(false);
        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001");
        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();

        await c.Svc.ApagarMaterialAsync("T1", local.Id);

        Assert.Empty(await c.Fila.ListarAsync("ana"));
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Apagar_online_devolve_o_aviso_de_o_lugar_nao_ter_sido_reposto()
    {
        var c = await CenarioEmCursoAsync();
        var m = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 1, 5, "P001", "LOC-A-01-1-01");
        c.Remoto.AvisoApagar = "Material apagado, mas a peça saiu sem repor no lugar: avise o supervisor.";

        Assert.Equal(c.Remoto.AvisoApagar, await c.Svc.ApagarMaterialAsync("T1", m.Id));
    }

    [Fact]
    public async Task Online_escolha_o_lugar_chega_ao_ecra_como_excecao_com_os_lugares()
    {
        var c = await ComPecaEmVariosLugaresAsync();

        var e = await Assert.ThrowsAsync<EscolhaLugarException>(() => c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001"));

        Assert.Equal(2, e.Lugares.Count);
        Assert.Empty(await c.Fila.ListarAsync("ana"));
        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 2, 3, "P001", "LOC-A-01-1-01");
        Assert.Equal(["LOC-A-01-1-01"], c.Remoto.LocaisMaterial);
    }

    [Fact]
    public async Task Validar_na_fila_com_conferencia_por_confirmar_deixa_aviso_em_vez_de_o_perder()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = RemotoFalso.NovaFicha("F1", "Compressor de ar 02", EstadoIntervencao.Concluida, Cenario.Hoje);
        await c.Svc.ListarPorValidarAsync();
        c.Remoto.Rede = false; c.Rede.Definir(false);
        await c.Svc.ValidarAsync("F1");
        c.Remoto.AvisoValidar = "Conferência por confirmar";
        c.Remoto.Rede = true; c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        var aviso = Assert.Single(await c.Sinc.ListarAvisosAsync());
        Assert.Contains("Conferência por confirmar", aviso.Mensagem);
        Assert.Contains("Repetir confirmação", aviso.Mensagem);
        Assert.Equal("Compressor de ar 02", aviso.Equipamento);
        Assert.Empty(await c.Fila.ListarAsync("ana"));
    }

    [Fact]
    public async Task Validar_na_fila_sem_aviso_nao_cria_aviso()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = RemotoFalso.NovaFicha("F1", "Compressor de ar 02", EstadoIntervencao.Concluida, Cenario.Hoje);
        await c.Svc.ListarPorValidarAsync();
        c.Remoto.Rede = false; c.Rede.Definir(false);
        await c.Svc.ValidarAsync("F1");
        c.Remoto.Rede = true; c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        Assert.Empty(await c.Sinc.ListarAvisosAsync());
    }

    [Fact]
    public async Task Api_409_com_texto_mostra_o_texto_e_com_codigo_usa_o_mapa()
    {
        var comTexto = Api(new Fixo(HttpStatusCode.Conflict, """{"erro":"Stock insuficiente para a saída."}"""));
        var e1 = await Assert.ThrowsAsync<InvalidOperationException>(() => comTexto.RepetirSaidaAsync("T1", "M1"));
        Assert.Equal("Stock insuficiente para a saída.", e1.Message);

        var comCodigo = Api(new Fixo(HttpStatusCode.Conflict, """{"erro":"sem_permissao"}"""));
        var e2 = await Assert.ThrowsAsync<InvalidOperationException>(() => comCodigo.ApagarMaterialAsync("T1", "M1"));
        Assert.Equal(MensagensIntervencao.Para("sem_permissao"), e2.Message);
    }
}
