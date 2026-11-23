using System.Net;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

// Supervisor (validar, devolver) e Qualidade (concluir) trabalham sem rede: a decisão vai para a fila e segue quando a rede volta.
public class OfflineDecisoesTests
{
    private static Intervencao Ficha(string id, string equipamento, EstadoIntervencao estado) =>
        RemotoFalso.NovaFicha(id, equipamento, estado, Cenario.Hoje) with { Tecnico = "Ana Silva" };

    private static AtualizadorCache Atualizador(Cenario c, Perfil perfil) => new(c.Remoto, c.Mc, c.Rede, c.Tempo, () => perfil);

    private static void SemRede(Cenario c) { c.Remoto.Rede = false; c.Rede.Definir(false); }

    private static void ComRede(Cenario c) { c.Remoto.Rede = true; c.Rede.Definir(true); }

    [Fact]
    public async Task Supervisor_valida_sem_rede_e_a_decisao_segue_quando_a_rede_volta()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = Ficha("F1", "Compressor de ar 02", EstadoIntervencao.Concluida);
        c.Remoto.Fichas["F2"] = Ficha("F2", "Câmara de frio 01", EstadoIntervencao.Concluida);
        Assert.Equal(2, (await c.Svc.ListarPorValidarAsync()).Count);

        SemRede(c);
        Assert.Equal(2, (await c.Svc.ListarPorValidarAsync()).Count);
        Assert.True(c.Svc.AMostrarCache);

        var validada = await c.Svc.ValidarAsync("F1");
        Assert.True(validada.Pendente);
        Assert.Equal(EstadoIntervencao.Validada, validada.Estado);
        Assert.Equal("F2", Assert.Single(await c.Svc.ListarPorValidarAsync()).Id);
        Assert.Equal(1, await c.Fila.ContarPendentesAsync("ana"));
        Assert.Equal(EstadoIntervencao.Concluida, c.Remoto.Fichas["F1"].Estado);

        ComRede(c);
        await c.Sinc.SincronizarAsync();
        Assert.Equal(EstadoIntervencao.Validada, c.Remoto.Fichas["F1"].Estado);
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Supervisor_devolve_sem_rede_com_o_motivo()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = Ficha("F1", "Compressor de ar 02", EstadoIntervencao.Concluida);
        await c.Svc.ListarPorValidarAsync();

        SemRede(c);
        await c.Svc.DevolverAsync("F1", "  Falta apertar a correia ");
        Assert.Empty(await c.Svc.ListarPorValidarAsync());

        ComRede(c);
        await c.Sinc.SincronizarAsync();
        var f = c.Remoto.Fichas["F1"];
        Assert.Equal(EstadoIntervencao.EmCurso, f.Estado);
        Assert.Equal("Falta apertar a correia", f.Motivo);
    }

    [Fact]
    public async Task Qualidade_conclui_sem_rede_com_o_nome_de_quem_higienizou()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = Ficha("F1", "Compressor de ar 02", EstadoIntervencao.Validada);
        Assert.Single(await c.Svc.ListarPorConcluirAsync());

        SemRede(c);
        Assert.Single(await c.Svc.ListarPorConcluirAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.ConcluirQualidadeAsync("F1", "  "));
        Assert.True((await c.Svc.ConcluirQualidadeAsync("F1", " Limpezas Silva ")).Pendente);
        Assert.Empty(await c.Svc.ListarPorConcluirAsync());

        ComRede(c);
        await c.Sinc.SincronizarAsync();
        Assert.Equal("Limpezas Silva", c.Remoto.Higienizacoes["F1"]);
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Decisao_recusada_pelo_servidor_vira_aviso_com_o_texto_escrito()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = Ficha("F1", "Compressor de ar 02", EstadoIntervencao.Validada);
        await c.Svc.ListarPorConcluirAsync();
        SemRede(c);
        await c.Svc.ConcluirQualidadeAsync("F1", "Limpezas Silva");

        ComRede(c);
        c.Remoto.Higienizacoes["F1"] = "Outra pessoa"; // alguém concluiu entretanto
        await c.Sinc.SincronizarAsync();

        Assert.Equal("Outra pessoa", c.Remoto.Higienizacoes["F1"]);
        var aviso = Assert.Single(await c.Fila.ListarAvisosAsync("ana"));
        Assert.Equal("Compressor de ar 02", aviso.Equipamento);
        Assert.Contains(aviso.Textos, t => t.Texto == "Limpezas Silva");
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Com_rede_a_decisao_vai_logo_ao_servidor_sem_fila()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = Ficha("F1", "Compressor de ar 02", EstadoIntervencao.Concluida);
        await c.Svc.ListarPorValidarAsync();

        var r = await c.Svc.ValidarAsync("F1");

        Assert.False(r.Pendente);
        Assert.Equal(EstadoIntervencao.Validada, c.Remoto.Fichas["F1"].Estado);
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
    }

    [Fact]
    public async Task Atualizador_guarda_a_lista_certa_por_perfil_sem_pedir_fichas_de_tecnico()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = Ficha("F1", "Compressor de ar 02", EstadoIntervencao.Concluida);
        c.Remoto.Fichas["F2"] = Ficha("F2", "Câmara de frio 01", EstadoIntervencao.Validada);
        // A Qualidade não tem fichas suas: o servidor recusaria ListarMinhas.
        c.Remoto.Falha = m => m == "ListarMinhas" ? new HttpRequestException("403", null, HttpStatusCode.Forbidden) : null;

        Assert.True(await Atualizador(c, Perfil.Qualidade).AtualizarCacheAsync());
        SemRede(c);

        Assert.Equal("F2", Assert.Single(await c.Svc.ListarPorConcluirAsync()).Id);
    }

    [Fact]
    public async Task Atualizador_do_supervisor_guarda_a_lista_por_validar()
    {
        var c = new Cenario();
        c.Remoto.Fichas["F1"] = Ficha("F1", "Compressor de ar 02", EstadoIntervencao.Concluida);

        Assert.True(await Atualizador(c, Perfil.Supervisor).AtualizarCacheAsync());
        SemRede(c);

        Assert.Equal("F1", Assert.Single(await c.Svc.ListarPorValidarAsync()).Id);
    }
}
