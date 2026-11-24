namespace GestorManutencao.Api;

// Modo Development e testes: desenho fixo (ou o que o teste der) com as mesmas regras do SQL.
public sealed class RepositorioEstruturaMemoria(IEnumerable<EstanteDto>? estantes = null) : IRepositorioEstrutura
{
    private readonly List<EstanteDto> _estantes = (estantes ?? [new("A", 1, 3, 4), new("A", 2, 4, 6), new("B", 1, 2, 3)]).ToList();
    private LugarDto? _buffer;

    public void DefinirBuffer(string? texto) => _buffer = RegrasLocalizacao.LerBuffer(texto);

    public Task<IReadOnlyList<EstanteDto>> ListarAsync(CancellationToken ct) =>
        Task.FromResult(RegrasEstrutura.SemBuffer(_estantes, _buffer));
}
