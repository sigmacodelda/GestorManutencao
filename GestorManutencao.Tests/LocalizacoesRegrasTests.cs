using GestorManutencao.Api;

namespace GestorManutencao.Tests;

public class LocalizacoesRegrasTests
{
    [Fact]
    public void Etiqueta_completa_le_corredor_estante_nivel_e_posicao()
        => Assert.Equal(("A", 3, 2, 4), RegrasLocalizacao.LerEtiqueta("LOC-A-03-2-04"));

    [Fact]
    public void Separador_e_tolerante()
    {
        Assert.Equal(("A", 3, 2, 4), RegrasLocalizacao.LerEtiqueta("LOC'A'03'2'04"));
        Assert.Equal(("A", 3, 2, 4), RegrasLocalizacao.LerEtiqueta("  loc a 03 2 04 "));
    }

    [Fact]
    public void Etiqueta_sem_posicao_nao_serve_para_LerEtiqueta_mas_serve_para_LerLocal()
    {
        Assert.Null(RegrasLocalizacao.LerEtiqueta("LOC-A-03-2"));
        Assert.Equal(("A", 3, 2, (int?)null), RegrasLocalizacao.LerLocal("LOC-A-03-2"));
    }

    [Fact]
    public void LerLocal_aceita_o_texto_sem_prefixo()
    {
        Assert.Equal(("A", 2, 1, (int?)1), RegrasLocalizacao.LerLocal("A-02-1-01"));
        Assert.Equal(("A", 2, 1, (int?)null), RegrasLocalizacao.LerLocal("A-02-1"));
        Assert.Equal(("A", 2, 1, (int?)1), RegrasLocalizacao.LerLocal("LOC-A-02-1-01"));
    }

    [Theory]
    [InlineData("LOC-A-03")]
    [InlineData("LOC-A-0-2-1")]
    [InlineData("ABC-A-03-2-04")]
    [InlineData("LOC-A-03-2-0")]
    [InlineData("")]
    [InlineData(null)]
    public void Textos_invalidos_dao_null(string? texto)
    {
        Assert.Null(RegrasLocalizacao.LerEtiqueta(texto));
        Assert.Null(RegrasLocalizacao.LerLocal(texto));
    }

    [Theory]
    [InlineData("LOC-ABCD-03-2-04")] // corredor com 4 letras
    [InlineData("LOC-A-1000-2-04")]  // estante 1000
    [InlineData("LOC-A-03-100-04")]  // nível 100
    [InlineData("LOC-A-03-2-100")]   // posição 100
    public void Limites_ultrapassados_dao_null(string texto)
    {
        Assert.Null(RegrasLocalizacao.LerEtiqueta(texto));
        Assert.Null(RegrasLocalizacao.LerLocal(texto));
    }

    [Fact]
    public void Limites_maximos_sao_aceites()
        => Assert.Equal(("ABC", 999, 99, (int?)99), RegrasLocalizacao.LerLocal("ABC-999-99-99"));

    [Fact]
    public async Task Memoria_descontar_e_repor()
    {
        var pecas = new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso", "560001", "A-01-1-01", 10, 4, 0.12m)]);
        var repo = new RepositorioLocalizacoesMemoria(pecas);
        var ct = CancellationToken.None;

        Assert.True((await repo.DescontarAsync("P001", "A", "A-02", 1, 1, 2, "ab", ct)).Ok);
        Assert.Equal(1, Assert.Single((await repo.ListarAsync("P001", ct)).Lugares).Qtt);

        // Não fica negativo: pedir mais do que existe falha e não altera.
        var falha = await repo.DescontarAsync("P001", "A", "A-02", 1, 1, 5, "ab", ct);
        Assert.False(falha.Ok);
        Assert.Equal(1, Assert.Single((await repo.ListarAsync("P001", ct)).Lugares).Qtt);

        // Lugar inexistente e corredor diferente também falham.
        Assert.False((await repo.DescontarAsync("P001", "A", "A-09", 1, 1, 1, "ab", ct)).Ok);
        Assert.False((await repo.DescontarAsync("P001", "B", "A-02", 1, 1, 1, "ab", ct)).Ok);

        // A 0 continua Ok; repor soma de volta.
        Assert.True((await repo.DescontarAsync("P001", "A", "A-02", 1, 1, 1, "ab", ct)).Ok);
        Assert.Equal(0, Assert.Single((await repo.ListarAsync("P001", ct)).Lugares).Qtt);
        Assert.True((await repo.ReporAsync("P001", "A", "A-02", 1, 1, 2, "ab", ct)).Ok);
        Assert.Equal(2, Assert.Single((await repo.ListarAsync("P001", ct)).Lugares).Qtt);
    }

    [Fact]
    public async Task Memoria_repor_cria_o_lugar_se_nao_existe()
    {
        var repo = new RepositorioLocalizacoesMemoria(new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso", "560001", "A-01-1-01", 10, 4, 0.12m)]));
        Assert.True((await repo.ReporAsync("P001", "b", "b-03", 2, 4, 1.5m, "ab", CancellationToken.None)).Ok);
        Assert.Contains((await repo.ListarAsync("P001", CancellationToken.None)).Lugares, l => l.Texto == "B-03-2-04" && l.Qtt == 1.5m);
    }

    [Fact]
    public void Estante_normalizada() => Assert.Equal("A-03", RegrasLocalizacao.NormalizarEstante("a", 3));

    [Theory]
    [InlineData(10, 3, 7)]
    [InlineData(3, 3, 0)]
    [InlineData(2, 5, 0)]
    public void Por_arrumar_nunca_e_negativo(double stock, double localizado, double esperado)
        => Assert.Equal((decimal)esperado, RegrasLocalizacao.Calcular((decimal)stock, (decimal)localizado));

    [Fact]
    public void Texto_do_lugar() => Assert.Equal("A-02-1-01", new LugarDto("A", "A-02", 1, 1, 3).Texto);
}
