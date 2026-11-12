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
    // Pausa e retoma a ficha em curso (o estado não muda; Intervencao.EmPausa diz se está em pausa). Repetir o pedido é inofensivo.
    Task<Intervencao> PausarAsync(string id, CancellationToken ct = default);
    Task<Intervencao> RetomarAsync(string id, CancellationToken ct = default);
    // Só para supervisores: fichas concluídas por validar e a validação (quem concluiu não valida a sua).
    Task<IReadOnlyList<Intervencao>> ListarPorValidarAsync(CancellationToken ct = default);
    Task<Intervencao> ValidarAsync(string id, CancellationToken ct = default);
    Task<Intervencao> DevolverAsync(string id, string motivo, CancellationToken ct = default);
    // Só para supervisores: planeadas e em curso de todos, atribuição de técnico (tecno 0 = sem dono) e geração das planeadas.
    Task<IReadOnlyList<Intervencao>> ListarPlaneadasAsync(CancellationToken ct = default);
    Task<Intervencao> AtribuirAsync(string id, int tecno, CancellationToken ct = default);
    // Só para supervisores: técnicos adicionais da intervenção (substitui a lista; o principal é ignorado).
    Task<Intervencao> DefinirAdicionaisAsync(string id, IReadOnlyList<int> tecnos, CancellationToken ct = default);
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

    // Ficha completa e ações novas do backoffice. São só online: sem rede lançam InvalidOperationException com MensagensIntervencao.SemRede
    // e nunca vão para a fila de saída. A mensagem das InvalidOperationException vem pronta para mostrar.
    // Ficha completa com mão de obra, materiais, totais e bloqueios (null se não existe ou não é do utilizador).
    Task<FichaDetalhe?> ObterDetalheAsync(string id, CancellationToken ct = default);
    Task<FichaDetalhe> GuardarFichaAsync(string id, EdicaoFicha edicao, CancellationToken ct = default);
    // Só supervisor: Planeada ou EmCurso passa a Cancelada (motivo obrigatório) e gera-se a seguinte da tarefa.
    Task<Intervencao> CancelarAsync(string id, string motivo, CancellationToken ct = default);
    // Operador e supervisor: nasce em curso, com o utilizador como técnico.
    Task<Intervencao> CriarFichaManualAsync(NovaFicha ficha, CancellationToken ct = default);
    // Só supervisor, só fichas antigas (sem estado).
    Task<FichaDetalhe> FecharFichaAntigaAsync(string id, bool supervisao, bool fechada, CancellationToken ct = default);
    Task<Material> EditarMaterialAsync(string id, string materialId, string design, decimal qtt, decimal epv, CancellationToken ct = default);
    // Mão de obra interna: custo = horas x custo por hora; o custo por hora sugerido é o do último registo do técnico.
    Task<IReadOnlyList<TempoMaoDeObra>> ListarMaoDeObraAsync(string id, CancellationToken ct = default);
    Task<TempoMaoDeObra> AdicionarMaoDeObraAsync(string id, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default);
    Task<TempoMaoDeObra> EditarMaoDeObraAsync(string id, string tempoId, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default);
    Task ApagarMaoDeObraAsync(string id, string tempoId, CancellationToken ct = default);
    Task<decimal> CustoHoraSugeridoAsync(int tecno, CancellationToken ct = default);
    // Compras ligadas à ficha (só leitura; vazio se a configuração não usa compras) e fornecedores (2 letras no mínimo) para escolher na ficha.
    Task<IReadOnlyList<CompraLigada>> ListarComprasAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default);
    // Só supervisor: contadores do menu.
    Task<ContadoresMenu> ObterContadoresAsync(CancellationToken ct = default);
}

// O utilizador ainda não tem o número de técnico do PHC: o supervisor tem de o indicar na Equipa.
public sealed class SemNumeroTecnicoException : Exception;

// A mensagem de uma InvalidOperationException vem pronta para mostrar ao utilizador.
public static class MensagensIntervencao
{
    public const string Mudou = "Esta intervenção já mudou. Volte à lista e abra-a outra vez.";
    public const string SemRede = "Sem ligação. Esta ação só funciona com rede.";
    public const string SemRedeAMeio = "Sem ligação. Pode ter ficado gravado; confirme antes de repetir.";
    public const string NaoPausa = "Não é possível pausar esta intervenção.";
    public const string NaoRetoma = "Não é possível retomar esta intervenção.";
    public const string ComPendentes = "Esta intervenção tem alterações por enviar. Tente outra vez depois de serem enviadas.";

