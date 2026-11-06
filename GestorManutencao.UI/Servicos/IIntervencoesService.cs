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
    Task<Intervencao> DevolverAsync(string id, string motivo, CancellationToken ct = default);
    // Só para supervisores: planeadas e em curso de todos, atribuição de técnico (tecno 0 = sem dono) e geração das planeadas.
    Task<IReadOnlyList<Intervencao>> ListarPlaneadasAsync(CancellationToken ct = default);
    Task<Intervencao> AtribuirAsync(string id, int tecno, CancellationToken ct = default);
    Task<RelatorioGeracao> GerarPlaneadasAsync(CancellationToken ct = default);
    // Material usado na ficha (só se lança com a ficha em curso).
    Task<IReadOnlyList<Material>> ListarMateriaisAsync(string id, CancellationToken ct = default);
    Task<Material> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, CancellationToken ct = default);
    Task ApagarMaterialAsync(string id, string materialId, CancellationToken ct = default);
}

// O utilizador ainda não tem o número de técnico do PHC: o supervisor tem de o indicar na Equipa.
public sealed class SemNumeroTecnicoException : Exception;

// A mensagem de uma InvalidOperationException vem pronta para mostrar ao utilizador.
public static class MensagensIntervencao
{
    public const string Mudou = "Esta intervenção já mudou. Volte à lista e abra-a outra vez.";

    public static string Para(string? codigo) => codigo switch
    {
        "fora_de_ordem" => "Há intervenções mais antigas nesta zona. Comece pela primeira da lista.",
        "externa_sem_documento" => "Falta o fornecedor ou o documento externo. Peça ao supervisor para os indicar no PHC.",
        "propria" => "Não pode validar uma intervenção feita por si.",
        _ => Mudou
    };
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
            new("T5", "Balança 03", "Expedição", TipoIntervencao.Preventiva, hoje.AddDays(-10), EstadoIntervencao.Concluida, null, "Trocado o cabo de ligação", new TimeOnly(9, 0), new TimeOnly(10, 15), "Operador de Teste", 101),
            new("T6", "Porta seccionada 07", "Armazém", TipoIntervencao.Preventiva, hoje.AddDays(2), EstadoIntervencao.Planeada, null, null, null, null, "Novo Operador de Teste", 102)
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
        if (RegrasIntervencao.ValidarAvaria(equipamentoId, anomalia) is not null) throw new InvalidOperationException(MensagensIntervencao.Mudou);
        lock (_trinco)
        {
            var eq = Equipamentos.FirstOrDefault(e => e.Id == equipamentoId) ?? throw new InvalidOperationException(MensagensIntervencao.Mudou);
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
        if (RegrasIntervencao.ValidarConclusao(feito, inicio, fim) is not null) throw new InvalidOperationException(MensagensIntervencao.Mudou);
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

    public Task<Intervencao> DevolverAsync(string id, string motivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new InvalidOperationException(MensagensIntervencao.Mudou);
        return Alterar(id, EstadoIntervencao.Concluida, i => i with { Estado = EstadoIntervencao.EmCurso, HoraFim = null });
    }

    public Task<IReadOnlyList<Intervencao>> ListarPlaneadasAsync(CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<Intervencao>>(_lista.Where(i => i.PorFazer).ToList());
    }

    public Task<Intervencao> AtribuirAsync(string id, int tecno, CancellationToken ct = default)
    {
        var nome = tecno switch { 101 => "Operador de Teste", 102 => "Novo Operador de Teste", _ => null };
        return Alterar(id, null, i => i.PorFazer ? i with { Tecno = tecno, Tecnico = tecno == 0 ? null : nome } : throw new InvalidOperationException(MensagensIntervencao.Mudou));
    }

    public Task<RelatorioGeracao> GerarPlaneadasAsync(CancellationToken ct = default) => Task.FromResult(new RelatorioGeracao(0, _lista.Count(i => i.PorFazer), 0, 0));

    private readonly Dictionary<string, List<Material>> _materiais = [];

    public Task<IReadOnlyList<Material>> ListarMateriaisAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<Material>>(_materiais.GetValueOrDefault(id)?.ToList() ?? []);
    }

    public Task<Material> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, CancellationToken ct = default)
    {
        if (RegrasMaterial.Validar(design, qtt, epv) is not null) throw new InvalidOperationException(MensagensIntervencao.Mudou);
        lock (_trinco)
        {
            if (_lista.FirstOrDefault(i => i.Id == id) is not { Estado: EstadoIntervencao.EmCurso }) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            var m = new Material(Guid.NewGuid().ToString("N"), design.Trim(), qtt, epv, Math.Round(qtt * epv, 3));
            if (!_materiais.TryGetValue(id, out var lista)) _materiais[id] = lista = [];
            lista.Add(m);
            return Task.FromResult(m);
        }
    }

    public Task ApagarMaterialAsync(string id, string materialId, CancellationToken ct = default)
    {
        lock (_trinco) _materiais.GetValueOrDefault(id)?.RemoveAll(m => m.Id == materialId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Equipamento>>(Equipamentos
            .Where(e => string.IsNullOrWhiteSpace(texto) || e.Nome.Contains(texto.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .ToList());

    private Task<Intervencao> Alterar(string id, EstadoIntervencao? esperado, Func<Intervencao, Intervencao> mudar)
    {
        lock (_trinco)
        {
            var pos = _lista.FindIndex(i => i.Id == id);
            if (pos < 0 || (esperado is not null && _lista[pos].Estado != esperado)) throw new InvalidOperationException(MensagensIntervencao.Mudou);
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
    public Task<Intervencao> DevolverAsync(string id, string motivo, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Intervencao>> ListarPlaneadasAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> AtribuirAsync(string id, int tecno, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RelatorioGeracao> GerarPlaneadasAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Material>> ListarMateriaisAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Material> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ApagarMaterialAsync(string id, string materialId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default) => throw new NotSupportedException();
}
