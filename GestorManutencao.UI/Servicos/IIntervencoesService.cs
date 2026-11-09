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
    // Fotos da intervenção (JPEG): o técnico junta e apaga com a ficha em curso; o supervisor só vê.
    Task<IReadOnlyList<Foto>> ListarFotosAsync(string id, CancellationToken ct = default);
    Task<byte[]?> ObterFotoAsync(string id, string fotoId, CancellationToken ct = default);
    Task<Foto> AdicionarFotoAsync(string id, string nome, byte[] jpeg, CancellationToken ct = default);
    Task ApagarFotoAsync(string id, string fotoId, CancellationToken ct = default);
    // Só para supervisores: todas as fichas, filtradas e por páginas (pagina começa em 1).
    Task<PaginaFichas> ListarTodasAsync(FiltroFichas filtro, int pagina, CancellationToken ct = default);
    // Só para supervisores: configuração da manutenção. Guardar lança InvalidOperationException com a mensagem a mostrar se o servidor recusar os valores.
    Task<ConfiguracaoManutencao> ObterConfiguracaoAsync(CancellationToken ct = default);
    Task<ConfiguracaoManutencao> GuardarConfiguracaoAsync(ConfiguracaoManutencao configuracao, CancellationToken ct = default);
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
        "sem_material" => "Indique o material usado antes de concluir.",
        "limite_fotos" => $"No máximo {RegrasFoto.Max} fotos por intervenção.",
        _ => Mudou
    };
}

public interface IEquipamentosService
{
    Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default);
    // Só leitura, para operador e supervisor; obsoletos só vêm se o filtro os pedir.
    Task<IReadOnlyList<Equipamento>> ListarAsync(FiltroEquipamentos filtro, CancellationToken ct = default);
    Task<FichaEquipamento?> ObterFichaAsync(string id, CancellationToken ct = default);
}

// Só para desenvolvimento: dados fictícios em memória, até a ligação ao PHC estar confirmada.
public sealed class DevIntervencoesService : IIntervencoesService, IEquipamentosService
{
    private readonly object _trinco = new();
    private readonly List<Intervencao> _lista;
    private static readonly Equipamento[] Equipamentos =
    [
        new("E1", "Compressor de ar 02", "Produção", "CMP-02", 2, "Compressor", "Atlas Copco", "GA 30", "AC-77120"),
        new("E2", "Empilhador 05", "Armazém", "EMP-05", 5, "Empilhador", "Toyota", "8FBE15", "TY-30418"),
        new("E3", "Câmara de frio 01", "Frio", "CF-01", 1, "Frio", "Kide", "CF-500", "KD-1190"),
        new("E4", "Linha de embalagem", "Produção", "LE-01", 4, "Linha", "Cavanna", "LP-2", "CV-2231"),
        new("E5", "Balança 03", "Expedição", "BAL-03", 3, "Balança", "Mettler", "IND-570", "MT-8841"),
        new("E6", "Porta seccionada 07", "Armazém", "PRT-07", 7, "Portas", "Hormann", "SPU-40", "HR-5522", ForaDeServico: true),
        new("E7", "Serra de fita antiga", "Produção", "SRF-01", 9, "Serra", "Bomar", "STG-280", "BM-0042", Obsoleto: true)
    ];

    private sealed record TarefaDev(string EquipamentoId, string Descricao, string Periodo, int Dias);

    private static readonly TarefaDev[] Tarefas =
    [
        new("E1", "Verificar o nível de óleo e os filtros", "Mensal", 30),
        new("E1", "Drenar os condensados", "Semanal", 7),
        new("E3", "Limpar os evaporadores", "Trimestral", 90)
    ];

