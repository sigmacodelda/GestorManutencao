namespace GestorManutencao.Api;

public enum UnidadePeriodo { Dias, Meses, Nenhum }

// Configuração da manutenção (u_configman, uma linha); sem linha valem estes valores, como na LandeiroMaui.
public sealed record ConfigManutencao(
    int Antecedencia = 7, bool GerarPlaneadas = true, bool AvancaOrdem = false, bool ValidarPropria = false,
    bool ExtExigeDoc = true, bool ComprasFichaInt = true,
    // Colunas do backoffice do Landeiro (só se lêem, nunca se gravam aqui): início do plano, limite de atraso em meses
    // (0 = sem limite) e se a regularização manual está ligada. Sem início (ano < 2000) vale a regra antiga.
    DateTime DataInicio = default, int MesesRegularizar = 12, bool Regularizacao = false)
{
    public bool TemInicio => DataInicio.Year >= 2000;
}

public enum DecisaoGeracao { Gerar, PorRegularizar, SemPeriodicidade }

// Data da planeada a criar (só com Gerar); Devia = última feita + 1 período (null sem histórico).
public readonly record struct DecisaoPlaneada(DecisaoGeracao Decisao, DateTime Data, DateTime? Devia);

// Regras copiadas da LandeiroMaui (Helpers/ManutencaoRegras e PeriodicidadeHelper) para as duas apps tratarem as fichas da mesma forma.
public static class RegrasManutencao
{

    // Equipamento parado: não gera planeadas (u_equip.status).
    public const string StatusForaServico = "Fora de Serviço";

    // Há números de ficha errados que não entram no "próximo número do ano".
    public const decimal LimiteNumeroFicha = 1_000_000m;

    // Tamanhos das colunas de u_fichaint e u_matinterv.
    public const int TamanhoNomeTecnico = 45, TamanhoDesignMaterial = 35, TamanhoMotivo = 254;

    public static string Cortar(string? texto, int tamanho)
    {
        var t = (texto ?? "").Trim();
        return t.Length <= tamanho ? t : t[..tamanho].TrimEnd();
    }

    public static UnidadePeriodo LerUnidade(string? unidade) => (unidade ?? "").Trim().ToLowerInvariant() switch
    {
        "dias" => UnidadePeriodo.Dias,
        "meses" => UnidadePeriodo.Meses,
        _ => UnidadePeriodo.Nenhum
    };

    // Próxima intervenção = data da última feita + periodicidade (mês curto usa o último dia).
    public static DateTime? ProximaDepoisDaUltima(DateTime ultima, UnidadePeriodo unidade, int intervalo)
    {
        if (intervalo <= 0 || ultima.Year <= 1900) return null;
        return unidade switch
        {
            UnidadePeriodo.Dias => ultima.Date.AddDays(intervalo),
            UnidadePeriodo.Meses => ultima.Date.AddMonths(intervalo),
            _ => null
        };
    }

    // Primeira data do calendário (âncora + k períodos, k ≥ 1) que não é antes de hoje.
    public static DateTime? ProximaNoCalendario(DateTime ancora, UnidadePeriodo unidade, int intervalo, DateTime hoje)
    {
        if (intervalo <= 0 || unidade == UnidadePeriodo.Nenhum || ancora.Year <= 1900) return null;
        hoje = hoje.Date;
        for (var k = 1; k <= 20000; k++)
        {
            var d = unidade == UnidadePeriodo.Dias ? ancora.Date.AddDays((double)k * intervalo) : ancora.Date.AddMonths(k * intervalo);
            if (d >= hoje) return d;
        }
        return null;
    }

    // Mesma regra do Landeiro (PeriodicidadeHelper.Decidir, 09/11/2026): devia = última + 1 período. Por chegar → nessa data;
    // já passou há no máximo limiteMeses → hoje (fica em atraso); sem histórico ou mais atrasada → por regularizar. Limite 0 = sem limite.
    public static DecisaoPlaneada Decidir(DateTime? ultima, UnidadePeriodo unidade, int intervalo, DateTime hoje, int limiteMeses)
    {
        if (unidade == UnidadePeriodo.Nenhum || intervalo <= 0) return new(DecisaoGeracao.SemPeriodicidade, default, null);
        if (ultima is not { } u || u.Year <= 1900) return new(DecisaoGeracao.PorRegularizar, default, null);
        hoje = hoje.Date;
        var devia = ProximaDepoisDaUltima(u, unidade, intervalo)!.Value;
        if (devia >= hoje) return new(DecisaoGeracao.Gerar, devia, devia);
        if (limiteMeses > 0 && devia < hoje.AddMonths(-limiteMeses)) return new(DecisaoGeracao.PorRegularizar, default, devia);
        return new(DecisaoGeracao.Gerar, hoje, devia);
    }

