using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

// Assinatura da empresa externa sem rede: entra na fila, segue antes da conclusão da mesma ficha e um conflito vira aviso.
public class AssinaturaExternaOfflineTests
{
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static void SemRede(Cenario c) { c.Remoto.Rede = false; c.Rede.Definir(false); }

    private static void ComRede(Cenario c) { c.Remoto.Rede = true; c.Rede.Definir(true); }

    private static async Task<Cenario> CenarioExternaAsync(bool externa = true)
    {
        var c = new Cenario();
        c.Semear(RemotoFalso.NovaFicha("F1", "Compressor de ar 02", EstadoIntervencao.EmCurso, Cenario.Hoje) with { Externa = externa });
        await c.Svc.ListarMinhasAsync();
        return c;
    }

    [Fact]
    public async Task Sem_rede_a_assinatura_entra_na_fila_e_segue_antes_da_conclusao()
    {
        var c = await CenarioExternaAsync();
        SemRede(c);

        var assinada = await c.Svc.AssinarExternaAsync("F1", "  Frio Norte ", Png);
        Assert.True(assinada.Pendente);
        Assert.True(assinada.TemAssinaturaExterna);
        // A cópia local já permite concluir sem rede.
        var concluida = await c.Svc.ConcluirAsync("F1", "Reparação feita", new TimeOnly(8, 0), new TimeOnly(9, 0));
        Assert.Equal(EstadoIntervencao.Concluida, concluida.Estado);
        Assert.Equal(2, await c.Fila.ContarPendentesAsync("ana"));
        Assert.Empty(c.Remoto.Assinaturas);

        ComRede(c);
        await c.Sinc.SincronizarAsync();

        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
        Assert.Equal(("Frio Norte", Png), c.Remoto.Assinaturas["F1"]);
        Assert.Equal(EstadoIntervencao.Concluida, c.Remoto.Fichas["F1"].Estado);
        Assert.Equal(["AssinarExterna:F1", "Concluir:F1"], c.Remoto.Chamadas.Where(x => x.StartsWith("AssinarExterna") || x.StartsWith("Concluir")).ToList());
        Assert.Empty(await c.Fila.ListarAvisosAsync("ana"));
    }

    [Fact]
    public async Task A_carga_da_fila_guarda_o_desenho_inteiro_sem_cortar()
    {
        var c = await CenarioExternaAsync();
        SemRede(c);
        // Perto do máximo aceite pelo servidor (cabeçalho PNG válido seguido de dados longos).
        var grande = Png[..^8] + new string('A', 60_000) + "AAAAAAA=";
        Assert.True(RegrasAssinatura.EImagem(grande));

        await c.Svc.AssinarExternaAsync("F1", "Frio Norte", grande);

        var operacao = Assert.Single(await c.Fila.ListarAsync("ana"));
        Assert.Equal(TipoOperacao.AssinarExterna, operacao.Tipo);
        Assert.Contains(grande, operacao.Payload);
        ComRede(c);
        await c.Sinc.SincronizarAsync();
        Assert.Equal(grande, c.Remoto.Assinaturas["F1"].Assinatura);
    }

