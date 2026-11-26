namespace GestorManutencao.Api;

// Conferência das peças da ficha: lugar de onde sai cada peça, saída em conferência e as falhas parciais.
// Regra comum: nada disto desfaz o estado da ficha; o que falha fica "por dar" ou "por confirmar" e repete-se.
// A API não mexe no saldo dos lugares (u_pecaloc): a saída só grava o lugar na linha bi e o trigger desce o saldo quando a ficha é validada.
public static partial class EndpointsIntervencoes
{
    private const string ErroEscolhaLugar = "escolha_o_lugar";
    private const string ErroLocalInvalido = "Indique o local completo (corredor, estante, nível e posição), por exemplo A-02-1-01.";
    private const string AvisoSemLugar = "Peça sem localização: saiu sem descontar de nenhum lugar.";
    private const string AvisoPorConfirmar = "Conferência por confirmar";
    private const string AvisoPorCancelar = "Conferência por cancelar";
    private const string AvisoLugaresSemFormato = "Os lugares da peça não têm um formato reconhecido: saiu sem descontar de nenhum lugar.";

    // Lugar, aviso (peça sem lugar) ou erro de resposta (400 lugar inválido, 409 escolha_o_lugar).
    private sealed record LugarResolvido(LugarDto? Lugar, string? Aviso, IResult? Erro);

