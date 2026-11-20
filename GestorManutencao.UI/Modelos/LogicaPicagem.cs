using GestorManutencao.UI.Servicos;

namespace GestorManutencao.UI.Modelos;

public enum TipoLeitura { Peca, Local }

// Peça e local picam-se por qualquer ordem; cada tipo novo substitui o anterior do mesmo tipo.
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
}
