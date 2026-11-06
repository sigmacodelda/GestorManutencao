using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class IntervencoesTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 6);

    private static Intervencao Nova(string id, int dias, EstadoIntervencao estado) =>
        new(id, "Eq " + id, "Zona", TipoIntervencao.Preventiva, Hoje.AddDays(dias), estado);

    [Fact]
    public void Atrasada_so_se_ainda_por_fazer_e_data_passada()
    {
        Assert.True(Nova("a", -1, EstadoIntervencao.Planeada).EstaAtrasada(Hoje));
        Assert.True(Nova("b", -1, EstadoIntervencao.EmCurso).EstaAtrasada(Hoje));
        Assert.False(Nova("c", 0, EstadoIntervencao.Planeada).EstaAtrasada(Hoje));
        Assert.False(Nova("d", -5, EstadoIntervencao.Concluida).EstaAtrasada(Hoje));
        Assert.False(Nova("e", -5, EstadoIntervencao.Cancelada).EstaAtrasada(Hoje));
    }

    [Fact]
    public void Agrupa_em_curso_depois_atrasadas_depois_as_restantes_por_data()
    {
        var grupos = AgrupadorIntervencoes.Agrupar(
        [
            Nova("futura2", 5, EstadoIntervencao.Planeada),
            Nova("atrasada", -2, EstadoIntervencao.Planeada),
            Nova("futura1", 1, EstadoIntervencao.Planeada),
            Nova("curso", -4, EstadoIntervencao.EmCurso)
        ], Hoje);

        Assert.Equal(["Em curso", "Atrasadas", "Para fazer"], grupos.Select(g => g.Titulo));
        Assert.Equal(["futura1", "futura2"], grupos[2].Itens.Select(i => i.Id));
        Assert.Equal("curso", grupos[0].Itens.Single().Id);
    }

    [Fact]
    public void Agrupar_ignora_o_que_ja_nao_ha_para_fazer_e_omite_grupos_vazios()
    {
        var grupos = AgrupadorIntervencoes.Agrupar(
        [
            Nova("c", -1, EstadoIntervencao.Concluida),
            Nova("v", -1, EstadoIntervencao.Validada),
            Nova("x", 2, EstadoIntervencao.Cancelada),
            Nova("p", 3, EstadoIntervencao.Planeada)
        ], Hoje);

        var grupo = Assert.Single(grupos);
        Assert.Equal("Para fazer", grupo.Titulo);
        Assert.Equal("p", grupo.Itens.Single().Id);
    }

    [Fact]
    public async Task Servico_de_desenvolvimento_devolve_dados_e_o_indisponivel_avisa()
    {
        Assert.NotEmpty(await new DevIntervencoesService().ListarMinhasAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => new IntervencoesIndisponiveis().ListarMinhasAsync());
    }
}

public class RegistoTests
{
    private static readonly TimeOnly H8 = new(8, 0), H9 = new(9, 0);

    [Theory]
    [InlineData(null, "x", "Escolha o equipamento.")]
    [InlineData("Eq", " ", "Descreva o problema.")]
    [InlineData("Eq", "Fuga de óleo", null)]
    public void Avaria_exige_equipamento_e_problema(string? equipamento, string? anomalia, string? esperado) =>
        Assert.Equal(esperado, RegrasIntervencao.ValidarAvaria(equipamento, anomalia));

    [Fact]
    public void Conclusao_exige_texto_horas_e_fim_depois_do_inicio()
    {
        Assert.NotNull(RegrasIntervencao.ValidarConclusao("", H8, H9));
        Assert.NotNull(RegrasIntervencao.ValidarConclusao("feito", null, H9));
        Assert.NotNull(RegrasIntervencao.ValidarConclusao("feito", H9, H8));
        Assert.Null(RegrasIntervencao.ValidarConclusao("feito", H8, H8));
        Assert.Null(RegrasIntervencao.ValidarConclusao("feito", H8, H9));
    }

    [Fact]
    public async Task Circuito_planeada_em_curso_concluida_e_nao_salta_passos()
    {
        var s = new DevIntervencoesService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ConcluirAsync("T3", "feito", H8, H9));

        Assert.Equal(EstadoIntervencao.EmCurso, (await s.IniciarAsync("T3")).Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.IniciarAsync("T3"));

        var feita = await s.ConcluirAsync("T3", "Troca de filtro", H8, H9);
        Assert.Equal(EstadoIntervencao.Concluida, feita.Estado);
        Assert.Equal("Troca de filtro", feita.Feito);
    }

    [Fact]
    public async Task Avaria_nasce_em_curso_e_aparece_na_lista()
    {
        var s = new DevIntervencoesService();
        var nova = await s.RegistarAvariaAsync("E5", "Não liga");

        Assert.Equal(EstadoIntervencao.EmCurso, nova.Estado);
        Assert.Equal(TipoIntervencao.Curativa, nova.Tipo);
        Assert.Equal("Expedição", nova.Zona);
        Assert.Contains(await s.ListarMinhasAsync(), i => i.Id == nova.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.RegistarAvariaAsync("", "x"));
    }
}
