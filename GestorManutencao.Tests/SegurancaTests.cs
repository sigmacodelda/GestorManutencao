using GestorManutencao.Api;
using GestorManutencao.UI.Modelos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Tests;

public class SegurancaTests
{
    [Fact]
    public void Ligacao_nao_deixa_a_password_injetar_opcoes_e_desliga_o_pooling()
    {
        var ligacao = SqlValidadorCredenciais.ConstruirLigacao("Server=s;Database=d", "ana", "x;Database=outra;Pooling=true");
        var lida = new SqlConnectionStringBuilder(ligacao);

        Assert.Equal("d", lida.InitialCatalog);
        Assert.Equal("x;Database=outra;Pooling=true", lida.Password);
        Assert.False(lida.Pooling);
        Assert.Equal("ana", lida.UserID);
    }

    [Fact]
    public void Ligacao_ignora_seguranca_integrada_da_cadeia_base()
    {
        var ligacao = SqlValidadorCredenciais.ConstruirLigacao("Server=s;Database=d;Integrated Security=true", "ana", "p");

        Assert.False(new SqlConnectionStringBuilder(ligacao).IntegratedSecurity);
    }

    [Fact]
    public void Utilizador_nao_mostra_o_token_no_ToString() =>
        Assert.DoesNotContain("segredo-do-token", new GestorManutencao.UI.Modelos.Utilizador("Ana", GestorManutencao.UI.Modelos.Perfil.Operador, "segredo-do-token").ToString());

    [Fact]
    public void Sessao_expirada_e_detetada() =>
        Assert.True(new GestorManutencao.UI.Modelos.Utilizador("Ana", GestorManutencao.UI.Modelos.Perfil.Operador, "t", DateTimeOffset.UtcNow.AddMinutes(-1)).Expirou);

    [Fact]
    public void Producao_sem_chave_jwt_nao_arranca()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production"));

        Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());
    }

    [Fact]
    public void Producao_com_chave_curta_nao_arranca()
    {
        using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("Jwt:Chave", "curta"));

        Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());
    }
}
