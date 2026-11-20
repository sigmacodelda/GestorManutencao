namespace GestorManutencao.UI.Servicos;

public enum ModoLeitura { Automatico, Camara, Leitor }

public interface IPreferenciasLeitura
{
    ModoLeitura Modo { get; set; }
}

public interface IDetetorLeitor
{
    bool PareceLeitorIndustrial { get; }

    // O que foi visto (fabricante e modelo), para mostrar no ecrã do leitor.
    string Descricao { get; }
}

public interface ILeitorCamara
{
    // Texto lido, ou null se a pessoa cancelou. Lança InvalidOperationException com a mensagem a mostrar (sem permissão, sem câmara...).
    Task<string?> LerAsync(CancellationToken ct = default);
}

// Sem implementação nativa (Windows, desenvolvimento): o leitor é um teclado e a câmara não existe.
public sealed class DetetorLeitorNenhum : IDetetorLeitor
{
    public bool PareceLeitorIndustrial => true;
    public string Descricao => "Computador: teclado ou leitor ligado";
}

public sealed class LeitorCamaraNenhum : ILeitorCamara
{
    public Task<string?> LerAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("A câmara não está disponível neste aparelho.");
}

public sealed class PreferenciasLeituraMemoria : IPreferenciasLeitura
{
    public ModoLeitura Modo { get; set; } = ModoLeitura.Automatico;
}

public static class LogicaLeitura
{
    // Um leitor tipo teclado pode acrescentar CR/LF e espaços; os códigos de peça são arbitrários, por isso nada mais se mexe.
    public static string NormalizarCodigo(string? texto) =>
        texto is null ? "" : string.Concat(texto.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)));

    // "motorola" sozinho é o telemóvel de consumo; só a Motorola Solutions (e a Symbol) faz coletores.
    private static readonly string[] Fabricantes =
    [
        "zebra", "symbol", "motorola solutions", "honeywell", "intermec", "datalogic", "urovo",
        "chainway", "newland", "unitech", "cipherlab", "point mobile", "pointmobile"
    ];

    public static bool PareceLeitorIndustrial(string? fabricante, string? modelo, IEnumerable<string>? pacotes)
    {
        var f = (fabricante ?? "").Trim().ToLowerInvariant();
        var m = (modelo ?? "").Trim().ToLowerInvariant();
        if (Fabricantes.Any(x => f.Contains(x) || m.Contains(x))) return true;
        // Sunmi também faz telemóveis e caixas: só a linha industrial (L2...) ou o que o diz no modelo.
        if (f.Contains("sunmi") && (m.Contains("industrial") || m.StartsWith("l2", StringComparison.Ordinal))) return true;
        return (pacotes ?? []).Any(p =>
            p.Equals("com.symbol.datawedge", StringComparison.OrdinalIgnoreCase)
            || p.Equals("com.honeywell.decode.service", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("com.urovo.", StringComparison.OrdinalIgnoreCase));
    }

    public static ModoLeitura ModoEfetivo(ModoLeitura escolhido, bool pareceLeitor) =>
        escolhido == ModoLeitura.Automatico ? (pareceLeitor ? ModoLeitura.Leitor : ModoLeitura.Camara) : escolhido;

    // Etiqueta de local do armazém: LOC-A-03-2 ou LOC-A-03-2-04. Um leitor tipo teclado pode trocar o hífen por apóstrofo
    // ou outro símbolo, por isso qualquer coisa que não seja letra ou número separa. Só serve para não a tratar como peça.
    public static bool EEtiquetaLocal(string? texto)
    {
        var partes = new List<string>();
        var atual = new System.Text.StringBuilder();
        foreach (var c in (texto ?? "").Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c)) { atual.Append(c); continue; }
            if (atual.Length > 0) { partes.Add(atual.ToString()); atual.Clear(); }
        }
        if (atual.Length > 0) partes.Add(atual.ToString());

        if (partes.Count is not (4 or 5) || partes[0] != "LOC") return false;
        if (!int.TryParse(partes[2], out var estante) || estante <= 0) return false;
        if (!int.TryParse(partes[3], out var nivel) || nivel <= 0) return false;
        return partes.Count == 4 || (int.TryParse(partes[4], out var posicao) && posicao > 0);
    }
}
