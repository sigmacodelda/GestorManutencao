using GestorManutencao.Armazenamento;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;
using SQLite;

namespace GestorManutencao.Tests;

public class TecnicosAdicionaisOfflineTests
{
    private static Intervencao ComAdicional(string id) =>
        RemotoFalso.NovaFicha(id, "Compressor de ar 02", EstadoIntervencao.Planeada, Cenario.Hoje) with
        {
            Tecno = 999, Tecnico = "Outra Pessoa", Adicionais = [new TecnicoFicha(101, "Ana Silva")]
        };

    [Fact]
    public async Task Lista_offline_do_tecnico_inclui_as_fichas_onde_e_adicional()
    {
        var c = new Cenario();
        c.Semear(ComAdicional("T1"));

        var online = await c.Svc.ListarMinhasAsync();
        Assert.Equal("T1", Assert.Single(online).Id);

        c.Remoto.Rede = false;
        c.Tempo.Agora = c.Tempo.Agora.AddHours(1);
        var offline = await c.Svc.ListarMinhasAsync();

        Assert.True(c.Svc.AMostrarCache);
        var ficha = Assert.Single(offline);
        Assert.Equal("Ana Silva", Assert.Single(ficha.OutrosTecnicos).Nome);
        Assert.True(ficha.Envolve(101));
        Assert.False(ficha.Envolve(103));
        Assert.Equal("T1", (await c.Svc.ObterAsync("T1"))!.Id);
    }

    [Fact]
    public async Task Cache_sqlite_guarda_e_devolve_os_adicionais()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "gm-teste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        var bd = new BaseLocalSqlite(Path.Combine(pasta, "teste.db3"));
        try
        {
            var cache = new CacheLocalSqlite(bd);
            await cache.GuardarFichasAsync([ComAdicional("T1")]);

            var lida = (await cache.ObterFichaAsync("T1"))!;

            Assert.Equal([(101, "Ana Silva")], lida.OutrosTecnicos.Select(a => (a.Tecno, a.Nome)));
        }
        finally
        {
            await bd.FecharAsync();
            SQLiteAsyncConnection.ResetPool();
            try { Directory.Delete(pasta, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Ficha_antiga_sem_adicionais_continua_valida()
    {
        var ficha = RemotoFalso.NovaFicha("T1", "Compressor de ar 02", EstadoIntervencao.Planeada, Cenario.Hoje) with { Tecno = 101 };

        Assert.Empty(ficha.OutrosTecnicos);
        Assert.True(ficha.Envolve(101));
        Assert.False(ficha.Envolve(0));
    }

    [Fact]
    public void Quem_e_adicionado_recebe_o_aviso_de_atribuicao_como_o_principal()
    {
        var agora = new DateTime(2026, 11, 9, 7, 0, 0);
        var ficha = ComAdicional("T1");

        var doAdicional = CentroAvisos.Gerar(Perfil.Operador, 101, [ficha], agora);
        var doPrincipal = CentroAvisos.Gerar(Perfil.Operador, 999, [ficha], agora);
        var deOutro = CentroAvisos.Gerar(Perfil.Operador, 103, [ficha], agora);

        Assert.Contains(doAdicional, a => a.Tipo == TipoAviso.Atribuida && a.FichaId == "T1");
        Assert.Contains(doPrincipal, a => a.Tipo == TipoAviso.Atribuida && a.FichaId == "T1");
        Assert.DoesNotContain(deOutro, a => a.FichaId == "T1");
    }

    [Fact]
    public async Task Servico_de_desenvolvimento_define_adicionais_sem_incluir_o_principal()
    {
        var s = new DevIntervencoesService();
        await s.AtribuirAsync("T3", 101);

        var ficha = await s.DefinirAdicionaisAsync("T3", [101, 102, 102]);

        Assert.Equal([102], ficha.OutrosTecnicos.Select(a => a.Tecno));
        Assert.Equal(1, (await s.ListarTodasAsync(new FiltroFichas(Tecno: 102), 1)).Itens.Count(i => i.Id == "T3"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.DefinirAdicionaisAsync("T5", [102]));
        Assert.Empty((await s.AtribuirAsync("T3", 102)).OutrosTecnicos);
    }
}
