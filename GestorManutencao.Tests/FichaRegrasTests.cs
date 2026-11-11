using GestorManutencao.Api;
using E = GestorManutencao.Api.EstadosFicha;

namespace GestorManutencao.Tests;

// Regras copiadas dos testes da LandeiroMaui (ManutencaoRegrasTestes e EstadosIntervencaoTestes), para o equivalente desta API.
public class ManutencaoRegrasPortadasTests
{
    [Fact]
    public void ProximoNumero_ignora_zeros_e_numeros_errados()
    {
        Assert.Equal(397m, RegrasManutencao.ProximoNumeroFicha([0m, 395m, 396m, 21000650m]));
    }

    [Fact]
    public void ProximoNumero_ano_sem_fichas_comeca_em_1()
    {
        Assert.Equal(1m, RegrasManutencao.ProximoNumeroFicha([]));
        Assert.Equal(1m, RegrasManutencao.ProximoNumeroFicha([0m, 21000697m]));
    }

    [Fact]
    public void Totais_somam_interno_e_externo()
    {
        var (totMo, totRef) = RegrasManutencao.Totais(15.645m, 52.5m, 75.02m, 1763.88m);
        Assert.Equal(68.145m, totMo);
        Assert.Equal(1838.90m, totRef);
    }

    [Fact]
    public void CustoMaterial_quantidade_vezes_preco()
    {
        Assert.Equal(92.7m, RegrasManutencao.CustoMaterial(18m, 5.15m));
        Assert.Equal(0.99m, RegrasManutencao.CustoMaterial(3m, 0.33m));
    }

    [Fact]
    public void CustoTempo_horas_vezes_custo_hora()
    {
        Assert.Equal(15.645m, RegrasManutencao.CustoTempo(3.5m, 4.47m));
    }

    [Fact]
    public void Cortar_respeita_o_tamanho_do_campo()
    {
        Assert.Equal("Sempre que necessári", RegrasManutencao.Cortar("Sempre que necessário", RegrasManutencao.TamanhoPeriodo));
        Assert.Equal("Mensal", RegrasManutencao.Cortar("  Mensal  ", RegrasManutencao.TamanhoPeriodo));
        Assert.Equal("", RegrasManutencao.Cortar(null, 10));
    }

    // Aqui devolve o código "externa_sem_documento" (a mensagem fica na UI); a regra é a mesma da LandeiroMaui.
    [Fact]
    public void Externa_so_fecha_com_fornecedor_e_documento_externo()
    {
        Assert.Null(RegrasManutencao.FaltaParaFechar("Interna", 0, ""));
        Assert.Null(RegrasManutencao.FaltaParaFechar("Externa", 7000004072m, "1479/2026"));
        Assert.Equal("externa_sem_documento", RegrasManutencao.FaltaParaFechar("Externa", 0, "1479/2026"));
        Assert.Equal("externa_sem_documento", RegrasManutencao.FaltaParaFechar("Externa ", 123m, "  "));
        Assert.Equal("externa_sem_documento", RegrasManutencao.FaltaParaFechar("externa", 0, null));
    }

    [Fact]
    public void DataOuNula_trata_a_data_vazia_do_phc()
    {
        Assert.Null(RegrasManutencao.DataOuNula(new DateTime(1900, 1, 1)));
        Assert.Null(RegrasManutencao.DataOuNula(null));
        Assert.Equal(new DateTime(2026, 10, 13), RegrasManutencao.DataOuNula(new DateTime(2026, 10, 13)));
    }

