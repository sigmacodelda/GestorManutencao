using GestorManutencao.UI.Modelos;

namespace GestorManutencao.Tests;

// Lógica dos ecrãs da fase 2: filtros, KPIs, ordenação, campos editáveis e textos de estado.
public class VistaFichasTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 10);

    private static Intervencao Ficha(string id, EstadoIntervencao estado, int numero, string equipamento = "Compressor", TipoIntervencao tipo = TipoIntervencao.Preventiva,
        decimal total = 0, bool fechada = false, bool antiga = false, bool porSupervisionar = true, int diasAtras = 0, bool foraDeOrdem = false) =>
        new(id, equipamento, "Produção", tipo, Hoje.AddDays(-diasAtras), estado, Numero: numero, Total: total, Fechada: fechada, Antiga: antiga, PorSupervisionar: porSupervisionar, ForaDeOrdem: foraDeOrdem);

    private static readonly Intervencao[] Todas =
    [
        Ficha("A", EstadoIntervencao.Planeada, 1, "Balança", total: 5m, diasAtras: 3),
        Ficha("B", EstadoIntervencao.EmCurso, 2, "Empilhador", TipoIntervencao.Curativa, 20m, diasAtras: 1),
        Ficha("C", EstadoIntervencao.Concluida, 3, "Câmara", total: 10m),
        Ficha("D", EstadoIntervencao.Validada, 4, "Balança", fechada: true, porSupervisionar: false, diasAtras: 40),
        Ficha("E", EstadoIntervencao.Cancelada, 5, "Serra", diasAtras: 20),
        Ficha("F", EstadoIntervencao.Concluida, 6, "Torno", antiga: true, diasAtras: 90),
        Ficha("G", EstadoIntervencao.Validada, 7, "Torno", antiga: true, fechada: true, porSupervisionar: false, diasAtras: 100)
    ];

    private static string[] Ids(FiltroFichas f) => VistaFichas.Aplicar(Todas, f).Select(i => i.Id).ToArray();

    [Fact]
    public void Abertas_sao_as_nao_fechadas_e_nao_canceladas_incluindo_antigas_por_fechar()
    {
        Assert.Equal(["A", "B", "C", "F"], Ids(new FiltroFichas(Situacao: SituacaoFichas.Abertas, Ordem: OrdemFichas.Numero, Descendente: false)));
    }

    [Fact]
    public void Fechadas_incluem_validadas_e_antigas_fechadas()
    {
        Assert.Equal(["D", "G"], Ids(new FiltroFichas(Situacao: SituacaoFichas.Fechadas, Ordem: OrdemFichas.Numero, Descendente: false)));
    }

    [Fact]
    public void Todas_inclui_tambem_as_canceladas()
    {
        Assert.Equal(7, Ids(new FiltroFichas(Situacao: SituacaoFichas.Todas)).Length);
        Assert.Contains("E", Ids(new FiltroFichas()));
    }

    [Fact]
    public void So_por_supervisionar_tipo_e_texto()
    {
        Assert.DoesNotContain("D", Ids(new FiltroFichas(SoPorSupervisionar: true)));
        Assert.Equal(["B"], Ids(new FiltroFichas(Tipo: TipoIntervencao.Curativa)));
        Assert.Equal(["D", "A"], Ids(new FiltroFichas(Texto: "balan", Ordem: OrdemFichas.Numero, Descendente: true)));
        Assert.Equal(["C"], Ids(new FiltroFichas(Texto: "3")));
    }

    [Fact]
    public void Intervalo_de_datas_e_estado()
    {
        Assert.Equal(["C", "B", "A"], Ids(new FiltroFichas(Desde: Hoje.AddDays(-5), Ate: Hoje)));
        Assert.Equal(["B"], Ids(new FiltroFichas(Estado: EstadoIntervencao.EmCurso)));
    }

    [Theory]
    [InlineData(OrdemFichas.Numero, false, "A")]
    [InlineData(OrdemFichas.Numero, true, "G")]
    [InlineData(OrdemFichas.Total, true, "B")]
    [InlineData(OrdemFichas.Equipamento, false, "A")]
    [InlineData(OrdemFichas.Equipamento, true, "G")]
    [InlineData(OrdemFichas.Data, true, "C")]
    [InlineData(OrdemFichas.Data, false, "G")]
    [InlineData(OrdemFichas.Tipo, true, "B")]
    public void Ordenacao_pelo_primeiro(OrdemFichas ordem, bool descendente, string primeiro) =>
        Assert.Equal(primeiro, Ids(new FiltroFichas(Ordem: ordem, Descendente: descendente))[0]);

    [Fact]
    public void Intervalo_omissao_e_o_ultimo_ano_ate_hoje()
    {
        Assert.Equal((new DateOnly(2025, 11, 10), (DateOnly?)Hoje), VistaFichas.IntervaloOmissao(Hoje));
        Assert.Equal((new DateOnly(2025, 11, 10), (DateOnly?)Hoje), VistaFichas.IntervaloOmissao(Hoje, SituacaoFichas.Fechadas));
        // Nas abertas não há limite superior: uma planeada para a semana que vem continua a aparecer.
        Assert.Equal((new DateOnly(2025, 11, 10), (DateOnly?)null), VistaFichas.IntervaloOmissao(Hoje, SituacaoFichas.Abertas));
    }

    [Fact]
    public void Validacao_do_filtro()
    {
        Assert.Null(VistaFichas.Validar(new FiltroFichas()));
        Assert.NotNull(VistaFichas.Validar(new FiltroFichas(Texto: "a")));
        Assert.NotNull(VistaFichas.Validar(new FiltroFichas(Desde: Hoje, Ate: Hoje.AddDays(-1))));
        Assert.NotNull(VistaFichas.Validar(new FiltroFichas(Desde: new DateOnly(1800, 1, 1))));
    }

    [Fact]
    public void Chips_mostram_atrasada_fora_de_ordem_e_o_estado_das_fichas_antigas()
    {
        var atrasada = VistaFichas.Chips(Ficha("x", EstadoIntervencao.Planeada, 1, diasAtras: 2, foraDeOrdem: true), Hoje).Select(c => c.Texto).ToArray();
        Assert.Equal(["Planeada", "Atrasada", "Fora de ordem"], atrasada);

        var antigaAberta = VistaFichas.Chips(Ficha("y", EstadoIntervencao.Concluida, 2, antiga: true), Hoje).Select(c => c.Texto).ToArray();
        Assert.Equal(["Ficha antiga", "Aberta", "Por supervisionar"], antigaAberta);

        var antigaFechada = VistaFichas.Chips(Ficha("z", EstadoIntervencao.Validada, 3, antiga: true, fechada: true, porSupervisionar: false), Hoje).Select(c => c.Texto).ToArray();
        Assert.Equal(["Ficha antiga", "Fechada"], antigaFechada);

        Assert.Equal(["Por validar"], VistaFichas.Chips(Ficha("w", EstadoIntervencao.Concluida, 4), Hoje).Select(c => c.Texto).ToArray());
    }
}

