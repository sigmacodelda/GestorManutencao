namespace GestorManutencao.Api;

// Ficha completa, edição, cancelamento, ficha manual, mão de obra, compras, fornecedores e contadores, com as mesmas regras do repositório SQL.
public sealed partial class RepositorioIntervencoesMemoria
{
    private int _numero = 1000;

    private int ProximoNumero() => ++_numero;

    // Validada fecha e supervisiona a ficha (como no SQL); nas antigas valem os campos que o supervisor marcou.
    private static bool EstaFechada(Ficha f) => f.Fechada || (!f.Antiga && f.D.Estado == EstadoIntervencao.Validada);

    private static bool Supervisionada(Ficha f) => f.Supervisao || (!f.Antiga && f.D.Estado == EstadoIntervencao.Validada);

    private decimal TotalDe(Ficha f)
    {
        var (moI, moE, matI, matE) = Totais(f);
        return moI + moE + matI + matE;
    }

    private string RefDe(Ficha f) => Equipamentos.FirstOrDefault(e => e.Nome == f.D.Equipamento)?.Ref ?? "";

    private static readonly FornecedorDto[] Fornecedores =
    [
        new(7000004072, "Frio Industrial, Lda"),
        new(7000001234, "Elevadores do Norte"),
        new(7000009999, "Pneus e Peças, SA")
    ];

    // Só para testes e desenvolvimento: uma ficha do PHC sem estado (fora do circuito).
    public void SemearAntiga(string id, string equipamento = "Balança 03", bool fechada = false, string tipoManut = "Interna", int tecno = 101)
    {
        var e = Equipamentos.First(x => x.Nome == equipamento);
        lock (Trinco())
            _lista.Add(new Ficha
            {
                D = Nova(id, e.Nome, e.Zona, TipoIntervencao.Preventiva, Hoje.AddDays(-200), fechada ? EstadoIntervencao.Validada : EstadoIntervencao.Concluida, null, "Revisão geral",
                    new TimeOnly(8, 0), new TimeOnly(10, 0), "Ana Silva", tecno),
                Tecno = tecno, Antiga = true, Fechada = fechada, Supervisao = fechada, TipoManut = tipoManut, Numero = ProximoNumero()
            });
    }

    // Só para testes e desenvolvimento: linha de compra ligada a uma ficha (fn).
    public void LigarCompra(string id, CompraLigadaDto compra)
    {
        lock (Trinco()) _lista.First(x => x.D.Id == id).Compras.Add(compra);
    }

    private (decimal MoInterna, decimal MoExterna, decimal MatInterno, decimal MatExterno) Totais(Ficha f)
    {
        var (moExterna, matExterno) = RegrasManutencao.Externos(_cfg.ComprasFichaInt, f.Compras, f.MoExterna, f.MatExterno);
        return (f.Tempos.Sum(t => t.Custo), moExterna, f.Materiais.Sum(m => m.Custo), matExterno);
    }

    private FichaLida Lida(Ficha f)
    {
        var e = Equipamentos.FirstOrDefault(x => x.Nome == f.D.Equipamento);
        var (moI, moE, matI, matE) = Totais(f);
        var data = f.D.DataPlaneada.ToDateTime(TimeOnly.MinValue);
        var hini = f.HIni ?? (f.D.HoraInicio is { } hi ? data + hi.ToTimeSpan() : RegrasFicha.SemData);
        var hfim = f.HFim ?? (f.D.HoraFim is { } hf ? data + hf.ToTimeSpan() : RegrasFicha.SemData);
        return new FichaLida(
            f.D.Id, f.Numero, e?.Id ?? "", f.D.Equipamento, f.D.Zona, e?.Ref ?? "", f.D.Tipo.ToString(), f.TipoManut, f.Periodo,
            data, EstadoTexto(f), f.Fechada, f.Supervisao, f.Tecno, f.D.Tecnico ?? "", f.Tecnos, hini, hfim, f.HorasFrio, f.HorasConf,
            f.D.Anomalia ?? "", f.D.Feito ?? "", f.Obs, f.LimRem, f.NoFornec, f.NomeFornec, f.DocExterno,
            moI, moE, matI, matE, moI + moE, matI + matE, f.Motivo, f.Versao.ToString());
    }

