using GestorManutencao.Api;
using static GestorManutencao.Api.Periodicidades;

namespace GestorManutencao.Tests;

// Casos de aceitação das periodicidades da LandeiroMaui (PeriodicidadeTestes), adaptados (hoje = 2026-11-05, horizonte 60 dias, até 2027-01-04).
public class PeriodicidadesPortadasTests
{
    private static readonly DateTime Hoje = new(2026, 11, 5);
    private static readonly DateTime Vazia = new(1900, 1, 1);
    private const string Todos = "111111111111";
    private const string Nenhum = "000000000000";

    private static Plano P(string periodo, string meses, int dia, DateTime ultima, bool ativo = true, bool obsoleto = false, DateTime? inicio = null) =>
        new(periodo, meses, dia, inicio ?? new DateTime(2020, 1, 1), ultima, ativo, obsoleto);

    private static ResultadoGeracao G(Plano p, params DateTime[] existentes) => Gerar(p, Hoje, 60, 1, 7, existentes);

    private static DateTime D(int a, int m, int d) => new(a, m, d);

    [Fact]
    public void Mensal_Dia10_GeraAtrasadaEFuturasAteAoHorizonte()
    {
        var r = G(P("Mensal", Todos, 10, D(2026, 9, 10)));
        Assert.Equal([D(2026, 10, 10), D(2026, 11, 10), D(2026, 12, 10)], r.Criar);
        Assert.Empty(r.Saltadas);
    }

    [Fact]
    public void Mensal_SemMeses_UsaOIntervalo()
    {
        Assert.Equal([D(2026, 10, 10), D(2026, 11, 10), D(2026, 12, 10)], G(P("Mensal", Nenhum, 10, D(2026, 9, 10))).Criar);
    }

    [Fact]
    public void Quinzenal_GeraDe14Em14Dias()
    {
        Assert.Equal([D(2026, 11, 13), D(2026, 11, 27), D(2026, 12, 11), D(2026, 12, 25)], G(P("Quinzenal", Nenhum, 1, D(2026, 10, 30))).Criar);
    }

    [Fact]
    public void Semestral_JanJul_NaoGeraAgoraEProximaEmJaneiro()
    {
        var r = G(P("Semestral", "100000100000", 15, D(2026, 7, 15)));
        Assert.Empty(r.Criar);
        Assert.Equal(D(2027, 1, 15), r.ProximaData);
    }

    [Fact]
    public void GerarDuasVezes_NaoRepete()
    {
        var plano = P("Mensal", Todos, 10, D(2026, 9, 10));
        var primeira = G(plano);
        var segunda = G(plano, [.. primeira.Criar]);
        Assert.Empty(segunda.Criar);
        Assert.Equal(3, segunda.JaExistentes.Count);
    }

    [Fact]
    public void Dia31_EmFevereiro_UsaOUltimoDia()
    {
        Assert.Equal(D(2027, 2, 28), ProximaData(P("Mensal", Nenhum, 31, D(2027, 1, 31))));
        Assert.Equal(D(2028, 2, 29), ProximaData(P("Mensal", Todos, 31, D(2028, 1, 31))));
    }

    [Fact]
    public void SempreQueNecessario_Inativo_Obsoleto_NaoGeram()
    {
        Assert.Empty(G(P("Sempre que necessári", Todos, 1, D(2026, 1, 1))).Criar);
        Assert.Empty(G(P("Mensal", Todos, 1, D(2026, 1, 1), ativo: false)).Criar);
        Assert.Empty(G(P("Mensal", Todos, 1, D(2026, 1, 1), obsoleto: true)).Criar);
        Assert.NotNull(G(P("Mensal", Todos, 1, D(2026, 1, 1), ativo: false)).Motivo);
    }

    [Fact]
    public void DozeMesesDeAtraso_GeraUmaAtrasadaESalta11()
    {
        var r = G(P("Mensal", Nenhum, 1, D(2025, 11, 1)));
        Assert.Equal(11, r.Saltadas.Count);
        Assert.Equal([D(2026, 11, 1), D(2026, 12, 1), D(2027, 1, 1)], r.Criar);
    }

    [Fact]
    public void Semanal_ComMesesMarcados_DeSeteEmSeteSoNessesMeses()
    {
        var r = Gerar(P("Semanal", "111111111100", 1, D(2026, 10, 20)), D(2026, 10, 21), 60, 1, 7, []);
        Assert.Equal([D(2026, 10, 27)], r.Criar);
        Assert.InRange(Ocorrencias(P("Semanal", "111111111100", 1, D(2026, 12, 31)), D(2027, 12, 31)).Count(), 42, 44);
    }

    [Fact]
    public void PlanoSemDatas_ExplicaPorqueNaoGera()
    {
        var r = G(new Plano("Mensal", Todos, 1, Vazia, Vazia, true, false));
        Assert.Empty(r.Criar);
        Assert.NotNull(r.Motivo);
    }

