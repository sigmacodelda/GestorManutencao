using System.Globalization;
using GestorManutencao.UI.Temas;

namespace GestorManutencao.Tests;

public class TemaTests
{
    public static IEnumerable<object[]> Temas() => Tema.Disponiveis().Select(t => new object[] { t });

    [Fact]
    public void Existem_os_temas_sigmacode_e_landeiro() =>
        Assert.Contains("landeiro", Tema.Disponiveis());

    [Fact]
    public void Sem_id_usa_o_tema_por_defeito() =>
        Assert.Equal(Tema.TemaPorDefeito, Tema.Carregar(null).Id);

    [Fact]
    public void Tema_inexistente_falha_e_lista_os_disponiveis()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Tema.Carregar("nao-existe"));
        Assert.Contains("landeiro", ex.Message);
    }

    [Theory, MemberData(nameof(Temas))]
    public void Tema_tem_todas_as_chaves_nos_dois_modos(string id)
    {
        var tema = Tema.Carregar(id);
        foreach (var chave in Tema.ChavesObrigatorias)
        {
            Assert.True(tema.Claro.ContainsKey(chave), $"{id} claro sem {chave}");
            Assert.True(tema.Escuro.ContainsKey(chave), $"{id} escuro sem {chave}");
        }
        Assert.Contains("--primario:", tema.Css);
        Assert.Contains("prefers-color-scheme: dark", tema.Css);
    }

    // Garante que um cliente novo não entra com cores ilegíveis (WCAG AA, texto normal 4.5:1).
    [Theory, MemberData(nameof(Temas))]
    public void Tema_cumpre_contraste_minimo(string id)
    {
        var tema = Tema.Carregar(id);
        foreach (var (modo, v) in new[] { ("claro", tema.Claro), ("escuro", tema.Escuro) })
        {
            Contraste(id, modo, v, "texto", "fundo", 4.5);
            Contraste(id, modo, v, "texto", "superficie", 4.5);
            Contraste(id, modo, v, "texto-suave", "superficie", 4.5);
            Contraste(id, modo, v, "texto-suave", "fundo", 4.5);
            Contraste(id, modo, v, "sobre-primario", "primario", 4.5);
            Contraste(id, modo, v, "sobre-primario", "primario-hover", 4.5);
            Contraste(id, modo, v, "sobre-avatar", "avatar-fundo", 4.5);
            Contraste(id, modo, v, "erro", "erro-fundo", 4.5);
            Contraste(id, modo, v, "borda", "superficie", 3.0);
            Contraste(id, modo, v, "foco", "fundo", 3.0);
        }
    }

    private static void Contraste(string id, string modo, IReadOnlyDictionary<string, string> v, string frente, string fundo, double minimo)
    {
        var c = Razao(v[frente], v[fundo]);
        Assert.True(c >= minimo, $"{id}/{modo}: {frente} sobre {fundo} = {c:F2}:1 (mínimo {minimo}:1)");
    }

    private static double Razao(string a, string b)
    {
        var (la, lb) = (Luminancia(a), Luminancia(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminancia(string hex)
    {
        double Canal(int i)
        {
            var c = int.Parse(hex.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canal(0) + 0.7152 * Canal(1) + 0.0722 * Canal(2);
    }
}
