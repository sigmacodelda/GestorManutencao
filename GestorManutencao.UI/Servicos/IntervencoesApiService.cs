using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;

namespace GestorManutencao.UI.Servicos;

// Intervenções e equipamentos através da API; o token vem da sessão atual.
public sealed class IntervencoesApiService(HttpClient http, SessaoService sessao) : IIntervencoesRemoto
{
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

    public async Task<Material> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/materiais", new { design, qtt, epv }, ct);
        await VerificarAsync(resposta, ct);
        return await resposta.Content.ReadFromJsonAsync<Material>(ct) ?? throw new HttpRequestException();
    }

    public async Task ApagarMaterialAsync(string id, string materialId, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Delete, $"intervencoes/{Uri.EscapeDataString(id)}/materiais/{Uri.EscapeDataString(materialId)}", null, ct);
        if (resposta.StatusCode != HttpStatusCode.NotFound) await VerificarAsync(resposta, ct);
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
        if (resposta.StatusCode == HttpStatusCode.Conflict) throw new InvalidOperationException(MensagensIntervencao.Para(await LerErroAsync(resposta, ct)));
        resposta.EnsureSuccessStatusCode();
    }

    private sealed record CorpoErro(string? Erro);

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
