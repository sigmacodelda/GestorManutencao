using System.Net;
using RepositorioIntervencoesMemoria = GestorManutencao.Api.RepositorioIntervencoesMemoria;
using TecnicoAtual = GestorManutencao.Api.TecnicoAtual;
using GestorManutencao.Armazenamento;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

// Fila que não bloqueia, troca de utilizador a meio, cancelar contra o envio, trincos partilhados, fotos órfãs e tempos limite.
public class OfflineRobustezTests
{
    private const string AvisoSemEnvio = "Não foi possível enviar. O texto que escreveu está guardado aqui.";
    private const string NaoAceitou = "O servidor não aceitou os dados enviados.";

    private static HttpRequestException Http(HttpStatusCode codigo) => new("erro", null, codigo);

    // Fila com um gancho: corre uma vez, logo depois de uma listagem que contenha uma operação do tipo pedido.
    private sealed class FilaGancho(IFilaSaida interna) : IFilaSaida
    {
        public TipoOperacao? Tipo { get; set; }
        public Func<Task>? Gancho { get; set; }

        public Task AdicionarAsync(OperacaoSaida operacao) => interna.AdicionarAsync(operacao);
        public async Task<IReadOnlyList<OperacaoSaida>> ListarAsync(string dono)
        {
            var lista = await interna.ListarAsync(dono);
            if (Gancho is { } g && lista.Any(o => o.Tipo == Tipo))
            {
                Gancho = null;
                await g();
            }
            return lista;
        }
        public Task<IReadOnlyList<OperacaoSaida>> ListarTodasAsync() => interna.ListarTodasAsync();
        public Task AtualizarAsync(OperacaoSaida operacao) => interna.AtualizarAsync(operacao);
        public Task RemoverAsync(string id) => interna.RemoverAsync(id);
        public Task<int> ContarPendentesAsync(string dono) => interna.ContarPendentesAsync(dono);
        public Task RemapearFichaAsync(string dono, string idLocal, string idReal) => interna.RemapearFichaAsync(dono, idLocal, idReal);
        public Task AdicionarAvisoAsync(AvisoConflito aviso) => interna.AdicionarAvisoAsync(aviso);
        public Task<IReadOnlyList<AvisoConflito>> ListarAvisosAsync(string dono) => interna.ListarAvisosAsync(dono);
        public Task RemoverAvisoAsync(string id) => interna.RemoverAvisoAsync(id);
    }

    private sealed class FotosComFalha(IArmazemFotosPendentes interna) : IArmazemFotosPendentes
    {
        public bool FalharLeitura { get; set; }
        public Task GuardarAsync(string fotoId, byte[] bytes) => interna.GuardarAsync(fotoId, bytes);
        public Task<byte[]?> LerAsync(string fotoId) => FalharLeitura ? throw new IOException("disco") : interna.LerAsync(fotoId);
        public Task RemoverAsync(string fotoId) => interna.RemoverAsync(fotoId);
        public Task RemoverExcetoAsync(IReadOnlySet<string> manter) => interna.RemoverExcetoAsync(manter);
    }

