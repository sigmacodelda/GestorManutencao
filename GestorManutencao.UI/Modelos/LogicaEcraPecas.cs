using GestorManutencao.UI.Servicos;

namespace GestorManutencao.UI.Modelos;

// Regras puras dos ecrãs de peças (a UI não referencia o projeto da API).
public static class LogicaEcraPecas
{
    // Um leitor tipo teclado escreve só dígitos e termina com Enter.
    public static bool PareceCodigoDeBarras(string? texto) => texto is { Length: >= 6 } && texto.All(char.IsDigit);

    // Pesquisa sem resultados: com espaços é pesquisa livre; sem espaços, um código (lido ou escrito).
    public static string MensagemSemResultado(string texto) =>
        texto.Trim().Contains(' ') ? $"Nenhuma peça encontrada para {texto.Trim()}" : $"Código não encontrado: {texto.Trim()}";

    // Mesmo texto de RegrasPecas.AvisoStock na API.
    public static string? AvisoStock(decimal pedida, decimal stock)
    {
        if (pedida <= 0) return null;
        if (stock <= 0) return "Sem stock desta peça.";
        return pedida > stock ? $"Stock insuficiente: há {stock:0.###} e pede {pedida:0.###}." : null;
    }
}
