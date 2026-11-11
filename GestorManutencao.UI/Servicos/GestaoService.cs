using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;

namespace GestorManutencao.UI.Servicos;

// Gestão (só supervisor, só online, nunca na fila de saída). A mensagem das InvalidOperationException vem pronta para mostrar.
public interface IGestaoService
{
    Task<OpcoesEquipamento> ObterOpcoesAsync(CancellationToken ct = default);
    Task<EquipamentoGestao?> ObterEquipamentoAsync(string id, CancellationToken ct = default);
    // id null = criar (nasce com a instrução de manutenção vazia).
    Task<EquipamentoGestao> GravarEquipamentoAsync(string? id, EdicaoEquipamento edicao, CancellationToken ct = default);
    // Planeadas que ficam canceladas se o equipamento passar a obsoleto.
    Task<int> ContarPlaneadasAsync(string equipamentoId, CancellationToken ct = default);
    Task<IReadOnlyList<HistoricoEstado>> ListarHistoricoAsync(string equipamentoId, CancellationToken ct = default);
    Task<IReadOnlyList<InstrucaoManutencao>> ListarInstrucoesAsync(string equipamentoId, CancellationToken ct = default);
    // id null = nova versão do equipamento; marcar obsoleta cancela as planeadas das tarefas dessa versão.
    Task<InstrucaoManutencao> GravarInstrucaoAsync(string? equipamentoId, string? id, EdicaoInstrucao edicao, CancellationToken ct = default);
    Task<IReadOnlyList<TarefaManutencao>> ListarTarefasAsync(string instrucaoId, CancellationToken ct = default);
    // Gravar uma tarefa gera a planeada.
    Task<TarefaManutencao> GravarTarefaAsync(string? instrucaoId, string? id, EdicaoTarefa edicao, CancellationToken ct = default);
    Task ApagarTarefaAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Periodicidade>> ListarPeriodicidadesAsync(CancellationToken ct = default);
    Task<Periodicidade> GravarPeriodicidadeAsync(string id, EdicaoPeriodicidade edicao, CancellationToken ct = default);
    Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default);
}

