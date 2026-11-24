using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;

namespace GestorManutencao.UI.Servicos;

// Intervenções e equipamentos através da API; o token vem da sessão atual.
public sealed class IntervencoesApiService(HttpClient http, SessaoService sessao) : IIntervencoesRemoto, IDispositivosRemoto
{
    public Task RegistarAsync(string jwt, string token, CancellationToken ct = default) => DispositivoAsync(HttpMethod.Post, jwt, token, ct);

    public Task DesregistarAsync(string jwt, string token, CancellationToken ct = default) => DispositivoAsync(HttpMethod.Delete, jwt, token, ct);

    private async Task DispositivoAsync(HttpMethod metodo, string jwt, string token, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(metodo, "dispositivos")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", jwt) },
            Content = JsonContent.Create(new { token })
        };
        using var resposta = await http.SendAsync(pedido, ct);
        resposta.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, "intervencoes", null, ct);
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<Intervencao>>(ct) ?? [];
    }

    public async Task<Intervencao?> ObterAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/{Uri.EscapeDataString(id)}", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<Intervencao>(ct);
    }

    public Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, "intervencoes/avaria", new { equipamentoId, anomalia }, ct);

    // Repetir o mesmo opId devolve a mesma ficha: permite reenviar a avaria sem a duplicar.
    public Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, string opId, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, "intervencoes/avaria", new { equipamentoId, anomalia, opId }, ct);

    public async Task<IReadOnlyList<Intervencao>> ListarRecentesAsync(int dias, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/recentes?dias={dias}", null, ct);
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<Intervencao>>(ct) ?? [];
    }

    public Task<Intervencao> IniciarAsync(string id, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/iniciar", null, ct);

    public Task<Intervencao> ConcluirAsync(string id, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/concluir", new { feito, inicio, fim }, ct);

    public Task<Intervencao> AssinarExternaAsync(string id, string nome, string assinatura, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/assinatura-externa", new { nome, assinatura }, ct);

    public Task<Intervencao> PausarAsync(string id, CancellationToken ct = default) =>
        PausaAsync($"intervencoes/{Uri.EscapeDataString(id)}/pausar", MensagensIntervencao.NaoPausa, ct);

    public Task<Intervencao> RetomarAsync(string id, CancellationToken ct = default) =>
        PausaAsync($"intervencoes/{Uri.EscapeDataString(id)}/retomar", MensagensIntervencao.NaoRetoma, ct);

    // Recusa (404/409) tem mensagem própria, não a genérica de "já mudou".
    private async Task<Intervencao> PausaAsync(string url, string recusada, CancellationToken ct)
    {
        try { return await AlterarAsync(HttpMethod.Post, url, null, ct); }
        catch (InvalidOperationException e) when (e.Message == MensagensIntervencao.Mudou) { throw new InvalidOperationException(recusada); }
    }

    public Task<Intervencao> DevolverAsync(string id, string motivo, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, $"validacao/{Uri.EscapeDataString(id)}/devolver", new { motivo }, ct);

    public async Task<IReadOnlyList<Intervencao>> ListarPlaneadasAsync(CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, "planeadas", null, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<List<Intervencao>>(ct) ?? [];
    }

    public Task<Intervencao> AtribuirAsync(string id, int tecno, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Put, $"planeadas/{Uri.EscapeDataString(id)}/tecnico", new { tecno }, ct);

    // 404 com "tecnico_invalido": um dos técnicos já não existe ou está inativo (diferente de a ficha não existir).
    public async Task<Intervencao> DefinirAdicionaisAsync(string id, IReadOnlyList<int> tecnos, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Put, $"planeadas/{Uri.EscapeDataString(id)}/tecnicos", new { tecnos }, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(await LerErroAsync(resposta, ct) == "tecnico_invalido"
                ? "Um dos técnicos já não está disponível. Volte a abrir a lista."
                : MensagensIntervencao.Mudou);
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<Intervencao>(ct) ?? throw new HttpRequestException();
    }

    public async Task<RelatorioGeracao> GerarPlaneadasAsync(CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Post, "planeadas/gerar", null, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<RelatorioGeracao>(ct) ?? throw new HttpRequestException();
    }

    public async Task<IReadOnlyList<Material>> ListarMateriaisAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/{Uri.EscapeDataString(id)}/materiais", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return [];
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<Material>>(ct) ?? [];
    }

    public async Task<Material> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, string refPeca = "", string localPeca = "", CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/materiais", CorpoMaterial(design, qtt, epv, refPeca, localPeca), ct);
        await VerificarEscolhaLugarAsync(resposta, ct);
        // Com peça, o 400 traz a explicação (peça inexistente, quantidade inválida).
        if (resposta.StatusCode == HttpStatusCode.BadRequest && refPeca != "")
            throw new InvalidOperationException(await LerMensagemAsync(resposta, ct) ?? MensagensIntervencao.Para("pedido_invalido"));
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<Material>(ct) ?? throw new HttpRequestException();
    }

    // Local vazio: sem corpo, como antes (a API usa a peça já ligada ao material).
    public Task<Material> RepetirSaidaAsync(string id, string materialId, string localPeca = "", CancellationToken ct = default) =>
        MaterialJsonAsync(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/materiais/{Uri.EscapeDataString(materialId)}/saida", string.IsNullOrWhiteSpace(localPeca) ? null : new { local = localPeca.Trim() }, ct);

    // 204 = lugar reposto; 200 {avisoStock} = o material foi apagado mas a peça saiu sem repor o lugar.
    public async Task<string?> ApagarMaterialAsync(string id, string materialId, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Delete, $"intervencoes/{Uri.EscapeDataString(id)}/materiais/{Uri.EscapeDataString(materialId)}", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        await VerificarAsync(resposta, ct);
        if (resposta.StatusCode != HttpStatusCode.OK) return null;
        try { return (await resposta.Content.ReadFromJsonAsync<CorpoAviso>(ct))?.AvisoStock; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private sealed record CorpoAviso(string? AvisoStock);

    private static object CorpoMaterial(string design, decimal qtt, decimal epv, string? refPeca, string localPeca)
    {
        var campos = new Dictionary<string, object> { ["design"] = design, ["qtt"] = qtt, ["epv"] = epv };
        if (refPeca is not null) campos["ref"] = refPeca;
        if (!string.IsNullOrWhiteSpace(localPeca)) campos["local"] = localPeca.Trim();
        return campos;
    }

    private async Task<Material> MaterialJsonAsync(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(metodo, url, corpo, ct);
        await VerificarEscolhaLugarAsync(resposta, ct);
        await VerificarNovoAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<Material>(ct) ?? throw new HttpRequestException();
    }

    private sealed record CorpoEscolha(string? Erro, List<Lugar>? Lugares);

    // 409 escolha_o_lugar traz a lista dos lugares com stock: o utilizador escolhe um e repete.
    private static async Task VerificarEscolhaLugarAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        if (resposta.StatusCode != HttpStatusCode.Conflict) return;
        // Lê-se como texto (e não como fluxo): o corpo volta a ser lido a seguir se não for esta escolha (mensagem do 409 normal).
        CorpoEscolha? corpo;
        try { corpo = System.Text.Json.JsonSerializer.Deserialize<CorpoEscolha>(await resposta.Content.ReadAsStringAsync(ct), System.Text.Json.JsonSerializerOptions.Web); }
        catch (System.Text.Json.JsonException) { return; }
        if (corpo is { Erro: "escolha_o_lugar", Lugares: { Count: > 0 } lugares }) throw new EscolhaLugarException(lugares);
    }

    public async Task<IReadOnlyList<Foto>> ListarFotosAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/{Uri.EscapeDataString(id)}/fotos", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return [];
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<Foto>>(ct) ?? [];
    }

    public async Task<byte[]?> ObterFotoAsync(string id, string fotoId, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/{Uri.EscapeDataString(id)}/fotos/{Uri.EscapeDataString(fotoId)}", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<Foto> AdicionarFotoAsync(string id, string nome, byte[] jpeg, CancellationToken ct = default)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/fotos?nome={Uri.EscapeDataString(nome)}")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", sessao.Utilizador?.Token) },
            Content = new ByteArrayContent(jpeg) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }
        };
        using var resposta = await http.SendAsync(pedido, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException(MensagensIntervencao.Mudou);
        if (resposta.StatusCode == HttpStatusCode.RequestEntityTooLarge) throw new InvalidOperationException("A foto é demasiado grande. Tente outra vez.");
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<Foto>(ct) ?? throw new HttpRequestException();
    }

    public async Task ApagarFotoAsync(string id, string fotoId, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Delete, $"intervencoes/{Uri.EscapeDataString(id)}/fotos/{Uri.EscapeDataString(fotoId)}", null, ct);
        if (resposta.StatusCode != HttpStatusCode.NotFound) await VerificarAsync(resposta, ct);
    }

    public async Task<IReadOnlyList<Intervencao>> ListarPorValidarAsync(CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, "validacao", null, ct);
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<Intervencao>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<Intervencao>> ListarPorConcluirAsync(CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, "qualidade", null, ct);
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<Intervencao>>(ct) ?? [];
    }

    public Task<Intervencao> ConcluirQualidadeAsync(string id, string higienizacao, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, $"qualidade/{Uri.EscapeDataString(id)}/concluir", new { higienizacao }, ct);

    public Task<Intervencao> ValidarAsync(string id, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, $"validacao/{Uri.EscapeDataString(id)}", null, ct);

    public async Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"equipamentos?leve=true&q={Uri.EscapeDataString(texto)}", null, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<List<Equipamento>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<Equipamento>> ListarAsync(FiltroEquipamentos filtro, CancellationToken ct = default)
    {
        var url = $"equipamentos?q={Uri.EscapeDataString(filtro.Texto ?? "")}&tipo={Uri.EscapeDataString(filtro.Tipo ?? "")}&local={Uri.EscapeDataString(filtro.Local ?? "")}&obsoletos={(filtro.Obsoletos ? "true" : "false")}";
        using var resposta = await EnviarAsync(HttpMethod.Get, url, null, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<List<Equipamento>>(ct) ?? [];
    }

    public async Task<FichaEquipamento?> ObterFichaAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"equipamentos/{Uri.EscapeDataString(id)}", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<FichaEquipamento>(ct);
    }

    public async Task<PaginaFichas> ListarTodasAsync(FiltroFichas filtro, int pagina, CancellationToken ct = default)
    {
        var partes = new List<string> { $"pagina={pagina}" };
        if (filtro.Estado is { } estado) partes.Add($"estado={estado}");
        if (filtro.Desde is { } desde) partes.Add($"desde={desde:yyyy-MM-dd}");
        if (filtro.Ate is { } ate) partes.Add($"ate={ate:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(filtro.Texto)) partes.Add($"q={Uri.EscapeDataString(filtro.Texto.Trim())}");
        if (filtro.Tecno > 0) partes.Add($"tecno={filtro.Tecno}");
        if (filtro.Situacao != SituacaoFichas.Todas) partes.Add($"situacao={filtro.Situacao}");
        if (filtro.SoPorSupervisionar) partes.Add("porSupervisionar=true");
        if (filtro.Tipo is { } tipo) partes.Add($"tipo={tipo}");
        if (filtro.Ordem != OrdemFichas.Data) partes.Add($"ordem={filtro.Ordem}");
        if (!filtro.Descendente) partes.Add("desc=false");
        using var resposta = await EnviarAsync(HttpMethod.Get, "intervencoes/todas?" + string.Join('&', partes), null, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<PaginaFichas>(ct) ?? new PaginaFichas([], false);
    }

    public async Task<ConfiguracaoManutencao> ObterConfiguracaoAsync(CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, "configuracao", null, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<ConfiguracaoManutencao>(ct) ?? throw new HttpRequestException();
    }

    // 400: o servidor explica o que está mal no campo "mensagem".
    public async Task<ConfiguracaoManutencao> GuardarConfiguracaoAsync(ConfiguracaoManutencao configuracao, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Put, "configuracao", configuracao, ct);
        if (resposta.StatusCode == HttpStatusCode.BadRequest)
            throw new InvalidOperationException(await LerMensagemAsync(resposta, ct) ?? "Valores inválidos. Reveja a configuração.");
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<ConfiguracaoManutencao>(ct) ?? throw new HttpRequestException();
    }

    // ---- Ficha completa e ações novas: 404 = a ficha mudou (ou o técnico já não existe), 400 traz a mensagem do servidor, 403 = sem permissão. ----

    public async Task<FichaDetalhe?> ObterDetalheAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/{Uri.EscapeDataString(id)}/detalhe", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        await VerificarNovoAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<FichaDetalhe>(ct);
    }

    public Task<FichaDetalhe> GuardarFichaAsync(string id, EdicaoFicha edicao, CancellationToken ct = default) =>
        JsonAsync<FichaDetalhe>(HttpMethod.Put, $"intervencoes/{Uri.EscapeDataString(id)}", edicao, ct);

    public Task<Intervencao> CancelarAsync(string id, string motivo, CancellationToken ct = default) =>
        JsonAsync<Intervencao>(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/cancelar", new { motivo }, ct);

    public Task<Intervencao> CriarFichaManualAsync(NovaFicha ficha, CancellationToken ct = default) =>
        JsonAsync<Intervencao>(HttpMethod.Post, "intervencoes", ficha.OpId is null ? ficha with { OpId = Guid.NewGuid().ToString("N")[..22] } : ficha, ct);

    public Task<FichaDetalhe> FecharFichaAntigaAsync(string id, bool supervisao, bool fechada, CancellationToken ct = default) =>
        JsonAsync<FichaDetalhe>(HttpMethod.Put, $"intervencoes/{Uri.EscapeDataString(id)}/antiga", new { supervisao, fechada }, ct);

    // refPeca nulo = não se envia "ref" e a API mantém a peça e o lugar; "" desliga a peça (só quando a pessoa a tirou).
    public Task<Material> EditarMaterialAsync(string id, string materialId, string design, decimal qtt, decimal epv, string? refPeca = null, string localPeca = "", CancellationToken ct = default) =>
        MaterialJsonAsync(HttpMethod.Put, $"intervencoes/{Uri.EscapeDataString(id)}/materiais/{Uri.EscapeDataString(materialId)}", CorpoMaterial(design, qtt, epv, refPeca, localPeca), ct);

    public async Task<IReadOnlyList<TempoMaoDeObra>> ListarMaoDeObraAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/{Uri.EscapeDataString(id)}/mao-de-obra", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return [];
        await VerificarNovoAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<TempoMaoDeObra>>(ct) ?? [];
    }

    public Task<TempoMaoDeObra> AdicionarMaoDeObraAsync(string id, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default) =>
        JsonAsync<TempoMaoDeObra>(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/mao-de-obra", new { tecno, horas, custoHora }, ct);

    public Task<TempoMaoDeObra> EditarMaoDeObraAsync(string id, string tempoId, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default) =>
        JsonAsync<TempoMaoDeObra>(HttpMethod.Put, $"intervencoes/{Uri.EscapeDataString(id)}/mao-de-obra/{Uri.EscapeDataString(tempoId)}", new { tecno, horas, custoHora }, ct);

    public async Task ApagarMaoDeObraAsync(string id, string tempoId, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Delete, $"intervencoes/{Uri.EscapeDataString(id)}/mao-de-obra/{Uri.EscapeDataString(tempoId)}", null, ct);
        if (resposta.StatusCode != HttpStatusCode.NotFound) await VerificarNovoAsync(resposta, ct);
    }

    public Task<decimal> CustoHoraSugeridoAsync(int tecno, CancellationToken ct = default) =>
        JsonAsync<decimal>(HttpMethod.Get, $"tecnicos/{tecno}/custo-hora", null, ct);

    public async Task<IReadOnlyList<CompraLigada>> ListarComprasAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/{Uri.EscapeDataString(id)}/compras", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return [];
        await VerificarNovoAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<CompraLigada>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"fornecedores?q={Uri.EscapeDataString(texto ?? "")}", null, ct);
        await VerificarNovoAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<List<Fornecedor>>(ct) ?? [];
    }

    public Task<ContadoresMenu> ObterContadoresAsync(CancellationToken ct = default) =>
        JsonAsync<ContadoresMenu>(HttpMethod.Get, "intervencoes/contadores", null, ct);

    private async Task<T> JsonAsync<T>(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(metodo, url, corpo, ct);
        await VerificarNovoAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<T>(ct) ?? throw new HttpRequestException();
    }

    private static async Task VerificarNovoAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        switch (resposta.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new InvalidOperationException(await LerErroAsync(resposta, ct) == "tecnico_invalido" ? "Esse técnico já não está disponível." : MensagensIntervencao.Mudou);
            case HttpStatusCode.BadRequest:
                throw new InvalidOperationException(await LerMensagemAsync(resposta, ct) ?? MensagensIntervencao.Para("pedido_invalido"));
            case HttpStatusCode.Forbidden when await LerErroAsync(resposta, ct) != "sem_tecno":
                throw new InvalidOperationException(MensagensIntervencao.Para("sem_permissao"));
        }
        await VerificarAsync(resposta, ct);
    }

    private sealed record CorpoMensagem(string? Mensagem);

    private static async Task<string?> LerMensagemAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try { return (await resposta.Content.ReadFromJsonAsync<CorpoMensagem>(ct))?.Mensagem; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // 403 "sem_tecno": ainda não tem o número de técnico do PHC. Outros erros viram excepções genéricas.
    private static async Task VerificarAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        if (resposta.StatusCode == HttpStatusCode.Forbidden && (await LerErroAsync(resposta, ct)) == "sem_tecno") throw new SemNumeroTecnicoException();
        if (resposta.StatusCode == HttpStatusCode.Conflict)
        {
            // Os códigos não têm espaços; um texto com espaços é a explicação da API (ex.: falha na saída de stock).
            // "pecas_por_dar" (validar com peças sem saída) traz a explicação em "mensagem".
            var corpo = await LerConflitoAsync(resposta, ct);
            var erro = corpo?.Erro;
            throw new InvalidOperationException(erro is { } e && e.Contains(' ') ? e
                : erro == "sem_stock" ? MensagensIntervencao.SemStock(corpo?.Pecas)
                : erro == "pecas_por_dar" && !string.IsNullOrWhiteSpace(corpo?.Mensagem) ? corpo.Mensagem
                : MensagensIntervencao.Para(erro));
        }
        resposta.EnsureSuccessStatusCode();
    }

    private sealed record CorpoErro(string? Erro);

    private sealed record CorpoConflito(string? Erro, string? Mensagem, List<string>? Pecas = null);

    private static async Task<CorpoConflito?> LerConflitoAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try { return await resposta.Content.ReadFromJsonAsync<CorpoConflito>(ct); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static async Task<string?> LerErroAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try { return (await resposta.Content.ReadFromJsonAsync<CorpoErro>(ct))?.Erro; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // 404 e 409 querem dizer que a ficha já mudou ou não é desta pessoa; o 409 traz o motivo.
    private async Task<Intervencao> AlterarAsync(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(metodo, url, corpo, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException(MensagensIntervencao.Mudou);
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<Intervencao>(ct) ?? throw new HttpRequestException();
    }

    private Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        var pedido = new HttpRequestMessage(metodo, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", sessao.Utilizador?.Token) },
            Content = corpo is null ? null : JsonContent.Create(corpo)
        };
        return http.SendAsync(pedido, ct);
    }
}