    // Com o início do plano: com registo desde o início vale Decidir; sem registo o calendário parte do início, não recupera
    // datas passadas e a planeada fica na próxima data do calendário (nunca em atraso nem por regularizar).
    public static DecisaoPlaneada DecidirComInicio(DateTime? ultima, DateTime inicio, UnidadePeriodo unidade, int intervalo, DateTime hoje, int limiteMeses)
    {
        if (ultima is { } u && u.Year > 1900 && u.Date >= inicio.Date) return Decidir(ultima, unidade, intervalo, hoje, limiteMeses);
        if (unidade == UnidadePeriodo.Nenhum || intervalo <= 0) return new(DecisaoGeracao.SemPeriodicidade, default, null);
        var proxima = ProximaNoCalendario(inicio, unidade, intervalo, hoje);
        return proxima.HasValue ? new(DecisaoGeracao.Gerar, proxima.Value, proxima.Value) : new(DecisaoGeracao.SemPeriodicidade, default, null);
    }

    // Decisão da geração automática: com início do plano segue o Landeiro; sem ele mantém a regra antiga (sem histórico ou atrasada → hoje).
    // Por regularizar só se a regularização do backoffice estiver ligada; senão não fica nada à espera de ninguém e gera para hoje.
    public static DecisaoPlaneada DecidirGeracao(DateTime? ultima, UnidadePeriodo unidade, int intervalo, DateTime hoje, ConfigManutencao cfg)
    {
        if (unidade == UnidadePeriodo.Nenhum || intervalo <= 0) return new(DecisaoGeracao.SemPeriodicidade, default, null);
        hoje = hoje.Date;
        if (!cfg.TemInicio)
        {
            var prox = ultima.HasValue ? ProximaDepoisDaUltima(ultima.Value, unidade, intervalo) : null;
            return new(DecisaoGeracao.Gerar, prox is { } p && p > hoje ? p : hoje, prox);
        }
        var d = DecidirComInicio(ultima, cfg.DataInicio, unidade, intervalo, hoje, cfg.MesesRegularizar);
        return d.Decisao == DecisaoGeracao.PorRegularizar && !cfg.Regularizacao ? new(DecisaoGeracao.Gerar, hoje, d.Devia) : d;
    }

    public static decimal ProximoNumeroFicha(IEnumerable<decimal> numerosDoAno)
    {
        var validos = numerosDoAno.Where(n => n > 0 && n < LimiteNumeroFicha).ToList();
        return validos.Count == 0 ? 1 : validos.Max() + 1;
    }

    // Manutenção externa só conclui e valida com fornecedor e documento externo.
    public static string? FaltaParaFechar(string? tipoManut, decimal noFornecedor, string? docExterno) =>
        string.Equals((tipoManut ?? "").Trim(), "Externa", StringComparison.OrdinalIgnoreCase) && (noFornecedor <= 0 || string.IsNullOrWhiteSpace(docExterno))
            ? "externa_sem_documento"
            : null;

    public static decimal CustoMaterial(decimal qtt, decimal precoUnitario) => Math.Round(qtt * precoUnitario, 3);

    public static decimal CustoTempo(decimal horas, decimal custoHora) => Math.Round(horas * custoHora, 3);

    // Tamanhos das colunas de u_fichaint e u_tempinterv (os mesmos da LandeiroMaui).
    public const int TamanhoPeriodo = 20, TamanhoTipoFicha = 20, TamanhoProblema = 200, TamanhoResolucao = 254, TamanhoDocExterno = 25, TamanhoNomeFornecedor = 55,
        TamanhoRefFicha = 20, TamanhoDesignFicha = 45, TamanhoObs = 4000, TamanhoMotivoCancelamento = 240;

