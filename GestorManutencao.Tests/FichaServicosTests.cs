using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class RegrasFichaDetalheTests
{
    [Fact]
    public void Cancelamento_exige_motivo_ate_240()
    {
        Assert.NotNull(RegrasFichaDetalhe.ValidarCancelamento(null));
        Assert.NotNull(RegrasFichaDetalhe.ValidarCancelamento("  "));
        Assert.Null(RegrasFichaDetalhe.ValidarCancelamento(new string('a', 240)));
        Assert.NotNull(RegrasFichaDetalhe.ValidarCancelamento(new string('a', 241)));
    }

    [Fact]
    public void Tempo_exige_tecnico_e_horas_e_o_custo_e_horas_vezes_custo_hora()
    {
        Assert.Null(RegrasFichaDetalhe.ValidarTempo(101, 3.5m, 4.47m));
        Assert.NotNull(RegrasFichaDetalhe.ValidarTempo(0, 1, 1));
        Assert.NotNull(RegrasFichaDetalhe.ValidarTempo(101, 0, 1));
        Assert.NotNull(RegrasFichaDetalhe.ValidarTempo(101, 1000m, 1));
        Assert.NotNull(RegrasFichaDetalhe.ValidarTempo(101, 1, -1));
        Assert.Equal(15.645m, RegrasFichaDetalhe.CustoTempo(3.5m, 4.47m));
    }

    [Fact]
    public void Nova_ficha_valida_equipamento_tipo_de_manutencao_e_textos()
    {
        Assert.Null(RegrasFichaDetalhe.ValidarNova(new NovaFicha("E1")));
        Assert.NotNull(RegrasFichaDetalhe.ValidarNova(new NovaFicha("")));
        Assert.NotNull(RegrasFichaDetalhe.ValidarNova(new NovaFicha("E1", TipoManut: "Mista")));
        Assert.NotNull(RegrasFichaDetalhe.ValidarNova(new NovaFicha("E1", Periodicidade: new string('a', 21))));
        Assert.NotNull(RegrasFichaDetalhe.ValidarNova(new NovaFicha("E1", Problema: new string('a', RegrasIntervencao.MaxAnomaliaComOpId + 1))));
    }

    [Fact]
    public void Edicao_valida_limites_e_horas()
    {
        Assert.Null(RegrasFichaDetalhe.ValidarEdicao(new EdicaoFicha()));
        Assert.Null(RegrasFichaDetalhe.ValidarEdicao(new EdicaoFicha(HorasConf: 999.99m, Problema: new string('a', 200), Resolucao: new string('b', 254))));
        Assert.NotNull(RegrasFichaDetalhe.ValidarEdicao(new EdicaoFicha(HorasConf: 1000m)));
        Assert.NotNull(RegrasFichaDetalhe.ValidarEdicao(new EdicaoFicha(Problema: new string('a', 201))));
        Assert.NotNull(RegrasFichaDetalhe.ValidarEdicao(new EdicaoFicha(MatExterno: -1m)));
        var hoje = DateTime.Today;
        Assert.NotNull(RegrasFichaDetalhe.ValidarEdicao(new EdicaoFicha(HoraInicio: hoje.AddHours(9), HoraFim: hoje.AddHours(8))));
        Assert.Null(RegrasFichaDetalhe.ValidarEdicao(new EdicaoFicha(HoraInicio: hoje.AddHours(9), HoraFim: new DateTime(1900, 1, 1))));
    }

    [Fact]
    public void Detalhe_sem_listas_conta_como_vazio_e_o_total_soma_mao_de_obra_e_pecas()
    {
        var d = new FichaDetalhe("F1", "Compressor", EstadoIntervencao.EmCurso, new DateOnly(2026, 11, 9), TotMo: 20m, TotRef: 3m);

        Assert.Empty(d.Tempos);
        Assert.Empty(d.ListaMateriais);
        Assert.Equal(23m, d.Total);
    }

    [Fact]
    public void Mensagens_dos_codigos_novos_do_servidor()
    {
        Assert.Equal("Não tem permissão para esta ação.", MensagensIntervencao.Para("sem_permissao"));
        Assert.Contains("válido", MensagensIntervencao.Para("pedido_invalido"));
        Assert.Contains("periodicidade", MensagensIntervencao.Para("periodicidade_invalida"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MensagensIntervencao.Mudou, MensagensIntervencao.Para("conflito"));
    }
}

// Ficha completa e ações novas no serviço offline: só online, com mensagem clara, e nunca vão para a fila de saída.
public class OfflineFichaTests
{
    private static async Task<InvalidOperationException> Falha(Func<Task> acao) => await Assert.ThrowsAsync<InvalidOperationException>(acao);

    [Fact]
    public async Task Sem_rede_as_acoes_novas_falham_com_mensagem_clara_e_nao_entram_na_fila()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        var chamadasAntes = c.Remoto.Chamadas.Count;
        c.Rede.Definir(false);

        var erros = new[]
        {
            await Falha(() => c.Svc.CancelarAsync("T1", "x")),
            await Falha(() => c.Svc.GuardarFichaAsync("T1", new EdicaoFicha(Obs: "x"))),
            await Falha(() => c.Svc.CriarFichaManualAsync(new NovaFicha("E1"))),
            await Falha(() => c.Svc.AdicionarMaoDeObraAsync("T1", 101, 1, 1)),
            await Falha(() => c.Svc.EditarMaterialAsync("T1", "M1", "Cabo", 1, 1)),
            await Falha(() => c.Svc.FecharFichaAntigaAsync("T1", true, true)),
            await Falha(() => c.Svc.ObterDetalheAsync("T1")),
            await Falha(() => c.Svc.ObterContadoresAsync()),
            await Falha(() => c.Svc.ListarMaoDeObraAsync("T1")),
            await Falha(() => c.Svc.ListarComprasAsync("T1")),
            await Falha(() => c.Svc.PesquisarFornecedoresAsync("Frio")),
            await Falha(() => c.Svc.CustoHoraSugeridoAsync(101))
        };

        Assert.All(erros, e => Assert.Equal(MensagensIntervencao.SemRede, e.Message));
        Assert.Equal(chamadasAntes, c.Remoto.Chamadas.Count);
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Criacao_com_falha_a_meio_avisa_que_pode_ter_ficado_gravado()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Remoto.Rede = false;

        Assert.Equal(MensagensIntervencao.SemRedeAMeio, (await Falha(() => c.Svc.CriarFichaManualAsync(new NovaFicha("E1")))).Message);
        Assert.Equal(MensagensIntervencao.SemRedeAMeio, (await Falha(() => c.Svc.AdicionarMaoDeObraAsync("T1", 101, 1, 1))).Message);
        Assert.Equal(MensagensIntervencao.SemRede, (await Falha(() => c.Svc.CancelarAsync("T1", "x"))).Message);
    }

    [Fact]
    public async Task Ficha_manual_leva_um_opId_valido_gerado_pelo_servico()
    {
        var c = new Cenario();

        await c.Svc.CriarFichaManualAsync(new NovaFicha("E1"));

        var opId = Assert.Single(c.Remoto.OpIds);
        Assert.InRange(opId.Length, 8, 32);
        Assert.All(opId, ch => Assert.True(char.IsAsciiLetterOrDigit(ch)));
    }

    [Fact]
    public async Task Cancelar_e_guardar_online_atualizam_a_cache_local()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        await c.SemearEmCacheAsync("T2", "Câmara de frio 01", EstadoIntervencao.EmCurso);

        await c.Svc.CancelarAsync("T1", "Parado");
        await c.Svc.GuardarFichaAsync("T2", new EdicaoFicha(Problema: "Fuga nova"));

        Assert.Equal(EstadoIntervencao.Cancelada, (await c.Cache.ObterFichaAsync("T1"))!.Estado);
        Assert.Equal("Fuga nova", (await c.Cache.ObterFichaAsync("T2"))!.Anomalia);
    }

    [Fact]
    public async Task Contadores_ficam_30_segundos_em_cache_e_uma_escrita_online_limpa_os()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);

        await c.Svc.ObterContadoresAsync();
        await c.Svc.ObterContadoresAsync();
        Assert.Equal(1, c.Remoto.Contar("Contadores"));

        c.Tempo.Agora = c.Tempo.Agora.AddSeconds(31);
        await c.Svc.ObterContadoresAsync();
        Assert.Equal(2, c.Remoto.Contar("Contadores"));

        await c.Svc.CancelarAsync("T1", "x");
        await c.Svc.ObterContadoresAsync();
        Assert.Equal(3, c.Remoto.Contar("Contadores"));
    }

    [Fact]
    public async Task Validar_limpa_os_contadores_em_cache()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Concluida);

        await c.Svc.ObterContadoresAsync();
        await c.Svc.ValidarAsync("T1");
        await c.Svc.ObterContadoresAsync();

        Assert.Equal(2, c.Remoto.Contar("Contadores"));
    }

    [Fact]
    public async Task Iniciar_online_tambem_limpa_os_contadores()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);

        await c.Svc.ObterContadoresAsync();
        await c.Svc.IniciarAsync("T1");
        await c.Svc.ObterContadoresAsync();

        Assert.Equal(2, c.Remoto.Contar("Contadores"));
    }

    [Fact]
    public async Task Id_local_passa_ao_id_real_depois_de_enviado()
    {
        var c = new Cenario();
        await c.Atualizador.AtualizarCacheAsync();
        c.Rede.Definir(false);
        var avaria = await c.Svc.RegistarAvariaAsync("E1", "Fuga");
        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();

        await c.Svc.ObterDetalheAsync(avaria.Id);

        Assert.Contains("ObterDetalhe:S1", c.Remoto.Chamadas);
    }

    [Fact]
    public async Task Com_rede_vai_ao_servidor_sem_passar_pela_fila_e_avisa_que_houve_escrita()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        var escritas = 0;
        c.Svc.EscritaOnline += () => escritas++;

        var cancelada = await c.Svc.CancelarAsync("T1", "Parado");
        var manual = await c.Svc.CriarFichaManualAsync(new NovaFicha("E1", Problema: "Fuga"));
        var tempo = await c.Svc.AdicionarMaoDeObraAsync(manual.Id, 101, 2, 10m);
        var contadores = await c.Svc.ObterContadoresAsync();

        Assert.Equal(EstadoIntervencao.Cancelada, cancelada.Estado);
        Assert.Equal(EstadoIntervencao.EmCurso, manual.Estado);
        Assert.Equal(20m, tempo.Horas * tempo.CustoHora);
        Assert.Equal(new ContadoresMenu(2, 1), contadores);
        Assert.Equal(1, c.Remoto.Contar("Cancelar"));
        Assert.Equal(1, c.Remoto.Contar("CriarFichaManual"));
        Assert.Equal(3, escritas);
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Rede_que_falha_a_meio_da_acao_da_a_mesma_mensagem_sem_gravar_na_fila()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Remoto.Rede = false;

        var erro = await Falha(() => c.Svc.GuardarFichaAsync("T1", new EdicaoFicha(Obs: "x")));

        Assert.Equal(MensagensIntervencao.SemRede, erro.Message);
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Recusa_do_servidor_passa_com_a_mensagem_dele_e_erro_5xx_nao_vira_sem_rede()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);

        c.Remoto.Falha = m => m == "Cancelar" ? new InvalidOperationException("Não tem permissão para esta ação.") : null;
        Assert.Equal("Não tem permissão para esta ação.", (await Falha(() => c.Svc.CancelarAsync("T1", "x"))).Message);

        c.Remoto.Falha = _ => new HttpRequestException("erro", null, System.Net.HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => c.Svc.GuardarFichaAsync("T1", new EdicaoFicha(Obs: "x")));
    }

    [Fact]
    public async Task Ficha_com_operacoes_por_enviar_ou_so_local_nao_vai_ao_servidor()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        c.Remoto.Rede = false;
        await c.Svc.IniciarAsync("T1");
        c.Remoto.Rede = true;

        var pendente = await Falha(() => c.Svc.GuardarFichaAsync("T1", new EdicaoFicha(Obs: "x")));
        Assert.Equal(MensagensIntervencao.ComPendentes, pendente.Message);
        Assert.Equal(0, c.Remoto.Contar("GuardarFicha"));

        await c.Atualizador.AtualizarCacheAsync();
        c.Rede.Definir(false);
        var local = await c.Svc.RegistarAvariaAsync("E1", "Fuga");
        c.Rede.Definir(true);
        var soLocal = await Falha(() => c.Svc.CancelarAsync(local.Id, "x"));
        Assert.Equal(MensagensIntervencao.ComPendentes, soLocal.Message);
        Assert.Equal(0, c.Remoto.Contar("Cancelar"));
    }
}

