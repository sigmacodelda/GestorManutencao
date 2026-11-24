namespace GestorManutencao.UI.Modelos;

// Inventário do armazém (dossier 65 criado no desktop): o técnico só conta. Sem preços.
public sealed record Inventario(string Id, decimal Numero, DateOnly Data, string Descricao, int Tecno, string Tecnico, int Linhas, bool Concluido, bool Lancado, bool PodeEditar)
{
    public string Estado => Lancado ? "Lançado" : Concluido ? "Concluído" : "Por contar";
}

public sealed record LinhaInventario(string Id, string Ref, string Design, string Lugar, decimal Qtt);

public sealed record InventarioDetalhe(Inventario Inventario, IReadOnlyList<LinhaInventario> Linhas);

// Regras da contagem que o ecrã verifica antes de enviar (a API volta a verificar).
public static class LogicaInventario
{
    public const string ErroLugar = "O lugar tem de ter corredor, estante, nível e posição (por exemplo A-03-2-04).";

    // LOC-A-03-2-04 ou A-03-2-04: quatro partes depois do prefixo opcional.
    public static bool LugarCompleto(string? local)
    {
        var p = System.Text.RegularExpressions.Regex.Split((local ?? "").Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]+").Where(x => x.Length > 0).ToList();
        if (p.Count > 0 && p[0] == "LOC") p.RemoveAt(0);
        return p.Count == 4 && int.TryParse(p[1], out var e) && e > 0 && int.TryParse(p[2], out var n) && n > 0 && int.TryParse(p[3], out var po) && po > 0;
    }

    // Como LogicaPicagem.LerQuantidade, mas contar zero é válido (lugar vazio).
    public static bool LerQuantidade(string? texto, out decimal qtt)
    {
        qtt = 0;
        var t = (texto ?? "").Trim().Replace(',', '.');
        if (t.Length > 0 && t.Trim('0', '.').Length == 0 && t.Count(c => c == '.') <= 1 && t != ".") return true;
        return LogicaPicagem.LerQuantidade(t, out qtt);
    }
}
