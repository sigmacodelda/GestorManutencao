using GestorManutencao.Armazenamento;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using SQLite;

namespace GestorManutencao.Tests;

// Contrato das implementações SQLite e em ficheiros: mesmo comportamento das versões em memória, com ficheiros temporários.
public sealed class ArmazenamentoSqliteTests : IAsyncLifetime
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "gm-teste-" + Guid.NewGuid().ToString("N"));
    private BaseLocalSqlite _bd = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_pasta);
        _bd = new BaseLocalSqlite(Path.Combine(_pasta, "teste.db3"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _bd.FecharAsync();
        SQLiteAsyncConnection.ResetPool();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private static Intervencao Ficha(string id, EstadoIntervencao estado = EstadoIntervencao.Planeada, bool pendente = false) =>
        new(id, "Compressor de ar 02", "Produção", TipoIntervencao.Curativa, new DateOnly(2026, 11, 9), estado, "Ruído estranho: \"ção\"", null,
            new TimeOnly(8, 30), null, "ana", 3, Pendente: pendente);

    private static OperacaoSaida Operacao(string id, string ficha, TipoOperacao tipo = TipoOperacao.Iniciar, string dono = "ana", EstadoOperacao estado = EstadoOperacao.Pendente) =>
        new(id, dono, tipo, ficha, "{}", "op1", new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero), 0, estado);

    [Fact]
    public async Task Cache_meta_fichas_materiais_e_fotos_fazem_ida_e_volta()
    {
        var c = new CacheLocalSqlite(_bd);
        Assert.Null(await c.LerMetaAsync("dono"));
        await c.GuardarMetaAsync("dono", "ana");
        Assert.Equal("ana", await c.LerMetaAsync("dono"));
        await c.GuardarMetaAsync("dono", null);
        Assert.Null(await c.LerMetaAsync("dono"));

        await c.GuardarFichasAsync([Ficha("A"), Ficha("B", EstadoIntervencao.EmCurso, true)]);
        await c.GuardarFichasAsync([Ficha("A", EstadoIntervencao.EmCurso)]);
        Assert.Equal(2, (await c.ListarFichasAsync()).Count);
        var a = (await c.ObterFichaAsync("A"))!;
        Assert.Equal(EstadoIntervencao.EmCurso, a.Estado);
        Assert.Equal("Ruído estranho: \"ção\"", a.Anomalia);
        Assert.Equal(new TimeOnly(8, 30), a.HoraInicio);
        Assert.True((await c.ObterFichaAsync("B"))!.Pendente);
        Assert.Null(await c.ObterFichaAsync("X"));

        Assert.Null(await c.LerMateriaisAsync("A"));
        await c.GuardarMateriaisAsync("A", [new Material("M1", "Óleo", 2.5m, 3.2m, 8m)]);
        Assert.Equal(2.5m, (await c.LerMateriaisAsync("A"))![0].Qtt);
        await c.GuardarFotosAsync("A", [new Foto("F1", "a.jpg", 3)]);
        await c.GuardarFotoBytesAsync("A", "F1", [1, 2, 3]);
        Assert.Equal([1, 2, 3], await c.LerFotoBytesAsync("A", "F1"));
        Assert.Null(await c.LerFotoBytesAsync("B", "F1"));

        await c.RemoverFichasAsync(["A"]);
        Assert.Null(await c.ObterFichaAsync("A"));
        Assert.Null(await c.LerMateriaisAsync("A"));
        Assert.Null(await c.LerFotosAsync("A"));
        Assert.Null(await c.LerFotoBytesAsync("A", "F1"));
        Assert.NotNull(await c.ObterFichaAsync("B"));
    }

    [Fact]
    public async Task Cache_remover_fotos_apaga_lista_e_bytes_e_limpar_apaga_tudo()
    {
        var c = new CacheLocalSqlite(_bd);
        await c.GuardarFichasAsync([Ficha("A")]);
        await c.GuardarFotosAsync("A", [new Foto("F1", "a.jpg", 3)]);
        await c.GuardarFotoBytesAsync("A", "F1", [9]);
        await c.RemoverFotosAsync("A");
        Assert.Null(await c.LerFotosAsync("A"));
        Assert.Null(await c.LerFotoBytesAsync("A", "F1"));
        Assert.NotNull(await c.ObterFichaAsync("A"));

        await c.GuardarMetaAsync("dono", "ana");
        await c.GuardarEquipamentosLeveAsync([new Equipamento("E1", "X", "Z")]);
        await c.LimparAsync();
        Assert.Empty(await c.ListarFichasAsync());
        Assert.Null(await c.LerMetaAsync("dono"));
        Assert.Null(await c.LerEquipamentosLeveAsync());
    }

    [Fact]
    public async Task Cache_equipamentos_e_fichas_de_equipamento_com_retencao()
    {
        var c = new CacheLocalSqlite(_bd);
        Assert.Null(await c.LerEquipamentosAsync());
        await c.GuardarEquipamentosAsync([new Equipamento("E1", "Compressor", "Produção", Obsoleto: true, UltimaIntervencao: new DateOnly(2026, 1, 2))]);
        var lista = (await c.LerEquipamentosAsync())!;
        Assert.True(lista[0].Obsoleto);
        Assert.Equal(new DateOnly(2026, 1, 2), lista[0].UltimaIntervencao);

        var agora = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
        FichaEquipamentoGuardada Guardada(string id, DateTimeOffset aberta) =>
            new(new FichaEquipamento(new Equipamento(id, "N" + id, "Z"), [new TarefaPlaneada("T", "Verificar", "Mensal", null, null)], [Ficha("H" + id)]), aberta);
        await c.GuardarFichaEquipamentoAsync(Guardada("E1", agora.AddDays(-40)));
        await c.GuardarFichaEquipamentoAsync(Guardada("E2", agora));
        await c.RemoverFichasEquipamentoAbertasAntesDeAsync(agora.AddDays(-30));
        Assert.Null(await c.LerFichaEquipamentoAsync("E1"));
        var e2 = (await c.LerFichaEquipamentoAsync("E2"))!;
        Assert.Equal(agora, e2.Aberta);
        Assert.Single(e2.Ficha.Historico);
    }

    [Fact]
    public async Task Fila_mantem_ordem_estado_dono_e_remapeia()
    {
        var f = new FilaSaidaSqlite(_bd);
        await f.AdicionarAsync(Operacao("1", "L-x", TipoOperacao.RegistarAvaria));
        await f.AdicionarAsync(Operacao("2", "L-x", TipoOperacao.Concluir));
        await f.AdicionarAsync(Operacao("3", "T9", dono: "rui"));
        await f.AdicionarAsync(Operacao("4", "T8", estado: EstadoOperacao.Conflito));

        Assert.Equal(["1", "2", "4"], (await f.ListarAsync("ana")).Select(o => o.Id));
        Assert.Equal(2, await f.ContarPendentesAsync("ana"));
        Assert.Equal(1, await f.ContarPendentesAsync("rui"));

        await f.AtualizarAsync(Operacao("1", "L-x", TipoOperacao.RegistarAvaria, estado: EstadoOperacao.Incerta) with { Tentativas = 2, UltimoErro = "x" });
        var primeira = (await f.ListarAsync("ana"))[0];
        Assert.Equal(EstadoOperacao.Incerta, primeira.Estado);
        Assert.Equal(2, primeira.Tentativas);
        Assert.Equal("op1", primeira.OpId);

        await f.RemapearFichaAsync("ana", "L-x", "S1");
        Assert.All((await f.ListarAsync("ana")).Take(2), o => Assert.Equal("S1", o.FichaId));
        Assert.Equal("T9", (await f.ListarAsync("rui"))[0].FichaId);

        await f.RemoverAsync("2");
        Assert.Equal(["1", "4"], (await f.ListarAsync("ana")).Select(o => o.Id));
    }

    [Fact]
    public async Task Avisos_guardam_o_texto_do_tecnico_por_dono_e_sobrevivem_a_limpar_a_cache()
    {
        var cache = new CacheLocalSqlite(_bd);
        var f = new FilaSaidaSqlite(_bd);
        var aviso = new AvisoConflito("a1", "ana", "T1", "Compressor", "Não foi possível enviar.",
            [new TextoDoTecnico("O que foi feito", "Troquei o filtro, ação \"ok\".")], new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));
        await f.AdicionarAvisoAsync(aviso);
        await f.AdicionarAvisoAsync(aviso with { Id = "a2", Dono = "rui" });
        await f.AdicionarAsync(Operacao("1", "T1"));
        await cache.LimparAsync();

        var avisos = await f.ListarAvisosAsync("ana");
        Assert.Single(avisos);
        Assert.Equal("Troquei o filtro, ação \"ok\".", avisos[0].Textos[0].Texto);
        Assert.Equal("Compressor", avisos[0].Equipamento);
        Assert.Single(await f.ListarAsync("ana"));
        await f.RemoverAvisoAsync("a1");
        Assert.Empty(await f.ListarAvisosAsync("ana"));
        Assert.Single(await f.ListarAvisosAsync("rui"));
    }

    [Fact]
    public async Task Dados_sobrevivem_a_reabrir_a_base()
    {
        var caminho = Path.Combine(_pasta, "reabrir.db3");
        var primeira = new BaseLocalSqlite(caminho);
        await new FilaSaidaSqlite(primeira).AdicionarAsync(Operacao("1", "T1"));
        await new CacheLocalSqlite(primeira).GuardarFichasAsync([Ficha("T1")]);
        await primeira.FecharAsync();

        var segunda = new BaseLocalSqlite(caminho);
        Assert.Single(await new FilaSaidaSqlite(segunda).ListarAsync("ana"));
        Assert.NotNull(await new CacheLocalSqlite(segunda).ObterFichaAsync("T1"));
        await segunda.FecharAsync();
    }

    [Fact]
    public async Task Fotos_pendentes_em_ficheiro_guardam_leem_e_removem_e_recusam_ids_estranhos()
    {
        var a = new ArmazemFotosPendentesFicheiro(Path.Combine(_pasta, "fotos"));
        Assert.Null(await a.LerAsync("L-abc"));
        await a.GuardarAsync("L-abc", [1, 2, 3]);
        await a.GuardarAsync("L-abc", [4, 5]);
        Assert.Equal([4, 5], await a.LerAsync("L-abc"));
        await a.RemoverAsync("L-abc");
        Assert.Null(await a.LerAsync("L-abc"));
        await a.RemoverAsync("L-abc");

        await Assert.ThrowsAsync<ArgumentException>(() => a.GuardarAsync("..\\fora", [1]));
        await Assert.ThrowsAsync<ArgumentException>(() => a.LerAsync("a/b"));
    }

    [Fact]
    public async Task Ciclo_completo_offline_com_SQLite_avaria_concluir_e_sincronizar()
    {
        var c = new Cenario(new CacheLocalSqlite(_bd), new FilaSaidaSqlite(_bd), new ArmazemFotosPendentesFicheiro(Path.Combine(_pasta, "fotos")));
        await c.SemearEmCacheAsync("T1", "Câmara de frio 01", EstadoIntervencao.Planeada);
        await c.Svc.PesquisarAsync("");
        c.Remoto.Rede = false;

        var avaria = await c.Svc.RegistarAvariaAsync("E1", "Compressor a fazer ruído");
        Assert.True(avaria.Pendente);
        await c.Svc.AdicionarMaterialAsync(avaria.Id, "Óleo", 1, 5);
        await c.Svc.AdicionarFotoAsync(avaria.Id, "a.jpg", [1, 2, 3]);
        await c.Svc.ConcluirAsync(avaria.Id, "Reapertado", new TimeOnly(8, 0), new TimeOnly(9, 0));
        await c.Svc.IniciarAsync("T1");
        Assert.Equal(5, c.Sinc.PendentesCount);

        c.Remoto.Rede = true;
        await c.Sinc.SincronizarAsync();

        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
        Assert.Equal(EstadoSincronizacao.Sincronizado, c.Sinc.Estado);
        var real = c.Remoto.Fichas.Values.Single(f => f.Anomalia == "Compressor a fazer ruído");
        Assert.Equal(EstadoIntervencao.Concluida, real.Estado);
        Assert.Single(c.Remoto.Materiais[real.Id]);
        Assert.Single(c.Remoto.Fotos[real.Id]);
        Assert.Equal(real.Id, await c.Mc.ResolverIdAsync(avaria.Id));
        Assert.False((await c.Cache.ObterFichaAsync(real.Id))!.Pendente);
    }

    [Fact]
    public async Task Conflito_com_SQLite_guarda_aviso_com_o_texto_e_descartar_limpa()
    {
        var c = new Cenario(new CacheLocalSqlite(_bd), new FilaSaidaSqlite(_bd), new ArmazemFotosPendentesFicheiro(Path.Combine(_pasta, "fotos")));
        await c.SemearEmCacheAsync("T1", "Câmara de frio 01", EstadoIntervencao.EmCurso);
        c.Remoto.Rede = false;
        await c.Svc.ConcluirAsync("T1", "Substituí a válvula", new TimeOnly(8, 0), new TimeOnly(9, 0));
        c.Remoto.Rede = true;
        c.Semear(c.Remoto.Fichas["T1"] with { Estado = EstadoIntervencao.Planeada });

        await c.Sinc.SincronizarAsync();

        Assert.Equal(EstadoSincronizacao.ComConflitos, c.Sinc.Estado);
        var aviso = Assert.Single(await c.Sinc.ListarAvisosAsync());
        Assert.Equal("Substituí a válvula", aviso.Textos.Single().Texto);
        await c.Sinc.DescartarConflitoAsync(aviso.Id);
        Assert.Equal(EstadoSincronizacao.Sincronizado, c.Sinc.Estado);
        Assert.Empty(await c.Fila.ListarAsync("ana"));
    }
}
