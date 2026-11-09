using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using static GestorManutencao.Armazenamento.BaseLocalSqlite;

namespace GestorManutencao.Armazenamento;

public sealed class CacheLocalSqlite(BaseLocalSqlite bd) : ICacheLocal
{
    private const string Leve = "leve", Completa = "completa";

    private static string ChaveBytes(string fichaId, string fotoId) => fichaId + "\u001f" + fotoId;

    public async Task<string?> LerMetaAsync(string chave) =>
        (await (await bd.LigarAsync()).FindAsync<LinhaMeta>(chave))?.Valor;

    public async Task GuardarMetaAsync(string chave, string? valor)
    {
        var c = await bd.LigarAsync();
        if (valor is null) await c.ExecuteAsync("DELETE FROM Meta WHERE Chave = ?", chave);
        else await c.InsertOrReplaceAsync(new LinhaMeta { Chave = chave, Valor = valor });
    }

    public async Task LimparAsync()
    {
        var c = await bd.LigarAsync();
        await c.RunInTransactionAsync(t =>
        {
            t.DeleteAll<LinhaMeta>(); t.DeleteAll<LinhaFicha>(); t.DeleteAll<LinhaMateriais>(); t.DeleteAll<LinhaFotos>();
            t.DeleteAll<LinhaFotoBytes>(); t.DeleteAll<LinhaEquipamentos>(); t.DeleteAll<LinhaFichaEquipamento>();
        });
    }

    public async Task<IReadOnlyList<Intervencao>> ListarFichasAsync() =>
        (await (await bd.LigarAsync()).Table<LinhaFicha>().ToListAsync()).Select(l => Ler<Intervencao>(l.Json)).ToList();

    public async Task<Intervencao?> ObterFichaAsync(string id) =>
        await (await bd.LigarAsync()).FindAsync<LinhaFicha>(id) is { } l ? Ler<Intervencao>(l.Json) : null;

    public async Task GuardarFichasAsync(IEnumerable<Intervencao> fichas)
    {
        var linhas = fichas.Select(f => new LinhaFicha { Id = f.Id, Json = Serializar(f) }).ToList();
        await (await bd.LigarAsync()).RunInTransactionAsync(t => { foreach (var l in linhas) t.InsertOrReplace(l); });
    }

    public async Task RemoverFichasAsync(IEnumerable<string> ids)
    {
        var lista = ids.ToList();
        await (await bd.LigarAsync()).RunInTransactionAsync(t =>
        {
            foreach (var id in lista)
            {
                t.Execute("DELETE FROM Ficha WHERE Id = ?", id);
                t.Execute("DELETE FROM Materiais WHERE FichaId = ?", id);
                t.Execute("DELETE FROM Fotos WHERE FichaId = ?", id);
                t.Execute("DELETE FROM FotoBytes WHERE FichaId = ?", id);
            }
        });
    }

    public async Task<IReadOnlyList<Material>?> LerMateriaisAsync(string fichaId) =>
        await (await bd.LigarAsync()).FindAsync<LinhaMateriais>(fichaId) is { } l ? Ler<List<Material>>(l.Json) : null;

    public async Task GuardarMateriaisAsync(string fichaId, IReadOnlyList<Material> materiais) =>
        await (await bd.LigarAsync()).InsertOrReplaceAsync(new LinhaMateriais { FichaId = fichaId, Json = Serializar(materiais.ToList()) });

    public async Task<IReadOnlyList<Foto>?> LerFotosAsync(string fichaId) =>
        await (await bd.LigarAsync()).FindAsync<LinhaFotos>(fichaId) is { } l ? Ler<List<Foto>>(l.Json) : null;

    public async Task GuardarFotosAsync(string fichaId, IReadOnlyList<Foto> fotos) =>
        await (await bd.LigarAsync()).InsertOrReplaceAsync(new LinhaFotos { FichaId = fichaId, Json = Serializar(fotos.ToList()) });

    public async Task<byte[]?> LerFotoBytesAsync(string fichaId, string fotoId) =>
        (await (await bd.LigarAsync()).FindAsync<LinhaFotoBytes>(ChaveBytes(fichaId, fotoId)))?.Bytes;

    public async Task GuardarFotoBytesAsync(string fichaId, string fotoId, byte[] bytes) =>
        await (await bd.LigarAsync()).InsertOrReplaceAsync(new LinhaFotoBytes { Chave = ChaveBytes(fichaId, fotoId), FichaId = fichaId, Bytes = bytes });

    public async Task RemoverFotosAsync(string fichaId)
    {
        await (await bd.LigarAsync()).RunInTransactionAsync(t =>
        {
            t.Execute("DELETE FROM Fotos WHERE FichaId = ?", fichaId);
            t.Execute("DELETE FROM FotoBytes WHERE FichaId = ?", fichaId);
        });
    }

    private async Task<IReadOnlyList<Equipamento>?> LerListaAsync(string lista) =>
        await (await bd.LigarAsync()).FindAsync<LinhaEquipamentos>(lista) is { } l ? Ler<List<Equipamento>>(l.Json) : null;

    private async Task GuardarListaAsync(string lista, IReadOnlyList<Equipamento> equipamentos) =>
        await (await bd.LigarAsync()).InsertOrReplaceAsync(new LinhaEquipamentos { Lista = lista, Json = Serializar(equipamentos.ToList()) });

    public Task<IReadOnlyList<Equipamento>?> LerEquipamentosLeveAsync() => LerListaAsync(Leve);
    public Task GuardarEquipamentosLeveAsync(IReadOnlyList<Equipamento> equipamentos) => GuardarListaAsync(Leve, equipamentos);
    public Task<IReadOnlyList<Equipamento>?> LerEquipamentosAsync() => LerListaAsync(Completa);
    public Task GuardarEquipamentosAsync(IReadOnlyList<Equipamento> equipamentos) => GuardarListaAsync(Completa, equipamentos);

    public async Task<FichaEquipamentoGuardada?> LerFichaEquipamentoAsync(string id) =>
        await (await bd.LigarAsync()).FindAsync<LinhaFichaEquipamento>(id) is { } l ? Ler<FichaEquipamentoGuardada>(l.Json) : null;

    public async Task GuardarFichaEquipamentoAsync(FichaEquipamentoGuardada ficha) =>
        await (await bd.LigarAsync()).InsertOrReplaceAsync(new LinhaFichaEquipamento
        {
            Id = ficha.Ficha.Equipamento.Id, AbertaTicks = ficha.Aberta.UtcTicks, Json = Serializar(ficha)
        });

    public async Task RemoverFichasEquipamentoAbertasAntesDeAsync(DateTimeOffset limite) =>
        await (await bd.LigarAsync()).ExecuteAsync("DELETE FROM FichaEquipamento WHERE AbertaTicks < ?", limite.UtcTicks);
}
