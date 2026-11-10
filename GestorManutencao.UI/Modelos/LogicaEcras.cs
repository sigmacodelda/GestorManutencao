namespace GestorManutencao.UI.Modelos;

// Lógica dos ecrãs da fase 2 fora das páginas, para se poder testar: filtros, KPIs, ordenação, campos editáveis e textos de estado.

public static class VistaFichas
{
    // Último ano; nas abertas não há data final por omissão, para não esconder as planeadas futuras.
    public static (DateOnly Desde, DateOnly? Ate) IntervaloOmissao(DateOnly hoje, SituacaoFichas situacao = SituacaoFichas.Todas) =>
        (hoje.AddYears(-1), situacao == SituacaoFichas.Abertas ? null : hoje);

    // Validada fecha a ficha; nas antigas vale o campo Fechada.
    public static bool EstaFechada(Intervencao i) => i.Fechada || (!i.Antiga && i.Estado == EstadoIntervencao.Validada);

    public static string? Validar(FiltroFichas f)
    {
        var inf = new DateOnly(1900, 1, 1);
        var sup = new DateOnly(2999, 12, 31);
        if (!RegrasPesquisa.TextoValido(f.Texto)) return "Escreva pelo menos 2 letras para pesquisar.";
        if (f.Desde is { } d && f.Ate is { } a && a < d) return "A data final não pode ser antes da inicial.";
        if ((f.Desde is { } d1 && (d1 < inf || d1 > sup)) || (f.Ate is { } a1 && (a1 < inf || a1 > sup))) return "As datas têm de estar entre 01/01/1900 e 31/12/2999.";
        return null;
    }

    // Usada pelo serviço de desenvolvimento (a API e o SQL fazem o mesmo do lado do servidor).
    public static IEnumerable<Intervencao> Aplicar(IEnumerable<Intervencao> lista, FiltroFichas f)
    {
        var texto = (f.Texto ?? "").Trim();
        var filtradas = lista.Where(i =>
            (f.Estado is null || i.Estado == f.Estado)
            && (f.Desde is null || i.DataPlaneada >= f.Desde) && (f.Ate is null || i.DataPlaneada <= f.Ate)
            && (f.Tecno == 0 || i.Envolve(f.Tecno))
            && (f.Situacao == SituacaoFichas.Todas || (f.Situacao == SituacaoFichas.Abertas ? !EstaFechada(i) && i.Estado != EstadoIntervencao.Cancelada : EstaFechada(i)))
            && (!f.SoPorSupervisionar || i.PorSupervisionar)
            && (f.Tipo is null || i.Tipo == f.Tipo)
            && (texto.Length == 0 || Contem(i.Equipamento, texto) || Contem(i.Anomalia, texto) || Contem(i.Feito, texto) || Contem(i.Tecnico, texto) || i.Numero.ToString() == texto));
        var sentido = f.Descendente ? -1 : 1;
        int Comparar(Intervencao a, Intervencao b)
        {
            var c = f.Ordem switch
            {
                OrdemFichas.Numero => a.Numero.CompareTo(b.Numero),
                OrdemFichas.Equipamento => string.Compare(a.Equipamento, b.Equipamento, StringComparison.CurrentCultureIgnoreCase),
                OrdemFichas.Tipo => a.Tipo.CompareTo(b.Tipo),
                OrdemFichas.Total => a.Total.CompareTo(b.Total),
                _ => a.DataPlaneada.CompareTo(b.DataPlaneada)
            };
            return sentido * (c != 0 ? c : string.CompareOrdinal(a.Id, b.Id));
        }
        return filtradas.OrderBy(i => i, Comparer<Intervencao>.Create(Comparar));
    }

    private static bool Contem(string? valor, string texto) => valor?.Contains(texto, StringComparison.CurrentCultureIgnoreCase) == true;

    public static string Rotulo(OrdemFichas o) => o switch
    {
        OrdemFichas.Numero => "Número",
        OrdemFichas.Equipamento => "Equipamento",
        OrdemFichas.Tipo => "Tipo",
        OrdemFichas.Total => "Total",
        _ => "Data"
    };

    public static string Rotulo(SituacaoFichas s) => s switch { SituacaoFichas.Abertas => "Abertas", SituacaoFichas.Fechadas => "Fechadas", _ => "Todas" };

    public sealed record ChipFicha(string Texto, string Classe);

