using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class FormEquipamentoTests
{
    private static EquipamentoGestao Original() => new("E1", 2, "CMP-02", "Compressor", "Compressor", "Atlas", "GA", "S1", 2019, 100m, 7000004072, "Frio", new DateOnly(2019, 3, 1), "Produção", "Em Serviço", false, "", "", "3");

    [Fact]
    public void Sem_mexer_nao_ha_alteracoes_e_o_novo_vai_tudo()
    {
        var o = Original();
        Assert.False(FormEquipamento.De(o).TemAlteracoes(o));
        Assert.Null(FormEquipamento.De(o).ParaEdicao(o));

        var novo = new FormEquipamento { Ref = " R-1 ", Design = "Bomba" };
        Assert.True(novo.TemAlteracoes(null));
        var e = novo.ParaEdicao(null)!;
        Assert.Equal("R-1", e.Ref);
        Assert.Equal("Em Serviço", e.Estado);
        Assert.Null(e.Versao);
        Assert.False(new FormEquipamento().TemAlteracoes(null));
    }

    [Fact]
    public void Edicao_so_envia_o_que_mudou_e_a_versao_lida()
    {
        var o = Original();
        var f = FormEquipamento.De(o);
        f.Local = " Frio "; f.Ano = 2020; f.Estado = "Fora de Serviço";

        var e = f.ParaEdicao(o)!;

        Assert.Equal("Frio", e.Local);
        Assert.Equal(2020, e.Ano);
        Assert.Equal("Fora de Serviço", e.Estado);
        Assert.Null(e.Ref);
        Assert.Null(e.Marca);
        Assert.Equal("3", e.Versao);
    }

    [Fact]
    public void Obsoleto_pede_confirmacao_so_ao_passar_de_ativo_a_obsoleto()
    {
        var o = Original();
        var f = FormEquipamento.De(o);
        Assert.False(f.PedeConfirmacaoDeObsoleto(o));
        f.Obsoleto = true;
        Assert.True(f.PedeConfirmacaoDeObsoleto(o));
        Assert.False(f.PedeConfirmacaoDeObsoleto(o with { Obsoleto = true }));
    }

    [Theory]
    [InlineData(0, "Nenhuma planeada", "não tem planeadas")]
    [InlineData(1, "1 ficha", "1 ficha planeada")]
    [InlineData(4, "4 fichas", "4 fichas planeadas")]
    public void Texto_da_confirmacao_diz_quantas_planeadas_ficam_canceladas(int n, string _, string contem) =>
        Assert.Contains(contem, PassosEquipamento.TextoConfirmacaoObsoleto(n));

    [Fact]
    public void Passos_validam_cada_secao_e_levam_ao_primeiro_com_erro()
    {
        var f = new FormEquipamento { Ref = "R", Design = "D" };
        Assert.Null(PassosEquipamento.Validar(0, f));
        Assert.Equal(-1, PassosEquipamento.PrimeiroComErro(f));

        f.Tipo = "";
        f.Ano = 10000;
        Assert.NotNull(PassosEquipamento.Validar(1, f));
        Assert.Equal(1, PassosEquipamento.PrimeiroComErro(f));
        f.Ano = 2020; f.Email = "sem-arroba"; Assert.Equal(2, PassosEquipamento.PrimeiroComErro(f));
        f.Email = "a@b.pt"; f.Estado = "Parado"; Assert.NotNull(PassosEquipamento.Validar(2, f));
        f.Estado = "Em Serviço"; f.Ref = new string('r', 31);
        Assert.Equal(0, PassosEquipamento.PrimeiroComErro(f));
    }

    [Theory]
    [InlineData("dias", 7, true)]
    [InlineData("meses", 9999, true)]
    [InlineData("nenhum", null, true)]
    [InlineData("dias", 0, false)]
    [InlineData("meses", 10000, false)]
    [InlineData("anos", 1, false)]
    public void Periodicidade_valida_no_ecra(string unidade, int? intervalo, bool valida) =>
        Assert.Equal(valida, RegrasGestaoUi.ValidarPeriodicidade(unidade, intervalo) is null);

    [Fact]
    public void Texto_da_periodicidade_e_tarefa()
    {
        Assert.Equal("de 14 em 14 dias", RegrasGestaoUi.TextoPeriodicidade(new Periodicidade("1", "Quinzenal", "dias", 14, false, "0")));
        Assert.Equal("todos os meses", RegrasGestaoUi.TextoPeriodicidade(new Periodicidade("1", "Mensal", "meses", 1, false, "0")));
        Assert.Equal("sem intervalo fixo", RegrasGestaoUi.TextoPeriodicidade(new Periodicidade("1", "Regular", "nenhum", 0, false, "0")));
        Assert.NotNull(RegrasGestaoUi.ValidarTarefa("", "Mensal", "", ""));
        Assert.Null(RegrasGestaoUi.ValidarTarefa("x", "Mensal", "Empresa Externa", "Rui"));
        Assert.NotNull(RegrasGestaoUi.ValidarTarefa("x", new string('p', 21), "", ""));
    }

    [Fact]
    public void Equipamento_antigo_sem_estado_nao_define_estado_nem_historico_se_o_utilizador_nao_mexeu()
    {
        var antigo = new EquipamentoGestao("E9", 1, "R-9", "Velho", "Serra", "", "", "", 0, 0, 0, "", null, "", "", false, "", "", "1");
        var f = FormEquipamento.De(antigo);

        Assert.Equal("", f.Estado);
        Assert.Null(f.ParaEdicao(antigo));
        Assert.Null(PassosEquipamento.Validar(2, f));
        f.Marca = "Bomar";
        var e = f.ParaEdicao(antigo)!;
        Assert.Equal("Bomar", e.Marca);
        Assert.Null(e.Estado);
        f.Estado = "Fora de Serviço";
        Assert.Equal("Fora de Serviço", f.ParaEdicao(antigo)!.Estado);
    }

    [Fact]
    public void Periodicidade_da_tarefa_vem_cortada_a_20_sem_repetidos()
    {
        var longa = "Sempre que necessário e mais um pouco";
        var lista = new[]
        {
            new Periodicidade("1", longa, "nenhum", 0, false, "0"), new Periodicidade("2", longa + " outra", "nenhum", 0, false, "0"),
            new Periodicidade("3", "Mensal", "meses", 1, false, "0"), new Periodicidade("4", "mensal", "meses", 1, false, "0"), new Periodicidade("5", "  ", "dias", 1, false, "0")
        };

        var escolhas = RegrasGestaoUi.PeriodosParaEscolher(lista);

        Assert.Equal(["Sempre que necessári", "Mensal"], escolhas);
        Assert.All(escolhas, p => Assert.True(p.Length <= 20));
    }

    [Fact]
    public void Mensagens_de_gestao_nao_falam_de_intervencoes_e_a_sessao_terminada_tem_texto_proprio()
    {
        foreach (var codigo in new[] { "ref_duplicada", "planeadas_mudaram", "tarefa_com_fichas", "instrucao_obsoleta" })
            Assert.DoesNotContain("intervenç", MensagensIntervencao.Para(codigo), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obsoleta", MensagensIntervencao.Para("instrucao_obsoleta"));
        Assert.DoesNotContain("intervenç", PassosEquipamento.TextoConfirmacaoObsoleto(3), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sessão", MensagensGestao.Para(new HttpRequestException("x", null, System.Net.HttpStatusCode.Unauthorized), "outra"));
        Assert.Equal("já pronta", MensagensGestao.Para(new InvalidOperationException("já pronta"), "outra"));
        Assert.Equal("outra", MensagensGestao.Para(new HttpRequestException("x", null, System.Net.HttpStatusCode.BadGateway), "outra"));
    }

    [Fact]
    public void Mensagens_dos_codigos_da_gestao()
    {
        Assert.Contains("já existe", MensagensIntervencao.Para("ref_duplicada"));
        Assert.Contains("planeadas mudou", MensagensIntervencao.Para("planeadas_mudaram"));
        Assert.Contains("Desligue", MensagensIntervencao.Para("tarefa_com_fichas"));
    }
}

public class GestaoDevServiceTests
{
    [Fact]
    public async Task Equipamento_novo_numera_cria_instrucao_e_historico_e_recusa_ref_repetida_e_viaturas()
    {
        var s = new GestaoDevService();

        var e = await s.GravarEquipamentoAsync(null, new EdicaoEquipamento(Ref: "N-1", Design: "Bomba", Tipo: "Bomba", Estado: "Em Serviço"));

        Assert.Equal(6, e.NoInterno);
        Assert.Single(await s.ListarInstrucoesAsync(e.Id));
        Assert.Single(await s.ListarHistoricoAsync(e.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarEquipamentoAsync(null, new EdicaoEquipamento(Ref: "n-1", Design: "Outra")));
    }

    [Fact]
    public async Task Obsoleto_so_com_a_contagem_confirmada()
    {
        var s = new GestaoDevService();

        Assert.Equal(2, await s.ContarPlaneadasAsync("E1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarEquipamentoAsync("E1", new EdicaoEquipamento(Obsoleto: true)));
        var e = await s.GravarEquipamentoAsync("E1", new EdicaoEquipamento(Obsoleto: true, PlaneadasConfirmadas: 2));

        Assert.True(e.Obsoleto);
        Assert.Equal(0, await s.ContarPlaneadasAsync("E1"));
    }

    [Fact]
    public async Task Tarefas_e_periodicidades()
    {
        var s = new GestaoDevService();

        var t = await s.GravarTarefaAsync("I-E2", null, new EdicaoTarefa(Periodo: "Mensal", Descricao: "Lubrificar"));
        Assert.Single(await s.ListarTarefasAsync("I-E2"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarTarefaAsync("I-E2", null, new EdicaoTarefa(Periodo: "Bienal", Descricao: "x")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarTarefaAsync("I-E2", t.Id, new EdicaoTarefa(Descricao: "y", Concorrencia: "99")));
        await s.ApagarTarefaAsync(t.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ApagarTarefaAsync("T-E1-1"));

        var p = (await s.ListarPeriodicidadesAsync()).Single(x => x.Periodo == "Mensal");
        var editada = await s.GravarPeriodicidadeAsync(p.Id, new EdicaoPeriodicidade("dias", 30, true, p.Concorrencia));
        Assert.Equal(("dias", 30, true), (editada.Unidade, editada.Intervalo, editada.Aviso));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarPeriodicidadeAsync(p.Id, new EdicaoPeriodicidade("dias", 31, null, p.Concorrencia)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarPeriodicidadeAsync(p.Id, new EdicaoPeriodicidade("dias", 0)));
    }
}

// A sessão terminada (401) sai como nas outras chamadas da app: HttpRequestException que ClassificacaoErros.SemSessao reconhece.
public class GestaoApiServiceTests
{
    private sealed class Fixo(System.Net.HttpStatusCode codigo, string corpo = "") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(codigo) { Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json") });
    }

    private static GestaoApiService Servico(System.Net.HttpStatusCode codigo, string corpo = "") =>
        new(new HttpClient(new Fixo(codigo, corpo)) { BaseAddress = new Uri("https://exemplo.invalid/") }, new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria()));

    [Fact]
    public async Task Sessao_terminada_sai_como_401_reconhecido_pela_app()
    {
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Servico(System.Net.HttpStatusCode.Unauthorized).ListarPeriodicidadesAsync());

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, e.StatusCode);
        Assert.Contains("sessão", MensagensGestao.Para(e, "x"));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Conflict, "{\"erro\":\"instrucao_obsoleta\"}", "obsoleta")]
    [InlineData(System.Net.HttpStatusCode.Conflict, "{\"erro\":\"ref_duplicada\"}", "já existe")]
    [InlineData(System.Net.HttpStatusCode.BadRequest, "{\"erro\":\"pedido_invalido\",\"mensagem\":\"Indique a designação.\"}", "designação")]
    [InlineData(System.Net.HttpStatusCode.Forbidden, "", "permissão")]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, "", "PHC")]
    public async Task Erros_do_servidor_viram_mensagens_em_portugues(System.Net.HttpStatusCode codigo, string corpo, string contem)
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Servico(codigo, corpo).GravarTarefaAsync("I-1", null, new EdicaoTarefa(Descricao: "x")));

        Assert.Contains(contem, e.Message);
    }
}

