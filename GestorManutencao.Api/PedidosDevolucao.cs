using System.Text.Json;

namespace GestorManutencao.Api;

// Pedido do técnico para o supervisor devolver uma ficha concluída por validar. Não muda o estado da ficha: só o supervisor devolve.
public sealed record PedidoDevolucaoTecnico(string? Motivo);

public sealed record PedidoDevolucaoGuardado(string FichaId, int Tecno, string Motivo, DateTimeOffset Quando);

// Guardado à parte do PHC (não há campo para isto na ficha): sem caminho só vive em memória; com caminho grava-se o ficheiro inteiro a cada alteração.
public interface IRepositorioPedidosDevolucao
{
    void Registar(string fichaId, int tecno, string motivo);
    PedidoDevolucaoGuardado? Obter(string fichaId);
    void Remover(string fichaId);
}

public sealed class RepositorioPedidosDevolucao : IRepositorioPedidosDevolucao
{
    private readonly object _trinco = new();
    private readonly string? _caminho;
    private readonly TimeProvider _relogio;
    private readonly ILogger<RepositorioPedidosDevolucao>? _log;
    private readonly Dictionary<string, PedidoDevolucaoGuardado> _itens = [];

    public RepositorioPedidosDevolucao(TimeProvider relogio, string? caminho = null, ILogger<RepositorioPedidosDevolucao>? log = null)
    {
        _relogio = relogio;
        _caminho = caminho;
        _log = log;
        try
        {
            if (caminho is not null && File.Exists(caminho))
                foreach (var p in JsonSerializer.Deserialize<List<PedidoDevolucaoGuardado?>>(File.ReadAllText(caminho)) ?? [])
                    if (p is { FichaId: not null, Motivo: not null }) _itens[p.FichaId] = p;
        }
        catch (Exception e) { _log?.LogError("Ficheiro de pedidos de devolução ilegível ({Tipo}); começa vazio.", e.GetType().Name); }
    }

    // Repetir o pedido da mesma ficha substitui o anterior (o último motivo vale).
    public void Registar(string fichaId, int tecno, string motivo)
    {
        lock (_trinco)
        {
            _itens[fichaId] = new PedidoDevolucaoGuardado(fichaId, tecno, motivo, _relogio.GetUtcNow());
            Gravar();
        }
    }

    public PedidoDevolucaoGuardado? Obter(string fichaId)
    {
        lock (_trinco) return _itens.GetValueOrDefault(fichaId);
    }

    public void Remover(string fichaId)
    {
        lock (_trinco)
        {
            if (_itens.Remove(fichaId)) Gravar();
        }
    }

    private void Gravar()
    {
        if (_caminho is null) return;
        try
        {
            var pasta = Path.GetDirectoryName(_caminho);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
            var temporario = _caminho + ".tmp";
            File.WriteAllText(temporario, JsonSerializer.Serialize(_itens.Values.ToList()));
            File.Move(temporario, _caminho, overwrite: true);
        }
        catch (Exception e) { _log?.LogError("Não consegui gravar o ficheiro de pedidos de devolução ({Tipo}); confirme a permissão de escrita da pasta.", e.GetType().Name); }
    }
}
