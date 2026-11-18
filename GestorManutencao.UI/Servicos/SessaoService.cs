using GestorManutencao.UI.Modelos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GestorManutencao.UI.Servicos;

public sealed class SessaoService(IAutenticacaoService autenticacao, IArmazemSessao armazem)
{
    private bool _restaurada;

    public Utilizador? Utilizador { get; private set; }
    public ModoTrabalho Modo { get; private set; } = ModoTrabalho.Operador;
    public bool TemSessao => Utilizador is not null;
    public bool PodeSerSupervisor => Utilizador?.Perfil == Perfil.Supervisor;
    public bool EQualidade => Utilizador?.Perfil == Perfil.Qualidade;

    // Sem assinatura a pessoa só vê a página da assinatura. Só é verdadeiro depois de o servidor dizer que está vazia: sem rede não bloqueia.
    public bool AssinaturaEmFalta { get; private set; }

    public void DefinirAssinaturaEmFalta(bool emFalta)
    {
        if (AssinaturaEmFalta == emFalta) return;
        AssinaturaEmFalta = emFalta;
        Alterada?.Invoke();
    }

    public event Action? Alterada;

    public async Task RestaurarAsync()
    {
        if (_restaurada) return;
        _restaurada = true;
        Utilizador = await armazem.LerAsync();
        if (Utilizador is { Expirou: true })
        {
            Utilizador = null;
            await armazem.LimparAsync();
        }
        DefinirModoInicial();
        Alterada?.Invoke();
    }

    public Task<ResultadoLogin> EntrarAsync(string pin, CancellationToken ct = default) =>
        Estabelecer(autenticacao.EntrarAsync(pin, ct));

    public Task<ResultadoLogin> MudarPinAsync(string pinAtual, string pinNovo, CancellationToken ct = default) =>
        Estabelecer(autenticacao.MudarPinAsync(Utilizador?.Token, pinAtual, pinNovo, ct));

    private async Task<ResultadoLogin> Estabelecer(Task<ResultadoLogin> operacao)
    {
        var resultado = await operacao;
        if (resultado.Sucesso)
        {
            Utilizador = resultado.Utilizador;
            DefinirModoInicial();
            try { await armazem.GuardarAsync(resultado.Utilizador!); }
            catch (Exception) { /* sem armazenamento seguro a sessão fica só em memória */ }
            Alterada?.Invoke();
        }
        return resultado;
    }

    public async Task SairAsync()
    {
        Utilizador = null;
        AssinaturaEmFalta = false;
        Modo = ModoTrabalho.Operador;
        await armazem.LimparAsync();
        Alterada?.Invoke();
    }

    public void MudarModo(ModoTrabalho modo)
    {
        if (modo == ModoTrabalho.Supervisor && !PodeSerSupervisor) return;
        Modo = modo;
        Alterada?.Invoke();
    }

    // Um supervisor abre logo no modo supervisor; pode passar a operador quando trabalha na linha.
    private void DefinirModoInicial() =>
        Modo = PodeSerSupervisor ? ModoTrabalho.Supervisor : EQualidade ? ModoTrabalho.Qualidade : ModoTrabalho.Operador;
}

public sealed class ArmazemSessaoMemoria : IArmazemSessao
{
    private Utilizador? _utilizador;
    public Task<Utilizador?> LerAsync() => Task.FromResult(_utilizador);
    public Task GuardarAsync(Utilizador utilizador) { _utilizador = utilizador; return Task.CompletedTask; }
    public Task LimparAsync() { _utilizador = null; return Task.CompletedTask; }
}

public static class ServicosExtensions
{
    // Registo comum; o anfitrião acrescenta a autenticação e o armazém da sessão.
    // O aviso do sistema e o armazém das notificações têm versão neutra por defeito; o anfitrião pode registar as suas.
    public static IServiceCollection AddManutencaoUI(this IServiceCollection services, string? tema = null)
    {
        services.AddSingleton(GestorManutencao.UI.Temas.Tema.Carregar(tema)).AddScoped<SessaoService>();
        services.AddSingleton<CicloVidaApp>().AddScoped<NotificacaoService>().AddScoped<ToastService>();
        services.TryAddSingleton<IAvisoSistema, AvisoSistemaNenhum>();
        services.TryAddSingleton<IPushToken, PushTokenNenhum>();
        services.TryAddSingleton<ITempoReal, TempoRealNenhum>();
        services.TryAddSingleton<IArmazemAvisos, ArmazemAvisosMemoria>();
        services.TryAddSingleton<IDitado, DitadoNenhum>();
        services.TryAddSingleton<ICamara, CamaraNenhuma>();
        services.TryAddScoped<Offline.IGestorOffline, Offline.GestorOfflineNenhum>();
        services.TryAddSingleton<Offline.IAreaTransferencia, Offline.AreaTransferenciaNenhuma>();
        return services;
    }
}
