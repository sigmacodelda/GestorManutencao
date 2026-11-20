using GestorManutencao.UI.Servicos;

namespace GestorManutencao.UI.Modelos;

public enum TipoLeitura { Peca, Local }

// Peça e local picam-se por qualquer ordem; cada tipo novo substitui o anterior do mesmo tipo
// (a outra metade mantém-se). Leitura vazia não muda nada. Reutilizável por outros ecrãs de picagem (Task 6).
public sealed record EstadoPicagem(string? Peca, string? Local)
{
    public bool Completo => Peca is not null && Local is not null;

    public EstadoPicagem Aplicar(string leitura)
    {
        var l = LogicaLeitura.NormalizarCodigo(leitura);
        if (l.Length == 0) return this;
        return LogicaPicagem.Classificar(l) == TipoLeitura.Local ? this with { Local = l } : this with { Peca = l };
    }
}

public static class LogicaPicagem
{
    public static TipoLeitura Classificar(string leitura) => LogicaLeitura.EEtiquetaLocal(leitura) ? TipoLeitura.Local : TipoLeitura.Peca;

    // Vírgula ou ponto como decimal, no máximo um separador, até 3 casas. Parte decimal "000" recusa-se
    // porque "1.000" pode ser mil ou um (ambíguo); quem quer um escreve "1".
    public static bool LerQuantidade(string? texto, out decimal qtt)
    {
        qtt = 0;
        var t = (texto ?? "").Trim().Replace(',', '.');
        if (t.Length == 0 || t.Count(c => c == '.') > 1) return false;
        var i = t.IndexOf('.');
        if (i >= 0 && t[(i + 1)..] == "000") return false;
        if (!decimal.TryParse(t, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out qtt)) return false;
        return qtt > 0 && decimal.Round(qtt, 3) == qtt;
    }

    public static string TextoQuantidade(decimal qtt) => qtt.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("pt-PT"));
}
