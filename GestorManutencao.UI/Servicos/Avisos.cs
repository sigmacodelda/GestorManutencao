using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoAviso { Atribuida, Devolvida, Hoje, Atraso, PorValidar, Avaria, SemTecnico, Resumo }

// A Chave identifica o estado que originou o aviso: muda quando o estado muda e é igual entre execuções.
public sealed record Aviso(string Chave, TipoAviso Tipo, string Titulo, string Texto, string? FichaId, DateTime Quando)
{
    // Página que o toque no aviso abre.
    public string Destino(Perfil perfil) =>
        perfil == Perfil.Supervisor
            ? (Tipo == TipoAviso.PorValidar ? "validacao" : "planeadas")
            : (FichaId is null ? "intervencoes" : $"intervencoes/{Uri.EscapeDataString(FichaId)}");
}

public sealed record EntradaAviso(Aviso Aviso, bool Lido);

// O que fica guardado no aparelho, por utilizador.
public sealed record EstadoAvisos(bool Iniciado, IReadOnlyList<string> Vistas, IReadOnlyList<EntradaAviso> Lista)
{
    public static EstadoAvisos Vazio { get; } = new(false, [], []);
}

public sealed record ResultadoAvisos(EstadoAvisos Estado, IReadOnlyList<Aviso> Avisar, bool Mudou);

// Funções puras: dado o estado das fichas e a hora, dizem que avisos existem e quais são novos.
public static class CentroAvisos
{
    public const int MaxLista = 50, MaxVistas = 500, HoraResumo = 8;
    private const string PrefixoDevolvida = "devolvida:";

    public static IReadOnlyList<Aviso> Gerar(Perfil perfil, int tecno, IEnumerable<Intervencao> fichas, DateTime agora)
    {
        var hoje = DateOnly.FromDateTime(agora);
        var unicas = fichas.GroupBy(f => f.Id).Select(g => g.First()).OrderBy(f => f.DataPlaneada).ThenBy(f => f.Id, StringComparer.Ordinal).ToList();
        var avisos = perfil == Perfil.Supervisor ? ParaSupervisor(unicas, hoje, agora) : ParaTecnico(unicas, tecno, hoje, agora);
        if (ResumoDoDia(perfil, unicas, tecno, hoje, agora) is { } resumo) avisos.Add(resumo);
        return avisos;
    }

    // tecno 0 = desconhecido: a API já só devolve as fichas do próprio (principal ou adicional) e as sem dono, por isso basta excluir as sem dono.
    private static bool Minha(Intervencao f, int tecno) => f.Tecno != 0 && (tecno == 0 || f.Envolve(tecno));

    private static List<Aviso> ParaTecnico(List<Intervencao> fichas, int tecno, DateOnly hoje, DateTime agora)
    {
        var avisos = new List<Aviso>();
        foreach (var f in fichas.Where(f => Minha(f, tecno)))
        {
            // Também em curso: a ficha pode ter passado a "em curso" antes de a app a ver. Ficam de fora a devolvida (tem aviso próprio)
            // e a avaria (curativa), que é o próprio técnico que a abre.
            if (f.Estado == EstadoIntervencao.Planeada
                || (f.Estado == EstadoIntervencao.EmCurso && f.Tipo != TipoIntervencao.Curativa && string.IsNullOrWhiteSpace(f.Motivo)))
                avisos.Add(Novo($"atribuida:{f.Id}:{f.Tecno}", TipoAviso.Atribuida, "Nova intervenção atribuída", $"{f.Equipamento} · para {f.DataPlaneada:dd/MM}", f, agora));
            if (f.Estado == EstadoIntervencao.EmCurso && !string.IsNullOrWhiteSpace(f.Motivo))
                avisos.Add(Novo($"{PrefixoDevolvida}{f.Id}:{Hash(f.Motivo.Trim())}", TipoAviso.Devolvida, "Intervenção devolvida", $"{f.Equipamento}: {f.Motivo.Trim()}", f, agora));
            if (f.Estado == EstadoIntervencao.Planeada && f.DataPlaneada == hoje)
                avisos.Add(Novo($"hoje:{f.Id}:{hoje:yyyyMMdd}", TipoAviso.Hoje, "Intervenção para hoje", f.Equipamento, f, agora));
            if (f.EstaAtrasada(hoje))
                avisos.Add(Novo($"atraso:{f.Id}:{f.DataPlaneada:yyyyMMdd}", TipoAviso.Atraso, "Intervenção em atraso", $"{f.Equipamento} · devia ter sido feita a {f.DataPlaneada:dd/MM}", f, agora));
        }
        return avisos;
    }