    private FichaDetalheDto Detalhe(Ficha f, TecnicoAtual quem, bool supervisor) =>
        RegrasFicha.Montar(Lida(f), f.Tempos.ToList(), f.Materiais.ToList(), _cfg, supervisor, Pertence(f, quem.Tecno), quem.Tecno);

    private static string? Vazio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    public Task<FichaDetalheDto?> ObterDetalheAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(_lista.FirstOrDefault(x => x.D.Id == id && (supervisor || Minha(x, quem))) is { } f ? Detalhe(f, quem, supervisor) : null);
    }

    public Task<(ResultadoIntervencao, FichaDetalheDto?)> GuardarFichaAsync(string id, PedidoFicha pedido, string? nomeTecnico, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (Acesso(f, quem, supervisor) is { } negado) return Task.FromResult(Falha<FichaDetalheDto>(negado));
            if (!string.IsNullOrEmpty(pedido.Versao) && pedido.Versao != f!.Versao.ToString()) return Task.FromResult(Falha<FichaDetalheDto>(ResultadoIntervencao.Conflito));
            var (nova, resultado) = RegrasFicha.Aplicar(Lida(f!), pedido, supervisor, nomeTecnico, !_cfg.ComprasFichaInt, Hoje);
            if (nova is null) return Task.FromResult(Falha<FichaDetalheDto>(resultado));
            if (nova.NoFornec != f!.NoFornec && nova.NoFornec > 0)
            {
                if (Fornecedores.FirstOrDefault(x => x.No == (long)nova.NoFornec) is not { } fornecedor) return Task.FromResult(Falha<FichaDetalheDto>(ResultadoIntervencao.Invalido));
                nova = nova with { NomeFornec = fornecedor.Nome };
            }
            else if (nova.NoFornec == 0 && f.NoFornec != 0) nova = nova with { NomeFornec = "" };
            f.Versao++;

            var hini = nova.HIni.Year > 1900 ? nova.HIni : (DateTime?)null;
            var hfim = nova.HFim.Year > 1900 ? nova.HFim : (DateTime?)null;
            f!.D = f.D with
            {
                DataPlaneada = DateOnly.FromDateTime(nova.Data), Tipo = RegrasFicha.TipoDe(nova.Tipo), Tecnico = Vazio(nova.Tecnome),
                Anomalia = Vazio(nova.Problema), Feito = Vazio(nova.Resolucao),
                HoraInicio = hini is { } i ? TimeOnly.FromDateTime(i) : null, HoraFim = hfim is { } fim ? TimeOnly.FromDateTime(fim) : null
            };
            f.Tecno = nova.Tecno; f.Tecnos = nova.Tecnos; f.TipoManut = nova.TipoManut; f.Periodo = nova.Periodo;
            f.HIni = hini; f.HFim = hfim; f.HorasFrio = nova.HorasFrio; f.HorasConf = nova.HorasConf; f.Obs = nova.Obs; f.LimRem = nova.LimRem;
            f.NoFornec = nova.NoFornec; f.NomeFornec = nova.NomeFornec; f.DocExterno = nova.DocExterno;
            if (!_cfg.ComprasFichaInt) { f.MoExterna = nova.MoExterna; f.MatExterno = nova.MatExterno; }
            return Task.FromResult<(ResultadoIntervencao, FichaDetalheDto?)>((ResultadoIntervencao.Ok, Detalhe(f, quem, supervisor)));
        }
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> CancelarAsync(string id, string motivo, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (f is null) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
            if (!EstadosFicha.PodeMudar(EstadoTexto(f), EstadosFicha.Cancelada)) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.Conflito));
            f.Motivo = RegrasManutencao.Cortar("Cancelada: " + motivo.Trim(), RegrasManutencao.TamanhoMotivo);
            f.D = f.D with { Estado = EstadoIntervencao.Cancelada };
            GerarSeguinte(f);
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
        }
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> CriarFichaManualAsync(PedidoFichaManual pedido, TecnicoAtual tecnico, CancellationToken ct)
    {
        var e = Equipamentos.FirstOrDefault(x => x.Id == pedido.EquipamentoId && !x.Obsoleto && !RegrasGestao.EhViatura(x.Tipo));
        if (e is null) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
        var periodo = RegrasManutencao.Cortar(pedido.Periodicidade, RegrasManutencao.TamanhoPeriodo);
        if (periodo.Length > 0 && !Tarefas.Any(t => t.EquipamentoId == e.Id && !InstrucaoObsoleta(t.InstrucaoId) && string.Equals(t.Periodo, periodo, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.PeriodicidadeInvalida));

        var agora = relogio.GetLocalNow().DateTime;
        lock (Trinco())
        {
            if (pedido.OpId is not null && _lista.FirstOrDefault(x => x.OpId == pedido.OpId && x.Tecno == tecnico.Tecno) is { } repetida)
                return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(repetida)));
            var nova = new Ficha
            {
                D = Nova(Guid.NewGuid().ToString("N")[..25], e.Nome, e.Zona, pedido.Tipo ?? TipoIntervencao.Preventiva, DateOnly.FromDateTime(agora), EstadoIntervencao.EmCurso,
                    Vazio(pedido.Problema ?? ""), null, TimeOnly.FromDateTime(agora), null, tecnico.Nome, tecnico.Tecno),
                Tecno = tecnico.Tecno, OpId = pedido.OpId, Numero = ProximoNumero(), Periodo = periodo,
                TipoManut = RegrasFicha.NormalizarTipoManut(pedido.TipoManut) ?? RegrasManutencao.TipoInterna, HIni = agora
            };
            _lista.Add(nova);
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(nova)));
        }
    }

    public Task<(ResultadoIntervencao, FichaDetalheDto?)> FecharAntigaAsync(string id, bool supervisao, bool fechada, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (f is null) return Task.FromResult(Falha<FichaDetalheDto>(ResultadoIntervencao.NaoExiste));
            if (!RegrasFicha.PodeFecharAntiga(EstadoTexto(f), true)) return Task.FromResult(Falha<FichaDetalheDto>(ResultadoIntervencao.Conflito));
            if (fechada && !f.Fechada && _cfg.ExtExigeDoc && RegrasManutencao.FaltaParaFechar(f.TipoManut, f.NoFornec, f.DocExterno) is not null)
                return Task.FromResult(Falha<FichaDetalheDto>(ResultadoIntervencao.ExigeDocumento));
            f.Supervisao = supervisao;
            f.Fechada = fechada;
            f.D = f.D with { Estado = RegrasManutencao.EstadoDaFicha("", fechada) };
            return Task.FromResult<(ResultadoIntervencao, FichaDetalheDto?)>((ResultadoIntervencao.Ok, Detalhe(f, supervisor, true)));
        }
    }

    public Task<(ResultadoIntervencao, MaterialDto?)> EditarMaterialAsync(string id, string materialId, string design, decimal qtt, decimal epv, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (Acesso(f, quem, supervisor) is { } negado) return Task.FromResult(Falha<MaterialDto>(negado));
            var pos = f!.Materiais.FindIndex(m => m.Id == materialId);
            if (pos < 0) return Task.FromResult(Falha<MaterialDto>(ResultadoIntervencao.NaoExiste));
            var novo = new MaterialDto(materialId, RegrasManutencao.Cortar(design, RegrasManutencao.TamanhoDesignMaterial), qtt, epv, RegrasManutencao.CustoMaterial(qtt, epv));
            f.Materiais[pos] = novo;
            return Task.FromResult<(ResultadoIntervencao, MaterialDto?)>((ResultadoIntervencao.Ok, novo));
        }
    }

    public Task<IReadOnlyList<TempoDto>?> ListarMaoDeObraAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(_lista.FirstOrDefault(x => x.D.Id == id && (supervisor || Minha(x, quem))) is { } f ? (IReadOnlyList<TempoDto>?)f.Tempos.ToList() : null);
    }

    private static TempoDto NovoTempo(string id, int tecno, string nome, decimal horas, decimal custoHora) =>
        new(id, tecno, RegrasManutencao.Cortar(nome, RegrasManutencao.TamanhoNomeTecnico), horas, RegrasManutencao.CustoTempo(horas, custoHora), custoHora);

    public Task<(ResultadoIntervencao, TempoDto?)> AdicionarMaoDeObraAsync(string id, int tecno, string nome, decimal horas, decimal custoHora, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (Acesso(f, quem, supervisor) is { } negado) return Task.FromResult(Falha<TempoDto>(negado));
            // O operador só lança horas de quem está na ficha e usa o último custo/hora do técnico, lido aqui.
            if (!supervisor)
            {
                if (!Pertence(f!, tecno)) return Task.FromResult(Falha<TempoDto>(ResultadoIntervencao.TecnicoInvalido));
                custoHora = UltimoCustoHora(tecno);
            }
            var t = NovoTempo(Guid.NewGuid().ToString("N")[..25], tecno, nome, horas, custoHora);
            f!.Tempos.Add(t);
            return Task.FromResult<(ResultadoIntervencao, TempoDto?)>((ResultadoIntervencao.Ok, t));
        }
    }

    public Task<(ResultadoIntervencao, TempoDto?)> EditarMaoDeObraAsync(string id, string tempoId, int tecno, string nome, decimal horas, decimal custoHora, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (Acesso(f, quem, supervisor) is { } negado) return Task.FromResult(Falha<TempoDto>(negado));
            var pos = f!.Tempos.FindIndex(t => t.Id == tempoId);
            if (pos < 0) return Task.FromResult(Falha<TempoDto>(ResultadoIntervencao.NaoExiste));
            if (!supervisor)
            {
                if (!Pertence(f, tecno)) return Task.FromResult(Falha<TempoDto>(ResultadoIntervencao.TecnicoInvalido));
                custoHora = f.Tempos[pos].CustoHora;
            }
            var novo = NovoTempo(tempoId, tecno, nome, horas, custoHora);
            f.Tempos[pos] = novo;
            return Task.FromResult<(ResultadoIntervencao, TempoDto?)>((ResultadoIntervencao.Ok, novo));
        }
    }

    public Task<ResultadoIntervencao> ApagarMaoDeObraAsync(string id, string tempoId, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (Acesso(f, quem, supervisor) is { } negado) return Task.FromResult(negado);
            return Task.FromResult(f!.Tempos.RemoveAll(t => t.Id == tempoId) > 0 ? ResultadoIntervencao.Ok : ResultadoIntervencao.NaoExiste);
        }
    }

    // O último registo do técnico, em qualquer ficha.
    private decimal UltimoCustoHora(int tecno) =>
        _lista.SelectMany(f => f.Tempos).LastOrDefault(t => t.Tecno == tecno && t.Horas > 0 && t.Custo > 0)?.CustoHora ?? 0m;

    public Task<decimal> CustoHoraSugeridoAsync(int tecno, CancellationToken ct)
    {
        lock (Trinco()) return Task.FromResult(UltimoCustoHora(tecno));
    }

    public Task<IReadOnlyList<CompraLigadaDto>?> ListarComprasAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(_lista.FirstOrDefault(x => x.D.Id == id && (supervisor || Minha(x, quem))) is { } f
                ? (IReadOnlyList<CompraLigadaDto>?)(_cfg.ComprasFichaInt ? f.Compras.ToList() : [])
                : null);
    }

    public Task<IReadOnlyList<FornecedorDto>> PesquisarFornecedoresAsync(string texto, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FornecedorDto>>(Fornecedores.Where(f => Contem(f.Nome, texto) || f.No.ToString() == texto).Take(30).ToList());

    private DateOnly? Prevista(Ficha f)
    {
        var t = Tarefas.FirstOrDefault(x => x.Id == f.Tarefa);
        if (t is null) return null;
        var fichas = _lista.Where(x => x.Tarefa == t.Id).ToList();
        DateTime? Maior(Func<Ficha, bool> onde) => fichas.Where(onde).Select(x => (DateTime?)x.D.DataPlaneada.ToDateTime(TimeOnly.MinValue)).Max();
        var (unidade, intervalo) = Regra(t);
        var prevista = RegrasManutencao.ProximaDaTarefa(Maior(x => x.D.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada),
            Maior(x => x.D.Estado == EstadoIntervencao.Cancelada), null, unidade, intervalo, null);
        return prevista.HasValue ? DateOnly.FromDateTime(prevista.Value) : null;
    }

    public Task<ContadoresMenuDto> ContarMenuAsync(CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(new ContadoresMenuDto(
                _lista.Count(f => Aberta(f) && RegrasManutencao.EmAtraso(f.D.DataPlaneada, Prevista(f), Hoje)),
                _lista.Count(f => f.D.Estado == EstadoIntervencao.Concluida && !f.Antiga)));
    }
}
