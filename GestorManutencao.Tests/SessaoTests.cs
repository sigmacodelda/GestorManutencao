using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class SessaoTests
{
    private static SessaoService Criar(IAutenticacaoService? auth = null, IArmazemSessao? armazem = null) =>
        new(auth ?? new DevAutenticacaoService(), armazem ?? new ArmazemSessaoMemoria());

    [Fact]
    public async Task Operador_entra_em_modo_operador_e_nao_pode_ser_supervisor()
    {
        var sessao = Criar();
        var r = await sessao.EntrarAsync("operador", "teste-operador");

        Assert.True(r.Sucesso);
        Assert.Equal(ModoTrabalho.Operador, sessao.Modo);
        sessao.MudarModo(ModoTrabalho.Supervisor);
        Assert.Equal(ModoTrabalho.Operador, sessao.Modo);
    }

    [Fact]
    public async Task Supervisor_abre_em_modo_supervisor_e_pode_passar_a_operador()
    {
        var sessao = Criar();
        await sessao.EntrarAsync("Supervisor", "teste-supervisor");

        Assert.Equal(ModoTrabalho.Supervisor, sessao.Modo);
        sessao.MudarModo(ModoTrabalho.Operador);
        Assert.Equal(ModoTrabalho.Operador, sessao.Modo);
    }

    [Theory]
    [InlineData("operador", "errada")]
    [InlineData("inexistente", "teste-operador")]
    public async Task Credenciais_erradas_falham_sem_criar_sessao(string utilizador, string password)
    {
        var sessao = Criar();
        var r = await sessao.EntrarAsync(utilizador, password);

        Assert.False(r.Sucesso);
        Assert.Equal(ErroLogin.Credenciais, r.Erro);
        Assert.False(sessao.TemSessao);
    }

    [Fact]
    public async Task Sessao_e_restaurada_do_armazem_e_sair_limpa_tudo()
    {
        var armazem = new ArmazemSessaoMemoria();
        await Criar(armazem: armazem).EntrarAsync("supervisor", "teste-supervisor");

        var nova = Criar(armazem: armazem);
        await nova.RestaurarAsync();
        Assert.True(nova.TemSessao);
        Assert.Equal(Perfil.Supervisor, nova.Utilizador!.Perfil);

        await nova.SairAsync();
        Assert.False(nova.TemSessao);
        Assert.Null(await armazem.LerAsync());
    }

    [Fact]
    public async Task Falha_de_rede_e_distinguida_de_credenciais()
    {
        var sessao = Criar(auth: new AutenticacaoSemRede());
        var r = await sessao.EntrarAsync("x", "y");

        Assert.Equal(ErroLogin.Rede, r.Erro);
    }

    [Theory]
    [InlineData("Ana Maria Silva", "AS")]
    [InlineData("Rui", "R")]
    public void Iniciais_usam_primeiro_e_ultimo_nome(string nome, string esperado) =>
        Assert.Equal(esperado, new Utilizador(nome, Perfil.Operador).Iniciais);

    private sealed class AutenticacaoSemRede : IAutenticacaoService
    {
        public Task<ResultadoLogin> EntrarAsync(string utilizador, string password, CancellationToken ct = default) =>
            Task.FromResult(ResultadoLogin.Falha(ErroLogin.Rede));
    }
}
