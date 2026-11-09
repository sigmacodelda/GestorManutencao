using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Offline;

public sealed class CacheLocalMemoria : ICacheLocal
{
    private readonly object _trinco = new();
    private readonly Dictionary<string, string> _meta = [];
    private readonly Dictionary<string, Intervencao> _fichas = [];
    private readonly Dictionary<string, IReadOnlyList<Material>> _materiais = [];
    private readonly Dictionary<string, IReadOnlyList<Foto>> _fotos = [];
    private readonly Dictionary<(string, string), byte[]> _bytes = [];
    private readonly Dictionary<string, FichaEquipamentoGuardada> _fichasEquipamento = [];
    private IReadOnlyList<Equipamento>? _leve, _completa;

    public Task<string?> LerMetaAsync(string chave) { lock (_trinco) return Task.FromResult(_meta.GetValueOrDefault(chave)); }

    public Task GuardarMetaAsync(string chave, string? valor)
    {
        lock (_trinco) { if (valor is null) _meta.Remove(chave); else _meta[chave] = valor; }
        return Task.CompletedTask;
    }

    public Task LimparAsync()
    {
        lock (_trinco)
        {
            _meta.Clear(); _fichas.Clear(); _materiais.Clear(); _fotos.Clear(); _bytes.Clear(); _fichasEquipamento.Clear();
            _leve = _completa = null;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Intervencao>> ListarFichasAsync() { lock (_trinco) return Task.FromResult<IReadOnlyList<Intervencao>>(_fichas.Values.ToList()); }

    public Task<Intervencao?> ObterFichaAsync(string id) { lock (_trinco) return Task.FromResult(_fichas.GetValueOrDefault(id)); }

    public Task GuardarFichasAsync(IEnumerable<Intervencao> fichas)
    {
        lock (_trinco) foreach (var f in fichas) _fichas[f.Id] = f;
        return Task.CompletedTask;
    }

    public Task RemoverFichasAsync(IEnumerable<string> ids)
    {
        lock (_trinco)
            foreach (var id in ids.ToList())
            {
                _fichas.Remove(id); _materiais.Remove(id); RemoverFotosSemTrinco(id);
            }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Material>?> LerMateriaisAsync(string fichaId) { lock (_trinco) return Task.FromResult(_materiais.GetValueOrDefault(fichaId)); }

    public Task GuardarMateriaisAsync(string fichaId, IReadOnlyList<Material> materiais)
    {
        lock (_trinco) _materiais[fichaId] = materiais.ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Foto>?> LerFotosAsync(string fichaId) { lock (_trinco) return Task.FromResult(_fotos.GetValueOrDefault(fichaId)); }

    public Task GuardarFotosAsync(string fichaId, IReadOnlyList<Foto> fotos)
    {
        lock (_trinco) _fotos[fichaId] = fotos.ToList();
        return Task.CompletedTask;
    }

    public Task<byte[]?> LerFotoBytesAsync(string fichaId, string fotoId) { lock (_trinco) return Task.FromResult(_bytes.GetValueOrDefault((fichaId, fotoId))); }

    public Task GuardarFotoBytesAsync(string fichaId, string fotoId, byte[] bytes)
    {
        lock (_trinco) _bytes[(fichaId, fotoId)] = bytes;
        return Task.CompletedTask;
    }

    public Task RemoverFotosAsync(string fichaId)
    {
        lock (_trinco) RemoverFotosSemTrinco(fichaId);
        return Task.CompletedTask;
    }

    private void RemoverFotosSemTrinco(string fichaId)
    {
        _fotos.Remove(fichaId);
        foreach (var chave in _bytes.Keys.Where(k => k.Item1 == fichaId).ToList()) _bytes.Remove(chave);
    }

    public Task<IReadOnlyList<Equipamento>?> LerEquipamentosLeveAsync() { lock (_trinco) return Task.FromResult(_leve); }

    public Task GuardarEquipamentosLeveAsync(IReadOnlyList<Equipamento> equipamentos) { lock (_trinco) _leve = equipamentos.ToList(); return Task.CompletedTask; }

    public Task<IReadOnlyList<Equipamento>?> LerEquipamentosAsync() { lock (_trinco) return Task.FromResult(_completa); }

    public Task GuardarEquipamentosAsync(IReadOnlyList<Equipamento> equipamentos) { lock (_trinco) _completa = equipamentos.ToList(); return Task.CompletedTask; }

    public Task<FichaEquipamentoGuardada?> LerFichaEquipamentoAsync(string id) { lock (_trinco) return Task.FromResult(_fichasEquipamento.GetValueOrDefault(id)); }

    public Task GuardarFichaEquipamentoAsync(FichaEquipamentoGuardada ficha)
    {
        lock (_trinco) _fichasEquipamento[ficha.Ficha.Equipamento.Id] = ficha;
        return Task.CompletedTask;
    }

    public Task RemoverFichasEquipamentoAbertasAntesDeAsync(DateTimeOffset limite)
    {
        lock (_trinco)
            foreach (var id in _fichasEquipamento.Where(p => p.Value.Aberta < limite).Select(p => p.Key).ToList()) _fichasEquipamento.Remove(id);
        return Task.CompletedTask;
    }
}
