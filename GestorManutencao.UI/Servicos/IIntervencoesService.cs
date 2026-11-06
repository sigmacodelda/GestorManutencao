using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Lançam NotSupportedException se a fonte ainda não existe e InvalidOperationException se o estado não o permite.
public interface IIntervencoesService
{
    // Intervenções atribuídas ao utilizador autenticado.
    Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default);
    Task<Intervencao?> ObterAsync(string id, CancellationToken ct = default);
    // A avaria nasce logo em curso, com a hora de início de agora.
    Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, CancellationToken ct = default);
    Task<Intervencao> IniciarAsync(string id, CancellationToken ct = default);
    Task<Intervencao> ConcluirAsync(string id, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct = default);
    // Só para supervisores: fichas concluídas por validar e a validação (quem concluiu não valida a sua).
    Task<IReadOnlyList<Intervencao>> ListarPorValidarAsync(CancellationToken ct = default);
    Task<Intervencao> ValidarAsync(string id, CancellationToken ct = default);
}

public interface IEquipamentosService
{
    Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default);
}

// Só para desenvolvimento: dados fictícios em memória, até a ligação ao PHC estar confirmada.
public sealed class DevIntervencoesService : IIntervencoesService, IEquipamentosService
{
    private readonly object _trinco = new();
    private readonly List<Intervencao> _lista;
    private static readonly Equipamento[] Equipamentos =
    [
        new("E1", "Compressor de ar 02", "Produção"), new("E2", "Empilhador 05", "Armazém"), new("E3", "Câmara de frio 01", "Frio"),
        new("E4", "Linha de embalagem", "Produção"), new("E5", "Balança 03", "Expedição"), new("E6", "Porta seccionada 07", "Armazém")
    ];

    public DevIntervencoesService()
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        _lista =
        [
            new("T1", "Compressor de ar 02", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(-3), EstadoIntervencao.Planeada),
            new("T2", "Empilhador 05", "Armazém", TipoIntervencao.Curativa, hoje, EstadoIntervencao.EmCurso, "Trava a meio da subida", null, new TimeOnly(8, 30)),
            new("T3", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje, EstadoIntervencao.Planeada),
            new("T4", "Linha de embalagem", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(4), EstadoIntervencao.Planeada),
            new("T5", "Balança 03", "Expedição", TipoIntervencao.Preventiva, hoje.AddDays(-10), EstadoIntervencao.Concluida, null, "Trocado o cabo de ligação", new TimeOnly(9, 0), new TimeOnly(10, 15), "Ana Silva")
        ];
    }

    public async Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        lock (_trinco) return _lista.ToList();
    }

    public Task<Intervencao?> ObterAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult(_lista.FirstOrDefault(i => i.Id == id));
    }

    public Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, CancellationToken ct = default)
    {
        if (RegrasIntervencao.ValidarAvaria(equipamentoId, anomalia) is not null) throw new InvalidOperationException();
        lock (_trinco)
        {
            var eq = Equipamentos.FirstOrDefault(e => e.Id == equipamentoId) ?? throw new InvalidOperationException();
            var nova = new Intervencao(Guid.NewGuid().ToString("N"), eq.Nome, eq.Zona, TipoIntervencao.Curativa,
                DateOnly.FromDateTime(DateTime.Today), EstadoIntervencao.EmCurso, anomalia.Trim(), null, TimeOnly.FromDateTime(DateTime.Now));
            _lista.Add(nova);
            return Task.FromResult(nova);
        }
    }

    public Task<Intervencao> IniciarAsync(string id, CancellationToken ct = default) =>
        Alterar(id, EstadoIntervencao.Planeada, i => i with { Estado = EstadoIntervencao.EmCurso, HoraInicio = TimeOnly.FromDateTime(DateTime.Now) });

    public Task<Intervencao> ConcluirAsync(string id, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct = default)
    {
        if (RegrasIntervencao.ValidarConclusao(feito, inicio, fim) is not null) throw new InvalidOperationException();
        return Alterar(id, EstadoIntervencao.EmCurso, i => i with
        {
            Estado = EstadoIntervencao.Concluida, Feito = feito.Trim(), HoraInicio = inicio, HoraFim = fim
        });
    }

    public Task<IReadOnlyList<Intervencao>> ListarPorValidarAsync(CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<Intervencao>>(_lista.Where(i => i.Estado == EstadoIntervencao.Concluida).ToList());
    }

    public Task<Intervencao> ValidarAsync(string id, CancellationToken ct = default) =>
        Alterar(id, EstadoIntervencao.Concluida, i => i with { Estado = EstadoIntervencao.Validada });

    public Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Equipamento>>(Equipamentos
            .Where(e => string.IsNullOrWhiteSpace(texto) || e.Nome.Contains(texto.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .ToList());

    private Task<Intervencao> Alterar(string id, EstadoIntervencao esperado, Func<Intervencao, Intervencao> mudar)
    {
        lock (_trinco)
        {
            var pos = _lista.FindIndex(i => i.Id == id);
            if (pos < 0 || _lista[pos].Estado != esperado) throw new InvalidOperationException();
            _lista[pos] = mudar(_lista[pos]);
            return Task.FromResult(_lista[pos]);
        }
    }
}

// Quando não há fonte de dados configurada (Release sem ligação ao PHC).
public sealed class IntervencoesIndisponiveis : IIntervencoesService, IEquipamentosService
{
    public Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao?> ObterAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> IniciarAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> ConcluirAsync(string id, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Intervencao>> ListarPorValidarAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> ValidarAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default) => throw new NotSupportedException();
}