    // Estado da ficha mais o que a lista do supervisor também mostra: atrasada, fora de ordem e, nas antigas, aberta ou fechada e por supervisionar.
    public static IReadOnlyList<ChipFicha> Chips(Intervencao i, DateOnly hoje)
    {
        var chips = new List<ChipFicha>();
        if (i.Antiga)
        {
            chips.Add(new("Ficha antiga", ""));
            chips.Add(i.Fechada ? new("Fechada", "chip-ok") : new("Aberta", "chip-curso"));
            if (i.PorSupervisionar) chips.Add(new("Por supervisionar", "chip-aviso"));
            return chips;
        }
        chips.Add(new(ApresentacaoIntervencao.Estado(i.Estado), ApresentacaoIntervencao.ClasseChip(i.Estado)));
        if (i.EstaAtrasada(hoje)) chips.Add(new("Atrasada", "chip-erro"));
        if (i.ForaDeOrdem) chips.Add(new("Fora de ordem", "chip-aviso"));
        return chips;
    }
}

public enum KpiEquipamentos { Nenhum, Ativos, ComFichasAbertas, ForaDeServico, SemIntervencao }

public enum OrdemEquipamentos { Nome, Referencia, Tipo, Zona, UltimaIntervencao, FichasAbertas }

public static class VistaEquipamentos
{
    public sealed record Totais(int Ativos, int ComFichasAbertas, int ForaDeServico, int SemIntervencao);

    public static Totais Contar(IEnumerable<Equipamento> lista, DateOnly hoje)
    {
        var l = lista.ToList();
        return new(l.Count(e => !e.Obsoleto), l.Count(e => e.FichasAbertas > 0), l.Count(e => e.ForaDeServico), l.Count(e => e.SemIntervencaoHaUmAno(hoje)));
    }

    public static bool Corresponde(Equipamento e, KpiEquipamentos kpi, DateOnly hoje) => kpi switch
    {
        KpiEquipamentos.Ativos => !e.Obsoleto,
        KpiEquipamentos.ComFichasAbertas => e.FichasAbertas > 0,
        KpiEquipamentos.ForaDeServico => e.ForaDeServico,
        KpiEquipamentos.SemIntervencao => e.SemIntervencaoHaUmAno(hoje),
        _ => true
    };

    public static IReadOnlyList<Equipamento> Aplicar(IEnumerable<Equipamento> lista, KpiEquipamentos kpi, OrdemEquipamentos ordem, bool descendente, DateOnly hoje)
    {
        var filtrada = lista.Where(e => Corresponde(e, kpi, hoje));
        var sentido = descendente ? -1 : 1;
        int Comparar(Equipamento a, Equipamento b)
        {
            var c = ordem switch
            {
                OrdemEquipamentos.Referencia => string.Compare(a.Ref, b.Ref, StringComparison.CurrentCultureIgnoreCase),
                OrdemEquipamentos.Tipo => string.Compare(a.Tipo, b.Tipo, StringComparison.CurrentCultureIgnoreCase),
                OrdemEquipamentos.Zona => string.Compare(a.Zona, b.Zona, StringComparison.CurrentCultureIgnoreCase),
                OrdemEquipamentos.UltimaIntervencao => Nullable.Compare(a.UltimaIntervencao, b.UltimaIntervencao),
                OrdemEquipamentos.FichasAbertas => a.FichasAbertas.CompareTo(b.FichasAbertas),
                _ => string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase)
            };
            return sentido * (c != 0 ? c : string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase));
        }
        return filtrada.OrderBy(e => e, Comparer<Equipamento>.Create(Comparar)).ToList();
    }

    public static string Rotulo(OrdemEquipamentos o) => o switch
    {
        OrdemEquipamentos.Referencia => "Referência",
        OrdemEquipamentos.Tipo => "Tipo",
        OrdemEquipamentos.Zona => "Local",
        OrdemEquipamentos.UltimaIntervencao => "Última intervenção",
        OrdemEquipamentos.FichasAbertas => "Fichas abertas",
        _ => "Nome"
    };
}

public enum KpiPlaneadas { Nenhum, EmAberto, ForaDePrazo, AVencer, EmCurso }

public static class VistaPlaneadas
{
    public const string SemZona = "Sem zona";

    public sealed record Totais(int EmAberto, int ForaDePrazo, int AVencer, int EmCurso);

    // Fora de prazo: a mesma regra do contador do Início (data passada ou prevista passada). A vencer: planeada com hoje <= data <= hoje + antecedência.
    public static bool Corresponde(Intervencao f, KpiPlaneadas kpi, DateOnly hoje, int antecedencia) => kpi switch
    {
        KpiPlaneadas.EmAberto => f.PorFazer,
        KpiPlaneadas.ForaDePrazo => f.EstaAtrasada(hoje) || f.ForaDePrazo(hoje),
        KpiPlaneadas.AVencer => f.Estado == EstadoIntervencao.Planeada && f.DataPlaneada >= hoje && f.DataPlaneada <= hoje.AddDays(Math.Max(0, antecedencia)),
        KpiPlaneadas.EmCurso => f.Estado == EstadoIntervencao.EmCurso,
        _ => true
    };

