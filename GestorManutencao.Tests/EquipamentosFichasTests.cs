using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.Api;
using GestorManutencao.UI.Servicos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

// Dentro do namespace para o alias ganhar ao namespace GestorManutencao.UI.
using UI = GestorManutencao.UI.Modelos;

// Equipamentos (só leitura), todas as fichas e configuração: regras, rotas e repositório em memória.
public class EquipamentosFichasTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.Today);

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
        [
            (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
            (new UtilizadorApp("rui", "Rui Costa", Perfil.Operador, true, true, Tecno: 102), Hasher.Hash("357159")),
            (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918"))
        ])))));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private Task<HttpClient> OperadorAsync() => ComoAsync("482913");
    private Task<HttpClient> SupervisorAsync() => ComoAsync("603918");

    private sealed record CorpoErro(string? Erro, string? Mensagem);

    private static object Configuracao(int antecedencia = 14) =>
        new { antecedencia, gerarPlaneadas = false, avancaOrdem = true, validarPropria = true, extExigeDoc = false, comprasFichaInt = false };

    // ---- Regras ----

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(90, true)]
    [InlineData(-1, false)]
    [InlineData(91, false)]
    public void Configuracao_valida_a_antecedencia(int dias, bool valida)
    {
        var p = new PedidoConfiguracao(dias, true, true, true, true, true);

        Assert.Equal(valida, RegrasManutencao.ValidarConfiguracao(p) is null);
    }

    [Fact]
    public void Configuracao_exige_todas_as_opcoes()
    {
        Assert.NotNull(RegrasManutencao.ValidarConfiguracao(new PedidoConfiguracao(null, true, true, true, true, true)));
        Assert.NotNull(RegrasManutencao.ValidarConfiguracao(new PedidoConfiguracao(7, true, null, true, true, true)));
        Assert.Null(RegrasManutencao.ValidarConfiguracao(new PedidoConfiguracao(7, false, false, false, false, false)));
    }

    [Fact]
    public void Proxima_da_tarefa_usa_a_mais_recente_entre_feita_e_cancelada()
    {
        var feita = new DateTime(2026, 1, 10);
        var cancelada = new DateTime(2026, 3, 10);

        Assert.Equal(new DateTime(2026, 2, 10), RegrasManutencao.ProximaDaTarefa(feita, null, null, UnidadePeriodo.Meses, 1, null));
        Assert.Equal(new DateTime(2026, 4, 10), RegrasManutencao.ProximaDaTarefa(feita, cancelada, null, UnidadePeriodo.Meses, 1, null));
        Assert.Equal(new DateTime(2026, 1, 17), RegrasManutencao.ProximaDaTarefa(null, null, feita, UnidadePeriodo.Dias, 7, null));
    }

    [Fact]
    public void Proxima_da_tarefa_sem_historico_ou_periodicidade_cai_na_ficha_aberta()
    {
        var aberta = new DateTime(2026, 5, 1);

        Assert.Equal(aberta, RegrasManutencao.ProximaDaTarefa(null, null, null, UnidadePeriodo.Meses, 1, aberta));
        Assert.Equal(aberta, RegrasManutencao.ProximaDaTarefa(new DateTime(2026, 1, 10), null, null, UnidadePeriodo.Nenhum, 0, aberta));
        Assert.Null(RegrasManutencao.ProximaDaTarefa(null, null, null, UnidadePeriodo.Meses, 1, null));
    }

    [Theory]
    [InlineData("EmCurso", false, EstadoIntervencao.EmCurso)]
    [InlineData("Cancelada ", false, EstadoIntervencao.Cancelada)]
    [InlineData("", true, EstadoIntervencao.Validada)]
    [InlineData("", false, EstadoIntervencao.Concluida)]
    public void Fichas_antigas_sem_estado_contam_como_validadas_ou_concluidas(string estado, bool fechada, EstadoIntervencao esperado)
    {
        Assert.Equal(esperado, RegrasManutencao.EstadoDaFicha(estado, fechada));
    }

    // ---- Rotas dos equipamentos ----

    [Fact]
    public async Task Equipamentos_sem_token_da_401_e_com_pin_provisorio_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync("/equipamentos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync("/equipamentos/E1")).StatusCode);

        var provisorio = await ComoAsync("357159");
        Assert.Equal(HttpStatusCode.Forbidden, (await provisorio.GetAsync("/equipamentos")).StatusCode);
    }

    [Fact]
    public async Task Operador_ve_os_equipamentos_sem_obsoletos_e_com_obsoletos_se_pedir()
    {
        var c = await OperadorAsync();

        var ativos = (await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos"))!;
        var todos = (await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?obsoletos=true"))!;

        Assert.Equal(6, ativos.Count);
        Assert.DoesNotContain(ativos, e => e.Obsoleto);
        Assert.Equal(7, todos.Count);
        Assert.Contains(todos, e => e.Obsoleto);
    }

    [Fact]
    public async Task Equipamentos_filtram_por_texto_tipo_e_local()
    {
        var c = await OperadorAsync();

        var porTexto = (await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?q=toyota"))!;
        var porTipo = (await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?tipo=Frio"))!;
        var porLocal = (await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?local=Produ%C3%A7%C3%A3o"))!;

        Assert.Equal("E2", Assert.Single(porTexto).Id);
        Assert.Equal("E3", Assert.Single(porTipo).Id);
        Assert.Equal(new[] { "E1", "E4" }, porLocal.Select(e => e.Id).Order().ToArray());
    }

    [Fact]
    public async Task Equipamentos_trazem_ultima_intervencao_fichas_abertas_e_fora_de_servico()
    {
        var c = await OperadorAsync();

        var lista = (await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos"))!;

        var balanca = lista.Single(e => e.Id == "E5");
        Assert.Equal(Hoje, balanca.UltimaIntervencao);
        Assert.Equal(1, balanca.FichasAbertas);
        Assert.True(lista.Single(e => e.Id == "E6").ForaDeServico);
        Assert.Null(lista.Single(e => e.Id == "E4").UltimaIntervencao);
        Assert.Equal(Hoje.AddDays(-35), lista.Single(e => e.Id == "E1").UltimaIntervencao);
    }

    [Fact]
    public async Task Equipamentos_com_texto_curto_ou_longo_demais_vem_vazio()
    {
        var c = await OperadorAsync();

        Assert.Empty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?q=e"))!);
        Assert.Empty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?q=" + new string('a', 61)))!);
        Assert.Empty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?tipo=" + new string('a', 31)))!);
    }

    [Fact]
    public async Task Ficha_do_equipamento_traz_tarefas_com_proxima_data_e_historico()
    {
        var c = await OperadorAsync();

        var ficha = (await c.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E1"))!;

        Assert.Equal("Compressor de ar 02", ficha.Equipamento.Nome);
        Assert.Equal("AC-77120", ficha.Equipamento.Serie);
        Assert.Equal(2, ficha.Tarefas.Count);
        var mensal = ficha.Tarefas.Single(t => t.Periodicidade == "Mensal");
        Assert.Equal(Hoje.AddDays(-35), mensal.UltimaIntervencao);
        Assert.Equal(Hoje.AddDays(-35).AddMonths(1), mensal.ProximaData);
        Assert.Null(ficha.Tarefas.Single(t => t.Periodicidade == "Semanal").ProximaData);
        Assert.DoesNotContain(ficha.Historico, h => h.Estado == EstadoIntervencao.Planeada);
        Assert.Contains(ficha.Historico, h => h.Id == "H1" && h.Estado == EstadoIntervencao.Validada && h.Feito == "Óleo e filtros em bom estado");
    }

    [Fact]
    public async Task Do_historico_so_se_abre_a_ficha_propria_ou_sem_dono()
    {
        var ana = await OperadorAsync();
        var chefe = await SupervisorAsync();

        var daAna = (await ana.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E1"))!.Historico.Single(h => h.Id == "H1");
        var doChefe = (await chefe.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E1"))!.Historico.Single(h => h.Id == "H1");
        var semDono = (await ana.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E3"))!.Historico.Single(h => h.Id == "H3");

        Assert.True(daAna.PodeAbrir);
        Assert.False(doChefe.PodeAbrir);
        Assert.True(semDono.PodeAbrir);
    }

    [Fact]
    public async Task Ficha_de_equipamento_inexistente_ou_com_id_comprido_da_404()
    {
        var c = await OperadorAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/equipamentos/NAO")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/equipamentos/" + new string('X', 26))).StatusCode);
    }

    // ---- Todas as fichas ----

    [Fact]
    public async Task Todas_as_fichas_so_para_supervisor()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await OperadorAsync()).GetAsync("/intervencoes/todas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync("/intervencoes/todas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await SupervisorAsync()).GetAsync("/intervencoes/todas")).StatusCode);
    }

    [Fact]
    public async Task Todas_as_fichas_filtram_por_estado_texto_tecnico_e_datas()
    {
        var c = await SupervisorAsync();

        var validadas = (await c.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?estado=Validada"))!;
        var texto = (await c.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?q=bateria"))!;
        var tecnico = (await c.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?tecno=100"))!;
        var datas = (await c.GetFromJsonAsync<PaginaFichasDto>($"/intervencoes/todas?desde={Hoje.AddDays(-130):yyyy-MM-dd}&ate={Hoje.AddDays(-100):yyyy-MM-dd}"))!;

        Assert.Equal(new[] { "H1", "H2" }, validadas.Itens.Select(i => i.Id).Order().ToArray());
        Assert.Equal("H2", Assert.Single(texto.Itens).Id);
        Assert.Equal("H2", Assert.Single(tecnico.Itens).Id);
        Assert.Equal("H2", Assert.Single(datas.Itens).Id);
        Assert.False(validadas.TemMais);
    }

    [Fact]
    public async Task Todas_as_fichas_vem_da_mais_recente_para_a_mais_antiga()
    {
        var c = await SupervisorAsync();

        var datas = (await c.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas"))!.Itens.Select(i => i.DataPlaneada).ToList();

        Assert.Equal(datas.OrderByDescending(d => d).ToList(), datas);
    }

    [Fact]
    public async Task Todas_as_fichas_paginam_de_vinte_em_vinte()
    {
        var ana = await OperadorAsync();
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/intervencoes/avaria", new { equipamentoId = "E2", anomalia = "Avaria " + i })).StatusCode);
        var chefe = await SupervisorAsync();

        var primeira = (await chefe.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?pagina=1"))!;
        var segunda = (await chefe.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?pagina=2"))!;

        Assert.Equal(20, primeira.Itens.Count);
        Assert.True(primeira.TemMais);
        Assert.Equal(7, segunda.Itens.Count);
        Assert.False(segunda.TemMais);
        Assert.Empty(primeira.Itens.Select(i => i.Id).Intersect(segunda.Itens.Select(i => i.Id)));
    }

    [Theory]
    [InlineData("estado=Inventado")]
    [InlineData("pagina=0")]
    [InlineData("pagina=501")]
    [InlineData("tecno=-1")]
    [InlineData("tecno=100000")]
    [InlineData("desde=2026-05-10&ate=2026-05-01")]
    [InlineData("desde=ontem")]
    public async Task Todas_as_fichas_recusam_filtros_invalidos(string consulta)
    {
        var c = await SupervisorAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/intervencoes/todas?" + consulta)).StatusCode);
    }

    [Fact]
    public async Task Todas_as_fichas_recusam_texto_demasiado_longo()
    {
        var c = await SupervisorAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/intervencoes/todas?q=" + new string('a', 61))).StatusCode);
    }

    [Fact]
    public async Task Rota_fixa_de_todas_nao_se_confunde_com_o_id_de_uma_ficha()
    {
        var c = await SupervisorAsync();

        // "todas" é a lista; qualquer outro id continua a ser o detalhe de uma ficha.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/intervencoes/todas")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/intervencoes/INEXISTENTE")).StatusCode);
    }

    // ---- Configuração ----

    [Fact]
    public async Task Configuracao_so_para_supervisor()
    {
        var ana = await OperadorAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/configuracao")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync("/configuracao", Configuracao())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync("/configuracao")).StatusCode);
    }

    [Fact]
    public async Task Configuracao_guarda_e_devolve_os_valores()
    {
        var c = await SupervisorAsync();

        var antes = (await c.GetFromJsonAsync<ConfigManutencao>("/configuracao"))!;
        var resposta = await c.PutAsJsonAsync("/configuracao", Configuracao(14));
        var depois = (await c.GetFromJsonAsync<ConfigManutencao>("/configuracao"))!;

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(new ConfigManutencao(14, false, true, true, false, false), depois);
        Assert.NotEqual(antes, depois);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task Configuracao_recusa_antecedencia_fora_dos_limites_com_mensagem(int dias)
    {
        var c = await SupervisorAsync();

        var resposta = await c.PutAsJsonAsync("/configuracao", Configuracao(dias));
        var erro = await resposta.Content.ReadFromJsonAsync<CorpoErro>();

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("pedido_invalido", erro!.Erro);
        Assert.Contains("antecedência", erro.Mensagem);
    }

    [Fact]
    public async Task Configuracao_recusa_pedidos_incompletos_ou_mal_formados()
    {
        var c = await SupervisorAsync();

        var semOpcoes = await c.PutAsJsonAsync("/configuracao", new { antecedencia = 7 });
        var decimais = await c.PutAsJsonAsync("/configuracao", new { antecedencia = 7.5, gerarPlaneadas = true, avancaOrdem = true, validarPropria = true, extExigeDoc = true, comprasFichaInt = true });

        Assert.Equal(HttpStatusCode.BadRequest, semOpcoes.StatusCode);
        Assert.Equal("pedido_invalido", (await semOpcoes.Content.ReadFromJsonAsync<CorpoErro>())!.Erro);
        Assert.Equal(HttpStatusCode.BadRequest, decimais.StatusCode);
    }

    [Fact]
    public async Task Configuracao_recusada_nao_altera_o_que_estava_guardado()
    {
        var c = await SupervisorAsync();
        await c.PutAsJsonAsync("/configuracao", Configuracao(10));

        await c.PutAsJsonAsync("/configuracao", Configuracao(500));

        Assert.Equal(10, (await c.GetFromJsonAsync<ConfigManutencao>("/configuracao"))!.Antecedencia);
    }

    // ---- Repositório em memória ----

    [Fact]
    public async Task Repositorio_lista_equipamentos_por_designacao_como_o_sql()
    {
        var repo = new RepositorioIntervencoesMemoria(TimeProvider.System);

        var lista = await repo.ListarEquipamentosAsync(new FiltroEquipamentos("", "", "", false), default);

        Assert.Equal(lista.Select(e => e.Nome).Order(StringComparer.CurrentCultureIgnoreCase).ToList(), lista.Select(e => e.Nome).ToList());
    }

    [Fact]
    public async Task Pesquisa_leve_da_avaria_devolve_so_id_nome_e_zona_sem_agregados()
    {
        var c = await OperadorAsync();

        var leve = (await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?leve=true&q=balan"))!;

        var balanca = Assert.Single(leve);
        Assert.Equal("E5", balanca.Id);
        Assert.Equal("", balanca.Ref);
        Assert.Null(balanca.UltimaIntervencao);
        Assert.Equal(0, balanca.FichasAbertas);
    }

    [Fact]
    public async Task Pesquisa_leve_pede_texto_e_nao_mostra_obsoletos()
    {
        var c = await OperadorAsync();

        Assert.Empty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?leve=true"))!);
        Assert.Empty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?leve=true&q=e"))!);
        Assert.Empty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?leve=true&q=serra"))!);
    }

    [Fact]
    public async Task Operador_nao_ve_o_conteudo_das_fichas_dos_outros_no_historico()
    {
        var ana = await OperadorAsync();

        var h2 = (await ana.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E2"))!.Historico.Single(h => h.Id == "H2");

        Assert.Equal(EstadoIntervencao.Validada, h2.Estado);
        Assert.Equal(TipoIntervencao.Curativa, h2.Tipo);
        Assert.Equal(Hoje.AddDays(-120), h2.DataPlaneada);
        Assert.Null(h2.Anomalia);
        Assert.Null(h2.Feito);
        Assert.Null(h2.Tecnico);
        Assert.Equal(0, h2.Tecno);
        Assert.Null(h2.HoraInicio);
        Assert.False(h2.PodeAbrir);
    }

    [Fact]
    public async Task Operador_ve_tudo_no_historico_das_suas_fichas_e_supervisor_ve_tudo()
    {
        var ana = await OperadorAsync();
        var chefe = await SupervisorAsync();

        var propria = (await ana.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E1"))!.Historico.Single(h => h.Id == "H1");
        var doSupervisor = (await chefe.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E2"))!.Historico.Single(h => h.Id == "H2");

        Assert.Equal("Óleo e filtros em bom estado", propria.Feito);
        Assert.Equal("Ana Silva", propria.Tecnico);
        Assert.Equal("Substituída a bateria", doSupervisor.Feito);
        Assert.Equal("Não arranca", doSupervisor.Anomalia);
        Assert.Equal("Chefe de Turno", doSupervisor.Tecnico);
    }

    [Fact]
    public async Task Supervisor_ve_o_historico_de_outros_operadores()
    {
        var chefe = await SupervisorAsync();

        var h1 = (await chefe.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E1"))!.Historico.Single(h => h.Id == "H1");

        Assert.Equal("Ana Silva", h1.Tecnico);
        Assert.Equal(101, h1.Tecno);
    }

    [Theory]
    [InlineData("desde=1899-12-31")]
    [InlineData("ate=3000-01-01")]
    [InlineData("desde=0001-01-01")]
    public async Task Todas_as_fichas_recusam_datas_fora_de_1900_a_2999_com_mensagem(string consulta)
    {
        var c = await SupervisorAsync();

        var resposta = await c.GetAsync("/intervencoes/todas?" + consulta);
        var erro = await resposta.Content.ReadFromJsonAsync<CorpoErro>();

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("1900", erro!.Mensagem);
    }

    [Theory]
    [InlineData("desde=1900-01-01")]
    [InlineData("ate=2999-12-31")]
    public async Task Todas_as_fichas_aceitam_as_datas_limite(string consulta)
    {
        var c = await SupervisorAsync();

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/intervencoes/todas?" + consulta)).StatusCode);
    }

    [Fact]
    public async Task Configuracao_guardada_grava_com_o_registo_no_log_ativo()
    {
        var c = await SupervisorAsync();

        // Só garante que o registo no log não rebenta a gravação; o texto (código do supervisor e opções, sem PIN nem nome) não é verificado.
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/configuracao", Configuracao(21))).StatusCode);
        Assert.Equal(21, (await c.GetFromJsonAsync<ConfigManutencao>("/configuracao"))!.Antecedencia);
    }

    [Fact]
    public async Task Repositorio_nao_deixa_registar_avaria_em_equipamento_obsoleto()
    {
        var repo = new RepositorioIntervencoesMemoria(TimeProvider.System);

        var (resultado, _) = await repo.RegistarAvariaAsync("E7", "Ruído", new TecnicoAtual("ana", 101, "Ana Silva"), default);

        Assert.Equal(ResultadoIntervencao.NaoExiste, resultado);
    }

    [Fact]
    public async Task Repositorio_historico_nao_passa_de_cinquenta()
    {
        var repo = new RepositorioIntervencoesMemoria(TimeProvider.System);
        var ana = new TecnicoAtual("ana", 101, "Ana Silva");
        for (var i = 0; i < 60; i++) await repo.RegistarAvariaAsync("E4", "Avaria " + i, ana, default);

        var ficha = await repo.ObterEquipamentoAsync("E4", 101, default);

        Assert.Equal(RegrasManutencao.FichasNoHistorico, ficha!.Historico.Count);
    }

    [Fact]
    public async Task Repositorio_sem_numero_de_tecnico_nao_abre_fichas_do_historico()
    {
        var repo = new RepositorioIntervencoesMemoria(TimeProvider.System);

        var ficha = await repo.ObterEquipamentoAsync("E3", 0, default);

        Assert.All(ficha!.Historico, h => Assert.False(h.PodeAbrir));
    }

    // ---- Serviço de desenvolvimento da UI ----

    [Fact]
    public async Task Dev_lista_equipamentos_com_filtros_e_obsoletos()
    {
        var s = new DevIntervencoesService();

        Assert.Equal(6, (await s.ListarAsync(new UI.FiltroEquipamentos())).Count);
        Assert.Equal(7, (await s.ListarAsync(new UI.FiltroEquipamentos(Obsoletos: true))).Count);
        Assert.Equal("E3", Assert.Single(await s.ListarAsync(new UI.FiltroEquipamentos(Tipo: "Frio"))).Id);
        Assert.Equal("E5", Assert.Single(await s.ListarAsync(new UI.FiltroEquipamentos(Texto: "mettler"))).Id);
    }

    [Fact]
    public async Task Dev_ficha_do_equipamento_tem_tarefas_e_historico()
    {
        var s = new DevIntervencoesService();

        var ficha = await s.ObterFichaAsync("E1");

        Assert.NotNull(ficha);
        Assert.Equal(2, ficha.Tarefas.Count);
        Assert.Contains(ficha.Historico, h => h.Id == "H1");
        Assert.Null(await s.ObterFichaAsync("NAO"));
    }

    [Fact]
    public async Task Dev_todas_as_fichas_filtram_e_pagina()
    {
        var s = new DevIntervencoesService();

        var validadas = await s.ListarTodasAsync(new UI.FiltroFichas(UI.EstadoIntervencao.Validada), 1);
        var vazia = await s.ListarTodasAsync(new UI.FiltroFichas(), 2);

        Assert.Equal(2, validadas.Itens.Count);
        Assert.False(validadas.TemMais);
        Assert.Empty(vazia.Itens);
    }

    [Fact]
    public async Task Dev_configuracao_guarda_e_recusa_valores_invalidos()
    {
        var s = new DevIntervencoesService();

        await s.GuardarConfiguracaoAsync(new UI.ConfiguracaoManutencao(30, false, true, true, false, false));

        Assert.Equal(30, (await s.ObterConfiguracaoAsync()).Antecedencia);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GuardarConfiguracaoAsync(new UI.ConfiguracaoManutencao(91)));
        Assert.Equal(30, (await s.ObterConfiguracaoAsync()).Antecedencia);
    }

    [Fact]
    public async Task Servico_indisponivel_nao_finge_dados()
    {
        var s = new IntervencoesIndisponiveis();

        await Assert.ThrowsAsync<NotSupportedException>(() => s.ListarAsync(new UI.FiltroEquipamentos()));
        await Assert.ThrowsAsync<NotSupportedException>(() => s.ObterFichaAsync("E1"));
        await Assert.ThrowsAsync<NotSupportedException>(() => s.ListarTodasAsync(new UI.FiltroFichas(), 1));
        await Assert.ThrowsAsync<NotSupportedException>(() => s.ObterConfiguracaoAsync());
    }

    // ---- Regras da UI ----

    [Theory]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(-1, false)]
    [InlineData(91, false)]
    [InlineData(null, false)]
    public void UI_valida_a_antecedencia(int? dias, bool valida)
    {
        Assert.Equal(valida, UI.RegrasConfiguracao.Validar(dias) is null);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("a", false)]
    [InlineData("ab", true)]
    public void UI_pesquisa_pede_duas_letras_ou_nada(string texto, bool valido)
    {
        Assert.Equal(valido, UI.RegrasPesquisa.TextoValido(texto));
        Assert.False(UI.RegrasPesquisa.TextoValido(new string('a', 61)));
    }

    [Fact]
    public void UI_sem_intervencao_ha_um_ano_ignora_obsoletos_e_conta_os_que_nunca_tiveram()
    {
        var hoje = new DateOnly(2026, 11, 6);
        var base_ = new UI.Equipamento("E", "Eq", "Zona");

        Assert.True(base_.SemIntervencaoHaUmAno(hoje));
        Assert.True((base_ with { UltimaIntervencao = new DateOnly(2025, 11, 5) }).SemIntervencaoHaUmAno(hoje));
        Assert.False((base_ with { UltimaIntervencao = new DateOnly(2025, 11, 6) }).SemIntervencaoHaUmAno(hoje));
        Assert.False((base_ with { Obsoleto = true }).SemIntervencaoHaUmAno(hoje));
    }

    [Fact]
    public void UI_tarefa_em_atraso_so_com_proxima_data_passada()
    {
        var hoje = new DateOnly(2026, 11, 6);

        Assert.True(new UI.TarefaPlaneada("T", "d", "Mensal", null, hoje.AddDays(-1)).EmAtraso(hoje));
        Assert.False(new UI.TarefaPlaneada("T", "d", "Mensal", null, hoje).EmAtraso(hoje));
        Assert.False(new UI.TarefaPlaneada("T", "d", "Mensal", null, null).EmAtraso(hoje));
    }
}