    [Fact]
    public async Task Se_a_assinatura_falha_por_rede_a_conclusao_da_mesma_ficha_nao_e_enviada_primeiro()
    {
        var c = await CenarioExternaAsync();
        SemRede(c);
        await c.Svc.AssinarExternaAsync("F1", "Frio Norte", Png);
        await c.Svc.ConcluirAsync("F1", "Reparação feita", new TimeOnly(8, 0), new TimeOnly(9, 0));

        c.Rede.Definir(true);
        c.Remoto.Rede = true;
        c.Remoto.Falha = m => m == "AssinarExterna" ? new HttpRequestException("sem rede") : null;
        await c.Sinc.SincronizarAsync();

        Assert.Equal(0, c.Remoto.Contar("Concluir"));
        Assert.Equal(2, await c.Fila.ContarPendentesAsync("ana"));

        c.Remoto.Falha = null;
        await c.Sinc.SincronizarAsync();
        Assert.Equal(EstadoIntervencao.Concluida, c.Remoto.Fichas["F1"].Estado);
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Resposta_perdida_na_assinatura_repete_sem_conflito()
    {
        var c = await CenarioExternaAsync();
        SemRede(c);
        await c.Svc.AssinarExternaAsync("F1", "Frio Norte", Png);

        ComRede(c);
        // O servidor grava e a resposta perde-se: a repetição idêntica é ok.
        c.Remoto.DepoisFalha["AssinarExterna"] = new HttpRequestException("resposta perdida");
        await c.Sinc.SincronizarAsync();
        Assert.Equal(1, await c.Fila.ContarPendentesAsync("ana"));
        await c.Sinc.SincronizarAsync();

        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
        Assert.Empty(await c.Fila.ListarAvisosAsync("ana"));
        Assert.True(c.Remoto.Fichas["F1"].TemAssinaturaExterna);
    }

    [Fact]
    public async Task Conflito_do_servidor_ja_assinada_vira_aviso_e_a_conclusao_dependente_fica_por_rever()
    {
        var c = await CenarioExternaAsync();
        SemRede(c);
        await c.Svc.AssinarExternaAsync("F1", "Frio Norte", Png);
        await c.Svc.ConcluirAsync("F1", "Reparação feita", new TimeOnly(8, 0), new TimeOnly(9, 0));

        ComRede(c);
        // Entretanto o supervisor registou outra assinatura nessa ficha.
        c.Remoto.Assinaturas["F1"] = ("Outra Empresa", Png.Replace("DUlEQVR42mP8", "DUlEQVR42mP9"));
        c.Remoto.Fichas["F1"] = c.Remoto.Fichas["F1"] with { TemAssinaturaExterna = true };
        await c.Sinc.SincronizarAsync();

        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
        var aviso = Assert.Single(await c.Fila.ListarAvisosAsync("ana"));
        Assert.Equal("Compressor de ar 02", aviso.Equipamento);
        Assert.Contains(MensagensIntervencao.Para("ja_assinada"), aviso.Mensagem);
        Assert.Contains(aviso.Textos, t => t.Texto == "Frio Norte");
        Assert.Contains(aviso.Textos, t => t.Texto == "Reparação feita");
        Assert.Equal(0, c.Remoto.Contar("Concluir"));
        Assert.Equal("Outra Empresa", c.Remoto.Assinaturas["F1"].Nome);
    }

    [Fact]
    public async Task Com_rede_a_assinatura_vai_logo_ao_servidor_sem_fila()
    {
        var c = await CenarioExternaAsync();

        var r = await c.Svc.AssinarExternaAsync("F1", "Frio Norte", Png);

        Assert.False(r.Pendente);
        Assert.True(r.TemAssinaturaExterna);
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
        Assert.Equal(("Frio Norte", Png), c.Remoto.Assinaturas["F1"]);
    }

    [Fact]
    public async Task Concluir_externa_online_sem_assinatura_e_recusado_pelo_servidor()
    {
        var c = await CenarioExternaAsync();

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.ConcluirAsync("F1", "Reparação feita", new TimeOnly(8, 0), new TimeOnly(9, 0)));
        Assert.Equal(MensagensIntervencao.Para("externa_sem_assinatura"), erro.Message);
    }

    [Fact]
    public async Task Assinatura_invalida_ou_ficha_interna_e_recusada_logo_e_nada_entra_na_fila()
    {
        var c = await CenarioExternaAsync();
        SemRede(c);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.AssinarExternaAsync("F1", "A", Png));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.AssinarExternaAsync("F1", "Frio Norte", "texto"));
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));

        var interna = await CenarioExternaAsync(externa: false);
        SemRede(interna);
        await Assert.ThrowsAsync<InvalidOperationException>(() => interna.Svc.AssinarExternaAsync("F1", "Frio Norte", Png));
        Assert.Equal(0, await interna.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public void Mensagens_dos_conflitos_novos_sao_proprias()
    {
        foreach (var codigo in new[] { "externa_sem_assinatura", "nao_externa", "ja_assinada" })
            Assert.NotEqual(MensagensIntervencao.Mudou, MensagensIntervencao.Para(codigo));
    }
}