public sealed class GestaoApiService(HttpClient http, SessaoService sessao) : IGestaoService
{
    private sealed record Corpo(string? Erro, string? Mensagem);
    private sealed record Contagem(int Planeadas);

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        var pedido = new HttpRequestMessage(metodo, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", sessao.Utilizador?.Token) },
            Content = corpo is null ? null : JsonContent.Create(corpo)
        };
        return await http.SendAsync(pedido, ct);
    }

    private static async Task<Corpo?> LerCorpoAsync(HttpResponseMessage r, CancellationToken ct)
    {
        try { return await r.Content.ReadFromJsonAsync<Corpo>(ct); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // 400 traz a mensagem do servidor; 404 e 409 são "mudou" ou o motivo (código); 403 é falta de permissão; 503 é o PHC indisponível.
    private static async Task VerificarAsync(HttpResponseMessage r, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode) return;
        var corpo = r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.Forbidden ? await LerCorpoAsync(r, ct) : null;
        switch (r.StatusCode)
        {
            case HttpStatusCode.BadRequest: throw new InvalidOperationException(corpo?.Mensagem ?? MensagensIntervencao.Para("pedido_invalido"));
            case HttpStatusCode.NotFound: throw new InvalidOperationException(MensagensIntervencao.Mudou);
            case HttpStatusCode.Conflict: throw new InvalidOperationException(MensagensIntervencao.Para(corpo?.Erro));
            case HttpStatusCode.Forbidden: throw new InvalidOperationException(MensagensIntervencao.Para("sem_permissao"));
            // Sessão terminada: o mesmo erro que o resto da app deixa passar (HttpRequestException 401), que ClassificacaoErros.SemSessao reconhece.
            case HttpStatusCode.Unauthorized: throw new HttpRequestException("A sessão terminou.", null, HttpStatusCode.Unauthorized);
            case HttpStatusCode.ServiceUnavailable: throw new InvalidOperationException("O PHC não está a responder. Tente outra vez daqui a pouco.");
        }
        r.EnsureSuccessStatusCode();
    }

    private async Task<T> JsonAsync<T>(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        using var r = await EnviarAsync(metodo, url, corpo, ct);
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<T>(ct) ?? throw new HttpRequestException();
    }

    private static string E(string id) => Uri.EscapeDataString(id);

    public Task<OpcoesEquipamento> ObterOpcoesAsync(CancellationToken ct = default) => JsonAsync<OpcoesEquipamento>(HttpMethod.Get, "gestao/opcoes", null, ct);

    public async Task<EquipamentoGestao?> ObterEquipamentoAsync(string id, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"gestao/equipamentos/{E(id)}", null, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<EquipamentoGestao>(ct);
    }

    public Task<EquipamentoGestao> GravarEquipamentoAsync(string? id, EdicaoEquipamento edicao, CancellationToken ct = default) =>
        id is null ? JsonAsync<EquipamentoGestao>(HttpMethod.Post, "gestao/equipamentos", edicao, ct) : JsonAsync<EquipamentoGestao>(HttpMethod.Put, $"gestao/equipamentos/{E(id)}", edicao, ct);

    public async Task<int> ContarPlaneadasAsync(string equipamentoId, CancellationToken ct = default) =>
        (await JsonAsync<Contagem>(HttpMethod.Get, $"gestao/equipamentos/{E(equipamentoId)}/planeadas-a-cancelar", null, ct)).Planeadas;

    public async Task<IReadOnlyList<HistoricoEstado>> ListarHistoricoAsync(string equipamentoId, CancellationToken ct = default) =>
        await JsonAsync<List<HistoricoEstado>>(HttpMethod.Get, $"gestao/equipamentos/{E(equipamentoId)}/historico-estado", null, ct);

    public async Task<IReadOnlyList<InstrucaoManutencao>> ListarInstrucoesAsync(string equipamentoId, CancellationToken ct = default) =>
        await JsonAsync<List<InstrucaoManutencao>>(HttpMethod.Get, $"gestao/equipamentos/{E(equipamentoId)}/instrucoes", null, ct);

    public Task<InstrucaoManutencao> GravarInstrucaoAsync(string? equipamentoId, string? id, EdicaoInstrucao edicao, CancellationToken ct = default) =>
        id is null ? JsonAsync<InstrucaoManutencao>(HttpMethod.Post, $"gestao/equipamentos/{E(equipamentoId!)}/instrucoes", edicao, ct)
                   : JsonAsync<InstrucaoManutencao>(HttpMethod.Put, $"gestao/instrucoes/{E(id)}", edicao, ct);

    public async Task<IReadOnlyList<TarefaManutencao>> ListarTarefasAsync(string instrucaoId, CancellationToken ct = default) =>
        await JsonAsync<List<TarefaManutencao>>(HttpMethod.Get, $"gestao/instrucoes/{E(instrucaoId)}/tarefas", null, ct);

    public Task<TarefaManutencao> GravarTarefaAsync(string? instrucaoId, string? id, EdicaoTarefa edicao, CancellationToken ct = default) =>
        id is null ? JsonAsync<TarefaManutencao>(HttpMethod.Post, $"gestao/instrucoes/{E(instrucaoId!)}/tarefas", edicao, ct)
                   : JsonAsync<TarefaManutencao>(HttpMethod.Put, $"gestao/tarefas/{E(id)}", edicao, ct);

    public async Task ApagarTarefaAsync(string id, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Delete, $"gestao/tarefas/{E(id)}", null, ct);
        await VerificarAsync(r, ct);
    }

    public async Task<IReadOnlyList<Periodicidade>> ListarPeriodicidadesAsync(CancellationToken ct = default) =>
        await JsonAsync<List<Periodicidade>>(HttpMethod.Get, "gestao/periodicidades", null, ct);

    public Task<Periodicidade> GravarPeriodicidadeAsync(string id, EdicaoPeriodicidade edicao, CancellationToken ct = default) =>
        JsonAsync<Periodicidade>(HttpMethod.Put, $"gestao/periodicidades/{E(id)}", edicao, ct);

    public async Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default) =>
        await JsonAsync<List<Fornecedor>>(HttpMethod.Get, $"fornecedores?q={Uri.EscapeDataString(texto ?? "")}", null, ct);
}