    [Fact]
    public void PeriodicidadeForaDaLista_ERecusada()
    {
        var r = G(P("700 em 700 horas", Nenhum, 1, D(2026, 1, 1)));
        Assert.Empty(r.Criar);
        Assert.Contains("fora da lista", r.Motivo);
    }

    [Fact]
    public void Regular_SoNosMesesMarcados()
    {
        Assert.Empty(G(P("Regular", Nenhum, 1, D(2026, 1, 1))).Criar);
        Assert.Equal([D(2026, 11, 5), D(2026, 12, 5)], G(P("Regular", "000000000011", 5, D(2026, 6, 5))).Criar);
    }

    [Fact]
    public void PlanoNovoSemUltimaData_ComecaNoInicio()
    {
        var r = G(P("Semanal", Nenhum, 1, Vazia, inicio: D(2026, 11, 9)));
        Assert.Equal(D(2026, 11, 9), r.Criar[0]);
        Assert.Equal(9, r.Criar.Count);
        Assert.Equal(D(2027, 1, 4), r.Criar[^1]);
    }

    [Fact]
    public void HorizonteNuncaFicaAbaixoDaMaiorAntecedencia()
    {
        var r = Gerar(P("Mensal", Nenhum, 10, D(2026, 10, 10)), Hoje, 10, 1, 40, []);
        Assert.Equal([D(2026, 11, 10), D(2026, 12, 10)], r.Criar);
    }

    [Fact]
    public void Lista_tem_as_dez_periodicidades_com_textos_de_20_caracteres()
    {
        Assert.Equal(10, Lista.Count);
        Assert.All(Lista, p => Assert.True(p.Periodo.Length <= 20));
        Assert.Equal(UnidadePeriodo.Meses, Obter(" anual ")!.Unidade);
        Assert.Null(Obter("Bienal"));
    }

    [Theory]
    [InlineData("dias", 7, null)]
    [InlineData("meses", 9999, null)]
    [InlineData("nenhum", null, null)]
    [InlineData("nenhum", 0, null)]
    [InlineData(" Dias ", 1, null)]
    public void Unidade_e_intervalo_validos(string unidade, int? intervalo, string? esperado) => Assert.Equal(esperado, ValidarUnidadeEIntervalo(unidade, intervalo));

    [Theory]
    [InlineData("dias", 0)]
    [InlineData("dias", null)]
    [InlineData("meses", -1)]
    [InlineData("meses", 10000)]
    [InlineData("anos", 1)]
    [InlineData("", 1)]
    public void Unidade_e_intervalo_invalidos(string unidade, int? intervalo) => Assert.NotNull(ValidarUnidadeEIntervalo(unidade, intervalo));
}

public class RegrasGestaoTests
{
    private static PedidoEquipamento Valido() => new(Ref: "R-1", Design: "Bomba", Tipo: "Bomba");

    [Fact]
    public void Equipamento_novo_precisa_de_ref_e_designacao()
    {
        Assert.Null(RegrasGestao.ValidarEquipamento(Valido(), true));
        Assert.NotNull(RegrasGestao.ValidarEquipamento(new PedidoEquipamento(Design: "x"), true));
        Assert.NotNull(RegrasGestao.ValidarEquipamento(new PedidoEquipamento(Ref: "x"), true));
        Assert.Null(RegrasGestao.ValidarEquipamento(new PedidoEquipamento(Marca: "x"), false));
        Assert.NotNull(RegrasGestao.ValidarEquipamento(new PedidoEquipamento(Ref: "  "), false));
    }

    [Theory]
    [InlineData("Veículos")]
    [InlineData(" veículos ")]
    [InlineData("VEÍCULOS")]
    public void Viaturas_sao_recusadas(string tipo)
    {
        Assert.True(RegrasGestao.EhViatura(tipo));
        Assert.NotNull(RegrasGestao.ValidarEquipamento(Valido() with { Tipo = tipo }, true));
    }

    [Fact]
    public void Tamanhos_maximos_iguais_aos_do_landeiro()
    {
        Assert.Equal((30, 45, 25, 25, 25, 55), (RegrasGestao.Ref, RegrasGestao.Design, RegrasGestao.Tipo, RegrasGestao.Marca, RegrasGestao.Modelo, RegrasGestao.Serie));
        Assert.Equal((55, 65, 200, 254, 10), (RegrasGestao.NomeFornecedor, RegrasGestao.Local, RegrasGestao.ManualPdf, RegrasGestao.Email, RegrasGestao.Versao));
        Assert.Equal((20, 45, 25, 20), (RegrasGestao.Periodo, RegrasGestao.Responsabilidade, RegrasGestao.Responsavel, RegrasGestao.TipoManut));
        Assert.NotNull(RegrasGestao.ValidarEquipamento(Valido() with { Ref = new string('a', 31) }, true));
        Assert.Null(RegrasGestao.ValidarEquipamento(Valido() with { Ref = new string('a', 30) }, true));
        Assert.NotNull(RegrasGestao.ValidarEquipamento(Valido() with { Local = new string('l', 66) }, true));
        Assert.NotNull(RegrasGestao.ValidarEquipamento(Valido() with { ManualPdf = new string('m', 201) }, true));
    }