public class VistaEquipamentosTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 10);

    private static readonly Equipamento[] Lista =
    [
        new("1", "Compressor", "Produção", "CMP", 1, "Compressor", UltimaIntervencao: Hoje.AddDays(-10), FichasAbertas: 2),
        new("2", "Balança", "Expedição", "BAL", 2, "Balança", UltimaIntervencao: Hoje.AddDays(-400), ForaDeServico: true),
        new("3", "Empilhador", "Armazém", "EMP", 3, "Empilhador"),
        new("4", "Serra", "Produção", "SRF", 4, "Serra", UltimaIntervencao: Hoje.AddDays(-500), Obsoleto: true)
    ];

    private static string[] Ids(KpiEquipamentos kpi, OrdemEquipamentos ordem = OrdemEquipamentos.Nome, bool desc = false) =>
        VistaEquipamentos.Aplicar(Lista, kpi, ordem, desc, Hoje).Select(e => e.Id).ToArray();

    [Fact]
    public void Totais_dos_indicadores()
    {
        var t = VistaEquipamentos.Contar(Lista, Hoje);

        Assert.Equal(3, t.Ativos);
        Assert.Equal(1, t.ComFichasAbertas);
        Assert.Equal(1, t.ForaDeServico);
        Assert.Equal(2, t.SemIntervencao);
    }

    [Fact]
    public void Cada_indicador_filtra_a_lista()
    {
        Assert.Equal(["2", "1", "3"], Ids(KpiEquipamentos.Ativos));
        Assert.Equal(["1"], Ids(KpiEquipamentos.ComFichasAbertas));
        Assert.Equal(["2"], Ids(KpiEquipamentos.ForaDeServico));
        Assert.Equal(["2", "3"], Ids(KpiEquipamentos.SemIntervencao));
        Assert.Equal(4, Ids(KpiEquipamentos.Nenhum).Length);
    }

    [Fact]
    public void Sem_intervencao_nao_conta_obsoletos()
    {
        Assert.DoesNotContain("4", Ids(KpiEquipamentos.SemIntervencao));
        Assert.Equal(2, VistaEquipamentos.Contar(Lista, Hoje).SemIntervencao);
    }

    [Theory]
    [InlineData(OrdemEquipamentos.Nome, false, "2")]
    [InlineData(OrdemEquipamentos.Nome, true, "4")]
    [InlineData(OrdemEquipamentos.Referencia, false, "2")]
    [InlineData(OrdemEquipamentos.Zona, false, "3")]
    [InlineData(OrdemEquipamentos.FichasAbertas, true, "1")]
    [InlineData(OrdemEquipamentos.UltimaIntervencao, false, "3")]
    [InlineData(OrdemEquipamentos.UltimaIntervencao, true, "1")]
    public void Ordenacao_pelo_primeiro(OrdemEquipamentos ordem, bool desc, string primeiro) =>
        Assert.Equal(primeiro, Ids(KpiEquipamentos.Nenhum, ordem, desc)[0]);
}