    private static List<Aviso> ParaSupervisor(List<Intervencao> fichas, DateOnly hoje, DateTime agora)
    {
        var avisos = new List<Aviso>();
        foreach (var f in fichas)
        {
            if (f.Estado == EstadoIntervencao.Concluida && f.PodeValidar)
                avisos.Add(Novo($"validar:{f.Id}:{Hash((f.Feito ?? "") + f.HoraFim?.ToString("HH:mm", CultureInfo.InvariantCulture))}", TipoAviso.PorValidar, "Ficha por validar",
                    f.Tecnico is null ? f.Equipamento : $"{f.Equipamento} · {f.Tecnico}", f, agora));
            if (f.Tipo == TipoIntervencao.Curativa && f.PorFazer)
                avisos.Add(Novo($"avaria:{f.Id}", TipoAviso.Avaria, "Avaria registada",
                    string.IsNullOrWhiteSpace(f.Anomalia) ? f.Equipamento : $"{f.Equipamento}: {f.Anomalia.Trim()}", f, agora));
            if (EmAtraso(f, hoje))
            {
                var devia = f.EstaAtrasada(hoje) ? f.DataPlaneada : f.DataPrevista ?? f.DataPlaneada;
                avisos.Add(Novo($"atraso:{f.Id}:{devia:yyyyMMdd}", TipoAviso.Atraso, "Ficha em atraso", $"{f.Equipamento} · devia ter sido feita a {devia:dd/MM}", f, agora));
            }
            if (f.Estado == EstadoIntervencao.Planeada && f.Tecno == 0 && f.DataPlaneada >= hoje && f.DataPlaneada <= hoje.AddDays(1))
                avisos.Add(Novo($"semtecnico:{f.Id}:{f.DataPlaneada:yyyyMMdd}", TipoAviso.SemTecnico, "Ficha sem técnico",
                    $"{f.Equipamento} · {(f.DataPlaneada == hoje ? "hoje" : "amanhã")}", f, agora));
        }
        return avisos;
    }

    private static bool EmAtraso(Intervencao f, DateOnly hoje) => f.EstaAtrasada(hoje) || f.ForaDePrazo(hoje);

    // Um por dia e só depois das 08:00; a chave só tem a data.
    private static Aviso? ResumoDoDia(Perfil perfil, List<Intervencao> fichas, int tecno, DateOnly hoje, DateTime agora)
    {
        if (agora.Hour < HoraResumo) return null;
        var supervisor = perfil == Perfil.Supervisor;
        var minhas = supervisor ? fichas : fichas.Where(f => Minha(f, tecno)).ToList();
        var paraHoje = minhas.Count(f => f.PorFazer && f.DataPlaneada == hoje);
        var porValidar = supervisor ? minhas.Count(f => f.Estado == EstadoIntervencao.Concluida && f.PodeValidar) : 0;
        var emAtraso = minhas.Count(f => EmAtraso(f, hoje));

        var partes = new List<string>();
        if (paraHoje > 0) partes.Add($"{paraHoje} para hoje");
        if (porValidar > 0) partes.Add($"{porValidar} por validar");
        if (emAtraso > 0) partes.Add($"{emAtraso} em atraso");
        if (partes.Count == 0) return null;
        return new Aviso($"resumo:{hoje:yyyyMMdd}", TipoAviso.Resumo, "Resumo do dia", string.Join(", ", partes), null, agora);
    }

    private static Aviso Novo(string chave, TipoAviso tipo, string titulo, string texto, Intervencao f, DateTime agora) =>
        new(chave, tipo, titulo, Cortar(texto, 140), f.Id, agora);

    private static string Cortar(string texto, int max) => texto.Length <= max ? texto : texto[..(max - 1)] + "…";

    // FNV-1a: igual em todas as execuções (o GetHashCode do .NET muda a cada arranque).
    public static string Hash(string texto)
    {
        var h = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(texto)) h = (h ^ b) * 16777619u;
        return h.ToString("x8", CultureInfo.InvariantCulture);
    }

    // O que ainda não foi visto entra na lista; na primeira execução entra já lido e sem aviso (linha de base).
    public static ResultadoAvisos Processar(EstadoAvisos estado, IEnumerable<Aviso> gerados)
    {
        var geradosLista = gerados.ToList();
        var vistas = new HashSet<string>(estado.Vistas, StringComparer.Ordinal);
        // Uma devolução que já acabou (a ficha foi concluída outra vez) deixa de contar como vista: se for devolvida de novo, mesmo com o mesmo motivo, avisa outra vez.
        var devolvidasAtivas = geradosLista.Where(a => a.Tipo == TipoAviso.Devolvida).Select(a => a.Chave).ToHashSet(StringComparer.Ordinal);
        var rearmadas = vistas.RemoveWhere(k => k.StartsWith(PrefixoDevolvida, StringComparison.Ordinal) && !devolvidasAtivas.Contains(k));
        var novos = new List<Aviso>();
        foreach (var a in geradosLista)
            if (vistas.Add(a.Chave)) novos.Add(a);

        var baseline = !estado.Iniciado;
        if (novos.Count == 0 && !baseline && rearmadas == 0) return new(estado, [], false);

        var lista = novos.Select(a => new EntradaAviso(a, baseline)).Concat(estado.Lista)
            .OrderByDescending(e => e.Aviso.Quando).Take(MaxLista).ToList();
        var guardadas = estado.Vistas.Where(vistas.Contains).Concat(novos.Select(a => a.Chave)).TakeLast(Math.Max(MaxVistas, geradosLista.Count * 2)).ToList();
        return new(new EstadoAvisos(true, guardadas, lista), baseline ? [] : novos, true);
    }

    // Esvazia a lista mas mantém as vistas: o que foi limpo não volta a aparecer nem a avisar.
    public static EstadoAvisos Limpar(EstadoAvisos estado) => estado with { Lista = [] };

    public static EstadoAvisos MarcarTodasLidas(EstadoAvisos estado) =>
        estado with { Lista = estado.Lista.Select(e => e with { Lido = true }).ToList() };

    public static EstadoAvisos MarcarLida(EstadoAvisos estado, string chave) =>
        estado with { Lista = estado.Lista.Select(e => e.Aviso.Chave == chave ? e with { Lido = true } : e).ToList() };
}