    [Theory]
    [InlineData("Em Serviço", true)]
    [InlineData("fora de serviço", true)]
    [InlineData("Parado", false)]
    public void Estado_e_um_dos_dois(string estado, bool valido) => Assert.Equal(valido, RegrasGestao.NormalizarEstado(estado) is not null);

    [Fact]
    public void Aplicar_mantem_o_que_vem_a_null_apaga_com_vazio_e_normaliza()
    {
        var atual = RegrasGestao.EquipamentoVazio(new DateOnly(2026, 11, 10)) with { Ref = "R-1", Design = "Bomba", Marca = "KSB", Estado = "Em Serviço" };

        var mantem = RegrasGestao.AplicarEquipamento(atual, new PedidoEquipamento());
        var muda = RegrasGestao.AplicarEquipamento(atual, new PedidoEquipamento(Marca: "", Local: "  Frio ", Estado: "fora de serviço", Ano: 2020));

        Assert.Equal(atual, mantem);
        Assert.Equal("", muda.Marca);
        Assert.Equal("Frio", muda.Local);
        Assert.Equal("Fora de Serviço", muda.Estado);
        Assert.Equal(2020, muda.Ano);
    }

    [Fact]
    public void Tarefa_pede_descricao_e_tipo_valido_e_tamanhos()
    {
        Assert.NotNull(RegrasGestao.ValidarTarefa(new PedidoTarefa(Periodo: "Mensal"), true));
        Assert.Null(RegrasGestao.ValidarTarefa(new PedidoTarefa(Descricao: "x"), true));
        Assert.Null(RegrasGestao.ValidarTarefa(new PedidoTarefa(Ativa: false), false));
        Assert.NotNull(RegrasGestao.ValidarTarefa(new PedidoTarefa(Descricao: "  "), false));
        Assert.NotNull(RegrasGestao.ValidarTarefa(new PedidoTarefa(Descricao: "x", TipoManut: "Mista"), true));
        Assert.Null(RegrasGestao.ValidarTarefa(new PedidoTarefa(Descricao: "x", TipoManut: "externa"), true));
        Assert.NotNull(RegrasGestao.ValidarTarefa(new PedidoTarefa(Descricao: "x", Responsabilidade: new string('r', 46)), true));
        Assert.NotNull(RegrasGestao.ValidarTarefa(new PedidoTarefa(Descricao: "x", Responsavel: new string('r', 26)), true));
    }

    [Theory]
    [InlineData("Externa", "", "Externa")]
    [InlineData("", "Empresa Externa", "Externa")]
    [InlineData("", "Equipa interna", "Interna")]
    [InlineData(null, null, "Interna")]
    public void Tipo_da_tarefa_deduz_se_da_responsabilidade(string? tipo, string? responsabilidade, string esperado) =>
        Assert.Equal(esperado, RegrasGestao.TipoManutDaTarefa(tipo, responsabilidade));

    [Fact]
    public void Instrucao_valida_versao_e_observacoes()
    {
        Assert.Null(RegrasGestao.ValidarInstrucao(new PedidoInstrucao(Versao: new string('v', 10))));
        Assert.NotNull(RegrasGestao.ValidarInstrucao(new PedidoInstrucao(Versao: new string('v', 11))));
        Assert.NotNull(RegrasGestao.ValidarInstrucao(new PedidoInstrucao(Data: new DateOnly(1800, 1, 1))));
    }

    [Fact]
    public void Periodicidade_nenhuma_grava_intervalo_zero_e_as_outras_o_pedido()
    {
        Assert.Equal(("nenhum", 0), RegrasGestao.NormalizarPeriodicidade(new PedidoPeriodicidade("Nenhum", 5)));
        Assert.Equal(("dias", 30), RegrasGestao.NormalizarPeriodicidade(new PedidoPeriodicidade(" Dias ", 30)));
        Assert.NotNull(RegrasGestao.ValidarPeriodicidade(new PedidoPeriodicidade(null, 1)));
    }

    [Fact]
    public void A_ordem_do_sql_nunca_leva_valores_do_utilizador()
    {
        Assert.StartsWith(" order by ", SqlRepositorioIntervencoes.OrdenarTodas(OrdemFichas.Total, true));
    }
}
