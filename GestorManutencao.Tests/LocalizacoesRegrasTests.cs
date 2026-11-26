using System.Text.RegularExpressions;
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

    // Antes o saldo descia ao juntar o material (DescontarAsync) e subia ao apagar (ReporAsync); agora só o trigger o mexe, ao validar.
    private static (RepositorioPecasMemoria Pecas, RepositorioLocalizacoesMemoria Lugares) Memoria()
    {
        var pecas = new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso", "560001", "A-01-1-01", 10, 4, 0.12m)]);
        return (pecas, new RepositorioLocalizacoesMemoria(pecas));
    }

    [Fact]
    public async Task Memoria_saldo_so_desce_ao_validar_e_o_reservado_reduz_o_disponivel()
    {
        var (pecas, repo) = Memoria();
        var ct = CancellationToken.None;
        await pecas.AbrirConferenciaAsync("F1", "ab", ct);

        // Saída em conferência (emconf 1): o saldo do lugar fica nos 3, mas 2 ficam reservados.
        Assert.True((await pecas.DarSaidaAsync("F1", "M1", "P001", 2, "ab", ct, "A-02-1-01")).Ok);
        var d = await repo.ListarAsync("P001", ct);
        var lugar = Assert.Single(d.Lugares);
        Assert.Equal((1m, 2m), (lugar.Qtt, lugar.Reservado));
        Assert.Equal(3, d.Localizado);

        // Validar (emconf 1 -> 0): o saldo desce uma vez e deixa de haver reservado.
        await pecas.ConfirmarConferenciaAsync("F1", "ab", ct);
        await pecas.ConfirmarConferenciaAsync("F1", "ab", ct);
        d = await repo.ListarAsync("P001", ct);
        lugar = Assert.Single(d.Lugares);
        Assert.Equal((1m, 0m), (lugar.Qtt, lugar.Reservado));
        Assert.Equal(1, d.Localizado);
    }

    [Fact]
    public async Task Memoria_apagar_a_saida_em_conferencia_nao_repoe_nada_porque_nunca_saiu()
    {
        var (pecas, repo) = Memoria();
        var ct = CancellationToken.None;
        await pecas.AbrirConferenciaAsync("F1", "ab", ct);
        await pecas.DarSaidaAsync("F1", "M1", "P001", 2, "ab", ct, "A-02-1-01");

        Assert.True((await pecas.AnularSaidaAsync("M1", "ab", ct)).Ok);

        var d = await repo.ListarAsync("P001", ct);
        var lugar = Assert.Single(d.Lugares);
        Assert.Equal((3m, 0m), (lugar.Qtt, lugar.Reservado));
        Assert.Equal(3, d.Localizado);
    }

    [Fact]
    public async Task Memoria_reservar_mais_do_que_existe_deixa_o_disponivel_a_zero_e_o_localizado_no_saldo_fisico()
    {
        var (pecas, repo) = Memoria();
        var ct = CancellationToken.None;
        await pecas.DarSaidaAsync("F1", "M1", "P001", 5, "ab", ct, "A-02-1-01");

        var d = await repo.ListarAsync("P001", ct);
        var lugar = Assert.Single(d.Lugares);

        Assert.Equal((0m, 5m), (lugar.Qtt, lugar.Reservado));
        Assert.Equal(3, d.Localizado); // 0 + 5 passaria o saldo físico (3)
        Assert.Equal(7, d.PorArrumar);
    }

    [Fact]
    public async Task Memoria_saida_sem_lugar_nao_mexe_em_nenhum_lugar()
    {
        var (pecas, repo) = Memoria();
        var ct = CancellationToken.None;
        await pecas.DarSaidaAsync("F1", "M1", "P001", 2, "ab", ct);
        await pecas.ConfirmarConferenciaAsync("F1", "ab", ct);

        Assert.Equal(3, Assert.Single((await repo.ListarAsync("P001", ct)).Lugares).Qtt);
    }

    [Fact]
    public async Task Memoria_mover_so_move_o_disponivel_e_recusa_o_que_esta_todo_reservado()
    {
        var (pecas, repo) = Memoria();
        var ct = CancellationToken.None;
        var origem = new LugarDto("A", "A-02", 1, 1, 0);
        var destino = new LugarDto("B", "B-01", 2, 3, 0);
        await pecas.DarSaidaAsync("F1", "M1", "P001", 2, "ab", ct, "A-02-1-01");

        var acima = await repo.MoverAsync("P001", origem, destino, 2, "ab", ct); // só 1 disponível
        Assert.False(acima.Ok);
        Assert.Contains("Só há 1", acima.Erro);
        Assert.True((await repo.MoverAsync("P001", origem, destino, 1, "ab", ct)).Ok);

        // Sobraram 2 no lugar e os 2 estão reservados pela saída M1.
        var tudoReservado = await repo.MoverAsync("P001", origem, destino, 1, "ab", ct);
        Assert.False(tudoReservado.Ok);
        Assert.Equal(RegrasLocalizacao.ErroMoverReservado, tudoReservado.Erro);
    }

    [Fact]
    public async Task Memoria_mover_origem_sem_quantidade_indicada_recusa_e_nao_cria_destino_a_zero()
    {
        var (_, repo) = Memoria();
        var ct = CancellationToken.None;
        repo.Semear("P001", "C", "C-01", 1, 1, 0);

        var r = await repo.MoverAsync("P001", new LugarDto("C", "C-01", 1, 1, 0), new LugarDto("B", "B-01", 2, 3, 0), 1, "ab", ct);

        Assert.False(r.Ok);
        Assert.Equal(RegrasLocalizacao.ErroMoverSemQuantidade, r.Erro);
        Assert.Empty(repo.Movimentos);
        Assert.DoesNotContain((await repo.ListarAsync("P001", ct)).Lugares, l => l.Texto == "B-01-2-03");
    }

    [Fact]
    public async Task Memoria_semear_cria_o_lugar_se_nao_existe()
    {
        var repo = new RepositorioLocalizacoesMemoria(new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso", "560001", "A-01-1-01", 10, 4, 0.12m)]));
        repo.Semear("P001", "b", "b-03", 2, 4, 1.5m);
        Assert.Contains((await repo.ListarAsync("P001", CancellationToken.None)).Lugares, l => l.Texto == "B-03-2-04" && l.Qtt == 1.5m);
    }

    [Fact]
    public async Task Memoria_arrumar_e_mover_gravam_origem_e_destino_na_linha_do_dossier_66()
    {
        var repo = new RepositorioLocalizacoesMemoria(new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso", "560001", "A-01-1-01", 10, 4, 0.12m)]));
        var ct = CancellationToken.None;
        repo.DefinirBuffer("BUFFER");
        await repo.ArrumarAsync("P001", "C", 1, 1, 1, 2, "ab", ct);
        await repo.MoverAsync("P001", new LugarDto("A", "A-02", 1, 1, 0), new LugarDto("B", "B-01", 2, 3, 0), 1, "ab", ct);

        Assert.Equal([new MovimentoArrumacao(66, "P001", "BUFFER", "C-01-1-01", 2, "ab"), new MovimentoArrumacao(66, "P001", "A-02-1-01", "B-01-2-03", 1, "ab")], repo.Movimentos);
    }

    [Fact]
    public async Task Memoria_arrumar_regista_dossier_66_com_origem_no_buffer_e_destino()
    {
        var repo = new RepositorioLocalizacoesMemoria(new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso", "560001", "A-01-1-01", 10, 4, 0.12m)]));
        repo.DefinirBuffer("B-01-1-01");
        Assert.True((await repo.ArrumarAsync("P001", "A", 1, 2, 3, 2, "ab", CancellationToken.None)).Ok);
        var m = Assert.Single(repo.Movimentos);
        Assert.Equal(new MovimentoArrumacao(66, "P001", "B-01-1-01", "A-01-2-03", 2, "ab"), m);
    }

    [Fact]
    public async Task Memoria_arrumar_com_buffer_em_texto_livre_usa_o_texto_como_origem()
    {
        var repo = new RepositorioLocalizacoesMemoria(new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso", "560001", "A-01-1-01", 10, 4, 0.12m)]));
        repo.DefinirBuffer("buffer");
        await repo.ArrumarAsync("P001", "A", 1, 1, 1, 1, "ab", CancellationToken.None);
        Assert.Equal("BUFFER", Assert.Single(repo.Movimentos).Origem);
    }

    [Fact]
    public async Task Memoria_mover_regista_dossier_66_e_falha_nao_regista()
    {
        var repo = new RepositorioLocalizacoesMemoria(new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso", "560001", "A-01-1-01", 10, 4, 0.12m)]));
        var ct = CancellationToken.None;
        var origem = new LugarDto("A", "A-02", 1, 1, 0);
        var destino = new LugarDto("B", "B-01", 2, 3, 0);
        Assert.False((await repo.MoverAsync("P001", origem, destino, 9, "ab", ct)).Ok); // só há 3
        Assert.Empty(repo.Movimentos);
        Assert.True((await repo.MoverAsync("P001", origem, destino, 2, "ab", ct)).Ok);
        Assert.Equal(new MovimentoArrumacao(66, "P001", "A-02-1-01", "B-01-2-03", 2, "ab"), Assert.Single(repo.Movimentos));
    }

    [Fact]
    public void Texto_do_lugar_para_o_dossier()
    {
        Assert.Equal("", RegrasLocalizacao.TextoLugar(null));
        Assert.Equal("BUFFER", RegrasLocalizacao.TextoLugar(RegrasLocalizacao.LerBuffer("buffer")));
        Assert.Equal("A-03-2-04", RegrasLocalizacao.TextoLugar(RegrasLocalizacao.LerBuffer("A-03-2-04")));
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

    // O SQL da API não corre nos testes: estas verificações leem o código-fonte para garantir o desenho (saldo só pelo trigger).
    private static string[] FicheirosApi()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "GestorManutencao.Api"))) d = d.Parent;
        Assert.NotNull(d);
        return Directory.GetFiles(Path.Combine(d.FullName, "GestorManutencao.Api"), "*.cs");
    }

    [Fact]
    public void Api_nao_escreve_na_u_pecaloc_em_nenhum_fluxo()
    {
        // Com ou sem o esquema dbo., e também UPDATE alias SET ... FROM u_pecaloc (o alvo vem depois do FROM) e INSERT/DELETE/MERGE diretos.
        var escrita = new Regex(
            @"\b(UPDATE|INSERT\s+INTO|DELETE(\s+FROM)?|MERGE(\s+INTO)?|TRUNCATE\s+TABLE)\s+(\[?dbo\]?\.)?\[?u_pecaloc\b"
            + @"|\bUPDATE\b[^;]*?\bFROM\s+(\[?dbo\]?\.)?\[?u_pecaloc\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var ficheiros = FicheirosApi();
        Assert.Contains(ficheiros, f => f.EndsWith("SqlRepositorioLocalizacoes.cs"));
        Assert.DoesNotContain(ficheiros, f => escrita.IsMatch(File.ReadAllText(f)));

        // O próprio padrão apanha as formas que o teste promete apanhar.
        Assert.Matches(escrita, "UPDATE dbo.u_pecaloc SET qtt = 1");
        Assert.Matches(escrita, "UPDATE p SET qtt = 1 FROM dbo.u_pecaloc p WHERE 1 = 1");
        Assert.Matches(escrita, "DELETE FROM u_pecaloc");
        Assert.DoesNotMatch(escrita, "SELECT * FROM v_sc_pecaloc_disponivel");
    }

    [Fact]
    public void Codigo_da_localizacao_das_pecas_e_da_ficha_nao_usa_hints_de_locking()
    {
        var hints = new Regex(@"\b(TABLOCKX?|PAGLOCK|READPAST|XLOCK|UPDLOCK|HOLDLOCK|ROWLOCK)\b", RegexOptions.IgnoreCase);
        var nomes = new[] { "Localizacoes.cs", "RepositorioLocalizacoesMemoria.cs", "RepositorioPecasMemoria.cs", "SqlRepositorioPecas.cs", "FichaConferencia.cs" };
        var visados = FicheirosApi().Where(f => nomes.Contains(Path.GetFileName(f)) || Path.GetFileName(f).StartsWith("SqlRepositorioLocalizacoes")).ToList();
        Assert.Equal(7, visados.Count); // SqlRepositorioInventarios.cs fica fora (tem um UPDLOCK antigo)
        Assert.DoesNotContain(visados, f => hints.IsMatch(File.ReadAllText(f)));
        Assert.Matches(hints, "FROM x WITH (TABLOCK)");
        Assert.Matches(hints, "FROM x WITH (READPAST)");
    }

    [Fact]
    public void Verificacao_das_localizacoes_exige_a_vista_e_o_trigger()
    {
        var sql = File.ReadAllText(FicheirosApi().Single(f => f.EndsWith("SqlRepositorioLocalizacoes.cs")));
        Assert.Contains("v_sc_pecaloc_disponivel", sql);
        Assert.Contains("sc_bi_pecaloc", sql);
        Assert.Contains("sys.triggers", sql);
        Assert.Contains("is_disabled", sql);
    }

    [Fact]
    public void Dossier_66_grava_origem_e_destino_na_linha_bi_e_nao_no_bo()
    {
        var sql = File.ReadAllText(FicheirosApi().Single(f => f.EndsWith("SqlRepositorioLocalizacoesMovimentos.cs")));
        Assert.Matches(new Regex(@"INSERT INTO bi \([^)]*\blobs, lobs2\b", RegexOptions.Singleline), sql);
        Assert.DoesNotMatch(new Regex(@"INSERT INTO bo \([^)]*\blobs", RegexOptions.Singleline), sql);
    }
}