    private static async Task<Cenario> ComDuasFichasAsync()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        await c.SemearEmCacheAsync("T2", "Câmara de frio 01", EstadoIntervencao.Planeada);
        c.Rede.Definir(false);
        return c;
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Erro_permanente_numa_ficha_nao_bloqueia_as_outras_e_vira_conflito_na_5a_tentativa(HttpStatusCode codigo)
    {
        var c = await ComDuasFichasAsync();
        await c.Svc.ConcluirAsync("T1", "Reparado", new TimeOnly(8, 0), new TimeOnly(9, 0));
        await c.Svc.IniciarAsync("T2");
        c.Rede.Definir(true);
        c.Remoto.Falha = m => m == "Concluir" ? Http(codigo) : null;

        await c.Sinc.SincronizarAsync();

        Assert.Equal(1, c.Remoto.Contar("Iniciar"));
        var pendente = Assert.Single(await c.Fila.ListarAsync("ana"));
        Assert.Equal(TipoOperacao.Concluir, pendente.Tipo);
        Assert.Equal(1, pendente.Tentativas);
        Assert.Empty(await c.Sinc.ListarAvisosAsync());

        for (var i = 0; i < SincronizadorFila.MaxTentativas - 1; i++) await c.Sinc.SincronizarAsync();

        var aviso = Assert.Single(await c.Sinc.ListarAvisosAsync());
        Assert.Equal(AvisoSemEnvio, aviso.Mensagem);
        Assert.Contains(aviso.Textos, t => t.Texto == "Reparado");
        Assert.Equal(EstadoOperacao.Conflito, (await c.Fila.ListarAsync("ana")).Single().Estado);
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Falha_de_rede_continua_a_parar_sem_contar_tentativas()
    {
        var c = await ComDuasFichasAsync();
        await c.Svc.ConcluirAsync("T1", "Reparado", new TimeOnly(8, 0), new TimeOnly(9, 0));
        await c.Svc.IniciarAsync("T2");
        c.Rede.Definir(true);
        c.Remoto.Falha = m => m == "Concluir" ? new HttpRequestException("corte") : null;

        for (var i = 0; i < 8; i++) await c.Sinc.SincronizarAsync();

        Assert.Equal(0, c.Remoto.Contar("Iniciar"));
        Assert.Empty(await c.Sinc.ListarAvisosAsync());
        Assert.Equal(2, (await c.Fila.ListarAsync("ana")).Count(o => o.Estado != EstadoOperacao.Conflito));
    }

    [Fact]
    public async Task Erro_401_para_a_fila_toda()
    {
        var c = await ComDuasFichasAsync();
        await c.Svc.ConcluirAsync("T1", "Reparado", new TimeOnly(8, 0), new TimeOnly(9, 0));
        await c.Svc.IniciarAsync("T2");
        c.Rede.Definir(true);
        c.Remoto.Falha = m => m == "Concluir" ? Http(HttpStatusCode.Unauthorized) : null;

        await c.Sinc.SincronizarAsync();

        Assert.Equal(0, c.Remoto.Contar("Iniciar"));
        Assert.Equal(2, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Foto_ilegivel_no_telemovel_nao_e_rede_e_nao_bloqueia_as_outras_fichas()
    {
        var fotos = new FotosComFalha(new ArmazemFotosPendentesMemoria());
        var c = new Cenario(new CacheLocalMemoria(), new FilaSaidaMemoria(), fotos);
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        await c.SemearEmCacheAsync("T2", "Câmara de frio 01", EstadoIntervencao.Planeada);
        c.Rede.Definir(false);
        await c.Svc.AdicionarFotoAsync("T1", "a.jpg", [1, 2]);
        await c.Svc.IniciarAsync("T2");
        c.Rede.Definir(true);
        fotos.FalharLeitura = true;

        await c.Sinc.SincronizarAsync();

        Assert.Equal(1, c.Remoto.Contar("Iniciar"));
        Assert.Equal(0, c.Remoto.Contar("AdicionarFoto"));
        var foto = Assert.Single(await c.Fila.ListarAsync("ana"));
        Assert.Equal(1, foto.Tentativas);
    }

    [Fact]
    public async Task Excecao_em_ingles_nunca_chega_ao_aviso()
    {
        var c = await ComDuasFichasAsync();
        await c.Svc.IniciarAsync("T2");
        c.Rede.Definir(true);
        c.Remoto.Falha = m => m == "Iniciar" ? new InvalidOperationException("Sequence contains no elements") : null;

        await c.Sinc.SincronizarAsync();

        var aviso = Assert.Single(await c.Sinc.ListarAvisosAsync());
        Assert.DoesNotContain("Sequence", aviso.Mensagem);
        Assert.Contains(NaoAceitou, aviso.Mensagem);
    }

    [Fact]
    public async Task Troca_de_utilizador_a_meio_do_envio_nao_envia_mais_nada_nem_grava_na_cache_do_novo()
    {
        var c = await ComDuasFichasAsync();
        await c.Svc.IniciarAsync("T2");
        await c.Svc.ConcluirAsync("T1", "Reparado", new TimeOnly(8, 0), new TimeOnly(9, 0));
        c.Rede.Definir(true);
        var antes = await c.Cache.ObterFichaAsync("T2");
        c.Remoto.Falha = m =>
        {
            if (m == "Iniciar") c.Dono.Dono = "rui";
            return null;
        };

        await c.Sinc.SincronizarAsync();

        Assert.Equal(1, c.Remoto.Contar("Iniciar"));
        Assert.Equal(0, c.Remoto.Contar("Concluir"));
        Assert.Single(await c.Fila.ListarAsync("ana"));
        Assert.Equal(antes, await c.Cache.ObterFichaAsync("T2"));
    }

    [Fact]
    public async Task Token_cancelado_para_o_envio()
    {
        var c = await ComDuasFichasAsync();
        await c.Svc.IniciarAsync("T2");
        await c.Svc.ConcluirAsync("T1", "Reparado", new TimeOnly(8, 0), new TimeOnly(9, 0));
        c.Rede.Definir(true);
        using var cts = new CancellationTokenSource();
        c.Remoto.Falha = m => { if (m == "Iniciar") cts.Cancel(); return null; };

        await c.Sinc.SincronizarAsync(cts.Token);

        Assert.Equal(0, c.Remoto.Contar("Concluir"));
        Assert.NotEqual(EstadoSincronizacao.ASincronizar, c.Sinc.Estado);
    }

    [Fact]
    public async Task Troca_de_utilizador_a_meio_da_atualizacao_nao_grava_dados_do_anterior_na_cache()
    {
        var c = new Cenario();
        c.Semear(RemotoFalso.NovaFicha("T1", "Compressor de ar 02", EstadoIntervencao.Planeada, Cenario.Hoje));
        c.Remoto.Falha = m => { if (m == "ListarMinhas") c.Dono.Dono = "rui"; return null; };

        var atualizou = await c.Atualizador.AtualizarCacheAsync();

        Assert.False(atualizou);
        Assert.Empty(await c.Cache.ListarFichasAsync());
        Assert.Null(await c.Cache.LerEquipamentosAsync());
        Assert.Null(await c.Mc.LerAtualizadoEmAsync());
    }

    [Fact]
    public async Task Apagar_material_local_durante_o_envio_cancela_sem_enviar_nem_avisar()
    {
        var c0 = new Cenario();
        var fila = new FilaGancho(c0.Fila) { Tipo = TipoOperacao.AdicionarMaterial };
        var c = new Cenario(c0.Cache, fila, c0.Fotos);
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        var material = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 1, 5m);
        c.Rede.Definir(true);
        fila.Gancho = () => c.Svc.ApagarMaterialAsync("T1", material.Id);

        await c.Sinc.SincronizarAsync();

        Assert.Equal(0, c.Remoto.Contar("AdicionarMaterial"));
        Assert.Empty(await c.Fila.ListarAsync("ana"));
        Assert.Empty(await c.Sinc.ListarAvisosAsync());
        Assert.DoesNotContain(await c.Cache.LerMateriaisAsync("T1") ?? [], m => m.Id == material.Id);
    }

    [Fact]
    public async Task Apagar_foto_local_durante_o_envio_cancela_sem_aviso_mesmo_sem_o_ficheiro()
    {
        var c0 = new Cenario();
        var fila = new FilaGancho(c0.Fila) { Tipo = TipoOperacao.AdicionarFoto };
        var c = new Cenario(c0.Cache, fila, c0.Fotos);
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        var foto = await c.Svc.AdicionarFotoAsync("T1", "a.jpg", [1, 2, 3]);
        c.Rede.Definir(true);
        fila.Gancho = () => c.Svc.ApagarFotoAsync("T1", foto.Id);

        await c.Sinc.SincronizarAsync();

        Assert.Equal(0, c.Remoto.Contar("AdicionarFoto"));
        Assert.Empty(await c.Fila.ListarAsync("ana"));
        Assert.Empty(await c.Sinc.ListarAvisosAsync());
        Assert.Null(await c.Fotos.LerAsync(foto.Id));
    }

    [Fact]
    public async Task Dois_scopes_sobre_a_mesma_fila_nunca_sincronizam_em_paralelo()
    {
        var c1 = await ComDuasFichasAsync();
        await c1.Svc.IniciarAsync("T2");
        var c2 = new Cenario(c1.Cache, c1.Fila, c1.Fotos);
        c2.Rede.Definir(true);
        c1.Rede.Definir(true);
        using var entrou = new ManualResetEventSlim();
        using var libertar = new ManualResetEventSlim();
        c1.Remoto.Falha = m => { if (m == "Iniciar") { entrou.Set(); libertar.Wait(TimeSpan.FromSeconds(5)); } return null; };

        var primeira = Task.Run(() => c1.Sinc.SincronizarAsync());
        Assert.True(entrou.Wait(TimeSpan.FromSeconds(5)));
        await c2.Sinc.SincronizarAsync();

        Assert.Empty(c2.Remoto.Chamadas);
        libertar.Set();
        await primeira;
        Assert.Equal(1, c1.Remoto.Contar("Iniciar"));
    }

    [Fact]
    public async Task Dois_scopes_partilham_o_trinco_da_cache()
    {
        var c1 = new Cenario();
        var c2 = new Cenario(c1.Cache, c1.Fila, c1.Fotos);
        var solto = new TaskCompletionSource();
        var preso = c1.Mc.ComTrincoAsync(() => solto.Task);

        var segundo = c2.Mc.ComTrincoAsync(() => Task.CompletedTask);
        await Task.Delay(150);
        Assert.False(segundo.IsCompleted);

        solto.SetResult();
        await Task.WhenAll(preso, segundo).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Fusao_nao_volta_a_estado_antigo_se_a_operacao_terminou_entretanto()
    {
        var c = new Cenario();
        var antiga = await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.Planeada);
        var marca = c.Mc.Marca;
        c.Mc.FichaEnviada("T1");
        await c.Cache.GuardarFichasAsync([antiga with { Estado = EstadoIntervencao.EmCurso, Pendente = false }]);

        await c.Mc.MesclarMinhasAsync([antiga], "ana", marca);
        await c.Mc.MesclarFichasAsync([antiga], "ana", marca);
        var devolvida = await c.Mc.GuardarFichaServidorAsync(antiga, marca);

        Assert.Equal(EstadoIntervencao.EmCurso, (await c.Cache.ObterFichaAsync("T1"))!.Estado);
        Assert.Equal(EstadoIntervencao.EmCurso, devolvida.Estado);
    }

    [Fact]
    public async Task Fotos_orfas_sao_apagadas_e_as_referenciadas_ficam()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        c.Rede.Definir(false);
        var minha = await c.Svc.AdicionarFotoAsync("T1", "a.jpg", [1]);
        c.Dono.Dono = "rui";
        var carga = System.Text.Json.JsonSerializer.Serialize(new { FotoId = "L-rui", Nome = "x.jpg", Bytes = 1 });
        await c.Fila.AdicionarAsync(new OperacaoSaida("op-rui", "rui", TipoOperacao.AdicionarFoto, "T9", carga, null, c.Tempo.Agora));
        await c.Fotos.GuardarAsync("L-rui", [2]);
        await c.Fotos.GuardarAsync("L-orfa", [3]);

        await c.Sinc.LimparFotosOrfasAsync();

        Assert.NotNull(await c.Fotos.LerAsync(minha.Id));
        Assert.NotNull(await c.Fotos.LerAsync("L-rui"));
        Assert.Null(await c.Fotos.LerAsync("L-orfa"));
    }

    [Fact]
    public async Task Armazem_em_ficheiros_apaga_orfas_incluindo_temporarios()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "gm-fotos-" + Guid.NewGuid().ToString("N"));
        try
        {
            var armazem = new ArmazemFotosPendentesFicheiro(pasta);
            await armazem.GuardarAsync("L-fica", [1]);
            await armazem.GuardarAsync("L-sai", [2]);
            await File.WriteAllBytesAsync(Path.Combine(pasta, "L-resto.jpg.tmp"), [3]);
            await File.WriteAllBytesAsync(Path.Combine(pasta, "L-fica.jpg.tmp"), [4]);

            await armazem.RemoverExcetoAsync(new HashSet<string> { "L-fica" });

            Assert.Equal(["L-fica.jpg", "L-fica.jpg.tmp"], Directory.GetFiles(pasta).Select(f => Path.GetFileName(f)!).Order().ToArray());
        }
        finally { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
    }

