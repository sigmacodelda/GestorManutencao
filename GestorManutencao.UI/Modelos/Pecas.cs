namespace GestorManutencao.UI.Modelos;

public sealed record Peca(string Ref, string Design, string Codigo, string Local, decimal Stock, decimal StMin, decimal Preco)
{
    public bool AbaixoDoMinimo => StMin > 0 && Stock <= StMin;
}

// CampoExiste = false: falta criar stobs.u_manut no PHC. HaMais: a lista foi cortada, pedir mais texto.
public sealed record ResultadoPecas(bool CampoExiste, IReadOnlyList<Peca> Pecas, bool HaMais);

public sealed record EntradaPeca(string Id, string Ref, string Design, decimal Qtt, string Quem, string Hora);

// Regras puras dos ecrãs de peças (a UI não referencia o projeto da API).
public static class LogicaEcras
{
    // Um leitor tipo teclado escreve só dígitos e termina com Enter.
    public static bool PareceCodigoDeBarras(string? texto) => texto is { Length: >= 6 } && texto.All(char.IsDigit);

    // Mesmo texto de RegrasPecas.AvisoStock na API.
    public static string? AvisoStock(decimal pedida, decimal stock)
    {
        if (pedida <= 0) return null;
        if (stock <= 0) return "Sem stock desta peça.";
        return pedida > stock ? $"Stock insuficiente: há {stock:0.###} e pede {pedida:0.###}." : null;
    }
}