public class VistaPlaneadasTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 10);

    private static Intervencao P(string id, EstadoIntervencao estado, int dias, string zona = "Produção", DateOnly? prevista = null, string equipamento = "Compressor", string? tecnico = null) =>
        new(id, equipamento, zona, TipoIntervencao.Preventiva, Hoje.AddDays(dias), estado, Tecnico: tecnico, DataPrevista: prevista);

    private static readonly Intervencao[] Lista =
    [
        P("atrasada", EstadoIntervencao.Planeada, -2),
        P("foraDePrazo", EstadoIntervencao.Planeada, 0, prevista: Hoje.AddDays(-5), zona: "Frio"),
        P("hoje", EstadoIntervencao.Planeada, 0, zona: "Frio", equipamento: "Câmara", tecnico: "Ana"),
        P("limite", EstadoIntervencao.Planeada, 7),
        P("alemDoLimite", EstadoIntervencao.Planeada, 8),
        P("curso", EstadoIntervencao.EmCurso, 3, zona: ""),
        P("cursoAtrasada", EstadoIntervencao.EmCurso, -1)
    ];

    private static string[] Ids(KpiPlaneadas kpi, string zona = "", string texto = "") =>
        VistaPlaneadas.Filtrar(Lista, kpi, zona, texto, Hoje, 7).Select(i => i.Id).ToArray();

    [Fact]
    public void Totais_dos_indicadores()
    {
        var t = VistaPlaneadas.Contar(Lista, Hoje, 7);

        Assert.Equal(7, t.EmAberto);
        Assert.Equal(3, t.ForaDePrazo);
        Assert.Equal(3, t.AVencer);
        Assert.Equal(2, t.EmCurso);
    }

    [Fact]
    public void Fora_de_prazo_usa_a_regra_do_contador_do_inicio_data_passada_ou_prevista_passada()
    {
        Assert.Equal(["atrasada", "foraDePrazo", "cursoAtrasada"], Ids(KpiPlaneadas.ForaDePrazo));
    }

    [Fact]
    public void A_vencer_e_planeada_entre_hoje_e_hoje_mais_a_antecedencia()
    {
        Assert.Equal(["foraDePrazo", "hoje", "limite"], Ids(KpiPlaneadas.AVencer));
        Assert.Equal(["hoje", "foraDePrazo"], VistaPlaneadas.Filtrar(Lista, KpiPlaneadas.AVencer, "", "", Hoje, 0).Select(i => i.Id).OrderBy(i => i == "hoje" ? 0 : 1).ToArray());
    }

    [Fact]
    public void Em_curso_e_zona_e_texto()
    {
        Assert.Equal(["curso", "cursoAtrasada"], Ids(KpiPlaneadas.EmCurso));
        Assert.Equal(["foraDePrazo", "hoje"], Ids(KpiPlaneadas.Nenhum, zona: "Frio"));
        Assert.Equal(["curso"], Ids(KpiPlaneadas.Nenhum, zona: VistaPlaneadas.SemZona));
        Assert.Equal(["hoje"], Ids(KpiPlaneadas.Nenhum, texto: "câmara"));
        Assert.Equal(["hoje"], Ids(KpiPlaneadas.Nenhum, texto: "ana"));
        Assert.Equal(["foraDePrazo"], Ids(KpiPlaneadas.ForaDePrazo, zona: "Frio"));
    }

    [Fact]
    public void Zonas_com_sem_zona_no_fim()
    {
        Assert.Equal(["Frio", "Produção", VistaPlaneadas.SemZona], VistaPlaneadas.Zonas(Lista));
    }
}