    public static string Para(string? codigo) => codigo switch
    {
        "ref_duplicada" => "Esse nº de infraestrutura já existe. Altere o número.",
        "planeadas_mudaram" => "O número de planeadas mudou entretanto. Confirme outra vez.",
        "tarefa_com_fichas" => "Esta tarefa já tem fichas feitas. Desligue-a em vez de a apagar.",
        "instrucao_obsoleta" => "Esta versão da instrução está obsoleta. Crie uma versão nova para juntar tarefas.",
        "sem_permissao" => "Não tem permissão para esta ação.",
        "pedido_invalido" => "Algum dos valores indicados não é válido.",
        "periodicidade_invalida" => "Essa periodicidade não existe neste equipamento.",
        "fora_de_ordem" => "Há intervenções mais antigas nesta zona. Comece pela primeira da lista.",
        "externa_sem_documento" => "Falta o fornecedor ou o documento externo. Peça ao supervisor para os indicar no PHC.",
        "propria" => "Não pode validar uma intervenção feita por si.",
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
            new("T2", "Empilhador 05", "Armazém", TipoIntervencao.Curativa, hoje, EstadoIntervencao.EmCurso, "Trava a meio da subida", null, new TimeOnly(8, 30), PodePausar: true),
            new("T3", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje, EstadoIntervencao.Planeada),
            new("T4", "Linha de embalagem", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(4), EstadoIntervencao.Planeada),
            new("T5", "Balança 03", "Expedição", TipoIntervencao.Preventiva, hoje.AddDays(-10), EstadoIntervencao.Concluida, null, "Trocado o cabo de ligação", new TimeOnly(9, 0), new TimeOnly(10, 15), "Operador de Teste", 101),
            new("T6", "Porta seccionada 07", "Armazém", TipoIntervencao.Preventiva, hoje.AddDays(2), EstadoIntervencao.Planeada, null, null, null, null, "Novo Operador de Teste", 102),
            // Histórico já fechado, para a ficha do equipamento e para "Todas as fichas".
            new("H1", "Compressor de ar 02", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(-35), EstadoIntervencao.Validada, null, "Óleo e filtros em bom estado", new TimeOnly(8, 0), new TimeOnly(9, 0), "Operador de Teste", 101),
            new("H2", "Empilhador 05", "Armazém", TipoIntervencao.Curativa, hoje.AddDays(-120), EstadoIntervencao.Validada, "Não arranca", "Substituída a bateria", new TimeOnly(14, 0), new TimeOnly(16, 30), "Chefe de Turno", 100),
            new("H3", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje.AddDays(-60), EstadoIntervencao.Cancelada),
            new("T7", "Linha de embalagem", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(-1), EstadoIntervencao.EmCurso, null, "Limpeza feita", new TimeOnly(7, 30), null, "Operador de Teste", 101,
                Motivo: "Falta indicar o material usado.", PodePausar: true)
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
        Alterar(id, EstadoIntervencao.Planeada, i => i with { Estado = EstadoIntervencao.EmCurso, HoraInicio = TimeOnly.FromDateTime(DateTime.Now), PodePausar = true });

    public Task<Intervencao> ConcluirAsync(string id, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct = default)
    {
        if (RegrasIntervencao.ValidarConclusao(feito, inicio, fim) is not null) throw new InvalidOperationException(MensagensIntervencao.Mudou);
        return Alterar(id, EstadoIntervencao.EmCurso, i => i with
        {
            Estado = EstadoIntervencao.Concluida, Feito = feito.Trim(), HoraInicio = inicio, HoraFim = fim, Motivo = null, EmPausa = false, PodePausar = false
        });
    }

    public Task<Intervencao> PausarAsync(string id, CancellationToken ct = default) =>
        Alterar(id, EstadoIntervencao.EmCurso, i => i.PodePausar ? i with { EmPausa = true } : throw new InvalidOperationException(MensagensIntervencao.NaoPausa));

    public Task<Intervencao> RetomarAsync(string id, CancellationToken ct = default) =>
        Alterar(id, EstadoIntervencao.EmCurso, i => i.PodePausar ? i with { EmPausa = false } : throw new InvalidOperationException(MensagensIntervencao.NaoRetoma));

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
        return Alterar(id, null, i => i.PorFazer ? i with { Tecno = tecno, Tecnico = tecno == 0 ? null : nome, Adicionais = tecno == 0 ? null : i.OutrosTecnicos.Where(a => a.Tecno != tecno).ToList() is { Count: > 0 } l ? l : null } : throw new InvalidOperationException(MensagensIntervencao.Mudou));
    }

