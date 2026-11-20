using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;

namespace GestorManutencao.Tests;

public class MaterialPecasOfflineTests
{
    private static async Task<Cenario> CenarioEmCursoAsync()
    {
        var c = new Cenario();
        await c.SemearEmCacheAsync("T1", "Compressor de ar 02", EstadoIntervencao.EmCurso);
        return c;
    }

    [Fact]
    public async Task Material_com_peca_sem_rede_fica_na_fila_com_a_ref_e_por_dar()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);

        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 2, 3, "P001");

        Assert.Equal("P001", local.Ref);
        Assert.False(local.SaidaDada);
        var operacao = Assert.Single(await c.Fila.ListarAsync("ana"));
        Assert.Contains("\"Ref\":\"P001\"", operacao.Payload);
        Assert.Equal("P001", Assert.Single(await c.Svc.ListarMateriaisAsync("T1")).Ref);
    }

    [Fact]
    public async Task Ao_sincronizar_o_remoto_recebe_a_ref_e_o_material_passa_a_ter_saida_dada()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);
        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 2, 3, "P001");
        await c.Svc.AdicionarMaterialAsync("T1", "Massa", 1, 1);
        c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        Assert.Equal(["P001", ""], c.Remoto.RefsMaterial);
        var cache = (await c.Cache.LerMateriaisAsync("T1"))!;
        var comPeca = Assert.Single(cache, m => m.Ref == "P001");
        Assert.True(comPeca.SaidaDada);
        Assert.DoesNotContain(cache, m => m.Id.StartsWith("L-"));
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Apagar_material_local_com_peca_cancela_a_operacao_sem_chamar_o_remoto()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);
        var local = await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 1, 5, "P001");

        await c.Svc.ApagarMaterialAsync("T1", local.Id);

        Assert.Empty(await c.Fila.ListarAsync("ana"));
        c.Rede.Definir(true);
        await c.Sinc.SincronizarAsync();
        Assert.Equal(0, c.Remoto.Contar("AdicionarMaterial"));
        Assert.Equal(0, c.Remoto.Contar("ApagarMaterial"));
        Assert.Empty(c.Remoto.RefsMaterial);
    }

    [Fact]
    public async Task Operacao_gravada_antes_das_pecas_sincroniza_com_ref_vazia()
    {
        var c = await CenarioEmCursoAsync();
        c.Rede.Definir(false);
        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento", 1, 5);
        var operacao = Assert.Single(await c.Fila.ListarAsync("ana"));
        // Fila antiga: a carga não tem o campo Ref.
        var antiga = operacao.Payload.Replace(",\"Ref\":\"\"", "");
        Assert.DoesNotContain("Ref", antiga);
        await c.Fila.AtualizarAsync(operacao with { Payload = antiga });
        c.Rede.Definir(true);

        await c.Sinc.SincronizarAsync();

        Assert.Equal([""], c.Remoto.RefsMaterial);
        Assert.Equal(0, c.Sinc.PendentesCount);
    }

    [Fact]
    public async Task Timeout_com_peca_deixa_incerta_e_reenvia_so_uma_vez_com_a_ref()
    {
        var c = await CenarioEmCursoAsync();
        await c.Svc.ListarMateriaisAsync("T1");
        c.Remoto.DepoisFalha["AdicionarMaterial"] = new TaskCanceledException("timeout");

        await c.Svc.AdicionarMaterialAsync("T1", "Rolamento 6204", 2, 3, "P001");
        Assert.Equal(EstadoOperacao.Incerta, Assert.Single(await c.Fila.ListarAsync("ana")).Estado);

        await c.Sinc.SincronizarAsync();

        // O servidor já tinha o material (e a saída) quando a resposta se perdeu: a fila encontra-o e não repete.
        Assert.Equal(1, c.Remoto.Contar("AdicionarMaterial"));
        Assert.Equal(["P001"], c.Remoto.RefsMaterial);
        Assert.Equal("P001", Assert.Single((await c.Cache.LerMateriaisAsync("T1"))!).Ref);
    }
}