// Sem rede (ou com a rede a falhar a meio) a ação não se faz e diz-se porquê; a gestão nunca entra na fila de saída.
// aposEscrita corre depois de uma escrita aceite que pode ter cancelado planeadas (equipamento, instrução, tarefa): a app limpa os contadores e atualiza a cópia local das fichas.
public sealed class GestaoSoOnlineService(IGestaoService interno, IRedeEstado rede, Func<Task>? aposEscrita = null) : IGestaoService
{
    private async Task<T> Online<T>(Func<Task<T>> acao, bool escrita = false, CancellationToken ct = default, bool mexeEmPlaneadas = false)
    {
        if (!rede.Disponivel) throw new InvalidOperationException(MensagensIntervencao.SemRede);
        T resultado;
        try { resultado = await acao(); }
        catch (Exception e) when (ClassificacaoErros.FalhaDeRede(e, ct)) { throw new InvalidOperationException(escrita ? MensagensIntervencao.SemRedeAMeio : MensagensIntervencao.SemRede); }
        if (mexeEmPlaneadas && aposEscrita is not null)
            try { await aposEscrita(); } catch (Exception) { /* a escrita já foi aceite; os contadores e a cópia refrescam-se na próxima leitura */ }
        return resultado;
    }

    private Task<bool> Online(Func<Task> acao, bool escrita, CancellationToken ct, bool mexeEmPlaneadas = false) =>
        Online(async () => { await acao(); return true; }, escrita, ct, mexeEmPlaneadas);

    public Task<OpcoesEquipamento> ObterOpcoesAsync(CancellationToken ct = default) => Online(() => interno.ObterOpcoesAsync(ct), false, ct);
    public Task<EquipamentoGestao?> ObterEquipamentoAsync(string id, CancellationToken ct = default) => Online(() => interno.ObterEquipamentoAsync(id, ct), false, ct);
    public Task<EquipamentoGestao> GravarEquipamentoAsync(string? id, EdicaoEquipamento edicao, CancellationToken ct = default) => Online(() => interno.GravarEquipamentoAsync(id, edicao, ct), true, ct, true);
    public Task<int> ContarPlaneadasAsync(string equipamentoId, CancellationToken ct = default) => Online(() => interno.ContarPlaneadasAsync(equipamentoId, ct), false, ct);
    public Task<IReadOnlyList<HistoricoEstado>> ListarHistoricoAsync(string equipamentoId, CancellationToken ct = default) => Online(() => interno.ListarHistoricoAsync(equipamentoId, ct), false, ct);
    public Task<IReadOnlyList<InstrucaoManutencao>> ListarInstrucoesAsync(string equipamentoId, CancellationToken ct = default) => Online(() => interno.ListarInstrucoesAsync(equipamentoId, ct), false, ct);
    public Task<InstrucaoManutencao> GravarInstrucaoAsync(string? equipamentoId, string? id, EdicaoInstrucao edicao, CancellationToken ct = default) =>
        Online(() => interno.GravarInstrucaoAsync(equipamentoId, id, edicao, ct), true, ct, true);
    public Task<IReadOnlyList<TarefaManutencao>> ListarTarefasAsync(string instrucaoId, CancellationToken ct = default) => Online(() => interno.ListarTarefasAsync(instrucaoId, ct), false, ct);
    public Task<TarefaManutencao> GravarTarefaAsync(string? instrucaoId, string? id, EdicaoTarefa edicao, CancellationToken ct = default) =>
        Online(() => interno.GravarTarefaAsync(instrucaoId, id, edicao, ct), true, ct, true);
    public Task ApagarTarefaAsync(string id, CancellationToken ct = default) => Online(() => interno.ApagarTarefaAsync(id, ct), true, ct, true);
    public Task<IReadOnlyList<Periodicidade>> ListarPeriodicidadesAsync(CancellationToken ct = default) => Online(() => interno.ListarPeriodicidadesAsync(ct), false, ct);
    public Task<Periodicidade> GravarPeriodicidadeAsync(string id, EdicaoPeriodicidade edicao, CancellationToken ct = default) => Online(() => interno.GravarPeriodicidadeAsync(id, edicao, ct), true, ct);
    public Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default) => Online(() => interno.PesquisarFornecedoresAsync(texto, ct), false, ct);
}