    public Task<Intervencao> DefinirAdicionaisAsync(string id, IReadOnlyList<int> tecnos, CancellationToken ct = default) =>
        Alterar(id, null, i => i.PorFazer && i.Tecno > 0
            ? i with { Adicionais = tecnos.Distinct().Where(t => t != i.Tecno).Order().Select(t => new TecnicoFicha(t, NomeDev(t))).ToList() is { Count: > 0 } l ? l : null }
            : throw new InvalidOperationException(MensagensIntervencao.Mudou));

    private static string NomeDev(int tecno) => tecno switch { 100 => "Chefe de Turno", 101 => "Operador de Teste", 102 => "Novo Operador de Teste", _ => "PHC " + tecno };

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
        lock (_trinco)
        {
            var todas = VistaFichas.Aplicar(_lista, filtro).ToList();
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

    // ---- Ficha completa e ações novas (em desenvolvimento age como supervisor) ----

    private readonly Dictionary<string, FichaDetalhe> _extras = [];
    private readonly Dictionary<string, List<TempoMaoDeObra>> _tempos = [];
    private static readonly Fornecedor[] Fornecedores = [new(7000004072, "Frio Industrial, Lda"), new(7000001234, "Elevadores do Norte")];

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private FichaDetalhe DetalheDe(Intervencao i)
    {
        var eq = Equipamentos.FirstOrDefault(e => e.Nome == i.Equipamento);
        var extra = _extras.GetValueOrDefault(i.Id) ?? new FichaDetalhe(i.Id, i.Equipamento, i.Estado, i.DataPlaneada);
        var tempos = _tempos.GetValueOrDefault(i.Id)?.ToList() ?? [];
        var materiais = _materiais.GetValueOrDefault(i.Id)?.ToList() ?? [];
        var editavel = i.Estado is not (EstadoIntervencao.Validada or EstadoIntervencao.Cancelada);
        return extra with
        {
            Equipamento = i.Equipamento, Zona = i.Zona, EquipamentoId = eq?.Id ?? "", Ref = eq?.Ref ?? "", Tipo = i.Tipo, Data = i.DataPlaneada, Estado = i.Estado,
            Tecno = i.Tecno, Tecnico = i.Tecnico, Adicionais = i.Adicionais,
            HoraInicio = i.HoraInicio is { } h ? i.DataPlaneada.ToDateTime(h) : null, HoraFim = i.HoraFim is { } f ? i.DataPlaneada.ToDateTime(f) : null,
            Problema = i.Anomalia ?? "", Resolucao = i.Feito ?? "", Motivo = i.Motivo, MaoDeObra = tempos, Materiais = materiais,
            MoInterna = tempos.Sum(t => t.Custo), MatInterno = materiais.Sum(m => m.Custo),
            TotMo = tempos.Sum(t => t.Custo) + extra.MoExterna, TotRef = materiais.Sum(m => m.Custo) + extra.MatExterno,
            ExternosAMao = !_configuracao.ComprasFichaInt, PodeEditar = editavel, PodeEditarGestao = editavel, PodeCancelar = i.PorFazer
        };
    }

    public Task<FichaDetalhe?> ObterDetalheAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult(_lista.FirstOrDefault(i => i.Id == id) is { } ficha ? DetalheDe(ficha) : null);
    }

