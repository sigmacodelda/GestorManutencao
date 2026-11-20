using GestorManutencao.Api;
using Xunit;

namespace GestorManutencao.Tests;

public class PecasConferenciaTests
{
    private static RepositorioPecasMemoria Repo() => new([new PecaDto("P001", "Parafuso M8", "560001", "A-01", 10, 4, 0.12m)]);
    private static async Task<PecaDto> PecaAsync(RepositorioPecasMemoria r) => (await r.ObterPorCodigoAsync("P001", default))!;

    [Fact]
    public async Task Abrir_conferencia_e_idempotente()
    {
        var r = Repo();
        var a = await r.AbrirConferenciaAsync("F1", "AA", default);
        var b = await r.AbrirConferenciaAsync("F1", "AA", default);
        Assert.True(a.Ok);
        Assert.Equal(a.Id, b.Id);
    }

    [Fact]
    public async Task Saida_reserva_sem_descer_o_stock_real()
    {
        var r = Repo();
        Assert.True((await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default)).Ok);
        var p = await PecaAsync(r);
        Assert.Equal(10, p.Stock);
        Assert.Equal(4, p.EmConferencia);
        Assert.Equal(6, p.Disponivel);
        var mat = (await r.EnriquecerMateriaisAsync([new MaterialDto("M1", "x", 4, 0, 0)], default))[0];
        Assert.True(mat.SaidaDada);
    }

    [Fact]
    public async Task Anular_saida_devolve_o_disponivel()
    {
        var r = Repo();
        await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default);
        Assert.True((await r.AnularSaidaAsync("M1", "AA", default)).Ok);
        var p = await PecaAsync(r);
        Assert.Equal(10, p.Disponivel);
        Assert.Equal(0, p.EmConferencia);
    }

    [Fact]
    public async Task Confirmar_desce_o_stock_real_e_zera_a_conferencia()
    {
        var r = Repo();
        await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default);
        Assert.True((await r.ConfirmarConferenciaAsync("F1", "AA", default)).Ok);
        var p = await PecaAsync(r);
        Assert.Equal(6, p.Stock);
        Assert.Equal(0, p.EmConferencia);
        Assert.Equal(6, p.Disponivel);
    }

    [Fact]
    public async Task Confirmar_duas_vezes_nao_desce_duas()
    {
        var r = Repo();
        await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default);
        await r.ConfirmarConferenciaAsync("F1", "AA", default);
        Assert.True((await r.ConfirmarConferenciaAsync("F1", "AA", default)).Ok);
        Assert.Equal(6, (await PecaAsync(r)).Stock);
    }

    [Fact]
    public async Task Depois_de_validada_nao_aceita_saidas_nem_anulacoes_nem_cancelamento()
    {
        var r = Repo();
        await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default);
        await r.ConfirmarConferenciaAsync("F1", "AA", default);
        Assert.Equal(RegrasPecas.ErroValidada, (await r.DarSaidaAsync("F1", "M2", "P001", 1, "AA", default)).Erro);
        Assert.Equal(RegrasPecas.ErroValidada, (await r.AnularSaidaAsync("M1", "AA", default)).Erro);
        Assert.False((await r.CancelarConferenciaAsync("F1", "AA", default)).Ok);
        Assert.Equal(6, (await PecaAsync(r)).Stock);
    }

    [Fact]
    public async Task Cancelar_apaga_as_linhas_e_devolve_o_disponivel()
    {
        var r = Repo();
        await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default);
        Assert.True((await r.CancelarConferenciaAsync("F1", "AA", default)).Ok);
        var p = await PecaAsync(r);
        Assert.Equal(10, p.Stock);
        Assert.Equal(10, p.Disponivel);
        var mat = (await r.EnriquecerMateriaisAsync([new MaterialDto("M1", "x", 4, 0, 0)], default))[0];
        Assert.False(mat.SaidaDada);
        // Voltar a abrir depois de cancelar cria um dossier novo e aceita saídas.
        Assert.True((await r.DarSaidaAsync("F1", "M1", "P001", 2, "AA", default)).Ok);
    }

    [Fact]
    public async Task Em_conferencia_por_peca_exclui_a_propria_ficha()
    {
        var r = Repo();
        await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default);
        await r.DarSaidaAsync("F2", "M2", "P001", 3, "AA", default);
        var todas = await r.EmConferenciaPorPecaAsync(["P001"], null, default);
        var semF1 = await r.EmConferenciaPorPecaAsync(["P001"], "F1", default);
        Assert.Equal(7, todas["P001"]);
        Assert.Equal(3, semF1["P001"]);
    }

    [Fact]
    public async Task Confirmar_uma_ficha_nao_toca_na_outra()
    {
        var r = Repo();
        await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default);
        await r.DarSaidaAsync("F2", "M2", "P001", 3, "AA", default);
        await r.ConfirmarConferenciaAsync("F1", "AA", default);
        var p = await PecaAsync(r);
        Assert.Equal(6, p.Stock);
        Assert.Equal(3, p.EmConferencia);
        Assert.True((await r.DarSaidaAsync("F2", "M3", "P001", 1, "AA", default)).Ok);
        Assert.True((await r.CancelarConferenciaAsync("F2", "AA", default)).Ok);
    }

    [Fact]
    public async Task Em_conferencia_por_peca_depois_de_confirmar_e_de_cancelar()
    {
        var r = Repo();
        await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default);
        await r.DarSaidaAsync("F2", "M2", "P001", 3, "AA", default);
        await r.ConfirmarConferenciaAsync("F1", "AA", default);
        Assert.Equal(3, (await r.EmConferenciaPorPecaAsync(["P001"], null, default))["P001"]);
        await r.CancelarConferenciaAsync("F2", "AA", default);
        Assert.Equal(0, (await r.EmConferenciaPorPecaAsync(["P001"], null, default))["P001"]);
    }

    [Fact]
    public async Task Cancelar_sem_dossier_e_sucesso()
    {
        var r = Repo();
        Assert.True((await r.CancelarConferenciaAsync("F9", "AA", default)).Ok);
    }

    [Fact]
    public async Task Dar_saida_do_mesmo_material_duas_vezes_nao_duplica_a_linha()
    {
        var r = Repo();
        Assert.True((await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default)).Ok);
        Assert.False((await r.DarSaidaAsync("F1", "M1", "P001", 4, "AA", default)).Ok);
        Assert.Equal(4, (await PecaAsync(r)).EmConferencia);
    }

    [Fact]
    public void Aviso_de_stock_usa_o_disponivel()
    {
        Assert.Equal("Stock insuficiente: há 6 e pede 8.", RegrasPecas.AvisoStock(8, 6));
        Assert.Null(RegrasPecas.AvisoStock(5, 6));
    }
}
