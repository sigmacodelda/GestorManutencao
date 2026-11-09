using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class DitadoTests
{
    [Fact]
    public void Texto_vazio_recebe_a_frase_com_maiuscula()
    {
        var (texto, inserido) = TextoDitado.Aplicar("", "", "troquei o rolamento", 0);
        Assert.Equal("Troquei o rolamento", texto);
        Assert.Equal("Troquei o rolamento", inserido);
    }

    [Fact]
    public void Acrescenta_com_espaco_e_sem_maiuscula_a_meio_da_frase()
    {
        var (texto, _) = TextoDitado.Aplicar("Troquei o rolamento", "", "e lubrifiquei", 0);
        Assert.Equal("Troquei o rolamento e lubrifiquei", texto);
    }

    [Fact]
    public void Depois_de_ponto_final_volta_a_maiuscula()
    {
        var (texto, _) = TextoDitado.Aplicar("Fiz a troca.", "", "testei", 0);
        Assert.Equal("Fiz a troca. Testei", texto);
    }

    [Fact]
    public void Parcial_seguinte_substitui_o_anterior()
    {
        var (t1, i1) = TextoDitado.Aplicar("Fiz", "", "a tro", 0);
        var (t2, _) = TextoDitado.Aplicar(t1, i1, "a troca do motor", 0);
        Assert.Equal("Fiz a troca do motor", t2);
    }

    [Fact]
    public void O_que_a_pessoa_escreveu_durante_o_ditado_fica()
    {
        var (t1, i1) = TextoDitado.Aplicar("", "", "motor", 0);
        var (t2, _) = TextoDitado.Aplicar(t1 + " parado", i1, "motor parado", 0);
        // O parcial já não está no fim: não se apaga nada do que foi escrito.
        Assert.Equal("Motor parado motor parado", t2);
    }

    [Fact]
    public void Respeita_o_maximo_de_caracteres()
    {
        var (texto, _) = TextoDitado.Aplicar("abc", "", "defghijkl", 8);
        Assert.Equal(8, texto.Length);
        Assert.Equal("abc defg", texto);
    }

    [Fact]
    public void Texto_cheio_nao_recebe_nada()
    {
        var (texto, inserido) = TextoDitado.Aplicar("12345", "", "mais", 5);
        Assert.Equal("12345", texto);
        Assert.Equal("", inserido);
    }

    [Fact]
    public void Falado_vazio_retira_so_o_parcial()
    {
        var (t1, i1) = TextoDitado.Aplicar("Fiz", "", "a troca", 0);
        var (t2, i2) = TextoDitado.Aplicar(t1, i1, "  ", 0);
        Assert.Equal("Fiz", t2);
        Assert.Equal("", i2);
    }

    [Fact]
    public void Servico_por_defeito_nao_esta_disponivel() => Assert.False(new DitadoNenhum().Disponivel);
}
