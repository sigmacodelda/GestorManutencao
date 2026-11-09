using System.Runtime.CompilerServices;

namespace GestorManutencao.UI.Offline;

// Um trinco por conjunto de dados (a cache, a fila), e não por âmbito de injeção: mesmo com mais que um scope sobre a mesma
// base local, as alterações compostas e as sincronizações nunca correm em paralelo.
internal static class TrincosPartilhados
{
    private static readonly ConditionalWeakTable<object, Dictionary<string, SemaphoreSlim>> Tabela = new();

    public static SemaphoreSlim Obter(object dados, string nome)
    {
        var trincos = Tabela.GetOrCreateValue(dados);
        lock (trincos)
        {
            if (!trincos.TryGetValue(nome, out var trinco)) trincos[nome] = trinco = new SemaphoreSlim(1, 1);
            return trinco;
        }
    }
}
