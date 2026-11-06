using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Contrato do login. A implementação real fica pendente da decisão D-10 (API fina ou conta mínima).
public interface IAutenticacaoService
{
    Task<ResultadoLogin> EntrarAsync(string utilizador, string password, CancellationToken ct = default);
}

// Onde a sessão sobrevive ao fecho da app (SecureStorage no MAUI, memória no anfitrião web).
public interface IArmazemSessao
{
    Task<Utilizador?> LerAsync();
    Task GuardarAsync(Utilizador utilizador);
    Task LimparAsync();
}
