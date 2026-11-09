namespace GestorManutencao.UI.Offline;

public interface IRedeEstado
{
    bool Disponivel { get; }
    event Action<bool>? Alterada;
}

// A app liga isto ao estado de rede do sistema; por defeito assume que há rede.
public sealed class RedeEstadoManual : IRedeEstado
{
    public bool Disponivel { get; private set; } = true;
    public event Action<bool>? Alterada;

    public void Definir(bool disponivel)
    {
        if (disponivel == Disponivel) return;
        Disponivel = disponivel;
        Alterada?.Invoke(disponivel);
    }
}

// Para o ecrã dizer "a mostrar dados de dd/MM HH:mm" quando a última leitura veio da cache.
public interface IEstadoLeitura
{
    bool AMostrarCache { get; }
    DateTimeOffset? DadosDe { get; }
    string? TextoDados { get; }
    event Action? Mudou;
}
