using GestorManutencao.UI.Modelos;

namespace GestorManutencao.Tests;

public class PicagemTests
{
    private static readonly EstadoPicagem Vazio = new(null, null);

    [Fact]
    public void Peca_e_depois_local_fica_completo()
    {
        var e = Vazio.Aplicar("5600012345");
        Assert.False(e.Completo);
        e = e.Aplicar("LOC-A-02-1-01");
        Assert.True(e.Completo);
        Assert.Equal("5600012345", e.Peca);
        Assert.Equal("LOC-A-02-1-01", e.Local);
    }

    [Fact]
    public void Local_e_depois_peca_fica_completo()
    {
        var e = Vazio.Aplicar("LOC-A-02-1-01").Aplicar("5600012345");
        Assert.True(e.Completo);
    }

    [Fact]
    public void Duas_pecas_seguidas_ficam_com_a_ultima()
    {
        var e = Vazio.Aplicar("111111").Aplicar("222222");
        Assert.Equal("222222", e.Peca);
        Assert.Null(e.Local);
    }

    [Fact]
    public void Dois_locais_ficam_com_o_ultimo()
    {
        var e = Vazio.Aplicar("LOC-A-02-1-01").Aplicar("LOC-B-03-2-04");
        Assert.Equal("LOC-B-03-2-04", e.Local);
        Assert.Null(e.Peca);
    }

    [Fact]
    public void Apostrofos_dao_local_e_espaco_interno_da_peca()
    {
        Assert.Equal(TipoLeitura.Local, LogicaPicagem.Classificar("LOC'A'02'1'01"));
        Assert.Equal(TipoLeitura.Peca, LogicaPicagem.Classificar("AB 123"));
        Assert.Equal(TipoLeitura.Peca, LogicaPicagem.Classificar("5600012345"));
    }