public class ContadoresInicioTests
{
    [Fact]
    public void Resumo_do_supervisor_vem_do_endpoint_e_sem_resposta_nao_ha_resumo()
    {
        var linhas = ContadoresInicio.Supervisor(new ContadoresMenu(3, 5));

        Assert.Equal([("Por validar", 5, "validacao", false), ("Atrasadas", 3, "planeadas", true)], linhas.Select(l => (l.Rotulo, l.Valor, l.Href, l.Alerta)).ToArray());
        Assert.Empty(ContadoresInicio.Supervisor(null));
    }
}

public class FormFichaTests
{
    private static readonly DateTime Dia = new(2026, 11, 9);

    private static FichaDetalhe Ficha(bool editar = true, bool gestao = false, bool externosAMao = false) => new(
        "F1", "Compressor", EstadoIntervencao.EmCurso, new DateOnly(2026, 11, 9),
        Numero: 7, TipoManut: "Interna", Periodicidade: "Mensal", Tecno: 101, Tecnico: "Ana",
        HoraInicio: Dia.AddHours(8), HoraFim: Dia.AddHours(9), HorasFrio: 1, HorasConf: 0.5m,
        Problema: "Fuga", Resolucao: "Vedante", Obs: "nota", NoFornecedor: 7000004072, NomeFornecedor: "Frio", DocExterno: "123",
        ExternosAMao: externosAMao, PodeEditar: editar, PodeEditarGestao: gestao, Versao: "9");

    [Fact]
    public void Sem_mexer_nao_ha_alteracoes()
    {
        var d = Ficha();

        Assert.False(FormFicha.De(d).TemAlteracoes(d));
        Assert.Null(FormFicha.De(d).ParaEdicao(d));
    }

    [Fact]
    public void So_segue_o_que_mudou_e_a_versao_lida()
    {
        var d = Ficha();
        var f = FormFicha.De(d);
        f.Resolucao = "  Vedante e filtro  ";
        f.HorasConf = 2m;

        var e = f.ParaEdicao(d)!;

        Assert.Equal("Vedante e filtro", e.Resolucao);
        Assert.Equal(2m, e.HorasConf);
        Assert.Null(e.Problema);
        Assert.Null(e.HoraInicio);
        Assert.Equal("9", e.Versao);
        Assert.True(f.TemAlteracoes(d));
    }

    [Fact]
    public void Apagar_a_hora_de_fim_envia_1900_e_apagar_texto_envia_vazio()
    {
        var d = Ficha();
        var f = FormFicha.De(d);
        f.HoraFim = null;
        f.Obs = "";

        var e = f.ParaEdicao(d)!;

        Assert.Equal(new DateTime(1900, 1, 1), e.HoraFim);
        Assert.Equal("", e.Obs);
    }

    [Fact]
    public void Operador_nunca_envia_campos_de_gestao_nem_externos()
    {
        var d = Ficha(gestao: false, externosAMao: true);
        var f = FormFicha.De(d);
        f.Data = d.Data.AddDays(1); f.Tipo = TipoIntervencao.Curativa; f.TipoManut = "Externa"; f.Periodicidade = "Anual"; f.Tecno = 102;
        f.MoExterna = 10; f.MatExterno = 5;

        var e = f.ParaEdicao(d);

        // Os externos à mão seguem (a API é que decide); os de gestão nunca.
        Assert.NotNull(e);
        Assert.Null(e!.Data);
        Assert.Null(e.Tipo);
        Assert.Null(e.TipoManut);
        Assert.Null(e.Periodicidade);
        Assert.Null(e.Tecno);
    }

    [Fact]
    public void Supervisor_envia_gestao_e_externos_so_se_forem_a_mao()
    {
        var d = Ficha(gestao: true, externosAMao: false);
        var f = FormFicha.De(d);
        f.Data = d.Data.AddDays(1); f.Tecno = 102; f.MoExterna = 10; f.TipoManut = "Externa";

        var e = f.ParaEdicao(d)!;

        Assert.Equal(d.Data.AddDays(1), e.Data);
        Assert.Equal(102, e.Tecno);
        Assert.Equal("Externa", e.TipoManut);
        Assert.Null(e.MoExterna);
        Assert.True(FormFicha.De(Ficha(gestao: true, externosAMao: true)).ParaEdicao(Ficha(gestao: true, externosAMao: true)) is null);
    }

    [Fact]
    public void Ficha_so_de_leitura_nunca_tem_alteracoes_e_a_validacao_apanha_excessos()
    {
        var d = Ficha(editar: false);
        var f = FormFicha.De(d);
        f.Resolucao = "x";

        Assert.False(f.TemAlteracoes(d));

        var d2 = Ficha();
        var g = FormFicha.De(d2);
        g.Problema = new string('a', 201);
        Assert.NotNull(g.Validar(d2));
    }
}

