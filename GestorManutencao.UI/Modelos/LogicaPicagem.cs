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

// Regras puras da escolha de peça e lugar na ficha (material). O estado da escolha é sempre o EstadoPicagem (fonte única).
public static class LogicaFichaPeca
{
    // Só interessam os lugares com stock: é de lá que a peça pode ter saído.
    public static IReadOnlyList<Lugar> ComStock(LocalizacoesPeca? lugares) =>
        lugares?.Lugares.Where(l => l.Qtt > 0).ToList() ?? [];

    // Um só lugar com stock: escolhe-se sozinho. Vários ou nenhum: não.
    public static Lugar? LugarUnico(LocalizacoesPeca? lugares) => ComStock(lugares) is [var unico] ? unico : null;

    // Etiqueta que a API aceita (a mesma forma que a arrumação usa para um lugar da lista).
    public static string EtiquetaDe(Lugar lugar) => "LOC-" + lugar.Texto;

    // Depois de escolher a peça: se o local ainda não foi picado e a peça só está num lugar, fica esse.
    public static EstadoPicagem AplicarLugarUnico(EstadoPicagem estado, LocalizacoesPeca? lugares) =>
        estado.Peca is not null && estado.Local is null && LugarUnico(lugares) is { } l ? estado.Aplicar(EtiquetaDe(l)) : estado;

    // Peça em vários lugares e ainda sem lugar escolhido (nem picado nem da lista).
    public static bool PrecisaEscolherLugar(EstadoPicagem estado, LocalizacoesPeca? lugares) =>
        estado.Peca is not null && estado.Local is null && ComStock(lugares).Count > 1;

    // Há peça e não falta escolher lugar: segue-se para a quantidade. Uma peça sem lugar com stock segue (a API avisa "sem localização").
    public static bool PodeIrParaQuantidade(EstadoPicagem estado, LocalizacoesPeca? lugares) =>
        estado.Peca is not null && !PrecisaEscolherLugar(estado, lugares);

    // "LOC-A-02-1-01" ou "A-02-1-01" mostram-se como "A-02-1-01".
    public static string TextoLugar(string? local)
    {
        var partes = System.Text.RegularExpressions.Regex.Split((local ?? "").Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]+").Where(p => p.Length > 0).ToList();
        if (partes.Count > 0 && partes[0] == "LOC") partes.RemoveAt(0);
        return string.Join("-", partes);
    }

    // O aviso nunca bloqueia: a pessoa pode já ter a peça na mão.
    public static string? AvisoDisponivel(Peca peca, decimal qtt) => LogicaEcraPecas.AvisoStock(qtt, peca.QuantidadeDisponivel);
}

// O que o componente de escolha entrega ao ecrã: o estado da picagem (fonte única), a peça escolhida e os seus lugares.
public sealed record EscolhaPecaLugar(EstadoPicagem Estado, Peca? Peca, LocalizacoesPeca? Lugares)
{
    public string? Ref => Estado.Peca;
    public string? Local => Estado.Local;
    public bool PodeIrParaQuantidade => LogicaFichaPeca.PodeIrParaQuantidade(Estado, Lugares);
}
