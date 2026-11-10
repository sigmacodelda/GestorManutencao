namespace GestorManutencao.UI.Servicos;

// Câmara nativa. O WebView do Android ignora o "capture" do campo de ficheiro e abre o seletor de ficheiros.
public interface ICamara
{
    bool Disponivel { get; }

    // JPEG já reduzido (lado maior e tamanho máximos); null = a pessoa cancelou.
    // Lança InvalidOperationException com a mensagem a mostrar (sem permissão, sem câmara...).
    Task<byte[]?> TirarFotoAsync(int ladoMaximo, int bytesMaximos);
}

public sealed class CamaraNenhuma : ICamara
{
    public bool Disponivel => false;
    public Task<byte[]?> TirarFotoAsync(int ladoMaximo, int bytesMaximos) =>
        throw new InvalidOperationException("A câmara não está disponível neste aparelho.");
}
