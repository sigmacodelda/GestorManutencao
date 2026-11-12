namespace GestorManutencao.UI.Servicos;

public sealed record MensagemToast(long Id, string Texto);

// Aviso curto de confirmação ("Equipamento criado") que fica visível 2 segundos e sobrevive à mudança de página.
public sealed class ToastService
{
    public const int Milissegundos = 2000;

    private long _ultimo;

    public event Action<MensagemToast>? Mostrado;

    public void Sucesso(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;
        Mostrado?.Invoke(new MensagemToast(Interlocked.Increment(ref _ultimo), texto.Trim()));
    }
}