    public Task<FichaDetalhe> GuardarFichaAsync(string id, EdicaoFicha e, CancellationToken ct = default)
    {
        if (RegrasFichaDetalhe.ValidarEdicao(e) is { } mensagem) throw new InvalidOperationException(mensagem);
        lock (_trinco)
        {
            var pos = _lista.FindIndex(i => i.Id == id);
            if (pos < 0 || _lista[pos].Estado is EstadoIntervencao.Validada or EstadoIntervencao.Cancelada) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            var i = _lista[pos];
            var extra = _extras.GetValueOrDefault(id) ?? new FichaDetalhe(id, i.Equipamento, i.Estado, i.DataPlaneada);
            _lista[pos] = i with
            {
                Anomalia = e.Problema is null ? i.Anomalia : Texto(e.Problema), Feito = e.Resolucao is null ? i.Feito : Texto(e.Resolucao),
                HoraInicio = e.HoraInicio is { } hi ? (hi.Year <= 1900 ? null : TimeOnly.FromDateTime(hi)) : i.HoraInicio,
                HoraFim = e.HoraFim is { } hf ? (hf.Year <= 1900 ? null : TimeOnly.FromDateTime(hf)) : i.HoraFim,
                DataPlaneada = e.Data ?? i.DataPlaneada, Tipo = e.Tipo ?? i.Tipo
            };
            _extras[id] = extra with
            {
                TipoManut = e.TipoManut ?? extra.TipoManut, Periodicidade = e.Periodicidade ?? extra.Periodicidade, Obs = e.Obs ?? extra.Obs, LimRem = e.LimRem ?? extra.LimRem,
                HorasFrio = e.HorasFrio ?? extra.HorasFrio, HorasConf = e.HorasConf ?? extra.HorasConf, NoFornecedor = e.NoFornecedor ?? extra.NoFornecedor,
                NomeFornecedor = e.NomeFornecedor ?? extra.NomeFornecedor, DocExterno = e.DocExterno ?? extra.DocExterno,
                MoExterna = e.MoExterna ?? extra.MoExterna, MatExterno = e.MatExterno ?? extra.MatExterno
            };
            return Task.FromResult(DetalheDe(_lista[pos]));
        }
    }

    public Task<Intervencao> CancelarAsync(string id, string motivo, CancellationToken ct = default)
    {
        if (RegrasFichaDetalhe.ValidarCancelamento(motivo) is { } mensagem) throw new InvalidOperationException(mensagem);
        return Alterar(id, null, i => i.PorFazer ? i with { Estado = EstadoIntervencao.Cancelada } : throw new InvalidOperationException(MensagensIntervencao.Mudou));
    }

    public Task<Intervencao> CriarFichaManualAsync(NovaFicha ficha, CancellationToken ct = default)
    {
        if (RegrasFichaDetalhe.ValidarNova(ficha) is { } mensagem) throw new InvalidOperationException(mensagem);
        lock (_trinco)
        {
            var eq = Equipamentos.FirstOrDefault(e => e.Id == ficha.EquipamentoId && !e.Obsoleto) ?? throw new InvalidOperationException(MensagensIntervencao.Mudou);
            var nova = new Intervencao(Guid.NewGuid().ToString("N"), eq.Nome, eq.Zona, ficha.Tipo, DateOnly.FromDateTime(DateTime.Today), EstadoIntervencao.EmCurso,
                Texto(ficha.Problema), null, TimeOnly.FromDateTime(DateTime.Now), null, "Operador de Teste", 101);
            _lista.Add(nova);
            _extras[nova.Id] = new FichaDetalhe(nova.Id, nova.Equipamento, nova.Estado, nova.DataPlaneada, TipoManut: ficha.TipoManut, Periodicidade: (ficha.Periodicidade ?? "").Trim());
            return Task.FromResult(nova);
        }
    }

    public Task<FichaDetalhe> FecharFichaAntigaAsync(string id, bool supervisao, bool fechada, CancellationToken ct = default) =>
        throw new InvalidOperationException(MensagensIntervencao.Mudou);

    public Task<Material> EditarMaterialAsync(string id, string materialId, string design, decimal qtt, decimal epv, CancellationToken ct = default)
    {
        if (RegrasMaterial.Validar(design, qtt, epv) is not null) throw new InvalidOperationException(MensagensIntervencao.Mudou);
        lock (_trinco)
        {
            var lista = _materiais.GetValueOrDefault(id);
            var pos = lista?.FindIndex(m => m.Id == materialId) ?? -1;
            if (lista is null || pos < 0 || _lista.FirstOrDefault(i => i.Id == id)?.Estado is null or EstadoIntervencao.Validada or EstadoIntervencao.Cancelada)
                throw new InvalidOperationException(MensagensIntervencao.Mudou);
            return Task.FromResult(lista[pos] = new Material(materialId, design.Trim(), qtt, epv, Math.Round(qtt * epv, 3)));
        }
    }