// Texto para o utilizador a partir de uma falha da gestão: mensagens já prontas, sessão terminada (401) e o resto numa frase simples.
public static class MensagensGestao
{
    public static string Para(Exception e, string porOmissao) => e switch
    {
        InvalidOperationException i => i.Message,
        NotSupportedException => "Esta parte ainda não está disponível.",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "A sessão terminou. Entre outra vez.",
        _ => porOmissao
    };
}

// Só para desenvolvimento: dados fictícios em memória, com as regras principais (ref única, viaturas recusadas, histórico, obsoleto cancela planeadas).
public sealed class GestaoDevService : IGestaoService
{
    private readonly object _trinco = new();
    private readonly List<EquipamentoGestao> _equipamentos =
    [
        new("E1", 2, "CMP-02", "Compressor de ar 02", "Compressor", "Atlas Copco", "GA 30", "AC-77120", 2019, 12000m, 7000004072, "Frio Industrial, Lda", new DateOnly(2019, 3, 1), "Produção", "Em Serviço", false, "", "", "0"),
        new("E2", 5, "EMP-05", "Empilhador 05", "Empilhador", "Toyota", "8FBE15", "TY-30418", 2021, 25000m, 0, "", new DateOnly(2021, 6, 1), "Armazém", "Em Serviço", false, "", "", "0")
    ];
    private readonly Dictionary<string, List<HistoricoEstado>> _historico = [];
    private readonly List<InstrucaoManutencao> _instrucoes = [new("I-E1", "E1", "", new DateOnly(2019, 3, 1), false, "", 1, "0"), new("I-E2", "E2", "", new DateOnly(2021, 6, 1), false, "", 0, "0")];
    private readonly List<TarefaManutencao> _tarefas = [new("T-E1-1", "I-E1", "Mensal", "Equipa interna", "", "Verificar o nível de óleo e os filtros", "Interna", "0")];
    private readonly List<Periodicidade> _periodicidades = [new("P1", "Semanal", "dias", 7, false, "0"), new("P2", "Mensal", "meses", 1, false, "0"), new("P3", "Regular", "nenhum", 0, false, "0")];
    private int _planeadasE1 = 2;

    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    public Task<OpcoesEquipamento> ObterOpcoesAsync(CancellationToken ct = default)
    {
        lock (_trinco)
            return Task.FromResult(new OpcoesEquipamento(_equipamentos.Select(e => e.Tipo).Distinct().ToList(), _equipamentos.Select(e => e.Marca).Distinct().ToList(),
                _equipamentos.Select(e => e.Local).Distinct().ToList(), RegrasGestaoUi.Estados));
    }

