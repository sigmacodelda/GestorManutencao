using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;
using Microsoft.Extensions.DependencyInjection;

namespace GestorManutencao.Tests;

public class PushAppTests
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    private sealed class TokenFalso(string? token) : IPushToken
    {
        public Task<string?> ObterAsync(CancellationToken ct = default) => Task.FromResult(token);
    }

    private sealed class DispositivosFalsos : IDispositivosRemoto
    {
        public List<(string Jwt, string Token)> Registos { get; } = [];
        public List<(string Jwt, string Token)> Desregistos { get; } = [];
        public bool Falha { get; set; }
        public TaskCompletionSource<bool> Registado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Desregistado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RegistarAsync(string jwt, string token, CancellationToken ct = default)
        {
            if (Falha) throw new HttpRequestException();
            Registos.Add((jwt, token));
            Registado.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task DesregistarAsync(string jwt, string token, CancellationToken ct = default)
        {
            Desregistos.Add((jwt, token));
            Desregistado.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private static async Task<SessaoService> SessaoComTokenAsync(string pin = "4829", IArmazemSessao? armazem = null)
    {
        armazem ??= new ArmazemSessaoMemoria();
        var sessao = new SessaoService(new DevAutenticacaoService(), armazem);
        await sessao.EntrarAsync(pin);
        await armazem.GuardarAsync(sessao.Utilizador! with { Token = "jwt-teste" }); // sigma-ok: valor fictício de teste
        var comToken = new SessaoService(new DevAutenticacaoService(), armazem);
        await comToken.RestaurarAsync();
        return comToken;
    }

    private static NotificacaoService Criar(SessaoService sessao, IPushToken? push, IDispositivosRemoto? dispositivos) =>
        new(sessao, new DevIntervencoesService(), new ArmazemAvisosMemoria(), new AvisoSistemaNenhum(), new CicloVidaApp(), () => DateTime.Today.AddHours(10), push: push, dispositivos: dispositivos);

    [Fact]
    public async Task Iniciar_regista_o_token_com_o_jwt_da_sessao()
    {
        var dispositivos = new DispositivosFalsos();
        var sessao = await SessaoComTokenAsync();
        using var servico = Criar(sessao, new TokenFalso("fcm-1"), dispositivos);

        servico.Iniciar();
        await dispositivos.Registado.Task.WaitAsync(Espera);

        Assert.Equal([("jwt-teste", "fcm-1")], dispositivos.Registos);
        servico.Parar();
    }

    [Fact]
    public async Task Terminar_a_sessao_desregista_o_token_com_o_jwt_antigo()
    {
        var dispositivos = new DispositivosFalsos();
        var sessao = await SessaoComTokenAsync();
        using var servico = Criar(sessao, new TokenFalso("fcm-1"), dispositivos);
        servico.Iniciar();
        await dispositivos.Registado.Task.WaitAsync(Espera);

        servico.Parar();
        await dispositivos.Desregistado.Task.WaitAsync(Espera);

        Assert.Equal([("jwt-teste", "fcm-1")], dispositivos.Desregistos);
    }

    [Fact]
    public async Task Sem_token_ou_com_falha_na_api_o_servico_continua_e_nao_regista()
    {
        var semToken = new DispositivosFalsos();
        var sessao = await SessaoComTokenAsync();
        using (var servico = Criar(sessao, new TokenFalso(null), semToken))
        {
            servico.Iniciar();
            await Task.Delay(300);
            servico.Parar();
        }
        Assert.Empty(semToken.Registos);
        Assert.Empty(semToken.Desregistos);

        var comFalha = new DispositivosFalsos { Falha = true };
        using var outro = Criar(await SessaoComTokenAsync(), new TokenFalso("fcm-2"), comFalha);
        outro.Iniciar();
        await Task.Delay(300);
        outro.Parar();
        Assert.Empty(comFalha.Desregistos);
    }

    [Fact]
    public async Task Sem_implementacao_de_push_nao_faz_nada()
    {
        var sessao = await SessaoComTokenAsync();
        using var servico = Criar(sessao, null, null);
        servico.Iniciar();
        servico.Parar();
        using var nulo = Criar(sessao, new PushTokenNenhum(), new DispositivosFalsos());
        nulo.Iniciar();
        await Task.Delay(200);
        nulo.Parar();
    }

    private sealed class SistemaFalso : IAvisoSistema
    {
        public List<Aviso> Mostrados { get; } = [];
        public Task PedirPermissaoAsync() => Task.CompletedTask;
        public Task<bool> MostrarAsync(Aviso aviso) { Mostrados.Add(aviso); return Task.FromResult(true); }
    }

    private static async Task<(ServiceProvider Servicos, DevIntervencoesService Fichas, SistemaFalso Sistema)> ServicosAsync()
    {
        var armazem = new ArmazemSessaoMemoria();
        await SessaoComTokenAsync("4829", armazem);
        var fichas = new DevIntervencoesService();
        var sistema = new SistemaFalso();
        var servicos = new ServiceCollection()
            .AddManutencaoUI()
            .AddSingleton<IAutenticacaoService, DevAutenticacaoService>()
            .AddSingleton<IArmazemSessao>(armazem)
            .AddSingleton<IIntervencoesService>(fichas)
            .AddSingleton<IAvisoSistema>(sistema)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return (servicos, fichas, sistema);
    }

    [Fact]
    public async Task Push_sem_sessao_em_memoria_restaura_a_sessao_e_avisa_so_do_que_e_novo()
    {
        var (servicos, fichas, sistema) = await ServicosAsync();
        using var _ = servicos;

        await PushFichas.VerificarAsync(servicos, CancellationToken.None);
        Assert.Empty(sistema.Mostrados);

        await fichas.AtribuirAsync("T3", 101);
        await PushFichas.VerificarAsync(servicos, CancellationToken.None);
        Assert.NotEmpty(sistema.Mostrados);
        var total = sistema.Mostrados.Count;

        await PushFichas.VerificarAsync(servicos, CancellationToken.None);
        Assert.Equal(total, sistema.Mostrados.Count);
    }

    [Fact]
    public async Task Push_sem_sessao_guardada_nao_faz_nada_nem_rebenta()
    {
        var sistema = new SistemaFalso();
        using var servicos = new ServiceCollection()
            .AddManutencaoUI()
            .AddSingleton<IAutenticacaoService, DevAutenticacaoService>()
            .AddSingleton<IArmazemSessao, ArmazemSessaoMemoria>()
            .AddSingleton<IIntervencoesService, IntervencoesIndisponiveis>()
            .AddSingleton<IAvisoSistema>(sistema)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await PushFichas.VerificarAsync(servicos, CancellationToken.None);

        Assert.Empty(sistema.Mostrados);
    }

    [Fact]
    public async Task Push_com_a_app_viva_usa_o_servico_da_sessao_em_vez_de_abrir_outra()
    {
        var (servicos, _, _) = await ServicosAsync();
        using var _ = servicos;
        var chamado = false;
        servicos.GetRequiredService<CicloVidaApp>().Verificador = _ => { chamado = true; return Task.CompletedTask; };

        await PushFichas.VerificarAsync(servicos, CancellationToken.None);

        Assert.True(chamado);
    }

    [Fact]
    public async Task Novo_token_regista_se_houver_sessao()
    {
        var armazem = new ArmazemSessaoMemoria();
        await SessaoComTokenAsync("4829", armazem);
        var dispositivos = new DispositivosFalsos();
        using var servicos = new ServiceCollection()
            .AddManutencaoUI()
            .AddSingleton<IAutenticacaoService, DevAutenticacaoService>()
            .AddSingleton<IArmazemSessao>(armazem)
            .AddSingleton<IDispositivosRemoto>(dispositivos)
            .BuildServiceProvider();

        await PushFichas.RegistarTokenAsync(servicos, "fcm-novo", CancellationToken.None);
        Assert.Equal([("jwt-teste", "fcm-novo")], dispositivos.Registos);

        await armazem.LimparAsync();
        await PushFichas.RegistarTokenAsync(servicos, "fcm-outro", CancellationToken.None);
        Assert.Single(dispositivos.Registos);
    }
}