// A gestão nunca vai para a fila de saída: sem rede, ou com a rede a falhar a meio, recusa com uma mensagem clara.
public class GestaoSoOnlineTests
{
    private sealed class Contador : IGestaoService
    {
        private readonly GestaoDevService _dev = new();
        public int Chamadas { get; private set; }
        public bool Falhar { get; set; }
        private Task<T> Ir<T>(Func<Task<T>> acao) { Chamadas++; return Falhar ? throw new HttpRequestException("sem rede") : acao(); }
        public Task<OpcoesEquipamento> ObterOpcoesAsync(CancellationToken ct = default) => Ir(() => _dev.ObterOpcoesAsync(ct));
        public Task<EquipamentoGestao?> ObterEquipamentoAsync(string id, CancellationToken ct = default) => Ir(() => _dev.ObterEquipamentoAsync(id, ct));
        public Task<EquipamentoGestao> GravarEquipamentoAsync(string? id, EdicaoEquipamento edicao, CancellationToken ct = default) => Ir(() => _dev.GravarEquipamentoAsync(id, edicao, ct));
        public Task<int> ContarPlaneadasAsync(string equipamentoId, CancellationToken ct = default) => Ir(() => _dev.ContarPlaneadasAsync(equipamentoId, ct));
        public Task<IReadOnlyList<HistoricoEstado>> ListarHistoricoAsync(string equipamentoId, CancellationToken ct = default) => Ir(() => _dev.ListarHistoricoAsync(equipamentoId, ct));
        public Task<IReadOnlyList<InstrucaoManutencao>> ListarInstrucoesAsync(string equipamentoId, CancellationToken ct = default) => Ir(() => _dev.ListarInstrucoesAsync(equipamentoId, ct));
        public Task<InstrucaoManutencao> GravarInstrucaoAsync(string? equipamentoId, string? id, EdicaoInstrucao edicao, CancellationToken ct = default) => Ir(() => _dev.GravarInstrucaoAsync(equipamentoId, id, edicao, ct));
        public Task<IReadOnlyList<TarefaManutencao>> ListarTarefasAsync(string instrucaoId, CancellationToken ct = default) => Ir(() => _dev.ListarTarefasAsync(instrucaoId, ct));
        public Task<TarefaManutencao> GravarTarefaAsync(string? instrucaoId, string? id, EdicaoTarefa edicao, CancellationToken ct = default) => Ir(() => _dev.GravarTarefaAsync(instrucaoId, id, edicao, ct));
        public Task ApagarTarefaAsync(string id, CancellationToken ct = default) => Ir(async () => { await _dev.ApagarTarefaAsync(id, ct); return true; });
        public Task<IReadOnlyList<Periodicidade>> ListarPeriodicidadesAsync(CancellationToken ct = default) => Ir(() => _dev.ListarPeriodicidadesAsync(ct));
        public Task<Periodicidade> GravarPeriodicidadeAsync(string id, EdicaoPeriodicidade edicao, CancellationToken ct = default) => Ir(() => _dev.GravarPeriodicidadeAsync(id, edicao, ct));
        public Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default) => Ir(() => _dev.PesquisarFornecedoresAsync(texto, ct));
    }

    [Fact]
    public async Task Sem_rede_recusa_sem_chamar_o_servidor()
    {
        var interno = new Contador();
        var rede = new RedeEstadoManual();
        rede.Definir(false);
        var s = new GestaoSoOnlineService(interno, rede);

        var erros = new[]
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.ObterOpcoesAsync()),
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarEquipamentoAsync(null, new EdicaoEquipamento(Ref: "x", Design: "y"))),
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarTarefaAsync("I-E1", null, new EdicaoTarefa(Descricao: "x"))),
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.ApagarTarefaAsync("T-E1-1")),
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarPeriodicidadeAsync("P1", new EdicaoPeriodicidade("dias", 1)))
        };

        Assert.All(erros, e => Assert.Equal(MensagensIntervencao.SemRede, e.Message));
        Assert.Equal(0, interno.Chamadas);
    }

    [Fact]
    public async Task Com_rede_passa_e_a_rede_a_falhar_a_meio_diz_para_confirmar_antes_de_repetir()
    {
        var interno = new Contador();
        var s = new GestaoSoOnlineService(interno, new RedeEstadoManual());

        Assert.NotNull(await s.ObterEquipamentoAsync("E1"));
        interno.Falhar = true;

        Assert.Equal(MensagensIntervencao.SemRede, (await Assert.ThrowsAsync<InvalidOperationException>(() => s.ObterEquipamentoAsync("E1"))).Message);
        Assert.Equal(MensagensIntervencao.SemRedeAMeio, (await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarEquipamentoAsync("E1", new EdicaoEquipamento(Marca: "x")))).Message);
    }

    [Fact]
    public async Task Escritas_que_podem_cancelar_planeadas_avisam_a_app_e_as_outras_nao()
    {
        var avisos = 0;
        var s = new GestaoSoOnlineService(new GestaoDevService(), new RedeEstadoManual(), () => { avisos++; return Task.CompletedTask; });

        await s.GravarEquipamentoAsync("E2", new EdicaoEquipamento(Marca: "Linde"));
        await s.GravarInstrucaoAsync(null, "I-E2", new EdicaoInstrucao(Obs: "x"));
        await s.GravarTarefaAsync("I-E2", null, new EdicaoTarefa(Periodo: "Mensal", Descricao: "Lubrificar"));
        var p = (await s.ListarPeriodicidadesAsync()).First();
        await s.GravarPeriodicidadeAsync(p.Id, new EdicaoPeriodicidade("dias", 3));
        await s.ObterOpcoesAsync();

        Assert.Equal(3, avisos);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarEquipamentoAsync("E2", new EdicaoEquipamento(Ref: "CMP-02")));
        Assert.Equal(3, avisos);
    }

    [Fact]
    public async Task Uma_falha_do_aviso_nao_desfaz_a_escrita()
    {
        var s = new GestaoSoOnlineService(new GestaoDevService(), new RedeEstadoManual(), () => throw new HttpRequestException("sem rede"));

        var e = await s.GravarEquipamentoAsync("E2", new EdicaoEquipamento(Marca: "Linde"));

        Assert.Equal("Linde", e.Marca);
    }

    [Fact]
    public async Task Gestao_na_app_limpa_os_contadores_e_atualiza_a_copia_local()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        await c.Svc.ObterContadoresAsync();
        var minhasAntes = c.Remoto.Contar("ListarMinhas");

        await c.Svc.AposEscritaDeGestaoAsync();
        await c.Svc.ObterContadoresAsync();

        Assert.Equal(2, c.Remoto.Contar("Contadores"));
        Assert.Equal(minhasAntes + 1, c.Remoto.Contar("ListarMinhas"));
    }

    [Fact]
    public async Task Recusas_do_servidor_passam_com_a_mensagem_dele()
    {
        var s = new GestaoSoOnlineService(new GestaoDevService(), new RedeEstadoManual());

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => s.GravarEquipamentoAsync(null, new EdicaoEquipamento(Ref: "CMP-02", Design: "Outro")));

        Assert.Equal(MensagensIntervencao.Para("ref_duplicada"), erro.Message);
    }
}
