using System.Security.Cryptography;
using System.Text;
using GestorManutencao.Api;
using Microsoft.Extensions.Configuration;

namespace GestorManutencao.Tests;

// Connection string cifrada no mesmo formato da LandeiroMaui. Só valores e chave fictícios.
public class LigacaoPhcTests
{
    private const string Chave = "chave-ficticia-de-teste";

    private static string Cifrar(string texto, string chave)
    {
        using var tripleDes = TripleDES.Create();
        tripleDes.Key = MD5.HashData(Encoding.UTF8.GetBytes(chave));
        tripleDes.Mode = CipherMode.ECB;
        tripleDes.Padding = PaddingMode.PKCS7;
        var dados = Encoding.UTF8.GetBytes(texto);
        return Convert.ToBase64String(tripleDes.CreateEncryptor().TransformFinalBlock(dados, 0, dados.Length));
    }

    private static IConfiguration Config(string? ligacao, string? chave = null) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:PHCConn"] = ligacao,
        [LigacaoPhc.ChaveConfiguracao] = chave
    }).Build();

    [Fact]
    public void Desencripta_uma_connection_string_completa()
    {
        var original = "Server=srv-ficticio;Database=BD_TESTE;User Id=u;Password=p;"; // sigma-ok: valor de teste fictício

        Assert.Equal(original, LigacaoPhc.Obter(Config(Cifrar(original, Chave), Chave)));
    }

    [Fact]
    public void Desencripta_o_formato_de_linhas_da_landeiromaui()
    {
        var linhas = Cifrar("srv-ficticio\r\nBD_TESTE\r\nignorada\r\nutil\r\nsegredo", Chave);

        Assert.Equal("Server=srv-ficticio;Database=BD_TESTE;User Id=util;Password=segredo;TrustServerCertificate=True;", LigacaoPhc.Obter(Config(linhas, Chave))); // sigma-ok: valor de teste fictício
    }

    [Fact]
    public void Texto_em_claro_passa_sem_chave()
    {
        Assert.Equal("Server=a;Database=b;", LigacaoPhc.Obter(Config("Server=a;Database=b;")));
    }

    [Fact]
    public void Vazia_devolve_vazia()
    {
        Assert.True(string.IsNullOrWhiteSpace(LigacaoPhc.Obter(Config(""))));
    }

    [Fact]
    public void Cifrada_sem_chave_ou_com_chave_errada_fica_indisponivel()
    {
        var cifrada = Cifrar("Server=a;Database=b;", Chave);

        Assert.Throws<RepositorioIndisponivelException>(() => LigacaoPhc.Obter(Config(cifrada)));
        Assert.Throws<RepositorioIndisponivelException>(() => LigacaoPhc.Obter(Config(cifrada, "outra-chave")));
        Assert.Throws<RepositorioIndisponivelException>(() => LigacaoPhc.Obter(Config("isto-nao-e-base64!", Chave)));
    }
}
