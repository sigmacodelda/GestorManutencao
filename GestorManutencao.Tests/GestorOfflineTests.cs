using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

// Gatilhos do modo offline: arranque, rede que volta, escrita online e troca de utilizador. Sem sessão não há pedidos.
public class GestorOfflineTests
{
    private static async Task<(Cenario, SessaoService, GestorOffline)> CriarAsync(bool comSessao = true)
    {
        var c = new Cenario();
        var sessao = new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria());
        if (comSessao) await sessao.EntrarAsync("795316");
        var gestor = new GestorOffline(sessao, c.Sinc, c.Atualizador, c.Mc, c.Fila, c.Dono, c.Rede, c.Svc);
        return (c, sessao, gestor);
    }

    private static async Task EsperarAsync(Func<bool> condicao)
    {
        for (var i = 0; i < 200 && !condicao(); i++) await Task.Delay(25);
        Assert.True(condicao(), "A condição não se verificou a tempo.");
    }

    private static async Task<Cenario> ComPendenteAsync(Cenario c)
    {
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        c.Rede.Definir(false);
        c.Remoto.Rede = false;
        await c.Svc.IniciarAsync("T1");
        c.Remoto.Rede = true;
        c.Remoto.Chamadas.Clear();
        return c;
    }

    [Fact]
    public async Task Arranque_com_sessao_envia_a_fila_e_atualiza_a_cache()
    {
        var (c, _, gestor) = await CriarAsync();
        await ComPendenteAsync(c);
        c.Rede.Definir(true);

        gestor.Iniciar();

        await EsperarAsync(() => c.Remoto.Contar("ListarRecentes") > 0);
        Assert.Equal(1, c.Remoto.Contar("Iniciar"));
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
        gestor.Dispose();
    }

    [Fact]
    public async Task Sem_sessao_nao_faz_pedidos()
    {
        var (c, _, gestor) = await CriarAsync(comSessao: false);
        await ComPendenteAsync(c);
        c.Rede.Definir(true);

        gestor.Iniciar();
        await Task.Delay(300);

        Assert.Empty(c.Remoto.Chamadas);
        gestor.Dispose();
    }

    [Fact]
    public async Task Rede_que_volta_envia_primeiro_e_depois_atualiza_a_cache()
    {
        var (c, _, gestor) = await CriarAsync();
        await ComPendenteAsync(c);
        gestor.Iniciar();
        await Task.Delay(200);
        Assert.Equal(0, c.Remoto.Contar("Iniciar"));

        c.Rede.Definir(true);

        await EsperarAsync(() => c.Remoto.Contar("ListarRecentes") > 0);
        var ordem = c.Remoto.Chamadas;
        Assert.True(ordem.IndexOf("Iniciar:T1") < ordem.IndexOf("ListarMinhas"));
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("ana"));
        gestor.Dispose();
    }

    [Fact]
    public async Task Escrita_online_aceite_envia_o_que_ficou_na_fila()
    {
        var (c, _, gestor) = await CriarAsync();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Semear(RemotoFalso.NovaFicha("T2", "Câmara de frio 01", EstadoIntervencao.Planeada, Cenario.Hoje));
        c.Rede.Definir(false);
        await c.Svc.ConcluirAsync("T1", "Feito", new TimeOnly(8, 0), new TimeOnly(9, 0));
        c.Rede.Definir(true);
        await c.Svc.ListarMinhasAsync();

        // O servidor recusa o envio com 500: a operação fica na fila.
        c.Remoto.Falha = m => m == "Concluir" ? new HttpRequestException("erro", null, System.Net.HttpStatusCode.InternalServerError) : null;
        gestor.Iniciar();
        await EsperarAsync(() => c.Remoto.Contar("Concluir") >= 1);
        await EsperarAsync(() => c.Sinc.Estado != EstadoSincronizacao.ASincronizar);
        Assert.Equal(1, await c.Fila.ContarPendentesAsync("ana"));

        c.Remoto.Falha = null;
        await c.Svc.IniciarAsync("T2");

        await EsperarAsync(() => c.Remoto.Fichas["T1"].Estado == EstadoIntervencao.Concluida);
        await EsperarAsync(() => c.Sinc.PendentesCount == 0);
        gestor.Dispose();
    }

    [Fact]
    public async Task Troca_de_utilizador_limpa_a_cache_do_anterior_e_mantem_a_fila()
    {
        var (c, sessao, gestor) = await CriarAsync();
        await ComPendenteAsync(c);
        gestor.Iniciar();
        await Task.Delay(200);
        Assert.NotEmpty(await c.Cache.ListarFichasAsync());

        c.Rede.Definir(false);
        c.Dono.Dono = "rui";
        sessao.MudarModo(ModoTrabalho.Operador);

        await EsperarAsync(() => c.Sinc.PendentesCount == 0);
        Assert.Empty(await c.Cache.ListarFichasAsync());
        Assert.Equal(1, await c.Fila.ContarPendentesAsync("ana"));
        Assert.Equal(0, await c.Fila.ContarPendentesAsync("rui"));
        gestor.Dispose();
    }

    [Fact]
    public async Task Contar_pendentes_le_a_fila_do_utilizador_atual()
    {
        var (c, _, gestor) = await CriarAsync();
        await ComPendenteAsync(c);

        Assert.Equal(1, await gestor.ContarPendentesAsync());
        c.Dono.Dono = "rui";
        Assert.Equal(0, await gestor.ContarPendentesAsync());
        gestor.Dispose();
    }
}
