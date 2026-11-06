using System.Text.Json.Serialization;

namespace GestorManutencao.UI.Modelos;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoIntervencao { Planeada, EmCurso, Concluida, Validada, Cancelada }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoIntervencao { Preventiva, Curativa }

public sealed record Intervencao(
    string Id,
    string Equipamento,
    string Zona,
    TipoIntervencao Tipo,
    DateOnly DataPlaneada,
    EstadoIntervencao Estado,
    string? Anomalia = null,
    string? Feito = null,
    TimeOnly? HoraInicio = null,
    TimeOnly? HoraFim = null,
    string? Tecnico = null,
    int Tecno = 0,
    bool PodeValidar = true,
    DateOnly? UltimaIntervencao = null,
    DateOnly? UltimaCancelada = null,
    DateOnly? DataPrevista = null)
{
    public bool PorFazer => Estado is EstadoIntervencao.Planeada or EstadoIntervencao.EmCurso;

    // Derivado, nunca gravado: o estado "atrasada" depende só da data de hoje.
    public bool EstaAtrasada(DateOnly hoje) => PorFazer && DataPlaneada < hoje;

    // A data em que devia ter sido feita (última + periodicidade) já passou, mesmo que a planeada esteja para hoje.
    public bool ForaDePrazo(DateOnly hoje) => PorFazer && DataPrevista is { } p && p < hoje;
}

public sealed record Equipamento(string Id, string Nome, string Zona);

public sealed record Material(string Id, string Design, decimal Qtt, decimal Epv, decimal Custo);

public sealed record RelatorioGeracao(int Criadas, int JaTinham, int SemPeriodicidade, int Erros);

public static class RegrasMaterial
{
    // Tamanho da coluna do PHC.
    public const int MaxDesign = 35;

    public static string? Validar(string? design, decimal? qtt, decimal? epv) =>
        string.IsNullOrWhiteSpace(design) ? "Escreva o material."
        : design.Trim().Length > MaxDesign ? $"Escreva menos: no máximo {MaxDesign} letras."
        : qtt is null or <= 0 ? "Indique a quantidade."
        : epv is < 0 ? "O preço não pode ser negativo."
        : null;
}

// Regras de preenchimento: devolvem a mensagem para o técnico, ou null se estiver tudo certo.
public static class RegrasIntervencao
{
    // Limites das colunas da ficha de intervenção no PHC.
    public const int MaxAnomalia = 200, MaxFeito = 254;

    public static string? ValidarAvaria(string? equipamento, string? anomalia) =>
        string.IsNullOrWhiteSpace(equipamento) ? "Escolha o equipamento."
        : string.IsNullOrWhiteSpace(anomalia) ? "Descreva o problema."
        : anomalia.Trim().Length > MaxAnomalia ? $"Escreva menos: no máximo {MaxAnomalia} letras."
        : null;

    public static string? ValidarConclusao(string? feito, TimeOnly? inicio, TimeOnly? fim) =>
        string.IsNullOrWhiteSpace(feito) ? "Escreva o que foi feito."
        : feito.Trim().Length > MaxFeito ? $"Escreva menos: no máximo {MaxFeito} letras."
        : inicio is null || fim is null ? "Indique a hora de início e a de fim."
        : fim < inicio ? "A hora de fim não pode ser antes da de início."
        : null;
}

public sealed record GrupoIntervencoes(string Titulo, IReadOnlyList<Intervencao> Itens);

public static class AgrupadorIntervencoes
{
    // Só o que o técnico ainda tem de fazer: em curso, atrasadas e as restantes por data.
    public static IReadOnlyList<GrupoIntervencoes> Agrupar(IEnumerable<Intervencao> todas, DateOnly hoje)
    {
        var porFazer = todas.Where(i => i.PorFazer).ToList();
        var emCurso = porFazer.Where(i => i.Estado == EstadoIntervencao.EmCurso).OrderBy(i => i.DataPlaneada).ToList();
        var restantes = porFazer.Where(i => i.Estado != EstadoIntervencao.EmCurso).ToList();
        var atrasadas = restantes.Where(i => i.EstaAtrasada(hoje)).OrderBy(i => i.DataPlaneada).ToList();
        var seguintes = restantes.Where(i => !i.EstaAtrasada(hoje)).OrderBy(i => i.DataPlaneada).ToList();

        return new[]
        {
            new GrupoIntervencoes("Em curso", emCurso),
            new GrupoIntervencoes("Atrasadas", atrasadas),
            new GrupoIntervencoes("Para fazer", seguintes)
        }.Where(g => g.Itens.Count > 0).ToList();
    }
}
