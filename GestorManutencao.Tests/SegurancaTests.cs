using GestorManutencao.Api;
using UiPerfil = GestorManutencao.UI.Modelos.Perfil;
using UiUtilizador = GestorManutencao.UI.Modelos.Utilizador;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

public class SegurancaTests
{
    private static PinHasher Hasher(string pepper) => new(Options.Create(new PinOpcoes { Pepper = pepper }));

    // Pasta vazia: os testes de arranque não leem o appsettings.Production.json local que haja nesta máquina.
    private static string PastaVazia() => Directory.CreateTempSubdirectory("gm-teste").FullName;

    [Fact]
    public void Hash_do_pin_nao_contem_o_pin_e_depende_do_segredo_do_servidor()
    {
        var a = Hasher("segredo-a-com-mais-de-32-caracteres!!").Hash("482913");
        var b = Hasher("segredo-b-com-mais-de-32-caracteres!!").Hash("482913");

        Assert.Matches("^[0-9A-F]{64}$", a);
        Assert.DoesNotContain("482913", a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, Hasher("segredo-a-com-mais-de-32-caracteres!!").Hash("482913"));
    }

    [Theory]
    [InlineData("482913", true)]
    [InlineData("000000", false)]
    [InlineData("111111", false)]
    [InlineData("123456", false)]
    [InlineData("987654", false)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12 456", false)]
    [InlineData("٣٣٣٣٣٣", false)] // dígitos não latinos
    [InlineData(null, false)]
    public void Regras_do_pin(string? pin, bool aceitavel) => Assert.Equal(aceitavel, RegrasPin.Aceitavel(pin));

    [Fact]
    public void Pins_gerados_sao_sempre_aceitaveis_e_com_seis_digitos()
    {
        for (var i = 0; i < 500; i++) Assert.True(RegrasPin.Aceitavel(RegrasPin.Gerar()));
    }

    [Fact]
    public void Utilizador_nao_mostra_o_token_no_ToString() =>
        Assert.DoesNotContain("segredo-do-token", new UiUtilizador("Ana", UiPerfil.Operador, "segredo-do-token").ToString());

    [Fact]
    public void Sessao_expirada_e_detetada() =>
        Assert.True(new UiUtilizador("Ana", UiPerfil.Operador, "t", DateTimeOffset.UtcNow.AddMinutes(-1)).Expirou);

    [Fact]
    public void Producao_sem_chave_jwt_nao_arranca()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production").UseContentRoot(PastaVazia()));

        Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());
    }

    [Fact]
    public void Producao_com_chave_curta_nao_arranca()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production").UseContentRoot(PastaVazia())
            .UseSetting("Jwt:Chave", "curta"));

        Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());
    }

    [Fact]
    public void Producao_sem_segredo_do_pin_nao_arranca()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production").UseContentRoot(PastaVazia())
            .UseSetting("Jwt:Chave", new string('j', 40))
            .UseSetting("ConnectionStrings:PHCConn", "Server=x;Database=y"));

        Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());
    }

    [Fact]
    public void Producao_sem_ligacao_a_base_nao_arranca()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production").UseContentRoot(PastaVazia())
            .UseSetting("Jwt:Chave", new string('j', 40))
            .UseSetting("Pin:Pepper", new string('p', 40)));

        var erro = Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());
        Assert.Contains("PHCConn", erro.ToString());
    }

    [Fact]
    public void Producao_com_pepper_igual_a_chave_jwt_nao_arranca()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production").UseContentRoot(PastaVazia())
            .UseSetting("Jwt:Chave", new string('x', 40))
            .UseSetting("Pin:Pepper", new string('x', 40))
            .UseSetting("ConnectionStrings:PHCConn", "Server=x;Database=y"));

        Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());
    }
}
