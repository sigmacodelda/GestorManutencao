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
        var r = await sessao.EntrarAsync("4829");

        Assert.True(r.Sucesso);
        Assert.Equal(ModoTrabalho.Operador, sessao.Modo);
        sessao.MudarModo(ModoTrabalho.Supervisor);
        Assert.Equal(ModoTrabalho.Operador, sessao.Modo);
    }

    [Fact]
    public async Task Supervisor_abre_em_modo_supervisor_e_pode_passar_a_operador()
    {
        var sessao = Criar();
        await sessao.EntrarAsync("7953");

        Assert.Equal(ModoTrabalho.Supervisor, sessao.Modo);
        sessao.MudarModo(ModoTrabalho.Operador);
        Assert.Equal(ModoTrabalho.Operador, sessao.Modo);
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("999999")]
    public async Task Pin_errado_falha_sem_criar_sessao(string pin)
    {
        var sessao = Criar();
        var r = await sessao.EntrarAsync(pin);

        Assert.False(r.Sucesso);
        Assert.Equal(ErroLogin.Credenciais, r.Erro);
        Assert.False(sessao.TemSessao);
    }

    [Fact]
    public async Task Sessao_e_restaurada_do_armazem_e_sair_limpa_tudo()
    {
        var armazem = new ArmazemSessaoMemoria();
        await Criar(armazem: armazem).EntrarAsync("7953");

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
        var r = await sessao.EntrarAsync("123456");

        Assert.Equal(ErroLogin.Rede, r.Erro);
    }

    [Theory]
    [InlineData("Ana Maria Silva", "AS")]
    [InlineData("Rui", "R")]
    public void Iniciais_usam_primeiro_e_ultimo_nome(string nome, string esperado) =>
        Assert.Equal(esperado, new Utilizador(nome, Perfil.Operador).Iniciais);

    private sealed class AutenticacaoSemRede : IAutenticacaoService
    {
        public Task<ResultadoLogin> EntrarAsync(string pin, CancellationToken ct = default) =>
            Task.FromResult(ResultadoLogin.Falha(ErroLogin.Rede));

        public Task<ResultadoLogin> MudarPinAsync(string? token, string pinAtual, string pinNovo, CancellationToken ct = default) =>
            Task.FromResult(ResultadoLogin.Falha(ErroLogin.Rede));
    }

    [Fact]
    public async Task Pin_provisorio_obriga_a_mudar_e_a_sessao_passa_a_definitiva()
    {
        var sessao = Criar();
        await sessao.EntrarAsync("3571");
        Assert.True(sessao.Utilizador!.PinProvisorio);

        Assert.Equal(ErroLogin.PinFraco, (await sessao.MudarPinAsync("3571", "1111")).Erro);
        Assert.Equal(ErroLogin.Credenciais, (await sessao.MudarPinAsync("0000", "9137")).Erro);
        Assert.Equal(ErroLogin.PinEmUso, (await sessao.MudarPinAsync("3571", "7953")).Erro);
        Assert.True(sessao.Utilizador!.PinProvisorio);

        Assert.True((await sessao.MudarPinAsync("3571", "9137")).Sucesso);
        Assert.False(sessao.Utilizador!.PinProvisorio);
    }

    [Theory]
    [InlineData("4829", "4829", null)]
    [InlineData("123", "123", "O PIN tem de ter 4 números.")]
    [InlineData("1111", "1111", "Esse PIN é fácil de adivinhar. Escolha outro.")]
    [InlineData("9137", "9138", "Os dois PINs não são iguais.")]
    [InlineData("9137", "9137", null)]
    public void Validacao_do_pin_novo_no_ecra(string pin, string repetido, string? esperado)
    {
        var r = RegrasPin.ValidarNovo(pin, repetido);
        if (pin == "4829") Assert.Null(r); else Assert.Equal(esperado, r);
    }
}
