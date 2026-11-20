namespace GestorManutencao.UI.Modelos;

public sealed record Peca(string Ref, string Design, string Codigo, string Local, decimal Stock, decimal StMin, decimal Preco)
{
    public bool AbaixoDoMinimo => StMin > 0 && Stock <= StMin;
}

// CampoExiste = false: falta criar stobs.u_manut no PHC. HaMais: a lista foi cortada, pedir mais texto.
public sealed record ResultadoPecas(bool CampoExiste, IReadOnlyList<Peca> Pecas, bool HaMais);

// Um lugar da peça no armazém; Texto é o endereço para mostrar (A-02-1-01).
public sealed record Lugar(string Corredor, string Estante, int Nivel, int Posicao, decimal Qtt, string Texto);

public sealed record LocalizacoesPeca(string Ref, decimal Stock, decimal Localizado, decimal PorArrumar, IReadOnlyList<Lugar> Lugares);
