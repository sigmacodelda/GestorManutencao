namespace GestorManutencao.UI.Servicos;

// Ditado por voz. O reconhecimento é nativo (Android e Windows): o WebView não traz o da web.
public interface IDitado
{
    bool Disponivel { get; }

    // Texto ainda em curso (muda enquanto a pessoa fala) e frase já fechada.
    event Action<string>? Parcial;
    event Action<string>? Frase;

    // O ditado acabou: null = parou a pedido; senão, o motivo para mostrar.
    event Action<string?>? Terminou;

    // null = a ouvir; senão, a mensagem para mostrar (sem permissão, sem rede...).
    Task<string?> IniciarAsync();

    // Termina depois de entregar o que já foi dito; só devolve quando Terminou foi levantado.
    Task PararAsync();
}

public sealed class DitadoNenhum : IDitado
{
    public bool Disponivel => false;
    public event Action<string>? Parcial { add { } remove { } }
    public event Action<string>? Frase { add { } remove { } }
    public event Action<string?>? Terminou { add { } remove { } }
    public Task<string?> IniciarAsync() => Task.FromResult<string?>("O ditado não está disponível neste aparelho.");
    public Task PararAsync() => Task.CompletedTask;
}

public static class TextoDitado
{
    // Põe o que foi dito no fim do texto. "inseridoAntes" é o que o ditado pôs da última vez
    // e ainda não estava fechado: é substituído, e o que a pessoa escreveu entretanto fica.
    public static (string Texto, string Inserido) Aplicar(string atual, string inseridoAntes, string falado, int max)
    {
        atual ??= "";
        var baseTexto = inseridoAntes.Length > 0 && atual.EndsWith(inseridoAntes, StringComparison.Ordinal)
            ? atual[..^inseridoAntes.Length]
            : atual;

        falado = (falado ?? "").Trim();
        if (falado.Length == 0) return (baseTexto, "");

        var semTexto = baseTexto.TrimEnd();
        if (semTexto.Length == 0 || semTexto[^1] is '.' or '!' or '?')
            falado = char.ToUpperInvariant(falado[0]) + falado[1..];

        var inserido = (baseTexto.Length == 0 || char.IsWhiteSpace(baseTexto[^1]) ? "" : " ") + falado;
        if (max > 0 && baseTexto.Length + inserido.Length > max)
            inserido = inserido[..Math.Max(0, max - baseTexto.Length)];

        return (baseTexto + inserido, inserido);
    }
}
