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
    DateOnly? DataPrevista = null,
    bool PodeAbrir = false, string? Motivo = null,
    bool Pendente = false)
{
    public bool PorFazer => Estado is EstadoIntervencao.Planeada or EstadoIntervencao.EmCurso;

    // Derivado, nunca gravado: o estado "atrasada" depende só da data de hoje.
    public bool EstaAtrasada(DateOnly hoje) => PorFazer && DataPlaneada < hoje;

    // A data em que devia ter sido feita (última + periodicidade) já passou, mesmo que a planeada esteja para hoje.
    public bool ForaDePrazo(DateOnly hoje) => PorFazer && DataPrevista is { } p && p < hoje;
}

// Id, Nome e Zona bastam à pesquisa da avaria; o resto serve à lista e à ficha de equipamentos.
public sealed record Equipamento(
    string Id, string Nome, string Zona, string Ref = "", int NoInterno = 0, string Tipo = "", string Marca = "", string Modelo = "", string Serie = "",
    DateOnly? UltimaIntervencao = null, int FichasAbertas = 0, bool ForaDeServico = false, bool Obsoleto = false)
{
    // Sem intervenção há mais de um ano (ou nunca), só nos equipamentos em uso.
    public bool SemIntervencaoHaUmAno(DateOnly hoje) => !Obsoleto && (UltimaIntervencao is not { } u || u < hoje.AddYears(-1));
}

public sealed record TarefaPlaneada(string Id, string Descricao, string Periodicidade, DateOnly? UltimaIntervencao, DateOnly? ProximaData)
{
    public bool EmAtraso(DateOnly hoje) => ProximaData is { } p && p < hoje;
}

public sealed record FichaEquipamento(Equipamento Equipamento, IReadOnlyList<TarefaPlaneada> Tarefas, IReadOnlyList<Intervencao> Historico);

public sealed record FiltroEquipamentos(string Texto = "", string Tipo = "", string Local = "", bool Obsoletos = false);

public sealed record FiltroFichas(EstadoIntervencao? Estado = null, DateOnly? Desde = null, DateOnly? Ate = null, string Texto = "", int Tecno = 0);

public sealed record PaginaFichas(IReadOnlyList<Intervencao> Itens, bool TemMais);

public sealed record ConfiguracaoManutencao(
    int Antecedencia = 7, bool GerarPlaneadas = true, bool AvancaOrdem = false, bool ValidarPropria = false, bool ExtExigeDoc = true, bool ComprasFichaInt = true,
    bool ExigeMaterial = false);

public static class RegrasConfiguracao
{
    public const int MaxAntecedencia = 90;

    // Mesmas regras do servidor, só para avisar logo no ecrã.
    public static string? Validar(int? antecedencia) =>
        antecedencia is null or < 0 or > MaxAntecedencia ? $"A antecedência tem de ser um número inteiro de dias entre 0 e {MaxAntecedencia}." : null;
}

// O texto de pesquisa fica vazio ou tem de 2 a 60 letras, como no servidor (com 1 letra a lista vem vazia).
public static class RegrasPesquisa
{
    public const int MaxTexto = 60;

    // A API devolve no máximo este número de equipamentos mais um, para a UI saber que a lista foi cortada.
    public const int MaxEquipamentos = 500;

    public static bool TextoValido(string? texto) => (texto ?? "").Trim() is { Length: 0 } or { Length: >= 2 and <= MaxTexto };
}

public sealed record Material(string Id, string Design, decimal Qtt, decimal Epv, decimal Custo);

public sealed record Foto(string Id, string Nome, int Bytes);

public static class RegrasFoto
{
    // Mesmos limites do servidor: 10 fotos por intervenção, até 3 MB cada (a app comprime antes de enviar).
    public const int Max = 10, MaxBytes = 3 * 1024 * 1024, LadoMaximo = 1600;
}

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

    // Com o identificador da operação (avaria registada sem rede) o servidor guarda no máximo 200 - 6 - 32 letras; a UI usa sempre este limite.
    public const int MaxAnomaliaComOpId = 162;

    public static string? ValidarAvaria(string? equipamento, string? anomalia) =>
        string.IsNullOrWhiteSpace(equipamento) ? "Escolha o equipamento."
        : string.IsNullOrWhiteSpace(anomalia) ? "Descreva o problema."
        : anomalia.Trim().Length > MaxAnomaliaComOpId ? $"Escreva menos: no máximo {MaxAnomaliaComOpId} letras."
        : null;

    public static string? ValidarConclusao(string? feito, TimeOnly? inicio, TimeOnly? fim) =>
        string.IsNullOrWhiteSpace(feito) ? "Escreva o que foi feito."
        : feito.Trim().Length > MaxFeito ? $"Escreva menos: no máximo {MaxFeito} letras."
        : inicio is null || fim is null ? "Indique a hora de início e a de fim."
        : fim < inicio ? "A hora de fim não pode ser antes da de início."
        : null;
}

// Textos e cores (classes de chip) do estado, comuns à ficha do equipamento e a "Todas as fichas".
public static class ApresentacaoIntervencao
{
    public static string Estado(EstadoIntervencao estado) => estado switch
    {
        EstadoIntervencao.Planeada => "Planeada",
        EstadoIntervencao.EmCurso => "Em curso",
        EstadoIntervencao.Concluida => "Por validar",
        EstadoIntervencao.Validada => "Validada",
        _ => "Cancelada"
    };

    public static string ClasseChip(EstadoIntervencao estado) => estado switch
    {
        EstadoIntervencao.EmCurso => "chip-curso",
        EstadoIntervencao.Concluida => "chip-aviso",
        EstadoIntervencao.Validada => "chip-ok",
        EstadoIntervencao.Cancelada => "chip-erro",
        _ => ""
    };

    public static string Tipo(TipoIntervencao tipo) => tipo == TipoIntervencao.Curativa ? "Avaria" : "Preventiva";

    // O que se fez, ou o problema se ainda não há nada feito.
    public static string? Resumo(Intervencao i) =>
        !string.IsNullOrWhiteSpace(i.Feito) ? i.Feito : !string.IsNullOrWhiteSpace(i.Anomalia) ? i.Anomalia : null;
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