    public Task<IReadOnlyList<TempoMaoDeObra>> ListarMaoDeObraAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<TempoMaoDeObra>>(_tempos.GetValueOrDefault(id)?.ToList() ?? []);
    }

    private Task<TempoMaoDeObra> GravarTempo(string id, string? tempoId, int tecno, decimal horas, decimal custoHora)
    {
        if (RegrasFichaDetalhe.ValidarTempo(tecno, horas, custoHora) is { } mensagem) throw new InvalidOperationException(mensagem);
        lock (_trinco)
        {
            if (_lista.FirstOrDefault(i => i.Id == id) is not { } ficha || ficha.Estado is EstadoIntervencao.Validada or EstadoIntervencao.Cancelada)
                throw new InvalidOperationException(MensagensIntervencao.Mudou);
            if (!_tempos.TryGetValue(id, out var lista)) _tempos[id] = lista = [];
            var tempo = new TempoMaoDeObra(tempoId ?? Guid.NewGuid().ToString("N"), tecno, NomeDev(tecno), horas, RegrasFichaDetalhe.CustoTempo(horas, custoHora), custoHora);
            if (tempoId is null) lista.Add(tempo);
            else if (lista.FindIndex(t => t.Id == tempoId) is var pos and >= 0) lista[pos] = tempo;
            else throw new InvalidOperationException(MensagensIntervencao.Mudou);
            return Task.FromResult(tempo);
        }
    }

    public Task<TempoMaoDeObra> AdicionarMaoDeObraAsync(string id, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default) =>
        GravarTempo(id, null, tecno, horas, custoHora);

    public Task<TempoMaoDeObra> EditarMaoDeObraAsync(string id, string tempoId, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default) =>
        GravarTempo(id, tempoId, tecno, horas, custoHora);

    public Task ApagarMaoDeObraAsync(string id, string tempoId, CancellationToken ct = default)
    {
        lock (_trinco) _tempos.GetValueOrDefault(id)?.RemoveAll(t => t.Id == tempoId);
        return Task.CompletedTask;
    }

    public Task<decimal> CustoHoraSugeridoAsync(int tecno, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult(_tempos.Values.SelectMany(l => l).LastOrDefault(t => t.Tecno == tecno && t.CustoHora > 0)?.CustoHora ?? 0m);
    }

    public Task<IReadOnlyList<CompraLigada>> ListarComprasAsync(string id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CompraLigada>>([]);

    public Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Fornecedor>>(Fornecedores.Where(f => (texto ?? "").Trim().Length >= 2 && Contem(f.Nome, texto!.Trim())).ToList());

    public Task<ContadoresMenu> ObterContadoresAsync(CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        lock (_trinco) return Task.FromResult(new ContadoresMenu(_lista.Count(i => i.EstaAtrasada(hoje) || i.ForaDePrazo(hoje)), _lista.Count(i => i.Estado == EstadoIntervencao.Concluida)));
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
    public Task<Intervencao> PausarAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> RetomarAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Intervencao>> ListarPorValidarAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> ValidarAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> DevolverAsync(string id, string motivo, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Intervencao>> ListarPlaneadasAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> AtribuirAsync(string id, int tecno, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> DefinirAdicionaisAsync(string id, IReadOnlyList<int> tecnos, CancellationToken ct = default) => throw new NotSupportedException();
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
    public Task<FichaDetalhe?> ObterDetalheAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<FichaDetalhe> GuardarFichaAsync(string id, EdicaoFicha edicao, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> CancelarAsync(string id, string motivo, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> CriarFichaManualAsync(NovaFicha ficha, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<FichaDetalhe> FecharFichaAntigaAsync(string id, bool supervisao, bool fechada, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Material> EditarMaterialAsync(string id, string materialId, string design, decimal qtt, decimal epv, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<TempoMaoDeObra>> ListarMaoDeObraAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TempoMaoDeObra> AdicionarMaoDeObraAsync(string id, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TempoMaoDeObra> EditarMaoDeObraAsync(string id, string tempoId, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ApagarMaoDeObraAsync(string id, string tempoId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<decimal> CustoHoraSugeridoAsync(int tecno, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<CompraLigada>> ListarComprasAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ContadoresMenu> ObterContadoresAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
