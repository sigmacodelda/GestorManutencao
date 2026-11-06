using GestorManutencao.UI.Modelos;
using Microsoft.Extensions.DependencyInjection;

namespace GestorManutencao.UI.Servicos;

public sealed class SessaoService(IAutenticacaoService autenticacao, IArmazemSessao armazem)
{
    private bool _restaurada;

    public Utilizador? Utilizador { get; private set; }
    public ModoTrabalho Modo { get; private set; } = ModoTrabalho.Operador;
    public bool TemSessao => Utilizador is not null;
    public bool PodeSerSupervisor => Utilizador?.Perfil == Perfil.Supervisor;

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

    public async Task<ResultadoLogin> EntrarAsync(string utilizador, string password, CancellationToken ct = default)
    {
        var resultado = await autenticacao.EntrarAsync(utilizador, password, ct);
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
        Modo = PodeSerSupervisor ? ModoTrabalho.Supervisor : ModoTrabalho.Operador;
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
    public static IServiceCollection AddManutencaoUI(this IServiceCollection services, string? tema = null) =>
        services.AddSingleton(GestorManutencao.UI.Temas.Tema.Carregar(tema)).AddScoped<SessaoService>();
}
