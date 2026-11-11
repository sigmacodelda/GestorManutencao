using GestorManutencao.Api;

namespace GestorManutencao.Tests;

// Regra de geração das planeadas, a mesma do Landeiro (PeriodicidadeHelper.Decidir / DecidirComInicio).
public class RegrasGeracaoTests
{
    private static readonly DateTime Hoje = new(2026, 11, 11);
    private static readonly DateTime Inicio = new(2026, 1, 1);

    [Fact]
    public void Por_chegar_gera_na_data_devida()
    {
        var d = RegrasManutencao.Decidir(new DateTime(2026, 11, 5), UnidadePeriodo.Dias, 14, Hoje, 12);
        Assert.Equal(DecisaoGeracao.Gerar, d.Decisao);
        Assert.Equal(new DateTime(2026, 11, 19), d.Data);
    }

    [Fact]
    public void Atrasada_dentro_do_limite_gera_para_hoje()
    {
        var d = RegrasManutencao.Decidir(new DateTime(2026, 6, 1), UnidadePeriodo.Meses, 1, Hoje, 12);
        Assert.Equal(DecisaoGeracao.Gerar, d.Decisao);
        Assert.Equal(Hoje, d.Data);
        Assert.Equal(new DateTime(2026, 7, 1), d.Devia);
    }

    [Fact]
    public void Atrasada_alem_do_limite_ou_sem_historico_fica_por_regularizar()
    {
        Assert.Equal(DecisaoGeracao.PorRegularizar, RegrasManutencao.Decidir(new DateTime(2024, 1, 1), UnidadePeriodo.Meses, 1, Hoje, 12).Decisao);
        Assert.Equal(DecisaoGeracao.PorRegularizar, RegrasManutencao.Decidir(null, UnidadePeriodo.Meses, 1, Hoje, 12).Decisao);
    }

    [Fact]
    public void Limite_zero_nunca_deixa_por_regularizar()
    {
        var d = RegrasManutencao.Decidir(new DateTime(2020, 1, 1), UnidadePeriodo.Meses, 1, Hoje, 0);
        Assert.Equal(DecisaoGeracao.Gerar, d.Decisao);
        Assert.Equal(Hoje, d.Data);
    }

    [Fact]
    public void Sem_periodicidade_nao_gera()
    {
        Assert.Equal(DecisaoGeracao.SemPeriodicidade, RegrasManutencao.Decidir(Hoje, UnidadePeriodo.Nenhum, 0, Hoje, 12).Decisao);
        Assert.Equal(DecisaoGeracao.SemPeriodicidade, RegrasManutencao.DecidirComInicio(null, Inicio, UnidadePeriodo.Nenhum, 0, Hoje, 12).Decisao);
    }

    [Fact]
    public void Sem_registo_desde_o_inicio_parte_do_calendario_sem_recuperar_o_passado()
    {
        // Início 01/01, mensal: as datas de fevereiro a novembro já passaram; a próxima do calendário é 01/12.
        var d = RegrasManutencao.DecidirComInicio(null, Inicio, UnidadePeriodo.Meses, 1, Hoje, 12);
        Assert.Equal(DecisaoGeracao.Gerar, d.Decisao);
        Assert.Equal(new DateTime(2026, 12, 1), d.Data);

        // Uma feita antes do início conta como sem registo.
        var antiga = RegrasManutencao.DecidirComInicio(new DateTime(2025, 6, 1), Inicio, UnidadePeriodo.Meses, 1, Hoje, 12);
        Assert.Equal(new DateTime(2026, 12, 1), antiga.Data);
    }

    [Fact]
    public void Com_registo_desde_o_inicio_vale_a_regra_normal()
    {
        var d = RegrasManutencao.DecidirComInicio(new DateTime(2026, 9, 1), Inicio, UnidadePeriodo.Meses, 1, Hoje, 12);
        Assert.Equal(Hoje, d.Data); // devia 01/10, já passou: hoje
    }

    [Fact]
    public void Geracao_sem_inicio_mantem_a_regra_antiga()
    {
        var cfg = new ConfigManutencao();
        Assert.Equal(Hoje, RegrasManutencao.DecidirGeracao(null, UnidadePeriodo.Meses, 1, Hoje, cfg).Data); // sem histórico: hoje
        Assert.Equal(Hoje, RegrasManutencao.DecidirGeracao(new DateTime(2020, 1, 1), UnidadePeriodo.Meses, 1, Hoje, cfg).Data); // atrasada: hoje
        Assert.Equal(new DateTime(2026, 11, 19), RegrasManutencao.DecidirGeracao(new DateTime(2026, 11, 5), UnidadePeriodo.Dias, 14, Hoje, cfg).Data);
    }

    [Fact]
    public void Por_regularizar_so_fica_a_espera_com_a_regularizacao_ligada()
    {
        var velha = new DateTime(2024, 1, 1);
        var desligada = new ConfigManutencao(DataInicio: new DateTime(2023, 1, 1), MesesRegularizar: 12, Regularizacao: false);
        var ligada = desligada with { Regularizacao = true };

        var a = RegrasManutencao.DecidirGeracao(velha, UnidadePeriodo.Meses, 1, Hoje, desligada);
        Assert.Equal(DecisaoGeracao.Gerar, a.Decisao);
        Assert.Equal(Hoje, a.Data);
        Assert.Equal(DecisaoGeracao.PorRegularizar, RegrasManutencao.DecidirGeracao(velha, UnidadePeriodo.Meses, 1, Hoje, ligada).Decisao);
    }
}
