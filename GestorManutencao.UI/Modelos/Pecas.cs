namespace GestorManutencao.UI.Modelos;

public sealed record Peca(string Ref, string Design, string Codigo, string Local, decimal Stock, decimal StMin, decimal Preco, decimal? Disponivel = null)
{
    // Stock menos o que está em conferência noutras fichas; fichas antigas na cache e servidores sem o campo caem no stock.
    public decimal QuantidadeDisponivel => Disponivel ?? Stock;

    public bool AbaixoDoMinimo => StMin > 0 && Stock <= StMin;
}

// CampoExiste = false: falta criar stobs.u_manut no PHC. HaMais: a lista foi cortada, pedir mais texto.
public sealed record ResultadoPecas(bool CampoExiste, IReadOnlyList<Peca> Pecas, bool HaMais);

// Um lugar da peça no armazém; Texto é o endereço para mostrar (A-02-1-01).
public sealed record PecaNoLugar(string Ref, string Design, decimal Qtt);

// Qtt = disponível (saldo menos o reservado em conferência); Reservado = o que está reservado em fichas em conferência (0 nas respostas antigas).
// Qtt 0 só quer dizer "quantidade não indicada" se Reservado também for 0.
public sealed record Lugar(string Corredor, string Estante, int Nivel, int Posicao, decimal Qtt, string Texto, decimal Reservado = 0)
{
    public bool NaoIndicada => Qtt == 0 && Reservado == 0;
    public bool SoReservado => Qtt == 0 && Reservado > 0;
}

public sealed record LocalizacoesPeca(string Ref, decimal Stock, decimal Localizado, decimal PorArrumar, IReadOnlyList<Lugar> Lugares);
