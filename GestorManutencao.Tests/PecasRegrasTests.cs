using GestorManutencao.Api;

namespace GestorManutencao.Tests;

public class PecasRegrasTests
{
    [Fact]
    public void Palavras_corta_a_quatro_em_minusculas_e_sem_espacos_a_mais()
        => Assert.Equal(["a", "b", "c", "d"], RegrasPecas.Palavras("  A  B c D e "));

    [Fact]
    public void Palavras_vazio_devolve_nenhuma()
    {
        Assert.Empty(RegrasPecas.Palavras(null));
        Assert.Empty(RegrasPecas.Palavras("   "));
    }

    [Fact]
    public void EscaparLike_escapa_curinga_e_barra()
        => Assert.Equal(@"a\%b\_c\[d\\e", RegrasPecas.EscaparLike(@"a%b_c[d\e"));

    [Theory]
    [InlineData(0, 5, null)]
    [InlineData(2, 0, "Sem stock desta peça.")]
    [InlineData(3, 2, "Stock insuficiente: há 2 e pede 3.")]
    [InlineData(2, 2, null)]
    public void AvisoStock_avisa_mas_nunca_bloqueia(double pedida, double disponivel, string? esperado)
        => Assert.Equal(esperado, RegrasPecas.AvisoStock((decimal)pedida, (decimal)disponivel));

    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(-1.0, false)]
    [InlineData(0.5, true)]
    [InlineData(1000000.0, false)]
    [InlineData(1.234, true)]
    [InlineData(0.0005, false)]
    public void QuantidadeValida_positiva_e_abaixo_do_limite(double? q, bool esperado)
        => Assert.Equal(esperado, RegrasPecas.QuantidadeValida(q is null ? null : (decimal)q));

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("P001", true)]
    [InlineData("123456789012345678", true)]
    [InlineData("1234567890123456789", false)]
    public void ReferenciaValida_nao_vazia_e_ate_18_caracteres(string? r, bool esperado)
        => Assert.Equal(esperado, RegrasPecas.ReferenciaValida(r));
}