    [Fact]
    public async Task Gestor_apaga_fotos_orfas_no_arranque()
    {
        var c = new Cenario();
        var sessao = new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria());
        await sessao.EntrarAsync("795316");
        await c.Fotos.GuardarAsync("L-orfa", [3]);
        c.Rede.Definir(false);
        var gestor = new GestorOffline(sessao, c.Sinc, c.Atualizador, c.Mc, c.Fila, c.Dono, c.Rede, c.Svc);

        gestor.Iniciar();
        for (var i = 0; i < 100 && await c.Fotos.LerAsync("L-orfa") is not null; i++) await Task.Delay(25);

        Assert.Null(await c.Fotos.LerAsync("L-orfa"));
        gestor.Dispose();
    }

    [Fact]
    public async Task Tempo_limite_maior_so_para_pedidos_de_fotos()
    {
        using var lento = new HttpClient(new TempoLimitePorPedidoHandler(new AtrasoHandler(TimeSpan.FromMilliseconds(300)),
            normal: TimeSpan.FromMilliseconds(80), fotos: TimeSpan.FromSeconds(5)))
        { BaseAddress = new Uri("https://x.test/"), Timeout = Timeout.InfiniteTimeSpan };

        using var foto = await lento.PostAsync("intervencoes/T1/fotos?nome=a.jpg", new ByteArrayContent([1]));
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        await Assert.ThrowsAsync<TaskCanceledException>(() => lento.GetAsync("intervencoes"));
    }

    private sealed class AtrasoHandler(TimeSpan atraso) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(atraso, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Numeracao_concorrente_de_avarias_na_API_em_memoria_nao_repete_fichas()
    {
        var repo = new RepositorioIntervencoesMemoria(TimeProvider.System);
        var ana = new TecnicoAtual("ana", 101, "Ana Silva");

        var resultados = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            Task.Run(() => repo.RegistarAvariaAsync("E4", "Avaria " + i, ana, default, "op" + (i % 20).ToString("D8")))));

        var ids = resultados.Select(r => r.Item2!.Id).ToList();
        Assert.Equal(20, ids.Distinct().Count());
    }
}
