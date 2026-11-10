namespace GestorManutencao.Api;

// Estados da ficha (u_fichaint.estado) como texto, tal como no PHC. Vazio = ficha antiga, fora do circuito.
// Planeada -> EmCurso -> Concluida -> Validada; Concluida -> EmCurso (devolvida); Planeada e EmCurso -> Cancelada.
public static class EstadosFicha
{
    public const string Legado = "", Planeada = "Planeada", EmCurso = "EmCurso", Concluida = "Concluida", Validada = "Validada", Cancelada = "Cancelada";

    private static readonly Dictionary<string, string[]> Transicoes = new()
    {
        [Planeada] = [EmCurso, Cancelada],
        [EmCurso] = [Concluida, Cancelada],
        [Concluida] = [Validada, EmCurso],
        [Validada] = [],
        [Cancelada] = []
    };

    public static string Normalizar(string? estado) => (estado ?? "").Trim();

    public static bool PodeMudar(string? de, string para) => Transicoes.TryGetValue(Normalizar(de), out var destinos) && destinos.Contains(para);

    // Validada e Cancelada já não se alteram.
    public static bool SoLeitura(string? estado) => Normalizar(estado) is Validada or Cancelada;

    public static string Nome(string? estado) => Normalizar(estado) switch
    {
        Planeada => "Planeada",
        EmCurso => "Em curso",
        Concluida => "Concluída",
        Validada => "Validada",
        Cancelada => "Cancelada",
        _ => ""
    };

    // Atrasada = Planeada ou Em curso com data anterior a hoje (derivado, nunca gravado).
    public static bool Atrasada(string? estado, DateTime data, DateTime hoje) => Normalizar(estado) is Planeada or EmCurso && data.Date < hoje.Date;

    // A vencer = Planeada com data entre hoje e hoje + antecedência.
    public static bool AVencer(string? estado, DateTime data, DateTime hoje, int antecedencia) =>
        Normalizar(estado) == Planeada && data.Date >= hoje.Date && data.Date <= hoje.Date.AddDays(Math.Max(0, antecedencia));
}
