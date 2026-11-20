namespace GestorManutencao.Api;

// Conferência das peças da ficha: lugar de onde sai cada peça, saída em conferência e as falhas parciais.
// Regra comum: nada disto desfaz o estado da ficha; o que falha fica "por dar" ou "por confirmar" e repete-se.
public static partial class EndpointsIntervencoes
{
    private const string ErroEscolhaLugar = "escolha_o_lugar";
    private const string AvisoSemLugar = "Peça sem localização: saiu sem descontar de nenhum lugar.";
    private const string AvisoPorConfirmar = "Conferência por confirmar";

    // Lugar, aviso (peça sem lugar) ou erro de resposta (400 lugar inválido, 409 escolha_o_lugar).
    private sealed record LugarResolvido(LugarDto? Lugar, string? Aviso, IResult? Erro);

    private static string? Juntar(string? a, string? b) =>
        string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrEmpty(x))) is { Length: > 0 } t ? t : null;

    // Texto A-02-1-01 (ou LOC-A-02-1-01) para lugar completo; sem posição não serve para descontar.
    private static LugarDto? LerLugar(string? texto) =>
        RegrasLocalizacao.LerLocal(texto) is { Posicao: { } posicao } l
            ? new LugarDto(l.Corredor, RegrasLocalizacao.NormalizarEstante(l.Corredor, l.Estante), l.Nivel, posicao, 0)
            : null;

    // Pedido com Local: vale esse (400 se inválido). Sem Local: o lugar anterior da saída, se vier; senão o único lugar da peça com stock;
    // vários = 409 escolha_o_lugar com a lista; nenhum (ou tabela em falta) = grava e avisa.
    private static async Task<LugarResolvido> ResolverLugarAsync(IRepositorioLocalizacoes locs, string refPeca, string? pedido, string? anterior, ILogger log, CancellationToken ct)
    {
        if (refPeca == "") return new(null, null, null);
        pedido = (pedido ?? "").Trim();
        if (pedido != "")
            return LerLugar(pedido) is { } p ? new(p, null, null)
                : new(null, null, PedidoInvalido("Indique o local completo (corredor, estante, nível e posição), por exemplo A-02-1-01."));
        if (LerLugar(anterior) is { } ant) return new(ant, null, null);
        try
        {
            if (await locs.VerificarTabelaAsync(ct) != "") return new(null, AvisoSemLugar, null);
            var comStock = (await locs.ListarAsync(refPeca, ct)).Lugares.Where(l => l.Qtt > 0).ToList();
            return comStock.Count switch
            {
                1 => new(comStock[0], null, null),
                0 => new(null, AvisoSemLugar, null),
                _ => new(null, null, Results.Json(new { erro = ErroEscolhaLugar, lugares = comStock }, statusCode: StatusCodes.Status409Conflict))
            };
        }
        catch (RepositorioIndisponivelException ex)
        {
            log.LogError(ex, "Não foi possível consultar os lugares da peça {Peca}.", refPeca);
            return new(null, "Não foi possível consultar os lugares da peça: saiu sem descontar de nenhum lugar.", null);
        }
    }

    // Soma de volta ao lugar. Sem lugar conhecido não há nada a repor. Nunca lança: uma falha fica no log (o lugar fica desfasado).
    private static async Task<bool> ReporLugarAsync(IRepositorioLocalizacoes locs, string refPeca, string? local, decimal qtt, string iniciais, ILogger log)
    {
        if (LerLugar(local) is not { } l) return true;
        try
        {
            var r = await locs.ReporAsync(refPeca, l.Corredor, l.Estante, l.Nivel, l.Posicao, qtt, iniciais, CancellationToken.None);
            if (r.Ok) return true;
            log.LogError("Não foi possível repor {Qtt} da peça {Peca} no lugar {Lugar}: {Erro}", qtt, refPeca, l.Texto, r.Erro);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Não foi possível repor a peça {Peca} no lugar {Lugar}.", refPeca, l.Texto);
        }
        return false;
    }

    // Desconta do lugar e dá a saída em conferência (sem token do pedido: o material já está gravado). Sem quantidade no lugar sai na mesma, com aviso.
    // Se a saída falhar repõe o lugar e liga a peça ao material (u_matinterv.ref), para Ref preenchida e SaidaDada = false significar "peça por dar".
    private static async Task<(bool Ok, string? Erro, string? Aviso, string? Local)> TentarSaidaAsync(IRepositorioPecas pecas, IRepositorioLocalizacoes locs,
        string fichaId, string materialId, string refPeca, decimal qtt, LugarDto? lugar, string? avisoLugar, string iniciais, ILogger log)
    {
        string? avisoStock = null, local = null, aviso = avisoLugar;
        try
        {
            if (await pecas.ObterPorCodigoAsync(refPeca, CancellationToken.None) is { } peca) avisoStock = RegrasPecas.AvisoStock(qtt, peca.Disponivel);
        }
        catch (Exception ex) { log.LogError(ex, "Não foi possível ler o stock da peça {Peca}.", refPeca); }

        if (lugar is not null)
        {
            try
            {
                var d = await locs.DescontarAsync(refPeca, lugar.Corredor, lugar.Estante, lugar.Nivel, lugar.Posicao, qtt, iniciais, CancellationToken.None);
                if (d.Ok) local = lugar.Texto;
                else aviso = Juntar(aviso, $"O lugar {lugar.Texto} não tem quantidade suficiente: a peça saiu sem descontar do lugar.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha ao descontar a peça {Peca} do lugar {Lugar}.", refPeca, lugar.Texto);
                aviso = Juntar(aviso, $"Não foi possível descontar do lugar {lugar.Texto}: a peça saiu sem descontar do lugar.");
            }
        }

        string erro;
        try
        {
            var r = await pecas.DarSaidaAsync(fichaId, materialId, refPeca, qtt, iniciais, CancellationToken.None, local);
            if (r.Ok) return (true, null, Juntar(aviso, avisoStock), local);
            erro = r.Erro ?? "Não foi possível dar saída da peça.";
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao dar saída do material {Material}.", materialId);
            erro = "Não foi possível dar saída da peça agora; tente repetir.";
        }
        await ReporLugarAsync(locs, refPeca, local, qtt, iniciais, log);
        try { await pecas.LigarPecaAsync(materialId, refPeca, iniciais, CancellationToken.None); }
        catch (Exception ex) { log.LogError(ex, "Não foi possível ligar a peça ao material {Material}.", materialId); }
        return (false, erro, null, null);
    }

    // Repõe o que a anulação desfez (a saída com o seu lugar, ou só a ligação quando estava por dar). Devolve false se não conseguiu.
    private static async Task<bool> ReporSaidaAsync(IRepositorioPecas pecas, IRepositorioLocalizacoes locs, string fichaId, MaterialDto mat, string iniciais, ILogger log)
    {
        try
        {
            if (mat.SaidaDada)
            {
                var s = await TentarSaidaAsync(pecas, locs, fichaId, mat.Id, mat.Ref, mat.Qtt, LerLugar(mat.Local), null, iniciais, log);
                if (s.Ok) return true;
                log.LogError("Não foi possível repor a saída do material {Material}: {Erro}", mat.Id, s.Erro);
                return false;
            }
            if (mat.Ref != "") await pecas.LigarPecaAsync(mat.Id, mat.Ref, iniciais, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Não foi possível repor a saída do material {Material}.", mat.Id);
            return false;
        }
    }

    private static async Task AbrirConferenciaSeguraAsync(IRepositorioPecas pecas, string fichaId, string iniciais, ILogger log)
    {
        try
        {
            var r = await pecas.AbrirConferenciaAsync(fichaId, iniciais, CancellationToken.None);
            if (!r.Ok) log.LogError("Não foi possível abrir a conferência da ficha {Ficha}: {Erro}", fichaId, r.Erro);
        }
        catch (Exception ex) { log.LogError(ex, "Não foi possível abrir a conferência da ficha {Ficha}.", fichaId); }
    }

    // null = confirmada (ou nada a confirmar); senão o aviso para a resposta.
    private static async Task<string?> ConfirmarSeguraAsync(IRepositorioPecas pecas, string fichaId, string iniciais, ILogger log)
    {
        try
        {
            var r = await pecas.ConfirmarConferenciaAsync(fichaId, iniciais, CancellationToken.None);
            if (r.Ok) return null;
            log.LogError("Não foi possível confirmar a conferência da ficha {Ficha}: {Erro}", fichaId, r.Erro);
        }
        catch (Exception ex) { log.LogError(ex, "Não foi possível confirmar a conferência da ficha {Ficha}.", fichaId); }
        return AvisoPorConfirmar;
    }

    // Ficha cancelada: lê o lugar de cada saída, apaga a conferência e só então repõe os lugares (se a conferência não saiu, as saídas continuam e os lugares ficam).
    private static async Task LimparConferenciaAsync(IRepositorioPecas pecas, IRepositorioLocalizacoes locs, string fichaId, IReadOnlyList<MaterialDto> materiais, string iniciais, ILogger log)
    {
        try
        {
            var saidas = (await pecas.EnriquecerMateriaisAsync(materiais, CancellationToken.None)).Where(m => m.SaidaDada && m.Ref != "").ToList();
            var r = await pecas.CancelarConferenciaAsync(fichaId, iniciais, CancellationToken.None);
            if (!r.Ok)
            {
                log.LogError("Não foi possível cancelar a conferência da ficha {Ficha}: {Erro}", fichaId, r.Erro);
                return;
            }
            foreach (var m in saidas) await ReporLugarAsync(locs, m.Ref, m.Local, m.Qtt, iniciais, log);
        }
        catch (Exception ex) { log.LogError(ex, "Não foi possível cancelar a conferência da ficha {Ficha}.", fichaId); }
    }
}
