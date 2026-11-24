using GestorManutencao.UI.Modelos;

namespace GestorManutencao.Tests;

public class RecargasUiTests
{
    [Fact]
    public void Primeira_carga_mostra_spinner()
    {
        Assert.True(EstadoRecarga.MostrarSpinner(jaTemDados: false));
        Assert.True(EstadoRecarga.FalhaSubstituiLista(jaTemDados: false));
    }

    [Fact]
    public void Recarga_com_dados_mantem_a_lista_e_a_falha_nao_a_substitui()
    {
        Assert.False(EstadoRecarga.MostrarSpinner(jaTemDados: true));
        Assert.False(EstadoRecarga.FalhaSubstituiLista(jaTemDados: true));
    }
}
