namespace GestorManutencao.UI.Modelos;

public enum EstadoIntervencao { Planeada, EmCurso, Concluida, Validada, Cancelada }

public enum TipoIntervencao { Preventiva, Curativa }

public sealed record Intervencao(
    string Id,
    string Equipamento,
    string Zona,
    TipoIntervencao Tipo,
    DateOnly DataPlaneada,
    EstadoIntervencao Estado)
{
    public bool PorFazer => Estado is EstadoIntervencao.Planeada or EstadoIntervencao.EmCurso;

    // Derivado, nunca gravado: o estado "atrasada" depende só da data de hoje.
    public bool EstaAtrasada(DateOnly hoje) => PorFazer && DataPlaneada < hoje;
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