    public Task<EquipamentoGestao?> ObterEquipamentoAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult(_equipamentos.FirstOrDefault(e => e.Id == id));
    }

    public Task<EquipamentoGestao> GravarEquipamentoAsync(string? id, EdicaoEquipamento e, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            var atual = id is null ? new EquipamentoGestao("", 0, "", "", "", "", "", "", 0, 0, 0, "", Hoje, "", "", false, "", "", "0") : _equipamentos.FirstOrDefault(x => x.Id == id)
                ?? throw new InvalidOperationException(MensagensIntervencao.Mudou);
            if (id is not null && e.Versao is { Length: > 0 } v && v != atual.Versao) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            var nova = atual with
            {
                Ref = e.Ref ?? atual.Ref, Design = e.Design ?? atual.Design, Tipo = e.Tipo ?? atual.Tipo, Marca = e.Marca ?? atual.Marca, Modelo = e.Modelo ?? atual.Modelo,
                Serie = e.Serie ?? atual.Serie, Ano = e.Ano ?? atual.Ano, Valor = e.Valor ?? atual.Valor, NoFornecedor = e.NoFornecedor ?? atual.NoFornecedor,
                Data = e.Data ?? atual.Data, Local = e.Local ?? atual.Local, Estado = e.Estado ?? (atual.Estado.Length > 0 ? atual.Estado : "Em Serviço"),
                Obsoleto = e.Obsoleto ?? atual.Obsoleto, ManualPdf = e.ManualPdf ?? atual.ManualPdf, Email = e.Email ?? atual.Email
            };
            if (nova.Ref.Trim().Length == 0 || nova.Design.Trim().Length == 0) throw new InvalidOperationException("Indique o nº e a designação.");
            if (_equipamentos.Any(x => x.Id != id && string.Equals(x.Ref, nova.Ref, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(MensagensIntervencao.Para("ref_duplicada"));
            if (nova.Obsoleto && !atual.Obsoleto)
            {
                if (e.PlaneadasConfirmadas != (id == "E1" ? _planeadasE1 : 0)) throw new InvalidOperationException(MensagensIntervencao.Para("planeadas_mudaram"));
                if (id == "E1") _planeadasE1 = 0;
            }
            var stamp = id ?? "EN" + (_equipamentos.Count + 1);
            nova = nova with { Id = stamp, NoInterno = id is null ? _equipamentos.Max(x => x.NoInterno) + 1 : atual.NoInterno, Versao = (int.Parse(atual.Versao) + 1).ToString() };
            if (id is null) { _equipamentos.Add(nova); _instrucoes.Add(new("I-" + stamp, stamp, "", Hoje, false, "", 0, "0")); }
            else _equipamentos[_equipamentos.FindIndex(x => x.Id == id)] = nova;
            if (nova.Estado != (id is null ? "" : atual.Estado))
            {
                if (!_historico.TryGetValue(stamp, out var h)) _historico[stamp] = h = [];
                h.Insert(0, new HistoricoEstado(Hoje, nova.Estado, "dev"));
            }
            return Task.FromResult(nova);
        }
    }

    public Task<int> ContarPlaneadasAsync(string equipamentoId, CancellationToken ct = default) => Task.FromResult(equipamentoId == "E1" ? _planeadasE1 : 0);

    public Task<IReadOnlyList<HistoricoEstado>> ListarHistoricoAsync(string equipamentoId, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<HistoricoEstado>>(_historico.GetValueOrDefault(equipamentoId)?.ToList() ?? []);
    }

    public Task<IReadOnlyList<InstrucaoManutencao>> ListarInstrucoesAsync(string equipamentoId, CancellationToken ct = default)
    {
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<InstrucaoManutencao>>(_instrucoes.Where(i => i.EquipamentoId == equipamentoId)
                .Select(i => i with { NumTarefas = _tarefas.Count(t => t.InstrucaoId == i.Id) }).OrderBy(i => i.Obsoleta).ThenByDescending(i => i.Data).ToList());
    }

    public Task<InstrucaoManutencao> GravarInstrucaoAsync(string? equipamentoId, string? id, EdicaoInstrucao e, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            if (id is null)
            {
                var nova = new InstrucaoManutencao(Guid.NewGuid().ToString("N")[..25], equipamentoId ?? "", (e.Versao ?? "").Trim(), e.Data ?? Hoje, e.Obsoleta ?? false, (e.Obs ?? "").Trim(), 0, "0");
                _instrucoes.Add(nova);
                return Task.FromResult(nova);
            }
            var pos = _instrucoes.FindIndex(i => i.Id == id);
            if (pos < 0) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            var a = _instrucoes[pos];
            if (e.Concorrencia is { Length: > 0 } t && t != a.Concorrencia) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            return Task.FromResult(_instrucoes[pos] = a with { Versao = e.Versao ?? a.Versao, Data = e.Data ?? a.Data, Obsoleta = e.Obsoleta ?? a.Obsoleta, Obs = e.Obs ?? a.Obs, Concorrencia = (int.Parse(a.Concorrencia) + 1).ToString() });
        }
    }

    public Task<IReadOnlyList<TarefaManutencao>> ListarTarefasAsync(string instrucaoId, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<TarefaManutencao>>(_tarefas.Where(t => t.InstrucaoId == instrucaoId).ToList());
    }

    public Task<TarefaManutencao> GravarTarefaAsync(string? instrucaoId, string? id, EdicaoTarefa e, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            var anterior = id is null ? null : _tarefas.FirstOrDefault(t => t.Id == id) ?? throw new InvalidOperationException(MensagensIntervencao.Mudou);
            if (anterior is not null && e.Concorrencia is { Length: > 0 } t && t != anterior.Concorrencia) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            var descricao = e.Descricao ?? anterior?.Descricao;
            if (string.IsNullOrWhiteSpace(descricao)) throw new InvalidOperationException("Descreva a tarefa.");
            if (!string.IsNullOrWhiteSpace(e.Periodo) && !_periodicidades.Any(p => string.Equals(p.Periodo, e.Periodo.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(MensagensIntervencao.Para("periodicidade_invalida"));
            var nova = new TarefaManutencao(id ?? Guid.NewGuid().ToString("N")[..25], anterior?.InstrucaoId ?? instrucaoId ?? "", (e.Periodo ?? anterior?.Periodo ?? "").Trim(),
                (e.Responsabilidade ?? anterior?.Responsabilidade ?? "").Trim(), (e.Responsavel ?? anterior?.Responsavel ?? "").Trim(), descricao.Trim(),
                e.TipoManut ?? anterior?.TipoManut ?? "", anterior is null ? "0" : (int.Parse(anterior.Concorrencia) + 1).ToString());
            if (anterior is null) _tarefas.Add(nova); else _tarefas[_tarefas.IndexOf(anterior)] = nova;
            return Task.FromResult(nova);
        }
    }

    public Task ApagarTarefaAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            if (id == "T-E1-1") throw new InvalidOperationException(MensagensIntervencao.Para("tarefa_com_fichas"));
            if (_tarefas.RemoveAll(t => t.Id == id) == 0) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<Periodicidade>> ListarPeriodicidadesAsync(CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<Periodicidade>>(_periodicidades.ToList());
    }

    public Task<Periodicidade> GravarPeriodicidadeAsync(string id, EdicaoPeriodicidade e, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            var pos = _periodicidades.FindIndex(p => p.Id == id);
            if (pos < 0) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            var a = _periodicidades[pos];
            if (e.Concorrencia is { Length: > 0 } t && t != a.Concorrencia) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            if (RegrasGestaoUi.ValidarPeriodicidade(e.Unidade ?? "", e.Intervalo) is { } erro) throw new InvalidOperationException(erro);
            var unidade = e.Unidade!.Trim().ToLowerInvariant();
            return Task.FromResult(_periodicidades[pos] = a with { Unidade = unidade, Intervalo = unidade == "nenhum" ? 0 : e.Intervalo ?? 0, Aviso = e.Aviso ?? a.Aviso, Concorrencia = (int.Parse(a.Concorrencia) + 1).ToString() });
        }
    }

    public Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Fornecedor>>((texto ?? "").Trim().Length < 2 || !"frio industrial".Contains(texto!.Trim(), StringComparison.OrdinalIgnoreCase) ? [] : [new Fornecedor(7000004072, "Frio Industrial, Lda")]);
}
