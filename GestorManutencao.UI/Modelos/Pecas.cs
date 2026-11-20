namespace GestorManutencao.UI.Modelos;

public sealed record Peca(string Ref, string Design, string Codigo, string Local, decimal Stock, decimal StMin, decimal Preco)
{
    public bool AbaixoDoMinimo => StMin > 0 && Stock <= StMin;
}

// CampoExiste = false: falta criar stobs.u_manut no PHC. HaMais: a lista foi cortada, pedir mais texto.
public sealed record ResultadoPecas(bool CampoExiste, IReadOnlyList<Peca> Pecas, bool HaMais);
