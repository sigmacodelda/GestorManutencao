using System.Net;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class OfflineTests
{
    private static Intervencao Ficha(string id, string equipamento, EstadoIntervencao estado, int diasAtras = 0) =>
        RemotoFalso.NovaFicha(id, equipamento, estado, Cenario.Hoje.AddDays(-diasAtras));

    private static HttpRequestException Http(HttpStatusCode codigo) => new("erro", null, codigo);

    [Fact]
    public async Task Leitura_sem_rede_devolve_cache_e_com_rede_refresca()
    {
        var c = new Cenario();
        c.Semear(Ficha("T1", "Compressor de ar 02", EstadoIntervencao.Planeada));
        Assert.Single(await c.Svc.ListarMinhasAsync());
        Assert.False(c.Svc.AMostrarCache);

        c.Remoto.Rede = false;
        c.Tempo.Agora = c.Tempo.Agora.AddHours(1);
        Assert.Single(await c.Svc.ListarMinhasAsync());
        Assert.True(c.Svc.AMostrarCache);
        Assert.Equal("A mostrar dados de 09/11 10:00", c.Svc.TextoDados);
        Assert.Equal("T1", (await c.Svc.ObterAsync("T1"))!.Id);

        c.Remoto.Rede = true;
        c.Semear(Ficha("T2", "Câmara de frio 01", EstadoIntervencao.Planeada));
        Assert.Equal(2, (await c.Svc.ListarMinhasAsync()).Count);
        Assert.False(c.Svc.AMostrarCache);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Erros_401_e_5xx_nao_viram_offline_nem_apagam_nada(HttpStatusCode codigo)
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        c.Rede.Definir(false);
        await c.Svc.IniciarAsync("T1");
        c.Rede.Definir(true);

        c.Remoto.Falha = _ => Http(codigo);
        await Assert.ThrowsAsync<HttpRequestException>(() => c.Svc.ListarMinhasAsync());
        await c.Sinc.SincronizarAsync();

        Assert.Single(await c.Cache.ListarFichasAsync());
        Assert.Equal(1, await c.Fila.ContarPendentesAsync("ana"));
        Assert.Equal(EstadoSincronizacao.PorEnviar, c.Sinc.Estado);
    }

    [Fact]
    public async Task Escrita_sem_rede_vai_para_a_fila_e_a_ficha_fica_pendente()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        c.Remoto.Rede = false;

        var ficha = await c.Svc.IniciarAsync("T1");

        Assert.True(ficha.Pendente);
        Assert.Equal(EstadoIntervencao.EmCurso, ficha.Estado);
        Assert.Equal(1, await c.Fila.ContarPendentesAsync("ana"));
        Assert.True((await c.Svc.ObterAsync("T1"))!.Pendente);
        Assert.Equal(1, c.Sinc.PendentesCount);
        Assert.Equal(EstadoSincronizacao.PorEnviar, c.Sinc.Estado);

        // Com a ficha por enviar, as novas operações entram na fila sem ir ao servidor (mantém a ordem).
        c.Remoto.Rede = true;
        await c.Svc.ConcluirAsync("T1", "Feito", new TimeOnly(8, 0), new TimeOnly(9, 0));
        Assert.Equal(0, c.Remoto.Contar("Concluir"));
        Assert.Equal(2, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Avaria_e_conclusao_sem_rede_sincronizam_por_ordem_com_remapeamento()
    {
        var c = new Cenario();
        await c.Atualizador.AtualizarCacheAsync();
        c.Rede.Definir(false);

        var avaria = await c.Svc.RegistarAvariaAsync("E1", "Fuga de óleo");
        Assert.StartsWith("L-", avaria.Id);
        Assert.True(avaria.Pendente);
        Assert.Equal(EstadoIntervencao.EmCurso, avaria.Estado);
        var concluida = await c.Svc.ConcluirAsync(avaria.Id, "Vedante trocado", new TimeOnly(8, 0), new TimeOnly(9, 0));
        Assert.Equal(EstadoIntervencao.Concluida, concluida.Estado);
        Assert.Equal(0, c.Remoto.Contar("RegistarAvaria"));

        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();

        var chamadas = c.Remoto.Chamadas.Where(x => x.StartsWith("RegistarAvaria") || x.StartsWith("Concluir")).ToList();
        Assert.Equal(2, chamadas.Count);
        Assert.StartsWith("RegistarAvaria", chamadas[0]);
        Assert.Equal("Concluir:S1", chamadas[1]);
        Assert.Equal(0, c.Sinc.PendentesCount);
        Assert.Equal(EstadoSincronizacao.Sincronizado, c.Sinc.Estado);

        var final = await c.Cache.ObterFichaAsync("S1");
        Assert.NotNull(final);
        Assert.False(final!.Pendente);
        Assert.Equal(EstadoIntervencao.Concluida, final.Estado);
        Assert.Null(await c.Cache.ObterFichaAsync(avaria.Id));
        // A rota da página ainda tem o id local.
        Assert.Equal("S1", (await c.Svc.ObterAsync(avaria.Id))!.Id);
    }

    [Fact]
    public async Task Avaria_usa_sempre_o_mesmo_opId_mesmo_com_timeout_online()
    {
        var c = new Cenario();
        await c.Atualizador.AtualizarCacheAsync();
        // O servidor grava mas a resposta perde-se por timeout.
        c.Remoto.DepoisFalha["RegistarAvaria"] = new TaskCanceledException("timeout");

        var local = await c.Svc.RegistarAvariaAsync("E1", "Fuga de óleo");
        Assert.StartsWith("L-", local.Id);
        await c.Sinc.SincronizarAsync();

        Assert.Equal(2, c.Remoto.OpIds.Count);
        Assert.Equal(c.Remoto.OpIds[0], c.Remoto.OpIds[1]);
        Assert.Single(c.Remoto.Fichas);
        Assert.NotNull(await c.Cache.ObterFichaAsync("S1"));
    }

    [Fact]
    public async Task Conflito_poe_a_operacao_e_as_dependentes_em_conflito_com_aviso_e_ficha_do_servidor()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 1, 5);
        await c.Svc.ConcluirAsync("T1", "Troquei o rolamento", new TimeOnly(8, 0), new TimeOnly(9, 0));
        c.Semear(Ficha("T1", "Compressor de ar 02", EstadoIntervencao.Validada));
        c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        var operacoes = await c.Fila.ListarAsync("ana");
        Assert.All(operacoes, o => Assert.Equal(EstadoOperacao.Conflito, o.Estado));
        var aviso = Assert.Single(await c.Sinc.ListarAvisosAsync());
        Assert.Contains(aviso.Textos, t => t.Texto == "Troquei o rolamento");
        Assert.Contains(aviso.Textos, t => t.Texto == "Rolamento");
        Assert.Contains("já mudou", aviso.Mensagem);
        var ficha = (await c.Cache.ObterFichaAsync("T1"))!;
        Assert.Equal(EstadoIntervencao.Validada, ficha.Estado);
        Assert.False(ficha.Pendente);
        Assert.Equal(0, c.Sinc.PendentesCount);
        Assert.Equal(EstadoSincronizacao.ComConflitos, c.Sinc.Estado);

        await c.Sinc.DescartarConflitoAsync(aviso.Id);
        Assert.Empty(await c.Fila.ListarAsync("ana"));
        Assert.Equal(EstadoSincronizacao.Sincronizado, c.Sinc.Estado);
    }

    [Fact]
    public async Task Conflito_na_criacao_da_avaria_guarda_o_texto_e_remove_a_ficha_local()
    {
        var c = new Cenario();
        await c.Atualizador.AtualizarCacheAsync();
        c.Rede.Definir(false);
        var avaria = await c.Svc.RegistarAvariaAsync("E1", "Ruído estranho");
        c.Rede.Definir(true);
        c.Remoto.Falha = m => m == "RegistarAvaria" ? Http(HttpStatusCode.BadRequest) : null;

        await c.Sinc.SincronizarAsync();

        var aviso = Assert.Single(await c.Sinc.ListarAvisosAsync());
        Assert.Contains(aviso.Textos, t => t.Texto == "Ruído estranho");
        Assert.Null(await c.Cache.ObterFichaAsync(avaria.Id));
    }

    [Fact]
    public async Task Falha_de_rede_a_meio_pára_e_retoma_sem_duplicar()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Câmara de frio 01", EstadoIntervencao.Planeada);
        await c.Atualizador.AtualizarCacheAsync();
        c.Rede.Definir(false);
        var avaria = await c.Svc.RegistarAvariaAsync("E1", "Fuga");
        await c.Svc.ConcluirAsync(avaria.Id, "Reparada", new TimeOnly(8, 0), new TimeOnly(9, 0));
        await c.Svc.IniciarAsync("T1");
        c.Rede.Definir(true);
        var falhas = 0;
        c.Remoto.Falha = m => m == "Concluir" && falhas++ == 0 ? new HttpRequestException("corte") : null;

        await c.Sinc.SincronizarAsync();
        Assert.Equal(2, c.Sinc.PendentesCount);
        Assert.Equal(EstadoSincronizacao.PorEnviar, c.Sinc.Estado);
        Assert.Equal(0, c.Remoto.Contar("Iniciar"));

        await c.Sinc.SincronizarAsync();
        Assert.Equal(0, c.Sinc.PendentesCount);
        Assert.Equal(1, c.Remoto.Contar("RegistarAvaria"));
        Assert.Equal(1, c.Remoto.Contar("Iniciar"));
        Assert.Equal(EstadoIntervencao.Concluida, c.Remoto.Fichas["S1"].Estado);
        Assert.Equal(EstadoIntervencao.EmCurso, c.Remoto.Fichas["T1"].Estado);
    }

    [Fact]
    public async Task Timeout_no_material_deixa_incerta_e_verifica_antes_de_reenviar()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        await c.Svc.ListarMateriaisAsync("T1");
        c.Remoto.DepoisFalha["AdicionarMaterial"] = new TaskCanceledException("timeout");

        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 2, 3);

        Assert.StartsWith("L-", local.Id);
        Assert.Equal(EstadoOperacao.Incerta, Assert.Single(await c.Fila.ListarAsync("ana")).Estado);
        // Uma operação incerta já não se cancela: pode estar no servidor.
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.ApagarMaterialAsync("T1", local.Id));

        await c.Sinc.SincronizarAsync();

        Assert.Equal(1, c.Remoto.Contar("AdicionarMaterial"));
        Assert.Single(c.Remoto.Materiais["T1"]);
        var cache = await c.Cache.LerMateriaisAsync("T1");
        Assert.Equal(c.Remoto.Materiais["T1"][0].Id, Assert.Single(cache!).Id);
        Assert.Equal(0, c.Sinc.PendentesCount);
        Assert.False((await c.Cache.ObterFichaAsync("T1"))!.Pendente);
    }

    [Fact]
    public async Task Apagar_material_local_cancela_o_adicionar()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 1, 5);
        Assert.Equal(1, await c.Fila.ContarPendentesAsync("ana"));

        await c.Svc.ApagarMaterialAsync("T1", local.Id);

        Assert.Empty(await c.Fila.ListarAsync("ana"));
        Assert.Empty(await c.Svc.ListarMateriaisAsync("T1"));
        Assert.False((await c.Cache.ObterFichaAsync("T1"))!.Pendente);
        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();
        Assert.Equal(0, c.Remoto.Contar("AdicionarMaterial"));
        Assert.Equal(0, c.Remoto.Contar("ApagarMaterial"));
    }

    [Fact]
    public async Task Foto_sem_rede_guarda_bytes_fora_da_fila_e_envia_depois()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        var jpeg = new byte[] { 1, 2, 3, 4 };

        var foto = await c.Svc.AdicionarFotoAsync("T1", "a.jpg", jpeg);

        Assert.StartsWith("L-", foto.Id);
        Assert.Equal(jpeg, await c.Svc.ObterFotoAsync("T1", foto.Id));
        Assert.DoesNotContain("1,2,3", (await c.Fila.ListarAsync("ana")).Single().Payload);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.AdicionarFotoAsync("T1", "grande.jpg", new byte[RegrasFoto.MaxBytes + 1]));

        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();

        Assert.Single(c.Remoto.Fotos["T1"]);
        Assert.Null(await c.Fotos.LerAsync(foto.Id));
        var lista = await c.Svc.ListarFotosAsync("T1");
        Assert.Equal(c.Remoto.Fotos["T1"][0].Foto.Id, Assert.Single(lista).Id);
        Assert.Equal(jpeg, await c.Svc.ObterFotoAsync("T1", lista[0].Id));
    }

    [Fact]
    public async Task Limite_de_fotos_respeitado_sem_rede()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        for (var i = 0; i < RegrasFoto.Max; i++) await c.Svc.AdicionarFotoAsync("T1", $"{i}.jpg", [1]);
        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.AdicionarFotoAsync("T1", "x.jpg", [1]));
        Assert.Equal(MensagensIntervencao.Para("limite_fotos"), erro.Message);
    }

    [Fact]
    public async Task Avaria_valida_o_maximo_com_opId()
    {
        var c = new Cenario();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.RegistarAvariaAsync("E1", new string('a', RegrasIntervencao.MaxAnomalia + 1)));
        Assert.Empty(c.Remoto.Chamadas);
    }

    [Fact]
    public async Task Retencao_apaga_so_o_que_passou_de_30_dias_e_nunca_o_que_esta_por_enviar()
    {
        var c = new Cenario();
        await c.Cache.GuardarMetaAsync("dono", "ana");
        var protegidaPorOperacao = Ficha("OP", "X", EstadoIntervencao.Validada, 60);
        await c.Cache.GuardarFichasAsync(
        [
            Ficha("V31", "X", EstadoIntervencao.Validada, 31),
            Ficha("V29", "X", EstadoIntervencao.Concluida, 29),
            Ficha("V30", "X", EstadoIntervencao.Cancelada, 30),
            Ficha("ANT", "X", EstadoIntervencao.Planeada, 90),
            Ficha("CUR", "X", EstadoIntervencao.EmCurso, 90),
            Ficha("PEN", "X", EstadoIntervencao.Validada, 60) with { Pendente = true },
            Ficha("L-abc", "X", EstadoIntervencao.Validada, 60),
            protegidaPorOperacao
        ]);
        await c.Fila.AdicionarAsync(new OperacaoSaida("o1", "ana", TipoOperacao.Iniciar, "OP", "{}", null, c.Tempo.Agora, 0, EstadoOperacao.Conflito));
        await c.Cache.GuardarFichaEquipamentoAsync(new FichaEquipamentoGuardada(new FichaEquipamento(new Equipamento("E9", "Velho", "Z"), [], []), c.Tempo.Agora.AddDays(-31)));
        await c.Cache.GuardarFichaEquipamentoAsync(new FichaEquipamentoGuardada(new FichaEquipamento(new Equipamento("E8", "Recente", "Z"), [], []), c.Tempo.Agora.AddDays(-5)));

        await c.Mc.AplicarRetencaoAsync(Cenario.Hoje);

        var ids = (await c.Cache.ListarFichasAsync()).Select(f => f.Id).OrderBy(x => x).ToList();
        Assert.Equal(["ANT", "CUR", "L-abc", "OP", "PEN", "V29", "V30"], ids);
        Assert.Null(await c.Cache.LerFichaEquipamentoAsync("E9"));
        Assert.NotNull(await c.Cache.LerFichaEquipamentoAsync("E8"));
    }

    [Fact]
    public async Task Atualizar_cache_guarda_as_recentes_do_servidor_mesmo_com_data_antiga()
    {
        var c = new Cenario();
        c.Semear(Ficha("T1", "Compressor de ar 02", EstadoIntervencao.Planeada));
        c.Semear(Ficha("R1", "Compressor de ar 02", EstadoIntervencao.Concluida, 40));

        Assert.True(await c.Atualizador.AtualizarCacheAsync());

        Assert.NotNull(await c.Cache.ObterFichaAsync("R1"));
        Assert.NotNull(await c.Cache.LerFichaEquipamentoAsync("E1"));
        Assert.Null(await c.Cache.LerFichaEquipamentoAsync("E2"));
        Assert.Equal(3, (await c.Cache.LerEquipamentosAsync())!.Count);
    }

    [Fact]
    public async Task Atualizar_cache_sem_rede_falha_em_silencio()
    {
        var c = new Cenario();
        c.Remoto.Rede = false;
        Assert.False(await c.Atualizador.AtualizarCacheAsync());
    }

    [Fact]
    public async Task Troca_de_utilizador_limpa_a_cache_mas_preserva_fila_e_fotos_do_anterior()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        var foto = await c.Svc.AdicionarFotoAsync("T1", "a.jpg", [9, 9]);

        c.Dono.Dono = "rui";
        Assert.Empty(await c.Svc.ListarMinhasAsync());
        Assert.Empty(await c.Cache.ListarFichasAsync());
        Assert.Equal(1, await c.Fila.ContarPendentesAsync("ana"));
        Assert.NotNull(await c.Fotos.LerAsync(foto.Id));
        await c.Sinc.AtualizarContagensAsync();
        Assert.Equal(0, c.Sinc.PendentesCount);

        // O sincronizador do Rui não toca nas operações da Ana.
        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();
        Assert.Equal(0, c.Remoto.Contar("AdicionarFoto"));

        c.Dono.Dono = "ana";
        await c.Sinc.SincronizarAsync();
        Assert.Equal(1, c.Remoto.Contar("AdicionarFoto"));
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Pesquisa_offline_usa_as_regras_de_pesquisa_sem_acentos()
    {
        var c = new Cenario();
        c.Semear(Ficha("T1", "Compressor de ar 02", EstadoIntervencao.Planeada));
        await c.Atualizador.AtualizarCacheAsync();
        c.Rede.Definir(false);

        Assert.Equal("Câmara de frio 01", Assert.Single(await c.Svc.PesquisarAsync("CAMARA")).Nome);
        Assert.Single(await c.Svc.PesquisarAsync("producao"));
        Assert.Empty(await c.Svc.PesquisarAsync("c"));
        Assert.Empty(await c.Svc.PesquisarAsync(new string('a', RegrasPesquisa.MaxTexto + 1)));
        Assert.DoesNotContain(await c.Svc.PesquisarAsync(""), e => e.Obsoleto);

        var todos = await c.Svc.ListarAsync(new FiltroEquipamentos(Obsoletos: true));
        Assert.Equal(3, todos.Count);
        Assert.Equal(2, (await c.Svc.ListarAsync(new FiltroEquipamentos())).Count);
        Assert.Single(await c.Svc.ListarAsync(new FiltroEquipamentos(Local: "frio")));
        Assert.Equal("E1", (await c.Svc.ObterFichaAsync("E1"))!.Equipamento.Id);
        Assert.Null(await c.Svc.ObterFichaAsync("E2"));
    }

    [Fact]
    public async Task Nunca_correm_duas_sincronizacoes_ao_mesmo_tempo()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        c.Rede.Definir(false);
        await c.Svc.IniciarAsync("T1");
        c.Rede.Definir(true);
        c.Remoto.Atraso = TimeSpan.FromMilliseconds(200);

        await Task.WhenAll(c.Sinc.SincronizarAsync(), c.Sinc.SincronizarAsync(), c.Sinc.SincronizarAsync());

        Assert.Equal(1, c.Remoto.MaxConcorrencia);
        Assert.Equal(1, c.Remoto.Contar("Iniciar"));
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Concluir_offline_sem_material_sincroniza_sem_conflito()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        await c.Svc.ConcluirAsync("T1", "Limpeza", new TimeOnly(8, 0), new TimeOnly(9, 0));
        c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        Assert.Empty(await c.Sinc.ListarAvisosAsync());
        Assert.Equal(EstadoIntervencao.Concluida, c.Remoto.Fichas["T1"].Estado);
    }

    [Fact]
    public async Task Sem_utilizador_passa_direto_ao_servidor_sem_cache()
    {
        var c = new Cenario();
        c.Dono.Dono = null;
        c.Semear(Ficha("T1", "Compressor de ar 02", EstadoIntervencao.Planeada));

        Assert.Single(await c.Svc.ListarMinhasAsync());
        Assert.Empty(await c.Cache.ListarFichasAsync());
    }
}
