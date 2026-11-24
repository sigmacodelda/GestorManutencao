using System.Data;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Regra "iniciar sem stock" da LandeiroMaui (u_configman.iniciarsemstock): peças necessárias sem stock no armazém da manutenção.
public static class RegrasSemStock
{
    // "ref · designação (precisa X, há Y)", como no desktop.
    public static List<string> EmFalta(IReadOnlyDictionary<string, (string Design, decimal Qtt)> precisa, Func<string, decimal> stock)
    {
        var falta = new List<string>();
        foreach (var (r, v) in precisa)
        {
            var h = stock(r);
            if (h < v.Qtt) falta.Add($"{r} · {v.Design} (precisa {v.Qtt:0.###}, há {h:0.###})");
        }
        return falta;
    }

    public static void Somar(Dictionary<string, (string Design, decimal Qtt)> precisa, string r, string design, decimal qtt) =>
        precisa[r] = precisa.TryGetValue(r, out var a) ? (a.Design, a.Qtt + qtt) : (design, qtt);
}

public sealed partial class SqlRepositorioIntervencoes
{
    public Task<IReadOnlyList<string>> PecasSemStockAsync(string id, CancellationToken ct) => Executar(conn => PecasSemStockAsync(conn, id, ct));

    private static async Task<IReadOnlyList<string>> PecasSemStockAsync(SqlConnection conn, string id, CancellationToken ct)
    {
        var precisa = new Dictionary<string, (string Design, decimal Qtt)>(StringComparer.OrdinalIgnoreCase);
        string tarefa = "";
        var preventiva = false;
        await using (var ficha = new SqlCommand("select rtrim(tipointerv), rtrim(isnull(uitmanutlistamp, '')) from u_fichaint with (nolock) where u_fichaintstamp = @id", conn))
        {
            Texto(ficha, "@id", 25, id);
            await using var r = await ficha.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                preventiva = string.Equals(r.GetString(0).Trim(), "Preventiva", StringComparison.OrdinalIgnoreCase);
                tarefa = r.GetString(1).Trim();
            }
        }
        await using (var mats = new SqlCommand("select ltrim(rtrim(ref)), ltrim(rtrim(design)), qtt from u_matinterv with (nolock) where u_fichaintstamp = @id and ltrim(rtrim(ref)) <> '' and qtt > 0", conn))
        {
            Texto(mats, "@id", 25, id);
            await using var r = await mats.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) RegrasSemStock.Somar(precisa, r.GetString(0), r.GetString(1), r.GetDecimal(2));
        }
        if (preventiva && tarefa != "" && await ColunaTarefaPecaAsync(conn, ct) is { } col)
        {
            await using var pecas = new SqlCommand($@"
                select ltrim(rtrim(tp.ref)), ltrim(rtrim(isnull(nullif(st.design, ''), tp.design))), tp.qtt
                from u_tarefapeca tp with (nolock) left join st with (nolock) on st.ref = tp.ref
                where tp.[{col}] = @t and tp.qtt > 0 and ltrim(rtrim(tp.ref)) <> ''", conn);
            Texto(pecas, "@t", 25, tarefa);
            await using var r = await pecas.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                if (!precisa.ContainsKey(r.GetString(0))) precisa[r.GetString(0)] = (r.GetString(1), r.GetDecimal(2));
        }
        var stock = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in precisa.Keys)
        {
            await using var cmd = new SqlCommand("select isnull(sum(stock), 0) from sa with (nolock) where ltrim(rtrim(ref)) = @r and armazem = @a", conn);
            Texto(cmd, "@r", 18, r);
            cmd.Parameters.Add("@a", SqlDbType.Int).Value = RegrasPecas.Armazem;
            stock[r] = Convert.ToDecimal(await cmd.ExecuteScalarAsync(ct));
        }
        return RegrasSemStock.EmFalta(precisa, r => stock[r]);
    }

    // A coluna que liga u_tarefapeca à tarefa tem prefixo variável (…itmanutlistamp); sem a tabela não há peças da tarefa.
    private static async Task<string?> ColunaTarefaPecaAsync(SqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("select top 1 column_name from information_schema.columns where table_name = 'u_tarefapeca' and column_name like '%itmanutlistamp'", conn);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }
}