    public static Totais Contar(IReadOnlyCollection<Intervencao> lista, DateOnly hoje, int antecedencia) => new(
        lista.Count(f => Corresponde(f, KpiPlaneadas.EmAberto, hoje, antecedencia)), lista.Count(f => Corresponde(f, KpiPlaneadas.ForaDePrazo, hoje, antecedencia)),
        lista.Count(f => Corresponde(f, KpiPlaneadas.AVencer, hoje, antecedencia)), lista.Count(f => Corresponde(f, KpiPlaneadas.EmCurso, hoje, antecedencia)));

    public static string ZonaDe(Intervencao f) => string.IsNullOrWhiteSpace(f.Zona) ? SemZona : f.Zona;

    public static IReadOnlyList<string> Zonas(IEnumerable<Intervencao> lista) =>
        lista.Select(ZonaDe).Distinct().OrderBy(z => z == SemZona ? 1 : 0).ThenBy(z => z, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static IReadOnlyList<Intervencao> Filtrar(IEnumerable<Intervencao> lista, KpiPlaneadas kpi, string zona, string texto, DateOnly hoje, int antecedencia)
    {
        var t = (texto ?? "").Trim();
        return lista.Where(f => Corresponde(f, kpi, hoje, antecedencia)
            && (string.IsNullOrEmpty(zona) || ZonaDe(f) == zona)
            && (t.Length == 0 || f.Equipamento.Contains(t, StringComparison.CurrentCultureIgnoreCase) || (f.Tecnico ?? "").Contains(t, StringComparison.CurrentCultureIgnoreCase)
                || (f.Anomalia ?? "").Contains(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }
}

public static class ContadoresInicio
{
    public sealed record Linha(string Rotulo, int Valor, string Href, bool Alerta);

    // Resumo do supervisor a partir do endpoint leve; sem resposta (null) não há resumo.
    public static IReadOnlyList<Linha> Supervisor(ContadoresMenu? c) => c is null
        ? []
        : [new("Por validar", c.PorValidar, "validacao", false), new("Atrasadas", c.PlaneadasAtrasadas, "planeadas", true)];
}

// Valores do formulário da ficha completa; só segue para a API o que mudou e o que o utilizador pode alterar.
public sealed class FormFicha
{
    public DateOnly Data { get; set; }
    public string Periodicidade { get; set; } = "";
    public TipoIntervencao Tipo { get; set; }
    public string TipoManut { get; set; } = "Interna";
    public int Tecno { get; set; }
    public DateTime? HoraInicio { get; set; }
    public DateTime? HoraFim { get; set; }
    public decimal HorasFrio { get; set; }
    public decimal HorasConf { get; set; }
    public string Problema { get; set; } = "";
    public string Resolucao { get; set; } = "";
    public string Obs { get; set; } = "";
    public bool LimRem { get; set; }
    public long NoFornecedor { get; set; }
    public string NomeFornecedor { get; set; } = "";
    public string DocExterno { get; set; } = "";
    public decimal MoExterna { get; set; }
    public decimal MatExterno { get; set; }

    public static FormFicha De(FichaDetalhe d) => new()
    {
        Data = d.Data, Periodicidade = d.Periodicidade, Tipo = d.Tipo, TipoManut = d.TipoManut, Tecno = d.Tecno,
        HoraInicio = d.HoraInicio, HoraFim = d.HoraFim, HorasFrio = d.HorasFrio, HorasConf = d.HorasConf,
        Problema = d.Problema, Resolucao = d.Resolucao, Obs = d.Obs, LimRem = d.LimRem,
        NoFornecedor = d.NoFornecedor, NomeFornecedor = d.NomeFornecedor, DocExterno = d.DocExterno, MoExterna = d.MoExterna, MatExterno = d.MatExterno
    };

    private static readonly DateTime Vazia = new(1900, 1, 1);

    private static bool Igual(DateTime? a, DateTime? b) =>
        a is null || b is null ? a is null && b is null : a.Value.Date == b.Value.Date && a.Value.Hour == b.Value.Hour && a.Value.Minute == b.Value.Minute;

    private static bool Igual(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

    // null = nada mudou. O operador (sem PodeEditarGestao) nunca envia data, tipos, periodicidade nem técnico; os externos só se escrevem à mão sem compras.
    public EdicaoFicha? ParaEdicao(FichaDetalhe o)
    {
        if (!o.PodeEditar) return null;
        var e = new EdicaoFicha(
            HoraInicio: Igual(HoraInicio, o.HoraInicio) ? null : HoraInicio ?? Vazia,
            HoraFim: Igual(HoraFim, o.HoraFim) ? null : HoraFim ?? Vazia,
            HorasFrio: HorasFrio == o.HorasFrio ? null : HorasFrio,
            HorasConf: HorasConf == o.HorasConf ? null : HorasConf,
            Problema: Igual(Problema, o.Problema) ? null : Problema.Trim(),
            Resolucao: Igual(Resolucao, o.Resolucao) ? null : Resolucao.Trim(),
            Obs: Igual(Obs, o.Obs) ? null : Obs.Trim(),
            LimRem: LimRem == o.LimRem ? null : LimRem,
            NoFornecedor: NoFornecedor == o.NoFornecedor ? null : NoFornecedor,
            NomeFornecedor: Igual(NomeFornecedor, o.NomeFornecedor) ? null : NomeFornecedor.Trim(),
            DocExterno: Igual(DocExterno, o.DocExterno) ? null : DocExterno.Trim(),
            MoExterna: o.ExternosAMao && MoExterna != o.MoExterna ? MoExterna : null,
            MatExterno: o.ExternosAMao && MatExterno != o.MatExterno ? MatExterno : null);
        if (o.PodeEditarGestao)
            e = e with
            {
                Data = Data == o.Data ? null : Data,
                Tipo = Tipo == o.Tipo ? null : Tipo,
                TipoManut = Igual(TipoManut, o.TipoManut) ? null : TipoManut,
                Periodicidade = Igual(Periodicidade, o.Periodicidade) ? null : Periodicidade.Trim(),
                Tecno = Tecno == o.Tecno || Tecno <= 0 ? null : Tecno
            };
        // A versão lida vai junto: se a ficha mudou entretanto, o servidor recusa em vez de sobrepor.
        return e == new EdicaoFicha() ? null : e with { Versao = string.IsNullOrEmpty(o.Versao) ? null : o.Versao };
    }

    public bool TemAlteracoes(FichaDetalhe o) => ParaEdicao(o) is not null;

    public string? Validar(FichaDetalhe o) => ParaEdicao(o) is { } e ? RegrasFichaDetalhe.ValidarEdicao(e) : null;
}

// O que cada secção da ficha completa mostra e permite, a partir do que a API já calculou para quem pediu.
public static class SeccoesFicha
{
    public static bool EhExterna(FichaDetalhe d) => string.Equals(d.TipoManut, "Externa", StringComparison.OrdinalIgnoreCase);

    // O bloco do fornecedor mostra-se nas externas, ou quando já tem dados, ou a quem o pode preencher.
    public static bool MostrarExterno(FichaDetalhe d) => EhExterna(d) || d.NoFornecedor > 0 || d.DocExterno.Length > 0 || d.PodeEditar;

    // As compras e os custos externos só se mostram ao supervisor (a API oculta-os ao operador).
    public static bool MostrarCompras(FichaDetalhe d) => !d.ExternosAMao && !d.CustosOcultos;

    public static bool MostrarTotais(FichaDetalhe d) => !d.CustosOcultos;

    public static bool PodeJuntarMaoDeObra(FichaDetalhe d) => d.PodeEditar;

    public static bool PodeValidarOuDevolver(FichaDetalhe d, bool supervisor) => supervisor && !d.Antiga && d.Estado == EstadoIntervencao.Concluida;

    public static bool SoLeitura(FichaDetalhe d) => !d.PodeEditar;

    public static string? AvisoDocumentoExterno(FichaDetalhe d, ConfiguracaoManutencao? cfg) =>
        cfg is { ExtExigeDoc: true } && EhExterna(d) && (d.NoFornecedor <= 0 || d.DocExterno.Length == 0)
            ? "Manutenção externa: falta o fornecedor ou o documento externo para fechar a ficha."
            : null;

    public static string TextoSituacao(FichaDetalhe d) =>
        d.Antiga ? (d.Fechada ? "Ficha antiga fechada" : "Ficha antiga aberta") : ApresentacaoIntervencao.Estado(d.Estado);

    // Fornecedor escolhido: o número e o nome vão juntos.
    public static (long No, string Nome) Fornecedor(Fornecedor f) => (f.No, f.Nome.Length > RegrasFichaDetalhe.MaxNomeFornecedor ? f.Nome[..RegrasFichaDetalhe.MaxNomeFornecedor] : f.Nome);

    public static Intervencao ParaIntervencao(FichaDetalhe d) => new(
        d.Id, d.Equipamento, d.Zona, d.Tipo, d.Data, d.Estado,
        string.IsNullOrWhiteSpace(d.Problema) ? null : d.Problema, string.IsNullOrWhiteSpace(d.Resolucao) ? null : d.Resolucao,
        d.HoraInicio is { } i ? TimeOnly.FromDateTime(i) : null, d.HoraFim is { } f ? TimeOnly.FromDateTime(f) : null,
        d.Tecnico, d.Tecno, true, Motivo: d.Motivo, Adicionais: d.Adicionais,
        Numero: d.Numero, Total: d.Total, Fechada: d.Fechada, Antiga: d.Antiga);
}
