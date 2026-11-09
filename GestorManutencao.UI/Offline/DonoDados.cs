using GestorManutencao.UI.Servicos;

namespace GestorManutencao.UI.Offline;

// Quem é o dono dos dados guardados no telemóvel; null quando não há sessão.
public interface IDonoDados
{
    string? Dono { get; }
}

// Suposição: o Nome do utilizador identifica o dono (o Utilizador da UI não tem o número de técnico do PHC). A app pode registar outro.
public sealed class DonoDadosSessao(SessaoService sessao) : IDonoDados
{
    public string? Dono => sessao.Utilizador?.Nome;
}