public class DevFichaServicoTests
{
    [Fact]
    public async Task Cancelar_passa_a_cancelada_e_recusa_as_ja_fechadas()
    {
        var s = new DevIntervencoesService();

        var c = await s.CancelarAsync("T1", "Equipamento parado");

        Assert.Equal(EstadoIntervencao.Cancelada, c.Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.CancelarAsync("T1", "outra vez"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.CancelarAsync("H1", "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.CancelarAsync("T3", ""));
    }

    [Fact]
    public async Task Ficha_manual_nasce_em_curso_e_o_detalhe_traz_os_tipos()
    {
        var s = new DevIntervencoesService();

        var f = await s.CriarFichaManualAsync(new NovaFicha("E1", TipoIntervencao.Curativa, "Externa", "Mensal", "Fuga"));

        Assert.Equal(EstadoIntervencao.EmCurso, f.Estado);
        var d = (await s.ObterDetalheAsync(f.Id))!;
        Assert.Equal("Externa", d.TipoManut);
        Assert.Equal("Mensal", d.Periodicidade);
        Assert.Equal("Fuga", d.Problema);
        Assert.True(d.PodeEditar);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.CriarFichaManualAsync(new NovaFicha("E1", TipoManut: "Mista")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.CriarFichaManualAsync(new NovaFicha("NAOEXISTE")));
    }

    [Fact]
    public async Task Guardar_ficha_atualiza_os_campos_e_recusa_fichas_finais()
    {
        var s = new DevIntervencoesService();

        var d = await s.GuardarFichaAsync("T2", new EdicaoFicha(HorasConf: 1.5m, Resolucao: "Trocado o travão", Obs: "ok", DocExterno: "123"));

        Assert.Equal(1.5m, d.HorasConf);
        Assert.Equal("Trocado o travão", d.Resolucao);
        Assert.Equal("ok", d.Obs);
        Assert.Equal("123", d.DocExterno);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GuardarFichaAsync("H1", new EdicaoFicha(Obs: "x")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.GuardarFichaAsync("T2", new EdicaoFicha(HorasConf: 5000m)));
    }

    [Fact]
    public async Task Mao_de_obra_calcula_o_custo_e_entra_nos_totais()
    {
        var s = new DevIntervencoesService();

        var t = await s.AdicionarMaoDeObraAsync("T2", 101, 3.5m, 4.47m);

        Assert.Equal(15.645m, t.Custo);
        Assert.Equal(4.47m, await s.CustoHoraSugeridoAsync(101));
        Assert.Equal(0m, await s.CustoHoraSugeridoAsync(102));
        Assert.Equal(15.645m, (await s.ObterDetalheAsync("T2"))!.TotMo);

        var editado = await s.EditarMaoDeObraAsync("T2", t.Id, 102, 2, 10m);
        Assert.Equal(20m, editado.Custo);
        Assert.Equal(20m, Assert.Single(await s.ListarMaoDeObraAsync("T2")).Custo);

        await s.ApagarMaoDeObraAsync("T2", t.Id);
        Assert.Empty(await s.ListarMaoDeObraAsync("T2"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.AdicionarMaoDeObraAsync("T2", 101, 0, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.AdicionarMaoDeObraAsync("H1", 101, 1, 1));
    }

    [Fact]
    public async Task Editar_material_recalcula_o_custo()
    {
        var s = new DevIntervencoesService();
        var m = await s.AdicionarMaterialAsync("T2", "Cabo", 2, 1.5m);

        var editado = await s.EditarMaterialAsync("T2", m.Id, "Cabo novo", 3, 2.5m);

        Assert.Equal(7.5m, editado.Custo);
        Assert.Equal(7.5m, (await s.ObterDetalheAsync("T2"))!.MatInterno);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.EditarMaterialAsync("T2", "NAOEXISTE", "Cabo", 1, 1));
    }

    [Fact]
    public async Task Contadores_seguem_a_regra_do_kpi_das_planeadas()
    {
        var s = new DevIntervencoesService();

        var c = await s.ObterContadoresAsync();

        // T1 (planeada atrasada) e T7 (em curso, de ontem); T5 é a única por validar.
        Assert.Equal(2, c.PlaneadasAtrasadas);
        Assert.Equal(1, c.PorValidar);
    }
}
