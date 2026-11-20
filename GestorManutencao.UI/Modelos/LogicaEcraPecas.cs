namespace GestorManutencao.UI.Modelos;

// Regras puras dos ecrãs de peças (a UI não referencia o projeto da API).
public static class LogicaEcraPecas
{
    // Mesmos limites de RegrasPecas.QuantidadeValida na API.
    public const decimal QuantidadeMaxima = 999_999m;

    // Um leitor tipo teclado escreve só dígitos e termina com Enter.
    public static bool PareceCodigoDeBarras(string? texto) => texto is { Length: >= 6 } && texto.All(char.IsDigit);

    // Mesmo texto de RegrasPecas.AvisoStock na API.
    public static string? AvisoStock(decimal pedida, decimal stock)
    {
        if (pedida <= 0) return null;
        if (stock <= 0) return "Sem stock desta peça.";
        return pedida > stock ? $"Stock insuficiente: há {stock:0.###} e pede {pedida:0.###}." : null;
    }

    public static string? ValidarQuantidade(decimal? qtt)
    {
        if (qtt is not > 0) return "Indique uma quantidade positiva.";
        if (qtt > QuantidadeMaxima) return $"A quantidade máxima é {QuantidadeMaxima:0}.";
        if (decimal.Round(qtt.Value, 3) != qtt) return "A quantidade tem no máximo 3 casas decimais.";
        return null;
    }
}
