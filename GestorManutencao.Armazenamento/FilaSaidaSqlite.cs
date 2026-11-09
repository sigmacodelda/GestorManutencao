using GestorManutencao.UI.Offline;
using static GestorManutencao.Armazenamento.BaseLocalSqlite;

namespace GestorManutencao.Armazenamento;

// A fila e os avisos ficam em tabelas à parte da cache: LimparAsync da cache não lhes toca.
public sealed class FilaSaidaSqlite(BaseLocalSqlite bd) : IFilaSaida
{
    private static LinhaOperacao Linha(OperacaoSaida o) =>
        new() { Id = o.Id, Dono = o.Dono, FichaId = o.FichaId, Estado = (int)o.Estado, Json = Serializar(o) };

    public async Task AdicionarAsync(OperacaoSaida operacao) => await (await bd.LigarAsync()).InsertAsync(Linha(operacao));

    public async Task<IReadOnlyList<OperacaoSaida>> ListarAsync(string dono) =>
        (await (await bd.LigarAsync()).QueryAsync<LinhaOperacao>("SELECT * FROM Operacao WHERE Dono = ? ORDER BY Seq", dono))
        .Select(l => Ler<OperacaoSaida>(l.Json)).ToList();

    public async Task<IReadOnlyList<OperacaoSaida>> ListarTodasAsync() =>
        (await (await bd.LigarAsync()).QueryAsync<LinhaOperacao>("SELECT * FROM Operacao ORDER BY Seq")).Select(l => Ler<OperacaoSaida>(l.Json)).ToList();

    public async Task AtualizarAsync(OperacaoSaida operacao) =>
        await (await bd.LigarAsync()).ExecuteAsync("UPDATE Operacao SET Estado = ?, FichaId = ?, Json = ? WHERE Id = ?",
            (int)operacao.Estado, operacao.FichaId, Serializar(operacao), operacao.Id);

    public async Task RemoverAsync(string id) => await (await bd.LigarAsync()).ExecuteAsync("DELETE FROM Operacao WHERE Id = ?", id);

    public async Task<int> ContarPendentesAsync(string dono) =>
        await (await bd.LigarAsync()).ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Operacao WHERE Dono = ? AND Estado <> ?", dono, (int)EstadoOperacao.Conflito);

    public async Task RemapearFichaAsync(string dono, string idLocal, string idReal) =>
        await (await bd.LigarAsync()).RunInTransactionAsync(t =>
        {
            foreach (var l in t.Query<LinhaOperacao>("SELECT * FROM Operacao WHERE Dono = ? AND FichaId = ?", dono, idLocal))
            {
                var atualizada = Ler<OperacaoSaida>(l.Json) with { FichaId = idReal };
                t.Execute("UPDATE Operacao SET FichaId = ?, Json = ? WHERE Id = ?", idReal, Serializar(atualizada), l.Id);
            }
        });

    public async Task AdicionarAvisoAsync(AvisoConflito aviso) =>
        await (await bd.LigarAsync()).InsertAsync(new LinhaAviso { Id = aviso.Id, Dono = aviso.Dono, Json = Serializar(aviso) });

    public async Task<IReadOnlyList<AvisoConflito>> ListarAvisosAsync(string dono) =>
        (await (await bd.LigarAsync()).QueryAsync<LinhaAviso>("SELECT * FROM Aviso WHERE Dono = ? ORDER BY Seq", dono))
        .Select(l => Ler<AvisoConflito>(l.Json)).ToList();

    public async Task RemoverAvisoAsync(string id) => await (await bd.LigarAsync()).ExecuteAsync("DELETE FROM Aviso WHERE Id = ?", id);
}
