using GestorManutencao.UI.Modelos;
using static GestorManutencao.UI.Offline.ClassificacaoErros;

namespace GestorManutencao.UI.Offline;

// Mantém a cache fresca: minhas por fazer, recentes (30 dias), materiais das por fazer e equipamentos.
// A app chama-o no arranque, quando a rede volta e de 15 em 15 minutos com a app aberta.
// Se o utilizador mudar a meio, pára: nada do que veio para o dono anterior é gravado na cache do novo.
public sealed class AtualizadorCache(IIntervencoesRemoto remoto, ManutencaoCache mc, IRedeEstado rede, TimeProvider tempo, Func<Perfil?>? perfilAtual = null)
{
    // true se atualizou. Falha de rede é silenciosa (devolve false); 401, 403 e 5xx propagam sem mexer nos dados.
    public async Task<bool> AtualizarCacheAsync(CancellationToken ct = default)
    {
        if (!rede.Disponivel || await mc.GarantirDonoAsync() is not { } dono) return false;
        var cache = mc.Cache;
        try
        {
            ct.ThrowIfCancellationRequested();
            // Qualidade: só tem a lista das fichas por concluir (não tem fichas suas nem equipamentos).
            if (perfilAtual?.Invoke() == Perfil.Qualidade)
            {
                var porConcluir = await remoto.ListarPorConcluirAsync(ct);
                return await GravarSeDonoAsync(dono, () => cache.GuardarMetaAsync(ListasDecisao.Concluir, CargasFila.Escrever(porConcluir)))
                    && await GravarSeDonoAsync(dono, mc.MarcarAtualizadoAsync);
            }
            // Supervisor: guarda a lista por validar para poder validar e devolver sem rede.
            if (perfilAtual?.Invoke() == Perfil.Supervisor)
            {
                var porValidar = await remoto.ListarPorValidarAsync(ct);
                if (!await GravarSeDonoAsync(dono, () => cache.GuardarMetaAsync(ListasDecisao.Validar, CargasFila.Escrever(porValidar)))) return false;
            }
            var marca = mc.Marca;
            var minhas = await remoto.ListarMinhasAsync(ct);
            // Antes de gravar as recentes: a retenção usa a data planeada e apagaria as concluídas há pouco.
            await mc.AplicarRetencaoAsync(DateOnly.FromDateTime(tempo.GetLocalNow().DateTime), dono);
            await mc.MesclarMinhasAsync(minhas, dono, marca);
            var recentes = await remoto.ListarRecentesAsync(ManutencaoCache.DiasRetencao, ct);
            await mc.MesclarFichasAsync(recentes, dono, marca);

            foreach (var ficha in minhas.Where(f => !IdLocal(f.Id)))
            {
                if (ct.IsCancellationRequested || !mc.EDono(dono)) return false;
                var desde = mc.Marca;
                var materiais = await remoto.ListarMateriaisAsync(ficha.Id, ct);
                await mc.ComTrincoAsync(async () =>
                {
                    if (mc.EDono(dono) && !await mc.TemOperacoesAsync(dono, ficha.Id) && !mc.EnviadaDesde(ficha.Id, desde))
                        await cache.GuardarMateriaisAsync(ficha.Id, materiais);
                });
            }

            if (ct.IsCancellationRequested || !mc.EDono(dono)) return false;
            var completa = await remoto.ListarAsync(new FiltroEquipamentos(Obsoletos: true), ct);
            // A pesquisa leve da API não devolve nada sem texto; a lista leve offline (para registar avarias) sai da lista completa.
            var leve = completa.Where(e => !e.Obsoleto).ToList();
            if (!await GravarSeDonoAsync(dono, async () =>
            {
                await cache.GuardarEquipamentosLeveAsync(leve);
                await cache.GuardarEquipamentosAsync(completa);
            })) return false;

            // Fichas dos equipamentos com intervenções por fazer, para abrir sem rede.
            var nomes = minhas.Select(f => f.Equipamento).ToHashSet();
            foreach (var equipamento in completa.Where(e => nomes.Contains(e.Nome)))
            {
                if (ct.IsCancellationRequested || !mc.EDono(dono)) return false;
                if (await remoto.ObterFichaAsync(equipamento.Id, ct) is { } ficha
                    && !await GravarSeDonoAsync(dono, () => cache.GuardarFichaEquipamentoAsync(new FichaEquipamentoGuardada(ficha, tempo.GetUtcNow())))) return false;
            }

            return await GravarSeDonoAsync(dono, mc.MarcarAtualizadoAsync);
        }
        catch (Exception e) when (FalhaDeRede(e, ct)) { return false; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !mc.EDono(dono)) { return false; }
    }

    private Task<bool> GravarSeDonoAsync(string dono, Func<Task> gravar) => mc.ComTrincoAsync(async () =>
    {
        if (!mc.EDono(dono)) return false;
        await gravar();
        return true;
    });
}

// Chaves (na meta da cache) das listas de fichas à espera de decisão: por validar (supervisor) e por concluir (Qualidade).
internal static class ListasDecisao
{
    public const string Validar = "lista-validar", Concluir = "lista-concluir";
}