    [Theory]
    [InlineData("2", "2")]
    [InlineData(" 2,5 ", "2.5")]
    [InlineData("2.5", "2.5")]
    [InlineData("0,125", "0.125")]
    [InlineData("12,", "12")]
    public void Quantidade_aceita_virgula_ou_ponto(string texto, string esperado)
    {
        Assert.True(LogicaPicagem.LerQuantidade(texto, out var q));
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), q);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.000")]
    [InlineData("1,000")]
    [InlineData("1.000,5")]
    [InlineData("1,2,3")]
    [InlineData("1,2345")]
    [InlineData("1e3")]
    public void Quantidade_recusa_texto_ambiguo_ou_invalido(string texto) =>
        Assert.False(LogicaPicagem.LerQuantidade(texto, out _));

    [Fact]
    public void Quantidade_proposta_usa_virgula()
    {
        Assert.Equal("2,5", LogicaPicagem.TextoQuantidade(2.5m));
        Assert.Equal("3", LogicaPicagem.TextoQuantidade(3m));
    }

    private static LocalizacoesPeca Lugares(params (string Texto, decimal Qtt)[] lugares) =>
        new("P001", 10, lugares.Sum(l => l.Qtt), 0, lugares.Select(l =>
        {
            var p = l.Texto.Split('-');
            return new Lugar(p[0], p[0] + "-" + p[1], int.Parse(p[2]), int.Parse(p[3]), l.Qtt, l.Texto);
        }).ToList());

    [Fact]
    public void Ficha_peca_com_um_so_lugar_com_stock_escolhe_o_lugar_sozinha()
    {
        var e = LogicaFichaPeca.AplicarLugarUnico(Vazio.Aplicar("P001"), Lugares(("A-01-1-01", 4), ("A-01-2-03", 0)));

        Assert.Equal("LOC-A-01-1-01", e.Local);
        Assert.True(e.Completo);
        Assert.False(LogicaFichaPeca.PrecisaEscolherLugar(e, Lugares(("A-01-1-01", 4))));
    }

    [Fact]
    public void Ficha_peca_com_varios_lugares_obriga_a_escolher_e_nao_escolhe_sozinha()
    {
        var lug = Lugares(("A-01-1-01", 4), ("B-02-1-03", 2));
        var e = LogicaFichaPeca.AplicarLugarUnico(Vazio.Aplicar("P001"), lug);

        Assert.Null(e.Local);
        Assert.True(LogicaFichaPeca.PrecisaEscolherLugar(e, lug));
        Assert.False(LogicaFichaPeca.PodeIrParaQuantidade(e, lug));
        // Escolher à mão (lista) ou picar a etiqueta do lugar resolve.
        var daLista = e.Aplicar(LogicaFichaPeca.EtiquetaDe(lug.Lugares[1]));
        Assert.Equal("LOC-B-02-1-03", daLista.Local);
        Assert.True(LogicaFichaPeca.PodeIrParaQuantidade(daLista, lug));
        Assert.True(LogicaFichaPeca.PodeIrParaQuantidade(e.Aplicar("LOC-A-01-1-01"), lug));
    }

    [Fact]
    public void Ficha_peca_sem_lugar_com_stock_segue_para_a_quantidade()
    {
        var lug = Lugares(("A-01-1-01", 0));
        var e = LogicaFichaPeca.AplicarLugarUnico(Vazio.Aplicar("P001"), lug);

        Assert.Null(e.Local);
        Assert.True(LogicaFichaPeca.PodeIrParaQuantidade(e, lug));
        Assert.True(LogicaFichaPeca.PodeIrParaQuantidade(e, null));
    }

    [Fact]
    public void Ficha_local_picado_primeiro_nao_e_trocado_pelo_lugar_unico()
    {
        var e = LogicaFichaPeca.AplicarLugarUnico(Vazio.Aplicar("LOC-B-02-1-03").Aplicar("P001"), Lugares(("A-01-1-01", 4)));

        Assert.Equal("LOC-B-02-1-03", e.Local);
        Assert.Equal("P001", e.Peca);
    }

    [Fact]
    public void Ficha_sem_peca_nao_avanca_para_a_quantidade()
    {
        Assert.False(LogicaFichaPeca.PodeIrParaQuantidade(Vazio.Aplicar("LOC-A-01-1-01"), null));
    }

    [Theory]
    [InlineData("LOC-A-02-1-01", "A-02-1-01")]
    [InlineData("A-02-1-01", "A-02-1-01")]
    [InlineData("LOC'A'02'1'01", "A-02-1-01")]
    [InlineData(null, "")]
    public void Ficha_texto_do_lugar_sem_o_prefixo(string? local, string esperado) =>
        Assert.Equal(esperado, LogicaFichaPeca.TextoLugar(local));

    [Fact]
    public void Ficha_aviso_usa_o_disponivel_e_nunca_o_stock_real()
    {
        var peca = new Peca("P001", "Parafuso", "560001", "A-01-1-01", Stock: 10, StMin: 0, Preco: 1, Disponivel: 2);

        Assert.Equal("Stock insuficiente: há 2 e pede 3.", LogicaFichaPeca.AvisoDisponivel(peca, 3));
        Assert.Null(LogicaFichaPeca.AvisoDisponivel(peca, 2));
        // Servidor sem o campo (ou ficha antiga na cache): cai no stock.
        Assert.Equal(10, (peca with { Disponivel = null }).QuantidadeDisponivel);
    }

    [Fact]
    public void Leitura_vazia_e_ignorada()
    {
        var e = Vazio.Aplicar("111111");
        Assert.Equal(e, e.Aplicar("  "));
        Assert.Equal(e, e.Aplicar(""));
    }

    // ---- Troca de peça: o lugar derivado da peça anterior não passa para a nova ----

    [Fact]
    public void Troca_de_peca_limpa_o_lugar_unico_da_anterior_e_recalcula_o_da_nova()
    {
        var a = SelecaoPecaLugar.Vazia.EscolherPeca("PA", Lugares(("A-01-1-01", 4)));
        Assert.Equal("LOC-A-01-1-01", a.Estado.Local);
        Assert.Equal(OrigemLugar.DaPeca, a.Origem);

        var b = a.EscolherPeca("PB", Lugares(("B-02-1-03", 2)));

        Assert.Equal("PB", b.Estado.Peca);
        Assert.Equal("LOC-B-02-1-03", b.Estado.Local);
    }

    [Fact]
    public void Troca_para_peca_em_varios_lugares_deixa_o_lugar_por_escolher()
    {
        var a = SelecaoPecaLugar.Vazia.EscolherPeca("PA", Lugares(("A-01-1-01", 4)));

        var b = a.EscolherPeca("PB", Lugares(("B-02-1-03", 2), ("B-02-1-04", 1)));

        Assert.Null(b.Estado.Local);
        Assert.Equal(OrigemLugar.Nenhuma, b.Origem);
    }

    [Fact]
    public void Lugar_escolhido_da_lista_tambem_sai_ao_trocar_de_peca()
    {
        var lugares = Lugares(("A-01-1-01", 4), ("A-01-2-03", 2));
        var a = SelecaoPecaLugar.Vazia.EscolherPeca("PA", lugares).EscolherLugar(lugares.Lugares[1]);
        Assert.Equal("LOC-A-01-2-03", a.Estado.Local);

        var b = a.EscolherPeca("PB", Lugares(("B-02-1-03", 2), ("B-02-1-04", 1)));

        Assert.Null(b.Estado.Local);
    }

    [Fact]
    public void Mudar_de_peca_limpa_o_lugar_derivado_mas_nao_o_picado()
    {
        var derivado = SelecaoPecaLugar.Vazia.EscolherPeca("PA", Lugares(("A-01-1-01", 4))).LimparPeca();
        Assert.Null(derivado.Estado.Peca);
        Assert.Null(derivado.Estado.Local);

        var picado = SelecaoPecaLugar.Vazia.EscolherPeca("PA", Lugares(("A-01-1-01", 4), ("A-01-2-03", 2))).PicarLocal("LOC-C-03-1-01").LimparPeca();
        Assert.Null(picado.Estado.Peca);
        Assert.Equal("LOC-C-03-1-01", picado.Estado.Local);
    }

    [Fact]
    public void Local_picado_primeiro_mantem_se_ao_escolher_e_ao_trocar_de_peca()
    {
        var s = SelecaoPecaLugar.Vazia.PicarLocal("LOC-B-02-1-03");
        Assert.Equal(OrigemLugar.Picado, s.Origem);

        var a = s.EscolherPeca("PA", Lugares(("A-01-1-01", 4)));
        Assert.Equal("LOC-B-02-1-03", a.Estado.Local);
        Assert.Equal(OrigemLugar.Picado, a.Origem);

        var b = a.EscolherPeca("PB", Lugares(("C-01-1-01", 4)));
        Assert.Equal("LOC-B-02-1-03", b.Estado.Local);
    }

    [Fact]
    public void Mudar_de_lugar_deixa_de_haver_lugar_e_a_origem_limpa()
    {
        var s = SelecaoPecaLugar.Vazia.EscolherPeca("PA", Lugares(("A-01-1-01", 4))).LimparLocal();
        Assert.Null(s.Estado.Local);
        Assert.Equal(OrigemLugar.Nenhuma, s.Origem);
        Assert.Equal("PA", s.Estado.Peca);
    }

    [Fact]
    public void Leitura_vazia_de_local_nao_muda_a_selecao()
    {
        var s = SelecaoPecaLugar.Vazia.EscolherPeca("PA", Lugares(("A-01-1-01", 4)));
        Assert.Equal(s, s.PicarLocal("  "));
    }

    // ---- Conferência repetida: Pendente nunca confirma ----

    private static Intervencao Ficha(bool pendente, string? aviso) =>
        new("F1", "Compressor", "Produção", TipoIntervencao.Curativa, new DateOnly(2026, 11, 9), EstadoIntervencao.Validada, Pendente: pendente, AvisoStock: aviso);

    [Fact]
    public void Conferencia_pendente_nao_limpa_o_aviso_nem_diz_confirmada()
    {
        var r = LogicaConferencia.AposRepetir("Conferência por confirmar", Ficha(true, null));

        Assert.False(r.Confirmada);
        Assert.Equal("Conferência por confirmar", r.Aviso);
        Assert.Equal(LogicaConferencia.PorEnviar, r.Mensagem);
    }

    [Fact]
    public void Conferencia_sem_aviso_da_api_e_confirmada()
    {
        var r = LogicaConferencia.AposRepetir("Conferência por confirmar", Ficha(false, null));
        Assert.True(r.Confirmada);
        Assert.Null(r.Aviso);
    }

    [Fact]
    public void Conferencia_com_aviso_da_api_continua_por_confirmar_com_o_texto_novo()
    {
        var r = LogicaConferencia.AposRepetir("antigo", Ficha(false, "novo"));
        Assert.False(r.Confirmada);
        Assert.Equal("novo", r.Aviso);
        Assert.Null(r.Mensagem);
    }

    private static Material Mat(string id, string r, bool saida = false, bool falta = false) =>
        new(id, "Peça " + id, 1, 1, 1, r, saida, null, null, falta);

    [Fact]
    public void Candidatos_do_lugar_sao_so_pecas_ligadas_por_dar_e_gravadas()
    {
        var lista = new[] { Mat("1", "P1"), Mat("2", ""), Mat("3", "P3", saida: true), Mat("4", "P4", falta: true) };
        Assert.Equal(["1"], LogicaFichaPeca.CandidatosDoLugar(lista).Select(m => m.Id));
    }

    [Fact]
    public void Linha_da_peca_encontra_pela_ref_sem_distinguir_maiusculas()
    {
        var lista = new[] { Mat("1", "P1"), Mat("2", "ABC") };
        Assert.Equal("2", LogicaFichaPeca.LinhaDaPeca(lista, " abc ")?.Id);
        Assert.Null(LogicaFichaPeca.LinhaDaPeca(lista, "XYZ"));
        Assert.Null(LogicaFichaPeca.LinhaDaPeca(lista, null));
    }
}
