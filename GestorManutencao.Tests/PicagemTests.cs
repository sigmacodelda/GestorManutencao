using GestorManutencao.UI.Modelos;

namespace GestorManutencao.Tests;

public class PicagemTests
{
    private static readonly EstadoPicagem Vazio = new(null, null);

    [Fact]
    public void Peca_e_depois_local_fica_completo()
    {
        var e = Vazio.Aplicar("5600012345");
        Assert.False(e.Completo);
        e = e.Aplicar("LOC-A-02-1-01");
        Assert.True(e.Completo);
        Assert.Equal("5600012345", e.Peca);
        Assert.Equal("LOC-A-02-1-01", e.Local);
    }

    [Fact]
    public void Local_e_depois_peca_fica_completo()
    {
        var e = Vazio.Aplicar("LOC-A-02-1-01").Aplicar("5600012345");
        Assert.True(e.Completo);
    }

    [Fact]
    public void Duas_pecas_seguidas_ficam_com_a_ultima()
    {
        var e = Vazio.Aplicar("111111").Aplicar("222222");
        Assert.Equal("222222", e.Peca);
        Assert.Null(e.Local);
    }

    [Fact]
    public void Dois_locais_ficam_com_o_ultimo()
    {
        var e = Vazio.Aplicar("LOC-A-02-1-01").Aplicar("LOC-B-03-2-04");
        Assert.Equal("LOC-B-03-2-04", e.Local);
        Assert.Null(e.Peca);
    }

    [Fact]
    public void Apostrofos_dao_local_e_espaco_interno_da_peca()
    {
        Assert.Equal(TipoLeitura.Local, LogicaPicagem.Classificar("LOC'A'02'1'01"));
        Assert.Equal(TipoLeitura.Peca, LogicaPicagem.Classificar("AB 123"));
        Assert.Equal(TipoLeitura.Peca, LogicaPicagem.Classificar("5600012345"));
    }

    [Theory]
    [InlineData("2", "2")]
    [InlineData(" 2,5 ", "2.5")]
    [InlineData("2.5", "2.5")]
    [InlineData("0,125", "0.125")]
    [InlineData("12,", "12")]
    public void Quantidade_aceita_virgula_ou_ponto(string texto, string esperado)
    {
        Assert.True(LogicaPicagem.LerQuantidade(texto, out var q));
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), q);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.000")]
    [InlineData("1,000")]
    [InlineData("1.000,5")]
    [InlineData("1,2,3")]
    [InlineData("1,2345")]
    [InlineData("1e3")]
    public void Quantidade_recusa_texto_ambiguo_ou_invalido(string texto) =>
        Assert.False(LogicaPicagem.LerQuantidade(texto, out _));

    [Fact]
    public void Quantidade_proposta_usa_virgula()
    {
        Assert.Equal("2,5", LogicaPicagem.TextoQuantidade(2.5m));
        Assert.Equal("3", LogicaPicagem.TextoQuantidade(3m));
    }

    [Fact]
    public void Leitura_vazia_e_ignorada()
    {
        var e = Vazio.Aplicar("111111");
        Assert.Equal(e, e.Aplicar("  "));
        Assert.Equal(e, e.Aplicar(""));
    }
}