    [Theory]
    [InlineData("-----------", "")]
    [InlineData("  --  ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData(" Atlas ", "Atlas")]
    [InlineData("A-B", "A-B")]
    public void Visivel_trata_os_tracos_do_phc_como_vazio(string? texto, string esperado)
    {
        Assert.Equal(esperado, RegrasManutencao.Visivel(texto));
    }

    [Fact]
    public void ProximaDepoisDaUltima_dias_e_meses()
    {
        var u = new DateTime(2026, 1, 31);
        Assert.Equal(new DateTime(2026, 2, 14), RegrasManutencao.ProximaDepoisDaUltima(u, UnidadePeriodo.Dias, 14));
        Assert.Equal(new DateTime(2026, 2, 28), RegrasManutencao.ProximaDepoisDaUltima(u, UnidadePeriodo.Meses, 1));
        Assert.Equal(new DateTime(2027, 1, 31), RegrasManutencao.ProximaDepoisDaUltima(u, UnidadePeriodo.Meses, 12));
        Assert.Null(RegrasManutencao.ProximaDepoisDaUltima(u, UnidadePeriodo.Nenhum, 0));
        Assert.Null(RegrasManutencao.ProximaDepoisDaUltima(new DateTime(1900, 1, 1), UnidadePeriodo.Dias, 7));
        Assert.Equal(UnidadePeriodo.Meses, RegrasManutencao.LerUnidade(" Meses "));
        Assert.Equal(UnidadePeriodo.Nenhum, RegrasManutencao.LerUnidade(""));
    }
}

public class EstadosFichaTests
{
    [Theory]
    [InlineData(E.Planeada, E.EmCurso, true)]
    [InlineData(E.Planeada, E.Cancelada, true)]
    [InlineData(E.EmCurso, E.Concluida, true)]
    [InlineData(E.EmCurso, E.Cancelada, true)]
    [InlineData(E.Concluida, E.Validada, true)]
    [InlineData(E.Concluida, E.EmCurso, true)]
    [InlineData(E.Planeada, E.Validada, false)]
    [InlineData(E.Concluida, E.Cancelada, false)]
    [InlineData(E.Validada, E.EmCurso, false)]
    [InlineData(E.Validada, E.Cancelada, false)]
    [InlineData(E.Cancelada, E.Planeada, false)]
    [InlineData("", E.EmCurso, false)]
    [InlineData("", E.Cancelada, false)]
    [InlineData(" Planeada ", E.EmCurso, true)]
    public void Transicoes(string de, string para, bool esperado) => Assert.Equal(esperado, E.PodeMudar(de, para));

    [Fact]
    public void Validada_e_cancelada_sao_so_de_leitura()
    {
        Assert.True(E.SoLeitura("Validada"));
        Assert.True(E.SoLeitura(" Cancelada "));
        Assert.False(E.SoLeitura("EmCurso"));
        Assert.False(E.SoLeitura(""));
    }

    [Fact]
    public void Atrasada_e_a_vencer()
    {
        var hoje = new DateTime(2026, 11, 5);
        Assert.True(E.Atrasada(E.Planeada, new DateTime(2026, 11, 4), hoje));
        Assert.True(E.Atrasada(E.EmCurso, new DateTime(2026, 10, 1), hoje));
        Assert.False(E.Atrasada(E.Concluida, new DateTime(2026, 10, 1), hoje));
        Assert.True(E.AVencer(E.Planeada, new DateTime(2026, 11, 12), hoje, 7));
        Assert.False(E.AVencer(E.Planeada, new DateTime(2026, 11, 13), hoje, 7));
        Assert.True(E.AVencer(E.Planeada, hoje, hoje, 0));
    }

    [Fact]
    public void Nomes_para_mostrar()
    {
        Assert.Equal("Em curso", E.Nome("EmCurso"));
        Assert.Equal("Concluída", E.Nome("Concluida"));
        Assert.Equal("", E.Nome(""));
    }
}

public class RegrasFichaTests
{
    private static readonly DateTime Dia = new(2026, 11, 9);

    private static FichaLida Ficha(string estado = "EmCurso", int tecno = 101, string tecnos = "", string tipoManut = "Interna", string tipo = "Preventiva") => new(
        "F1", 7, "E1", "Compressor", "Produção", "CMP-02", tipo, tipoManut, "Mensal",
        Dia, estado, false, false, tecno, "Ana Silva", tecnos, Dia.AddHours(8), RegrasFicha.SemData, 0, 0,
        "Fuga", "", "", false, 0, "", "", 0, 0, 0, 0, 0, 0, "");

    private static readonly ConfigManutencao Cfg = new();

    private static (FichaLida? Nova, ResultadoIntervencao Resultado) Aplicar(FichaLida a, PedidoFicha p, bool supervisor, string? nome, bool externosAMao) =>
        RegrasFicha.Aplicar(a, p, supervisor, nome, externosAMao, DateOnly.FromDateTime(Dia));

    [Theory]
    [InlineData("EmCurso", true, false, true)]
    [InlineData("Planeada", true, false, true)]
    [InlineData("Concluida", true, false, true)]
    [InlineData("", true, false, true)]
    [InlineData("Validada", true, true, false)]
    [InlineData("Cancelada", true, true, false)]
    [InlineData("EmCurso", false, true, true)]
    [InlineData("EmCurso", false, false, false)]
    [InlineData("Planeada", false, true, false)]
    [InlineData("Concluida", false, true, false)]
    [InlineData("Validada", false, true, false)]
    public void Quem_pode_editar(string estado, bool supervisor, bool pertence, bool esperado) =>
        Assert.Equal(esperado, RegrasFicha.PodeEditar(estado, supervisor, pertence));

    [Theory]
    [InlineData("Planeada", true, true)]
    [InlineData("EmCurso", true, true)]
    [InlineData("Concluida", true, false)]
    [InlineData("Validada", true, false)]
    [InlineData("Cancelada", true, false)]
    [InlineData("", true, false)]
    [InlineData("Planeada", false, false)]
    [InlineData("EmCurso", false, false)]
    public void Quem_pode_cancelar(string estado, bool supervisor, bool esperado) => Assert.Equal(esperado, RegrasFicha.PodeCancelar(estado, supervisor));

    [Theory]
    [InlineData("", true, true)]
    [InlineData("", false, false)]
    [InlineData("Concluida", true, false)]
    public void Quem_pode_fechar_ficha_antiga(string estado, bool supervisor, bool esperado) => Assert.Equal(esperado, RegrasFicha.PodeFecharAntiga(estado, supervisor));

    [Fact]
    public void Validar_pedido_aceita_o_vazio_e_os_limites()
    {
        Assert.Null(RegrasFicha.ValidarPedido(new PedidoFicha()));
        Assert.Null(RegrasFicha.ValidarPedido(new PedidoFicha(HorasFrio: 999.99m, HorasConf: 0, Problema: new string('a', 200), Resolucao: new string('b', 254), DocExterno: new string('c', 25))));
    }

    [Theory]
    [InlineData(201, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 26)]
    public void Validar_pedido_recusa_textos_maiores_que_as_colunas(int problema, int resolucao, int doc)
    {
        var p = new PedidoFicha(Problema: new string('a', problema), Resolucao: new string('b', resolucao), DocExterno: new string('c', doc));
        Assert.NotNull(RegrasFicha.ValidarPedido(p));
    }

    [Fact]
    public void Validar_pedido_recusa_valores_impossiveis()
    {
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(HorasConf: 1000m)));
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(HorasFrio: -1m)));
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(MoExterna: -1m)));
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(NomeFornecedor: new string('x', 56))));
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(TipoManut: "Mista")));
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(Tecno: 0)));
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(Data: new DateOnly(1900, 1, 1))));
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(HoraInicio: Dia.AddHours(9), HoraFim: Dia.AddHours(8))));
        Assert.Null(RegrasFicha.ValidarPedido(new PedidoFicha(HoraInicio: Dia.AddHours(9), HoraFim: new DateTime(1900, 1, 1))));
    }

    [Fact]
    public void Validar_cancelamento_exige_motivo_ate_240()
    {
        Assert.NotNull(RegrasFicha.ValidarCancelamento(null));
        Assert.NotNull(RegrasFicha.ValidarCancelamento("   "));
        Assert.Null(RegrasFicha.ValidarCancelamento(new string('a', 240)));
        Assert.NotNull(RegrasFicha.ValidarCancelamento(new string('a', 241)));
    }

    [Fact]
    public void Validar_tempo_exige_tecnico_e_horas()
    {
        Assert.Null(RegrasFicha.ValidarTempo(101, 1.5m, 4.47m));
        Assert.Null(RegrasFicha.ValidarTempo(101, 1.5m, null));
        Assert.NotNull(RegrasFicha.ValidarTempo(null, 1, 1));
        Assert.NotNull(RegrasFicha.ValidarTempo(0, 1, 1));
        Assert.NotNull(RegrasFicha.ValidarTempo(101, 0, 1));
        Assert.NotNull(RegrasFicha.ValidarTempo(101, 1000m, 1));
        Assert.NotNull(RegrasFicha.ValidarTempo(101, 1, -0.5m));
    }

    [Fact]
    public void Validar_ficha_manual()
    {
        Assert.Null(RegrasFicha.ValidarFichaManual(new PedidoFichaManual("E1")));
        Assert.NotNull(RegrasFicha.ValidarFichaManual(new PedidoFichaManual(" ")));
        Assert.NotNull(RegrasFicha.ValidarFichaManual(new PedidoFichaManual("E1", TipoManut: "Mista")));
        Assert.NotNull(RegrasFicha.ValidarFichaManual(new PedidoFichaManual("E1", Periodicidade: new string('a', 21))));
        Assert.NotNull(RegrasFicha.ValidarFichaManual(new PedidoFichaManual("E1", Problema: new string('a', 201))));
        Assert.Null(RegrasFicha.ValidarFichaManual(new PedidoFichaManual("E1", Problema: new string('a', 170), OpId: new string('a', 32))));
        Assert.Null(RegrasFicha.ValidarFichaManual(new PedidoFichaManual("E1", Problema: new string('a', RegrasIntervencao.MaxAnomalia), OpId: new string('a', 32))));
        Assert.NotNull(RegrasFicha.ValidarFichaManual(new PedidoFichaManual("E1", OpId: "curto")));
    }

    [Fact]
    public void Aplicar_operador_so_mexe_na_execucao()
    {
        var (nova, r) = Aplicar(Ficha(), new PedidoFicha(HorasFrio: 2.4m, HorasConf: 1.5m, Resolucao: "  Limpo  ", LimRem: true), false, null, true);

        Assert.Equal(ResultadoIntervencao.Ok, r);
        Assert.Equal(2.4m, nova!.HorasFrio);
        Assert.Equal(1.5m, nova.HorasConf);
        Assert.Equal("Limpo", nova.Resolucao);
        Assert.True(nova.LimRem);
        Assert.Equal("Fuga", nova.Problema);
    }

    [Fact]
    public void Aplicar_operador_nao_muda_gestao_mas_pode_repetir_os_valores_atuais()
    {
        var a = Ficha();
        Assert.Equal(ResultadoIntervencao.SemPermissao, Aplicar(a, new PedidoFicha(Tecno: 102), false, "Rui", true).Resultado);
        Assert.Equal(ResultadoIntervencao.SemPermissao, Aplicar(a, new PedidoFicha(Data: DateOnly.FromDateTime(Dia).AddDays(1)), false, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.SemPermissao, Aplicar(a, new PedidoFicha(Tipo: TipoIntervencao.Curativa), false, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.SemPermissao, Aplicar(a, new PedidoFicha(TipoManut: "Externa"), false, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.SemPermissao, Aplicar(a, new PedidoFicha(Periodicidade: "Anual"), false, null, true).Resultado);

        var igual = new PedidoFicha(Tecno: 101, Data: DateOnly.FromDateTime(Dia), Tipo: TipoIntervencao.Preventiva, TipoManut: "interna", Periodicidade: "Mensal");
        Assert.Equal(ResultadoIntervencao.Ok, Aplicar(a, igual, false, null, true).Resultado);
    }

    [Fact]
    public void Aplicar_supervisor_muda_o_tecnico_e_tira_o_novo_principal_dos_adicionais()
    {
        var (nova, r) = Aplicar(Ficha(tecnos: ";102;103;"), new PedidoFicha(Tecno: 102), true, "Rui Costa", true);

        Assert.Equal(ResultadoIntervencao.Ok, r);
        Assert.Equal(102, nova!.Tecno);
        Assert.Equal("Rui Costa", nova.Tecnome);
        Assert.Equal(";103;", nova.Tecnos);
    }

    [Fact]
    public void Aplicar_supervisor_sem_tecnico_valido_recusa_so_se_o_tecnico_mudar()
    {
        Assert.Equal(ResultadoIntervencao.TecnicoInvalido, Aplicar(Ficha(), new PedidoFicha(Tecno: 102), true, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.Ok, Aplicar(Ficha(), new PedidoFicha(Tecno: 101), true, null, true).Resultado);
    }

    [Fact]
    public void Aplicar_supervisor_muda_data_tipos_e_periodicidade()
    {
        var (nova, r) = Aplicar(Ficha(), new PedidoFicha(Data: new DateOnly(2026, 12, 1), Tipo: TipoIntervencao.Curativa, TipoManut: "externa", Periodicidade: " Trimestral "), true, null, true);

        Assert.Equal(ResultadoIntervencao.Ok, r);
        Assert.Equal(new DateTime(2026, 12, 1), nova!.Data);
        Assert.Equal("Curativa", nova.Tipo);
        Assert.Equal("Externa", nova.TipoManut);
        Assert.Equal("Trimestral", nova.Periodo);
    }

    [Fact]
    public void Aplicar_recusa_fim_antes_do_inicio_e_aceita_limpar_o_fim_com_1900()
    {
        Assert.Equal(ResultadoIntervencao.Invalido, Aplicar(Ficha(), new PedidoFicha(HoraFim: Dia.AddHours(7)), true, null, true).Resultado);

        var (nova, r) = Aplicar(Ficha() with { HFim = Dia.AddHours(10) }, new PedidoFicha(HoraFim: new DateTime(1900, 1, 1)), false, null, true);
        Assert.Equal(ResultadoIntervencao.Ok, r);
        Assert.Equal(RegrasFicha.SemData, nova!.HFim);
    }

    [Fact]
    public void Aplicar_externos_a_mao_so_com_compras_desligadas()
    {
        var p = new PedidoFicha(MoExterna: 55m, MatExterno: 5m);

        var comCompras = Aplicar(Ficha(), p, true, null, externosAMao: false).Nova!;
        Assert.Equal(0, comCompras.MoExterna);
        Assert.Equal(0, comCompras.MatExterno);

        var aMao = Aplicar(Ficha(), p, true, null, externosAMao: true).Nova!;
        Assert.Equal(55m, aMao.MoExterna);
        Assert.Equal(5m, aMao.MatExterno);

        // O operador nunca escreve os externos.
        var operador = Aplicar(Ficha(), p, false, null, externosAMao: true).Nova!;
        Assert.Equal(0, operador.MoExterna);
        Assert.Equal(0, operador.MatExterno);
    }

    [Theory]
    [InlineData(false, 0, 0, false)]
    [InlineData(false, -1, 0, true)]
    [InlineData(false, 1, 0, true)]
    [InlineData(false, 0, 1, true)]
    [InlineData(true, -365, 0, false)]
    [InlineData(true, 0, 1, false)]
    [InlineData(true, 0, 2, true)]
    [InlineData(true, -366, 0, true)]
    public void Horas_fora_do_intervalo_para_o_operador_e_para_o_supervisor(bool supervisor, int diasDaFicha, int diasDeHoje, bool fora)
    {
        // diasDaFicha: dia da hora relativamente à data da ficha; diasDeHoje: relativamente a hoje (a ficha é de hoje).
        var hora = Dia.AddDays(diasDaFicha + diasDeHoje).AddHours(9);
        Assert.Equal(fora, RegrasFicha.HoraForaDoIntervalo(hora, Dia, DateOnly.FromDateTime(Dia), supervisor));
    }

    [Fact]
    public void Horas_de_frio_arredondam_a_2_decimais_para_cima_a_meio()
    {
        Assert.Equal(1.01m, Aplicar(Ficha(), new PedidoFicha(HorasFrio: 1.005m), false, null, true).Nova!.HorasFrio);
        Assert.Equal(1.5m, Aplicar(Ficha(), new PedidoFicha(HorasFrio: 1.5m), false, null, true).Nova!.HorasFrio);
        Assert.Equal(999.99m, Aplicar(Ficha(), new PedidoFicha(HorasFrio: 999.99m), false, null, true).Nova!.HorasFrio);
    }

    [Fact]
    public void Ficha_planeada_para_a_semana_seguinte_e_iniciada_hoje_deixa_o_operador_corrigir_as_horas_de_hoje()
    {
        var futura = Ficha() with { Data = Dia.AddDays(7), HIni = Dia.AddHours(8) };

        // O limite inferior é o dia do início gravado (hoje), não o dia da ficha (daqui a uma semana).
        Assert.Equal(ResultadoIntervencao.Ok, Aplicar(futura, new PedidoFicha(HoraFim: Dia.AddHours(10)), false, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.Ok, Aplicar(futura, new PedidoFicha(HoraInicio: Dia.AddHours(7)), false, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.Invalido, Aplicar(futura, new PedidoFicha(HoraInicio: Dia.AddDays(-1).AddHours(7)), false, null, true).Resultado);
        // Sem início gravado, vale o dia da ficha.
        var semInicio = futura with { HIni = RegrasFicha.SemData };
        Assert.Equal(ResultadoIntervencao.Invalido, Aplicar(semInicio, new PedidoFicha(HoraInicio: Dia.AddHours(7)), false, null, true).Resultado);
    }

    [Fact]
    public void Hora_com_outro_dia_recusa_ao_operador_mas_o_supervisor_aceita_o_dia_anterior()
    {
        var ontem = new PedidoFicha(HoraInicio: Dia.AddDays(-1).AddHours(8));
        Assert.Equal(ResultadoIntervencao.Invalido, Aplicar(Ficha(), ontem, false, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.Ok, Aplicar(Ficha(), ontem, true, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.Invalido, Aplicar(Ficha(), new PedidoFicha(HoraFim: Dia.AddDays(3)), false, null, true).Resultado);
        Assert.Equal(ResultadoIntervencao.Invalido, Aplicar(Ficha(), new PedidoFicha(HoraFim: Dia.AddDays(2)), true, null, true).Resultado);
    }

    [Fact]
    public void Hora_antiga_que_nao_mudou_nao_e_revalidada()
    {
        var antiga = Ficha() with { HIni = Dia.AddDays(-400).AddHours(8) };
        Assert.Equal(ResultadoIntervencao.Ok, Aplicar(antiga, new PedidoFicha(HoraInicio: Dia.AddDays(-400).AddHours(8), Obs: "x"), false, null, true).Resultado);
    }

    [Fact]
    public void Operador_nao_ve_custos_de_terceiros_externos_nem_totais()
    {
        var f = Ficha() with { MoInterna = 20, MoExterna = 100, MatInterno = 3, MatExterno = 40, TotMo = 120, TotRef = 43, Versao = "7" };
        var tempos = new[] { new TempoDto("T1", 101, "Ana", 2, 20, 10), new TempoDto("T2", 102, "Rui", 1, 5, 5) };

        var operador = RegrasFicha.Montar(f, tempos, [], new ConfigManutencao(ComprasFichaInt: false), false, true, 101);
        Assert.True(operador.CustosOcultos);
        Assert.False(operador.ExternosAMao);
        Assert.Equal((0m, 0m, 0m, 0m), (operador.MoInterna, operador.MoExterna, operador.TotMo, operador.TotRef));
        Assert.Equal(20m, operador.MaoDeObra[0].Custo);
        Assert.Equal((0m, 0m), (operador.MaoDeObra[1].Custo, operador.MaoDeObra[1].CustoHora));

        var supervisor = RegrasFicha.Montar(f, tempos, [], new ConfigManutencao(ComprasFichaInt: false), true, false);
        Assert.False(supervisor.CustosOcultos);
        Assert.True(supervisor.ExternosAMao);
        Assert.Equal(120m, supervisor.TotMo);
        Assert.Equal(5m, supervisor.MaoDeObra[1].Custo);
        Assert.Equal("7", supervisor.Versao);
    }

    [Fact]
    public void Fornecedor_cabe_em_16_digitos_e_material_em_decimal_16_3()
    {
        Assert.Null(RegrasFicha.ValidarPedido(new PedidoFicha(NoFornecedor: RegrasFicha.MaxNoFornecedor)));
        Assert.NotNull(RegrasFicha.ValidarPedido(new PedidoFicha(NoFornecedor: RegrasFicha.MaxNoFornecedor + 1)));
        Assert.Null(RegrasIntervencao.ValidarMaterial("Cabo", 1000, 1000));
        Assert.NotNull(RegrasIntervencao.ValidarMaterial("Cabo", 999_999, 999_999_999));
    }

    [Fact]
    public void Aplicar_texto_vazio_apaga_e_null_mantem()
    {
        var a = Ficha() with { Obs = "nota", DocExterno = "123", NomeFornec = "Frio, Lda", NoFornec = 7000004072m };

        var mantem = Aplicar(a, new PedidoFicha(), true, null, true).Nova!;
        Assert.Equal("nota", mantem.Obs);
        Assert.Equal(7000004072m, mantem.NoFornec);

        var apaga = Aplicar(a, new PedidoFicha(Obs: "", DocExterno: "", NomeFornecedor: "", NoFornecedor: 0), true, null, true).Nova!;
        Assert.Equal("", apaga.Obs);
        Assert.Equal("", apaga.DocExterno);
        Assert.Equal("", apaga.NomeFornec);
        Assert.Equal(0, apaga.NoFornec);
    }

    [Fact]
    public void Montar_trata_1900_e_tracos_como_vazios()
    {
        var f = Ficha() with { HFim = new DateTime(1900, 1, 1), Problema = "-----------", Obs = "---", NomeFornec = "----", Periodo = "--", Tecnome = "-----------" };

        var d = RegrasFicha.Montar(f, [], [], Cfg, true, true);

        Assert.Null(d.HoraFim);
        Assert.NotNull(d.HoraInicio);
        Assert.Equal("", d.Problema);
        Assert.Equal("", d.Obs);
        Assert.Equal("", d.NomeFornecedor);
        Assert.Equal("", d.Periodicidade);
        Assert.Null(d.Tecnico);
    }

    [Fact]
    public void Montar_ficha_antiga_conta_fechada_como_validada_e_aberta_como_concluida()
    {
        var aberta = RegrasFicha.Montar(Ficha(estado: ""), [], [], Cfg, true, true);
        var fechada = RegrasFicha.Montar(Ficha(estado: "") with { Fechada = true }, [], [], Cfg, true, true);

        Assert.True(aberta.Antiga);
        Assert.Equal(EstadoIntervencao.Concluida, aberta.Estado);
        Assert.Equal(EstadoIntervencao.Validada, fechada.Estado);
        Assert.True(aberta.PodeFecharAntiga);
        Assert.False(aberta.PodeCancelar);
        Assert.True(aberta.PodeEditar);
    }

    [Fact]
    public void Montar_bloqueios_do_operador_e_do_supervisor()
    {
        var operador = RegrasFicha.Montar(Ficha(), [], [], Cfg, false, true);
        Assert.True(operador.PodeEditar);
        Assert.False(operador.PodeEditarGestao);
        Assert.False(operador.PodeCancelar);
        Assert.False(operador.PodeFecharAntiga);

        var supervisor = RegrasFicha.Montar(Ficha(), [], [], Cfg, true, false);
        Assert.True(supervisor.PodeEditar);
        Assert.True(supervisor.PodeEditarGestao);
        Assert.True(supervisor.PodeCancelar);

        var validada = RegrasFicha.Montar(Ficha(estado: "Validada"), [], [], Cfg, true, true);
        Assert.False(validada.PodeEditar);
        Assert.False(validada.PodeEditarGestao);
        Assert.False(validada.PodeCancelar);
    }

    [Fact]
    public void Montar_externos_a_mao_segue_a_configuracao_e_o_motivo_so_em_curso_ou_cancelada()
    {
        Assert.False(RegrasFicha.Montar(Ficha(), [], [], new ConfigManutencao(ComprasFichaInt: true), true, true).ExternosAMao);
        Assert.True(RegrasFicha.Montar(Ficha(), [], [], new ConfigManutencao(ComprasFichaInt: false), true, true).ExternosAMao);

        Assert.Equal("Cancelada: parado", RegrasFicha.Montar(Ficha(estado: "Cancelada") with { ValObs = "Cancelada: parado" }, [], [], Cfg, true, true).Motivo);
        Assert.Equal("Falta material", RegrasFicha.Montar(Ficha() with { ValObs = "Falta material" }, [], [], Cfg, true, true).Motivo);
        Assert.Null(RegrasFicha.Montar(Ficha(estado: "Concluida") with { ValObs = "Falta material" }, [], [], Cfg, true, true).Motivo);
    }

    [Fact]
    public void Externos_so_das_compras_quando_ha_linhas_e_a_configuracao_as_usa()
    {
        var compras = new[]
        {
            new CompraLigadaDto(null, "FF 1", "Frio", "R1", "Mão de obra", 1, 100m, RegrasManutencao.CompraMaoObra),
            new CompraLigadaDto(null, "FF 1", "Frio", "R2", "Peça", 2, 40m, RegrasManutencao.CompraPecas),
            new CompraLigadaDto(null, "NC 2", "Frio", "R2", "Devolução", 1, -10m, RegrasManutencao.CompraPecas),
            new CompraLigadaDto(null, "FF 3", "Frio", "R3", "Outro tipo", 1, 999m, "Outro")
        };

        Assert.Equal((100m, 30m), RegrasManutencao.Externos(true, compras, 1m, 2m));
        Assert.Equal((1m, 2m), RegrasManutencao.Externos(true, [], 1m, 2m));
        Assert.Equal((1m, 2m), RegrasManutencao.Externos(false, compras, 1m, 2m));
    }

    [Fact]
    public void Em_atraso_conta_data_passada_ou_fora_de_prazo()
    {
        var hoje = new DateOnly(2026, 11, 10);
        Assert.True(RegrasManutencao.EmAtraso(hoje.AddDays(-1), null, hoje));
        Assert.True(RegrasManutencao.EmAtraso(hoje, hoje.AddDays(-1), hoje));
        Assert.False(RegrasManutencao.EmAtraso(hoje, hoje, hoje));
        Assert.False(RegrasManutencao.EmAtraso(hoje.AddDays(3), null, hoje));
        Assert.False(RegrasManutencao.EmAtraso(hoje, null, hoje));
    }

    [Fact]
    public void Marcador_da_operacao_separa_se_e_volta_a_juntar_se()
    {
        var marcador = RegrasIntervencao.Marcador("abcdef1234");

        Assert.Equal(marcador, RegrasIntervencao.ExtrairMarcador("Fuga" + marcador));
        Assert.Equal("Fuga", RegrasIntervencao.SemMarcador("Fuga" + marcador));
        Assert.Equal(marcador, RegrasIntervencao.ExtrairMarcador(marcador));
        Assert.Equal("", RegrasIntervencao.ExtrairMarcador("Fuga normal"));
        Assert.Equal("", RegrasIntervencao.ExtrairMarcador("Fuga [op:x]"));
        Assert.Equal("", RegrasIntervencao.ExtrairMarcador(""));
    }
}
