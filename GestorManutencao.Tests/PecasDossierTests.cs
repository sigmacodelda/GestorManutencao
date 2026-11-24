using GestorManutencao.Api;
using Xunit;

namespace GestorManutencao.Tests;

public class PecasDossierTests
{
    [Fact]
    public void AjustarACampo_corta_ao_tamanho_da_coluna()
        => Assert.Equal("A-01-2", RegrasPecas.AjustarACampo("A-01-2-05", 6));

    [Fact]
    public void AjustarACampo_sem_corte_em_varchar_max_e_sem_coluna_nao_grava()
    {
        Assert.Equal("Intervenção nº 12", RegrasPecas.AjustarACampo(" Intervenção nº 12 ", -1));
        Assert.Null(RegrasPecas.AjustarACampo("x", null));
        Assert.Null(RegrasPecas.AjustarACampo("x", 0));
    }

    [Fact]
    public void MotivoIntervencao_usa_o_numero_sem_decimais()
        => Assert.Equal("Intervenção nº 123", RegrasPecas.MotivoIntervencao(123.0m));

    [Theory]
    [InlineData(25, true)]
    [InlineData(40, true)]
    [InlineData(-1, true)]
    [InlineData(24, false)]
    [InlineData(0, false)]
    public void MaquinaCabe_so_se_a_coluna_comportar_o_stamp(int tamanho, bool esperado)
        => Assert.Equal(esperado, RegrasPecas.MaquinaCabe(tamanho, new string('a', 25)));

    [Fact]
    public void MaquinaCabe_sem_coluna_nao_grava()
        => Assert.False(RegrasPecas.MaquinaCabe(null, "abc"));
}