public class SeccoesFichaTests
{
    private static FichaDetalhe D(string tipoManut = "Interna", long fornecedor = 0, string doc = "", bool editar = false, bool ocultos = false, bool aMao = false,
        EstadoIntervencao estado = EstadoIntervencao.EmCurso, bool antiga = false, bool fechada = false) =>
        new("F1", "Compressor", estado, new DateOnly(2026, 11, 9), TipoManut: tipoManut, NoFornecedor: fornecedor, DocExterno: doc, PodeEditar: editar,
            CustosOcultos: ocultos, ExternosAMao: aMao, Antiga: antiga, Fechada: fechada);

    [Fact]
    public void Externo_mostra_se_externa_ou_com_dados_ou_a_quem_pode_editar()
    {
        Assert.False(SeccoesFicha.MostrarExterno(D()));
        Assert.True(SeccoesFicha.MostrarExterno(D(tipoManut: "Externa")));
        Assert.True(SeccoesFicha.MostrarExterno(D(doc: "1")));
        Assert.True(SeccoesFicha.MostrarExterno(D(fornecedor: 5)));
        Assert.True(SeccoesFicha.MostrarExterno(D(editar: true)));
    }

    [Fact]
    public void Compras_e_totais_so_para_quem_ve_os_custos_e_sem_externos_a_mao()
    {
        Assert.True(SeccoesFicha.MostrarCompras(D()));
        Assert.False(SeccoesFicha.MostrarCompras(D(ocultos: true)));
        Assert.False(SeccoesFicha.MostrarCompras(D(aMao: true)));
        Assert.False(SeccoesFicha.MostrarTotais(D(ocultos: true)));
        Assert.True(SeccoesFicha.MostrarTotais(D()));
    }

    [Fact]
    public void Aviso_de_documento_externo_so_com_a_configuracao_e_a_ficha_externa_incompleta()
    {
        var cfg = new ConfiguracaoManutencao(ExtExigeDoc: true);

        Assert.NotNull(SeccoesFicha.AvisoDocumentoExterno(D("Externa"), cfg));
        Assert.NotNull(SeccoesFicha.AvisoDocumentoExterno(D("Externa", 5), cfg));
        Assert.Null(SeccoesFicha.AvisoDocumentoExterno(D("Externa", 5, "1479"), cfg));
        Assert.Null(SeccoesFicha.AvisoDocumentoExterno(D("Interna"), cfg));
        Assert.Null(SeccoesFicha.AvisoDocumentoExterno(D("Externa"), new ConfiguracaoManutencao(ExtExigeDoc: false)));
        Assert.Null(SeccoesFicha.AvisoDocumentoExterno(D("Externa"), null));
    }

    [Fact]
    public void Validar_ou_devolver_so_o_supervisor_nas_concluidas_do_circuito()
    {
        Assert.True(SeccoesFicha.PodeValidarOuDevolver(D(estado: EstadoIntervencao.Concluida), true));
        Assert.False(SeccoesFicha.PodeValidarOuDevolver(D(estado: EstadoIntervencao.Concluida), false));
        Assert.False(SeccoesFicha.PodeValidarOuDevolver(D(estado: EstadoIntervencao.Concluida, antiga: true), true));
        Assert.False(SeccoesFicha.PodeValidarOuDevolver(D(estado: EstadoIntervencao.EmCurso), true));
    }

    [Fact]
    public void Texto_da_situacao_e_conversao_para_a_ficha_da_lista()
    {
        Assert.Equal("Em curso", SeccoesFicha.TextoSituacao(D()));
        Assert.Equal("Ficha antiga aberta", SeccoesFicha.TextoSituacao(D(antiga: true)));
        Assert.Equal("Ficha antiga fechada", SeccoesFicha.TextoSituacao(D(antiga: true, fechada: true)));

        var i = SeccoesFicha.ParaIntervencao(D() with { Problema = "Fuga", Resolucao = "", Numero = 9, TotMo = 5 });
        Assert.Equal("Fuga", i.Anomalia);
        Assert.Null(i.Feito);
        Assert.Equal(9, i.Numero);
        Assert.True(i.PodeValidar);
    }

    [Fact]
    public void Fornecedor_escolhido_leva_numero_e_nome_cortado()
    {
        var (no, nome) = SeccoesFicha.Fornecedor(new Fornecedor(7000004072, new string('x', 80)));

        Assert.Equal(7000004072, no);
        Assert.Equal(RegrasFichaDetalhe.MaxNomeFornecedor, nome.Length);
    }
}
