using GestorManutencao.UI.Servicos;

namespace GestorManutencao.UI.Modelos;

// Regras puras dos ecrãs de peças (a UI não referencia o projeto da API).
public static class LogicaEcraPecas
{
    // Mesmo limite de RegrasPecas: a API aceita qualquer valor abaixo de QuantidadeMaxima + 1 (ex.: 999999,5).
    public const decimal QuantidadeMaxima = 999_999m;

    // Sem resposta fiável (rede, timeout, 5xx, 200 ilegível) não se sabe se a entrada ficou gravada.
    public static bool ResultadoIncerto(Exception e) =>
        e is FalhaDeRedePecasException or EntradaIncertaPecasException or TaskCanceledException
        || e is HttpRequestException { StatusCode: not System.Net.HttpStatusCode.Unauthorized };

    // Texto depois de um resultado incerto, conforme a recarga da lista de hoje tenha resultado ou não.
    public static string AvisoResultadoIncerto(bool listaRecarregada) => listaRecarregada
        ? "Confirme na lista de hoje antes de repetir a entrada."
        : "Não foi possível confirmar; verifique a lista antes de repetir.";

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
        if (qtt >= QuantidadeMaxima + 1) return $"A quantidade máxima é {QuantidadeMaxima:0}.";
        if (decimal.Round(qtt.Value, 3) != qtt) return "A quantidade tem no máximo 3 casas decimais.";
        return null;
    }
}