    private ConfiguracaoManutencao _configuracao = new();

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
            new("T6", "Porta seccionada 07", "Armazém", TipoIntervencao.Preventiva, hoje.AddDays(2), EstadoIntervencao.Planeada, null, null, null, null, "Novo Operador de Teste", 102),
            // Histórico já fechado, para a ficha do equipamento e para "Todas as fichas".
            new("H1", "Compressor de ar 02", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(-35), EstadoIntervencao.Validada, null, "Óleo e filtros em bom estado", new TimeOnly(8, 0), new TimeOnly(9, 0), "Operador de Teste", 101),
            new("H2", "Empilhador 05", "Armazém", TipoIntervencao.Curativa, hoje.AddDays(-120), EstadoIntervencao.Validada, "Não arranca", "Substituída a bateria", new TimeOnly(14, 0), new TimeOnly(16, 30), "Chefe de Turno", 100),
            new("H3", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje.AddDays(-60), EstadoIntervencao.Cancelada),
            new("T7", "Linha de embalagem", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(-1), EstadoIntervencao.EmCurso, null, "Limpeza feita", new TimeOnly(7, 30), null, "Operador de Teste", 101,
                Motivo: "Falta indicar o material usado.")
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
            Estado = EstadoIntervencao.Concluida, Feito = feito.Trim(), HoraInicio = inicio, HoraFim = fim, Motivo = null
        }, () => _configuracao.ExigeMaterial && !(_materiais.GetValueOrDefault(id)?.Count > 0) ? throw new InvalidOperationException(MensagensIntervencao.Para("sem_material")) : null);
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
        return Alterar(id, EstadoIntervencao.Concluida, i => i with { Estado = EstadoIntervencao.EmCurso, HoraFim = null, Motivo = motivo.Trim() });
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

    private readonly Dictionary<string, List<(Foto Foto, byte[] Dados)>> _fotos = [];

    public Task<IReadOnlyList<Foto>> ListarFotosAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<Foto>>(_fotos.GetValueOrDefault(id)?.Select(f => f.Foto).ToList() ?? []);
    }

    public Task<byte[]?> ObterFotoAsync(string id, string fotoId, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult(_fotos.GetValueOrDefault(id)?.FirstOrDefault(f => f.Foto.Id == fotoId).Dados);
    }

    public Task<Foto> AdicionarFotoAsync(string id, string nome, byte[] jpeg, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            if (_lista.FirstOrDefault(i => i.Id == id) is not { Estado: EstadoIntervencao.EmCurso }) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            if (!_fotos.TryGetValue(id, out var lista)) _fotos[id] = lista = [];
            if (lista.Count >= RegrasFoto.Max) throw new InvalidOperationException(MensagensIntervencao.Para("limite_fotos"));
            var foto = new Foto(Guid.NewGuid().ToString("N"), nome, jpeg.Length);
            lista.Add((foto, jpeg));
            return Task.FromResult(foto);
        }
    }

    public Task ApagarFotoAsync(string id, string fotoId, CancellationToken ct = default)
    {
        lock (_trinco) _fotos.GetValueOrDefault(id)?.RemoveAll(f => f.Foto.Id == fotoId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default) =>
        ListarAsync(new FiltroEquipamentos(texto ?? ""), ct);

    private static bool Contem(string valor, string texto) => valor.Contains(texto, StringComparison.CurrentCultureIgnoreCase);

    // UltimaIntervencao e FichasAbertas calculam-se a partir das fichas, como no servidor.
    private Equipamento Completo(Equipamento e)
    {
        var fichas = _lista.Where(i => i.Equipamento == e.Nome).ToList();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var ultima = fichas.Where(i => i.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada && i.DataPlaneada <= hoje)
            .Select(i => (DateOnly?)i.DataPlaneada).Max();
        return e with { UltimaIntervencao = ultima, FichasAbertas = fichas.Count(i => i.Estado is EstadoIntervencao.EmCurso or EstadoIntervencao.Concluida) };
    }

    public Task<IReadOnlyList<Equipamento>> ListarAsync(FiltroEquipamentos filtro, CancellationToken ct = default)
    {
        var texto = (filtro.Texto ?? "").Trim();
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<Equipamento>>(Equipamentos
                .Where(e => (filtro.Obsoletos || !e.Obsoleto)
                    && (string.IsNullOrEmpty(filtro.Tipo) || string.Equals(e.Tipo, filtro.Tipo, StringComparison.CurrentCultureIgnoreCase))
                    && (string.IsNullOrEmpty(filtro.Local) || string.Equals(e.Zona, filtro.Local, StringComparison.CurrentCultureIgnoreCase))
                    && (texto.Length == 0 || Contem(e.Nome, texto) || Contem(e.Ref, texto) || Contem(e.Marca, texto) || Contem(e.Modelo, texto)))
                .OrderBy(e => e.Nome, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.NoInterno).Select(Completo).ToList());
    }

    public Task<FichaEquipamento?> ObterFichaAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            var e = Equipamentos.FirstOrDefault(x => x.Id == id);
            if (e is null) return Task.FromResult<FichaEquipamento?>(null);

            var fichas = _lista.Where(i => i.Equipamento == e.Nome).ToList();
            var ultima = fichas.Where(i => i.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada).Select(i => (DateOnly?)i.DataPlaneada).Max();
            var aberta = fichas.Where(i => i.Estado == EstadoIntervencao.Planeada).Select(i => (DateOnly?)i.DataPlaneada).Min();
            var tarefas = Tarefas.Where(t => t.EquipamentoId == id)
                .Select(t => new TarefaPlaneada(t.Descricao, t.Descricao, t.Periodo, ultima, ultima?.AddDays(t.Dias) ?? aberta)).ToList();
            var historico = fichas.Where(i => i.Estado != EstadoIntervencao.Planeada).OrderByDescending(i => i.DataPlaneada).Take(50)
                .Select(i => i with { PodeAbrir = true }).ToList();
            return Task.FromResult<FichaEquipamento?>(new FichaEquipamento(Completo(e), tarefas, historico));
        }
    }

    private const int PorPagina = 20;

    public Task<PaginaFichas> ListarTodasAsync(FiltroFichas filtro, int pagina, CancellationToken ct = default)
    {
        var texto = (filtro.Texto ?? "").Trim();
        lock (_trinco)
        {
            var todas = _lista
                .Where(i => (filtro.Estado is null || i.Estado == filtro.Estado)
                    && (filtro.Desde is null || i.DataPlaneada >= filtro.Desde) && (filtro.Ate is null || i.DataPlaneada <= filtro.Ate)
                    && (filtro.Tecno == 0 || i.Tecno == filtro.Tecno)
                    && (texto.Length == 0 || Contem(i.Equipamento, texto) || Contem(i.Anomalia ?? "", texto) || Contem(i.Feito ?? "", texto) || Contem(i.Tecnico ?? "", texto)))
                .OrderByDescending(i => i.DataPlaneada).ThenByDescending(i => i.Id).ToList();
            var itens = todas.Skip((Math.Max(pagina, 1) - 1) * PorPagina).Take(PorPagina).Select(i => i with { PodeAbrir = true }).ToList();
            return Task.FromResult(new PaginaFichas(itens, todas.Count > Math.Max(pagina, 1) * PorPagina));
        }
    }

    public Task<ConfiguracaoManutencao> ObterConfiguracaoAsync(CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult(_configuracao);
    }

    public Task<ConfiguracaoManutencao> GuardarConfiguracaoAsync(ConfiguracaoManutencao configuracao, CancellationToken ct = default)
    {
        if (RegrasConfiguracao.Validar(configuracao.Antecedencia) is { } mensagem) throw new InvalidOperationException(mensagem);
        lock (_trinco) return Task.FromResult(_configuracao = configuracao);
    }

    private Task<Intervencao> Alterar(string id, EstadoIntervencao? esperado, Func<Intervencao, Intervencao> mudar, Func<object?>? extra = null)
    {
        lock (_trinco)
        {
            var pos = _lista.FindIndex(i => i.Id == id);
            if (pos < 0 || (esperado is not null && _lista[pos].Estado != esperado)) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            extra?.Invoke();
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
    public Task<IReadOnlyList<Foto>> ListarFotosAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<byte[]?> ObterFotoAsync(string id, string fotoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Foto> AdicionarFotoAsync(string id, string nome, byte[] jpeg, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ApagarFotoAsync(string id, string fotoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Equipamento>> ListarAsync(FiltroEquipamentos filtro, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<FichaEquipamento?> ObterFichaAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PaginaFichas> ListarTodasAsync(FiltroFichas filtro, int pagina, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ConfiguracaoManutencao> ObterConfiguracaoAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ConfiguracaoManutencao> GuardarConfiguracaoAsync(ConfiguracaoManutencao configuracao, CancellationToken ct = default) => throw new NotSupportedException();
}
