using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class LeituraTests
{
    [Fact]
    public void NormalizarCodigo_tira_CR_LF_e_espacos()
    {
        Assert.Equal("5600", LogicaLeitura.NormalizarCodigo("  5600\r\n"));
        Assert.Equal("AB12", LogicaLeitura.NormalizarCodigo("A B\t1\u00002\r"));
    }

    [Fact]
    public void NormalizarCodigo_null_e_vazio()
    {
        Assert.Equal("", LogicaLeitura.NormalizarCodigo(null));
        Assert.Equal("", LogicaLeitura.NormalizarCodigo(" \r\n"));
    }

    [Fact]
    public void NormalizarCodigo_nao_troca_separadores_do_codigo_de_peca()
    {
        Assert.Equal("RL-6204/2RS'X", LogicaLeitura.NormalizarCodigo("RL-6204/2RS'X\n"));
    }

    [Theory]
    [InlineData(ModoLeitura.Automatico, true, ModoLeitura.Leitor)]
    [InlineData(ModoLeitura.Automatico, false, ModoLeitura.Camara)]
    [InlineData(ModoLeitura.Camara, true, ModoLeitura.Camara)]
    [InlineData(ModoLeitura.Camara, false, ModoLeitura.Camara)]
    [InlineData(ModoLeitura.Leitor, true, ModoLeitura.Leitor)]
    [InlineData(ModoLeitura.Leitor, false, ModoLeitura.Leitor)]
    public void ModoEfetivo_nunca_devolve_automatico(ModoLeitura escolhido, bool pareceLeitor, ModoLeitura esperado) =>
        Assert.Equal(esperado, LogicaLeitura.ModoEfetivo(escolhido, pareceLeitor));

    [Theory]
    [InlineData("Zebra Technologies", "TC21")]
    [InlineData("ZEBRA TECHNOLOGIES", "tc21")]
    [InlineData("zebra technologies", "TC21")]
    [InlineData("Honeywell", "CT40")]
    [InlineData("Urovo", "DT50")]
    [InlineData("Symbol Technologies", "MC33")]
    [InlineData("Motorola Solutions", "TC56")]
    [InlineData("Sunmi", "L2s")]
    public void PareceLeitorIndustrial_por_fabricante_ou_modelo(string fabricante, string modelo) =>
        Assert.True(LogicaLeitura.PareceLeitorIndustrial(fabricante, modelo, []));

    [Theory]
    [InlineData("samsung", "SM-A546B")]
    [InlineData("motorola", "moto g54")]
    [InlineData("Sunmi", "V2")]
    [InlineData("", "")]
    public void PareceLeitorIndustrial_telemoveis_normais_nao(string fabricante, string modelo) =>
        Assert.False(LogicaLeitura.PareceLeitorIndustrial(fabricante, modelo, ["com.android.chrome"]));

    [Theory]
    [InlineData("com.symbol.datawedge")]
    [InlineData("com.honeywell.decode.service")]
    [InlineData("com.urovo.scanner")]
    public void PareceLeitorIndustrial_por_pacote(string pacote) =>
        Assert.True(LogicaLeitura.PareceLeitorIndustrial("samsung", "SM-A546B", ["com.android.chrome", pacote]));

    [Theory]
    [InlineData("LOC-A-03-2")]
    [InlineData("LOC-A-03-2-04")]
    [InlineData("loc-a-03-2")]
    [InlineData("LOC'A'03'2")]
    [InlineData("LOC'A'03'2'04")]
    [InlineData("  LOC;A;03;2\r\n")]
    [InlineData("LOC--A-03-2")]
    public void EEtiquetaLocal_aceita_local_mesmo_com_separador_trocado(string texto) =>
        Assert.True(LogicaLeitura.EEtiquetaLocal(texto));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5600123456")]
    [InlineData("LOC-A-03")]
    [InlineData("LOC-A-03-2-04-1")]
    [InlineData("LOC-A-XX-2")]
    [InlineData("LOC-A-03-0")]
    [InlineData("LOC-A-03-2-0")]
    [InlineData("LOCAL-A-03-2")]
    [InlineData("RL-6204-2RS-1")]
    public void EEtiquetaLocal_recusa_o_que_nao_e_local(string? texto) =>
        Assert.False(LogicaLeitura.EEtiquetaLocal(texto));
}
