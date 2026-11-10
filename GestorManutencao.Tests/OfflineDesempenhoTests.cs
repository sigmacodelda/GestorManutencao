using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;

namespace GestorManutencao.Tests;

// A cache SQLite faz um commit por gravação: gravar as fichas uma a uma, debaixo do trinco partilhado, tornava a lista lenta no telemóvel.
public class OfflineDesempenhoTests
{
    private sealed class CacheContada(ICacheLocal i) : ICacheLocal
    {
        public int GravacoesDeFichas { get; private set; }
        public Task<string?> LerMetaAsync(string chave) => i.LerMetaAsync(chave);
        public Task GuardarMetaAsync(string chave, string? valor) => i.GuardarMetaAsync(chave, valor);
        public Task LimparAsync() => i.LimparAsync();
        public Task<IReadOnlyList<Intervencao>> ListarFichasAsync() => i.ListarFichasAsync();
        public Task<Intervencao?> ObterFichaAsync(string id) => i.ObterFichaAsync(id);
        public Task GuardarFichasAsync(IEnumerable<Intervencao> fichas) { GravacoesDeFichas++; return i.GuardarFichasAsync(fichas); }
        public Task RemoverFichasAsync(IEnumerable<string> ids) => i.RemoverFichasAsync(ids);
        public Task<IReadOnlyList<Material>?> LerMateriaisAsync(string fichaId) => i.LerMateriaisAsync(fichaId);
        public Task GuardarMateriaisAsync(string fichaId, IReadOnlyList<Material> materiais) => i.GuardarMateriaisAsync(fichaId, materiais);
        public Task<IReadOnlyList<Foto>?> LerFotosAsync(string fichaId) => i.LerFotosAsync(fichaId);
        public Task GuardarFotosAsync(string fichaId, IReadOnlyList<Foto> fotos) => i.GuardarFotosAsync(fichaId, fotos);
        public Task<byte[]?> LerFotoBytesAsync(string fichaId, string fotoId) => i.LerFotoBytesAsync(fichaId, fotoId);
        public Task GuardarFotoBytesAsync(string fichaId, string fotoId, byte[] bytes) => i.GuardarFotoBytesAsync(fichaId, fotoId, bytes);
        public Task RemoverFotosAsync(string fichaId) => i.RemoverFotosAsync(fichaId);
        public Task<IReadOnlyList<Equipamento>?> LerEquipamentosLeveAsync() => i.LerEquipamentosLeveAsync();
        public Task GuardarEquipamentosLeveAsync(IReadOnlyList<Equipamento> e) => i.GuardarEquipamentosLeveAsync(e);
        public Task<IReadOnlyList<Equipamento>?> LerEquipamentosAsync() => i.LerEquipamentosAsync();
        public Task GuardarEquipamentosAsync(IReadOnlyList<Equipamento> e) => i.GuardarEquipamentosAsync(e);
        public Task<FichaEquipamentoGuardada?> LerFichaEquipamentoAsync(string id) => i.LerFichaEquipamentoAsync(id);
        public Task GuardarFichaEquipamentoAsync(FichaEquipamentoGuardada f) => i.GuardarFichaEquipamentoAsync(f);
        public Task RemoverFichasEquipamentoAbertasAntesDeAsync(DateTimeOffset limite) => i.RemoverFichasEquipamentoAbertasAntesDeAsync(limite);
    }

    private static Intervencao Ficha(string id, EstadoIntervencao estado = EstadoIntervencao.Planeada, string desc = "") =>
        new(id, "Compressor " + id, "Produção", TipoIntervencao.Curativa, new DateOnly(2026, 11, 9), estado, desc, null, null, null, "ana", 3);

    private static (Cenario, CacheContada) NovoCenario()
    {
        var cache = new CacheContada(new CacheLocalMemoria());
        return (new Cenario(cache, new FilaSaidaMemoria(), new ArmazemFotosPendentesMemoria()), cache);
    }

    [Fact]
    public async Task Mesclar_fichas_recentes_grava_numa_so_operacao()
    {
        var (c, cache) = NovoCenario();
        await c.Mc.MesclarFichasAsync(Enumerable.Range(0, 500).Select(i => Ficha("F" + i, EstadoIntervencao.Concluida)).ToList());

        Assert.Equal(1, cache.GravacoesDeFichas);
        Assert.Equal(500, (await cache.ListarFichasAsync()).Count);
    }

    [Fact]
    public async Task Mesclar_minhas_grava_numa_so_operacao_e_remove_as_que_sairam()
    {
        var (c, cache) = NovoCenario();
        await cache.GuardarFichasAsync([Ficha("SAIU")]);
        var antes = cache.GravacoesDeFichas;

        await c.Mc.MesclarMinhasAsync(Enumerable.Range(0, 50).Select(i => Ficha("M" + i)).ToList());

        Assert.Equal(1, cache.GravacoesDeFichas - antes);
        var ids = (await cache.ListarFichasAsync()).Select(f => f.Id).ToHashSet();
        Assert.Equal(50, ids.Count);
        Assert.DoesNotContain("SAIU", ids);
    }

    [Fact]
    public async Task Mesclar_em_lote_nao_substitui_a_ficha_por_enviar()
    {
        var (c, cache) = NovoCenario();
        await cache.GuardarFichasAsync([Ficha("A", EstadoIntervencao.EmCurso, "texto do técnico") with { Pendente = true }]);

        await c.Mc.MesclarFichasAsync([Ficha("A", EstadoIntervencao.Planeada, "do servidor"), Ficha("B")]);

        var a = (await cache.ObterFichaAsync("A"))!;
        Assert.True(a.Pendente);
        Assert.Equal("texto do técnico", a.Anomalia);
        Assert.NotNull(await cache.ObterFichaAsync("B"));
    }
}