    public const decimal MaxHoras = 999.99m, MaxValor = 999_999_999m, MaxCustoHora = 99_999m;

    public const string TipoInterna = "Interna", TipoExterna = "Externa";

    // Tipo das linhas de compra (fn.u_tpint) que contam para os totais externos da ficha.
    public const string CompraMaoObra = "Mão de Obra", CompraPecas = "Peças";

    // No PHC há valores "-----------" a fazer de vazio: para mostrar, contam como vazio.
    public static string Visivel(string? texto)
    {
        var t = (texto ?? "").Trim();
        return t.TrimStart('-').Trim().Length == 0 ? "" : t;
    }

    // Datas vazias do PHC (1900) passam a null.
    public static DateTime? DataOuNula(DateTime? data) => data is null || data.Value.Year <= 1900 ? null : data;

    // Total M.O. = interna + externa; total peças = materiais internos + externos.
    public static (decimal TotMo, decimal TotRef) Totais(decimal moInterna, decimal moExterna, decimal matInterno, decimal matExterno) =>
        (moInterna + moExterna, matInterno + matExterno);

    // Externos: só vêm das compras quando a configuração as usa e há linhas ligadas; senão ficam os valores à mão.
    public static (decimal MoExterna, decimal MatExterno) Externos(bool usaCompras, IReadOnlyCollection<CompraLigadaDto> compras, decimal moManual, decimal matManual) =>
        !usaCompras || compras.Count == 0
            ? (moManual, matManual)
            : (compras.Where(c => c.Tipo == CompraMaoObra).Sum(c => c.Valor), compras.Where(c => c.Tipo == CompraPecas).Sum(c => c.Valor));

    // Mesma regra do KPI "Atrasadas" do ecrã de planeadas: data passada ou fora de prazo (prevista passada).
    public static bool EmAtraso(DateOnly data, DateOnly? prevista, DateOnly hoje) => data < hoje || (prevista is { } p && p < hoje);

    // Limites da pesquisa de equipamentos e fichas; tamanhos das colunas u_equip.tipo e u_equip.local.
    public const int MaxTextoPesquisa = 60, TamanhoTipo = 30, TamanhoLocal = 65, FichasPorPagina = 20, FichasNoHistorico = 50, MaxEquipamentos = 500, MaxPesquisaAvaria = 20;

    // Mesmo limite da LandeiroMaui; o valor por defeito do PIN (pinmaxtent) fica por conta do PHC e desta API não o usa.
    public const int MaxAntecedencia = 90;

    public static string? ValidarConfiguracao(PedidoConfiguracao p) =>
        p.GerarPlaneadas is null || p.AvancaOrdem is null || p.ValidarPropria is null || p.ExtExigeDoc is null || p.ComprasFichaInt is null
            ? "Faltam opções por indicar."
        : p.Antecedencia is null or < 0 or > MaxAntecedencia ? $"A antecedência tem de ser um número inteiro de dias entre 0 e {MaxAntecedencia}."
        : null;

    // As fichas antigas do PHC não têm estado: fechadas contam como validadas, as que ficaram abertas como concluídas.
    public static EstadoIntervencao EstadoDaFicha(string? estado, bool fechada) =>
        Enum.TryParse<EstadoIntervencao>((estado ?? "").Trim(), out var e) && Enum.IsDefined(e) ? e
        : fechada ? EstadoIntervencao.Validada : EstadoIntervencao.Concluida;

    // Próxima data de uma tarefa = a mais recente entre a última feita (ou, nas fichas antigas, a do equipamento) e a última cancelada, mais a periodicidade;
    // sem nada feito nem cancelado, vale a data da ficha aberta (se existir).
    public static DateTime? ProximaDaTarefa(DateTime? feita, DateTime? cancelada, DateTime? feitaAntiga, UnidadePeriodo unidade, int intervalo, DateTime? dataAberta)
    {
        var ultima = feita ?? feitaAntiga;
        var baseData = cancelada.HasValue && (!ultima.HasValue || cancelada > ultima) ? cancelada : ultima;
        var calculada = baseData.HasValue ? ProximaDepoisDaUltima(baseData.Value, unidade, intervalo) : null;
        return calculada ?? dataAberta;
    }
}
