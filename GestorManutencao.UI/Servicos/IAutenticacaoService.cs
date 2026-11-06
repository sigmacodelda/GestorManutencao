using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

public interface IAutenticacaoService
{
    Task<ResultadoLogin> EntrarAsync(string pin, CancellationToken ct = default);

    // Devolve o utilizador com um token novo, já sem PIN provisório.
    Task<ResultadoLogin> MudarPinAsync(string? token, string pinAtual, string pinNovo, CancellationToken ct = default);
}

// Onde a sessão sobrevive ao fecho da app (SecureStorage no MAUI, memória nos testes).
public interface IArmazemSessao
{
    Task<Utilizador?> LerAsync();
    Task GuardarAsync(Utilizador utilizador);
    Task LimparAsync();
}