    private static string? Juntar(string? a, string? b) =>
        string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrEmpty(x))) is { Length: > 0 } t ? t : null;

    // Pedido com Local: vale esse (400 se inválido ou sem peça). Sem Local: o lugar anterior da saída, se vier; senão o único lugar da peça com stock disponível
    // (ou, se todo o stock estiver reservado em conferência, o único lugar reservado: o aviso de disponível sai depois);
    // vários = 409 escolha_o_lugar com a lista; nenhum (ou tabela em falta) = grava e avisa.
    private static async Task<LugarResolvido> ResolverLugarAsync(IRepositorioLocalizacoes locs, string refPeca, string? pedido, string? anterior, ILogger log, CancellationToken ct)
    {
        pedido = (pedido ?? "").Trim();
        if (refPeca == "")
            return pedido == "" ? new(null, null, null) : new(null, null, PedidoInvalido("Indique a peça a que o local corresponde."));
        if (pedido != "")
            return RegrasLocalizacao.LerLugar(pedido) is { } p ? new(p, null, null) : new(null, null, PedidoInvalido(ErroLocalInvalido));
        if (RegrasLocalizacao.LerLugar(anterior) is { } ant) return new(ant, null, null);
        try
        {
            if (await locs.VerificarTabelaAsync(ct) != "") return new(null, AvisoSemLugar, null);
            var todos = (await locs.ListarAsync(refPeca, ct)).Lugares;
            var comStock = todos.Where(l => l.Qtt > 0).ToList();
            if (comStock.Count == 0) comStock = todos.Where(l => l.Reservado > 0).ToList();
            // Só se propõe um lugar que o trigger sabe ler: o texto tem de passar por LerLugar.
            var usaveis = comStock.Where(l => RegrasLocalizacao.LerLugar(l.Texto) is not null).ToList();
            return usaveis.Count switch
            {
                1 => new(RegrasLocalizacao.LerLugar(usaveis[0].Texto), null, null),
                0 => new(null, comStock.Count == 0 ? AvisoSemLugar : AvisoLugaresSemFormato, null),
                _ => new(null, null, Results.Json(new { erro = ErroEscolhaLugar, lugares = usaveis }, statusCode: StatusCodes.Status409Conflict))
            };
        }
        catch (RepositorioIndisponivelException ex)
        {
            log.LogError(ex, "Não foi possível consultar os lugares da peça {Peca}.", refPeca);
            return new(null, "Não foi possível consultar os lugares da peça: saiu sem descontar de nenhum lugar.", null);
        }
    }

    // Avisa se o lugar escolhido não tem disponível (saldo menos o reservado) para a quantidade; só avisa, a saída dá-se na mesma.
    private static async Task<string?> AvisoDisponivelAsync(IRepositorioLocalizacoes locs, string refPeca, LugarDto lugar, decimal qtt, ILogger log)
    {
        try
        {
            var l = (await locs.ListarAsync(refPeca, CancellationToken.None)).Lugares.FirstOrDefault(x => RegrasLocalizacao.MesmoLugar(x, lugar));
            var disponivel = l?.Qtt ?? 0;
            return disponivel >= qtt ? null : $"O lugar {lugar.Texto} só tem {disponivel:0.###} disponível: confirme o lugar da peça.";
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Não foi possível consultar a quantidade da peça {Peca} no lugar {Lugar}.", refPeca, lugar.Texto);
            return $"Não foi possível confirmar a quantidade disponível no lugar {lugar.Texto}.";
        }
    }

    private static async Task LigarSeguraAsync(IRepositorioPecas pecas, string materialId, string refPeca, string iniciais, ILogger log)
    {
        try { await pecas.LigarPecaAsync(materialId, refPeca, iniciais, CancellationToken.None); }
        catch (Exception ex) { log.LogError(ex, "Não foi possível ligar a peça ao material {Material}.", materialId); }
    }

    // Dá a saída em conferência com o lugar escolhido (sem token do pedido: o material já está gravado). Sem disponível no lugar sai na mesma, com aviso.
    // Se a saída falhar liga a peça ao material (u_matinterv.ref), para Ref preenchida e SaidaDada = false significar "peça por dar".
    private static async Task<(bool Ok, string? Erro, string? Aviso, string? Local)> TentarSaidaAsync(IRepositorioPecas pecas, IRepositorioLocalizacoes locs,
        string fichaId, string materialId, string refPeca, decimal qtt, LugarDto? lugar, string? avisoLugar, string iniciais, ILogger log)
    {
        string? avisoStock = null, aviso = avisoLugar;
        var local = lugar?.Texto;
        try
        {
            if (await pecas.ObterPorCodigoAsync(refPeca, CancellationToken.None) is { } peca) avisoStock = RegrasPecas.AvisoStock(qtt, peca.Disponivel);
        }
        catch (Exception ex) { log.LogError(ex, "Não foi possível ler o stock da peça {Peca}.", refPeca); }
        if (lugar is not null) aviso = Juntar(aviso, await AvisoDisponivelAsync(locs, refPeca, lugar, qtt, log));

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
        await LigarSeguraAsync(pecas, materialId, refPeca, iniciais, log);
        return (false, erro, avisoLugar, null);
    }

    // Repõe o que a anulação desfez (a saída com o seu lugar, ou só a ligação quando estava por dar). Devolve false se não conseguiu.
    private static async Task<bool> ReporSaidaAsync(IRepositorioPecas pecas, IRepositorioLocalizacoes locs, string fichaId, MaterialDto mat, string iniciais, ILogger log)
    {
        try
        {
            if (mat.SaidaDada)
            {
                var s = await TentarSaidaAsync(pecas, locs, fichaId, mat.Id, mat.Ref, mat.Qtt, RegrasLocalizacao.LerLugar(mat.Local), null, iniciais, log);
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

    // Materiais do detalhe com a peça, a saída e o lugar (o repositório de fichas só conhece o material).
    private static async Task<FichaDetalheDto> ComPecasAsync(FichaDetalheDto d, IRepositorioPecas pecas, CancellationToken ct) =>
        d.Materiais.Count == 0 ? d : d with { Materiais = await pecas.EnriquecerMateriaisAsync(d.Materiais, ct) };

    // Ficha validada com o dossier ainda em conferência = "por confirmar"; cancelada com dossier ou peças ligadas por limpar = "por cancelar".
    // Os materiais de d já vêm enriquecidos. Só informa: se não der para ler, não há aviso (a ficha abre na mesma).
    private static async Task<string?> AvisoConferenciaPendenteAsync(FichaDetalheDto d, IRepositorioPecas pecas, ILogger log, CancellationToken ct)
    {
        if (d.Antiga || d.Estado is not (EstadoIntervencao.Validada or EstadoIntervencao.Cancelada)) return null;
        try
        {
            var pendente = await pecas.ConferenciaPendenteAsync(d.Id, ct);
            if (d.Estado == EstadoIntervencao.Validada) return pendente ? AvisoPorConfirmar : null;
            return pendente || d.Materiais.Any(m => m.Ref != "") ? AvisoPorCancelar : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Não foi possível ver se a conferência da ficha {Ficha} ficou a meio.", d.Id);
            return null;
        }
    }

    // A ficha já validada, lida do detalhe, na forma que a resposta de validar sempre teve (o supervisor não passa por ObterAsync, que é do técnico).
    private static IntervencaoDto ComoIntervencao(FichaDetalheDto d, string? avisoStock) => new(
        d.Id, d.Equipamento, d.Zona, d.Tipo, d.Data, d.Estado,
        Anomalia: d.Problema, Feito: d.Resolucao,
        HoraInicio: d.HoraInicio is { } hi ? TimeOnly.FromDateTime(hi) : null, HoraFim: d.HoraFim is { } hf ? TimeOnly.FromDateTime(hf) : null,
        Tecnico: d.Tecnico, Tecno: d.Tecno, PodeValidar: false, Adicionais: d.Adicionais, Motivo: d.Motivo,
        Numero: d.Numero, Total: d.TotMo + d.TotRef, Fechada: d.Fechada, Antiga: d.Antiga, AvisoStock: avisoStock);

    // Ficha já cancelada com saídas ou peças ligadas por limpar (a limpeza anterior falhou): cancelar repete a limpeza em vez de recusar.
    // Sem nada por limpar mantém-se a recusa (409) de cancelar duas vezes. Se não der para ler, assume-se que há.
    private static async Task<bool> ConferenciaPorLimparAsync(IRepositorioPecas pecas, IReadOnlyList<MaterialDto> materiais, ILogger log)
    {
        try { return (await pecas.EnriquecerMateriaisAsync(materiais, CancellationToken.None)).Any(m => m.SaidaDada || m.Ref != ""); }
        catch (Exception ex)
        {
            log.LogError(ex, "Não foi possível ler as saídas da ficha cancelada.");
            return true;
        }
    }

    // Ficha cancelada: apaga a conferência e limpa a ligação das peças por dar. Os lugares não se repõem: o saldo só desce ao validar, por isso nada saiu.
    // null = tudo feito; senão o aviso para a resposta. Repetir é seguro.
    private static async Task<string?> LimparConferenciaAsync(IRepositorioPecas pecas, string fichaId, IReadOnlyList<MaterialDto> materiais, string iniciais, ILogger log)
    {
        try
        {
            var enriquecidos = await pecas.EnriquecerMateriaisAsync(materiais, CancellationToken.None);
            var porDar = enriquecidos.Where(m => !m.SaidaDada && m.Ref != "").ToList();
            var r = await pecas.CancelarConferenciaAsync(fichaId, iniciais, CancellationToken.None);
            if (!r.Ok)
            {
                log.LogError("Não foi possível cancelar a conferência da ficha {Ficha}: {Erro}", fichaId, r.Erro);
                return AvisoPorCancelar;
            }
            // Peças "por dar" não têm linha no dossier: a ligação limpa-se à parte (o cancelar repetido conta qualquer Ref, por isso tem de as limpar todas).
            var ligacoesLimpas = true;
            foreach (var m in porDar)
            {
                var a = await pecas.AnularSaidaAsync(m.Id, iniciais, CancellationToken.None);
                if (!a.Ok) { log.LogError("Não foi possível limpar a peça por dar do material {Material}: {Erro}", m.Id, a.Erro); ligacoesLimpas = false; }
            }
            return ligacoesLimpas ? null : AvisoPorCancelar;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Não foi possível cancelar a conferência da ficha {Ficha}.", fichaId);
            return AvisoPorCancelar;
        }
    }
}
