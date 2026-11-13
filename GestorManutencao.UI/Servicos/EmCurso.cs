using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// O que a notificação fixa da intervenção a contar tempo mostra. DecorridoMs já desconta as pausas;
// InicioMs é o instante (ms desde 1970) a que o cronómetro corre, só faz sentido sem pausa.
public sealed record AvisoEmCurso(string FichaId, string Titulo, bool EmPausa, long InicioMs, long DecorridoMs);

// Notificação fixa com o cronómetro e o botão Pausar/Retomar (no Android). Nos outros sistemas não faz nada.
public interface IAvisoEmCurso
{
    void Mostrar(AvisoEmCurso aviso);
    void Esconder();
}

public sealed class AvisoEmCursoNenhum : IAvisoEmCurso
{
    public void Mostrar(AvisoEmCurso aviso) { }
    public void Esconder() { }
}

// Pausas vistas por esta app para a ficha em curso (a API só diz se está em pausa agora, não quanto já esteve).
public sealed record PausasEmCurso(string FichaId, DateTime Inicio, long PausadoMs, DateTime? PausadaDesde);

public static class RelogioEmCurso
{
    // Só há aviso com exatamente uma intervenção em curso e que se possa pausar; com zero ou várias não há.
    public static Intervencao? Unica(IEnumerable<Intervencao> fichas)
    {
        var lista = fichas.Where(f => f is { Estado: EstadoIntervencao.EmCurso, PodePausar: true }).Take(2).ToList();
        return lista.Count == 1 ? lista[0] : null;
    }

    public static (AvisoEmCurso Aviso, PausasEmCurso Estado) Calcular(PausasEmCurso? anterior, Intervencao ficha, DateTime agora)
    {
        var inicio = agora.Date + (ficha.HoraInicio?.ToTimeSpan() ?? agora.TimeOfDay);
        if (inicio > agora) inicio = inicio.AddDays(-1);

        var estado = anterior is { } a && a.FichaId == ficha.Id && a.Inicio == inicio
            ? a
            : new PausasEmCurso(ficha.Id, inicio, 0, null);

        if (ficha.EmPausa && estado.PausadaDesde is null)
            estado = estado with { PausadaDesde = agora };
        else if (!ficha.EmPausa && estado.PausadaDesde is { } desde)
            estado = estado with { PausadoMs = estado.PausadoMs + (long)(agora - desde).TotalMilliseconds, PausadaDesde = null };

        var ate = estado.PausadaDesde ?? agora;
        var decorrido = Math.Max(0, (long)(ate - inicio).TotalMilliseconds - estado.PausadoMs);
        var inicioMs = new DateTimeOffset(agora).ToUnixTimeMilliseconds() - decorrido;
        return (new AvisoEmCurso(ficha.Id, ficha.Equipamento, ficha.EmPausa, inicioMs, decorrido), estado);
    }
}
