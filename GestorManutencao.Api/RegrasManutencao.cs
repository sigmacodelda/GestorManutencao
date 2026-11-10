namespace GestorManutencao.Api;

public enum UnidadePeriodo { Dias, Meses, Nenhum }

// Configuração da manutenção (u_configman, uma linha); sem linha valem estes valores, como na LandeiroMaui.
public sealed record ConfigManutencao(
    int Antecedencia = 7, bool GerarPlaneadas = true, bool AvancaOrdem = false, bool ValidarPropria = false,
    bool ExtExigeDoc = true, bool ComprasFichaInt = true);

// Regras copiadas da LandeiroMaui (Helpers/ManutencaoRegras e PeriodicidadeHelper) para as duas apps tratarem as fichas da mesma forma.
public static class RegrasManutencao
{
    public const string TipoVeiculos = "Veículos";

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
