using System.Security.Claims;
using System.Text.Json.Serialization;

namespace GestorManutencao.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoIntervencao { Planeada, EmCurso, Concluida, Validada, Cancelada }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoIntervencao { Preventiva, Curativa }

// PodePausar: ficha em curso e o utilizador que consulta tem linha de tempo aberta (só então pausa/retoma); EmPausa é a pausa dessa linha (só vale com PodePausar).
// Última, Cancelada e DataPrevista só vêm nas planeadas. Tecno é o número de utilizador do PHC de quem tem a ficha (0 = sem dono). PodeValidar só conta na lista de validação.
// Motivo só vem nas fichas devolvidas pelo supervisor (em curso com observação gravada). Adicionais: técnicos além do principal (omissão = nenhum).
public sealed record IntervencaoDto(
    string Id, string Equipamento, string Zona, TipoIntervencao Tipo, DateOnly DataPlaneada, EstadoIntervencao Estado,
    string? Anomalia = null, string? Feito = null, TimeOnly? HoraInicio = null, TimeOnly? HoraFim = null,
    string? Tecnico = null, int Tecno = 0, bool PodeValidar = true,
    DateOnly? UltimaIntervencao = null, DateOnly? UltimaCancelada = null, DateOnly? DataPrevista = null,
    bool PodeAbrir = false, string? Motivo = null, IReadOnlyList<TecnicoFichaDto>? Adicionais = null,
    int Numero = 0, decimal Total = 0, bool Fechada = false, bool PorSupervisionar = false, bool ForaDeOrdem = false, bool Antiga = false, bool EmPausa = false, bool PodePausar = false);

// Técnico adicional da intervenção: o número vem do memo tecnos da ficha e o nome resolve-se na API (nunca fica gravado na ficha).
public sealed record TecnicoFichaDto(int Tecno, string Nome);

// Id, Nome e Zona bastam à pesquisa da avaria; o resto serve à lista e à ficha de equipamentos.
public sealed record EquipamentoDto(
    string Id, string Nome, string Zona, string Ref = "", int NoInterno = 0, string Tipo = "", string Marca = "", string Modelo = "", string Serie = "",
    DateOnly? UltimaIntervencao = null, int FichasAbertas = 0, bool ForaDeServico = false, bool Obsoleto = false);

public sealed record TarefaPlaneadaDto(string Id, string Descricao, string Periodicidade, DateOnly? UltimaIntervencao, DateOnly? ProximaData);

public sealed record FichaEquipamentoDto(EquipamentoDto Equipamento, IReadOnlyList<TarefaPlaneadaDto> Tarefas, IReadOnlyList<IntervencaoDto> Historico);

public sealed record FiltroEquipamentos(string Texto, string Tipo, string Local, bool Obsoletos);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SituacaoFichas { Abertas, Fechadas, Todas }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrdemFichas { Numero, Data, Equipamento, Tipo, Total }

// Abertas = não fechada e não cancelada; Fechadas = fechada; Todas = tudo (omissão, como antes). Sem ordem pedida: data, da mais recente para a mais antiga.
public sealed record FiltroFichas(EstadoIntervencao? Estado, DateOnly? Desde, DateOnly? Ate, string Texto, int Tecno, int Pagina,
    SituacaoFichas Situacao = SituacaoFichas.Todas, bool SoPorSupervisionar = false, TipoIntervencao? Tipo = null, OrdemFichas Ordem = OrdemFichas.Data, bool Descendente = true);

public sealed record PaginaFichasDto(IReadOnlyList<IntervencaoDto> Itens, bool TemMais);

public sealed record PedidoConfiguracao(int? Antecedencia, bool? GerarPlaneadas, bool? AvancaOrdem, bool? ValidarPropria, bool? ExtExigeDoc, bool? ComprasFichaInt);

public sealed record MaterialDto(string Id, string Design, decimal Qtt, decimal Epv, decimal Custo);

// Foto anexada à intervenção (tabela anexos do PHC); os bytes só vêm no pedido da própria foto.
public sealed record FotoDto(string Id, string Nome, int Bytes);

public sealed record FotoConteudo(string Nome, byte[] Dados);

// OpId (opcional) identifica a operação para a repetição do pedido não criar outra ficha; vai gravado no campo opid da ficha.
public sealed record PedidoAvaria(string? EquipamentoId, string? Anomalia, string? OpId = null);

public sealed record PedidoConclusao(string? Feito, TimeOnly? Inicio, TimeOnly? Fim);

public sealed record PedidoMaterial(string? Design, decimal? Qtt, decimal? Epv);

public sealed record PedidoDevolucao(string? Motivo);

public sealed record PedidoAtribuicao(int? Tecno);

public sealed record PedidoAdicionais(IReadOnlyList<int>? Tecnos);

public sealed record RelatorioGeracao(int Criadas, int JaTinham, int SemPeriodicidade, int Erros, int PorRegularizar = 0);

public enum ResultadoIntervencao { Ok, Conflito, NaoExiste, ForaDeOrdem, ExigeDocumento, Propria, LimiteFotos, TecnicoInvalido, SemPermissao, Invalido, PeriodicidadeInvalida, ProblemaLongo, RefDuplicada, PlaneadasMudaram, TarefaComFichas, InstrucaoObsoleta }

// O nome vai em tecnome (45); nas colunas de auditoria vai o código (30); a identidade da pessoa nas fichas é o Tecno.
public sealed record TecnicoAtual(string Codigo, int Tecno, string Nome);

public static class RegrasIntervencao
{
    public const int MaxAnomalia = 200, MaxFeito = 254;

    // Técnicos adicionais: memo tecnos de u_fichaint com os números entre ponto e vírgula (";12;45;", vazio = nenhum); nunca inclui o principal.
    public const string ColunaTecnos = "tecnos";
    public const int MaxTecnoPhc = 99999, MaxAdicionais = 50;

    // Tolerante a lixo: ignora o que não for número entre 1 e 99999; sem duplicados e ordenado.
    public static IReadOnlyList<int> LerTecnos(string? texto) =>
        (texto ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n is >= 1 and <= MaxTecnoPhc).Distinct().Order().ToList();

    public static string EscreverTecnos(IEnumerable<int> tecnos, int principal = 0)
    {
        var lista = tecnos.Where(n => n is >= 1 and <= MaxTecnoPhc && n != principal).Distinct().Order().ToList();
        return lista.Count == 0 ? "" : ";" + string.Join(';', lista) + ";";
    }

    public static bool EstaNaLista(string? texto, int tecno) => tecno > 0 && LerTecnos(texto).Contains(tecno);

    // Padrão do LIKE sobre o memo: ";12;" nunca apanha ";123;".
    public static string PadraoTecno(int tecno) => $"%;{tecno};%";

    // Principal ou adicional.
    public static bool PertenceAFicha(int principal, string? tecnos, int tecno) => tecno > 0 && (principal == tecno || EstaNaLista(tecnos, tecno));

    // Pode mexer na ficha: é do técnico (principal ou adicional) ou ainda não tem dono.
    public static bool PodeMexer(int principal, string? tecnos, int tecno) => principal == 0 || PertenceAFicha(principal, tecnos, tecno);

    public static IReadOnlyList<TecnicoFichaDto>? AdicionaisDe(string? tecnos, int principal) =>
        LerTecnos(tecnos).Where(n => n != principal).Select(n => new TecnicoFichaDto(n, "")).ToList() is { Count: > 0 } l ? l : null;

    public static bool Envolve(IntervencaoDto ficha, int tecno) =>
        tecno > 0 && (ficha.Tecno == tecno || ficha.Adicionais?.Any(a => a.Tecno == tecno) == true);

    // O OpId grava-se no campo opid da ficha (32). O marcador " [op:...]" no fim do problema é só das fichas antigas: lê-se e esconde-se, já não se grava.
    public const int MinOpId = 8, MaxOpId = 32;

    public const int MinDiasRecentes = 1, MaxDiasRecentes = 90, DiasRecentesOmissao = 30, MaxRecentes = 500;

    public static bool OpIdValido(string? opId) =>
        opId is not null && opId.Length is >= MinOpId and <= MaxOpId && opId.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9'));

    public static string Marcador(string opId) => " [op:" + opId + "]";

    // Tira o marcador do fim do texto lido da ficha.
    public static string SemMarcador(string texto)
    {
        var t = texto.TrimEnd();
        if (!t.EndsWith(']')) return texto;
        var i = t.LastIndexOf(" [op:", StringComparison.Ordinal);
        return i >= 0 && OpIdValido(t[(i + 5)..^1]) ? t[..i] : texto;
    }

    // O marcador (se houver) que está no fim do texto lido da ficha, com o espaço inicial; vazio se não houver.
    public static string ExtrairMarcador(string texto)
    {
        var semMarcador = SemMarcador(texto);
        return ReferenceEquals(semMarcador, texto) || semMarcador.Length == texto.Length ? "" : texto.TrimEnd()[semMarcador.Length..];
    }

    public static string? ValidarAvaria(string? equipamentoId, string? anomalia, string? opId = null) =>
        string.IsNullOrWhiteSpace(equipamentoId) || equipamentoId.Length > 25 ? "Equipamento em falta."
        : opId is not null && !OpIdValido(opId) ? "Identificador da operação inválido."
        : string.IsNullOrWhiteSpace(anomalia) || anomalia.Trim().Length > MaxAnomalia ? "Descrição em falta ou demasiado longa."
        : null;

    public static string? ValidarConclusao(string? feito, TimeOnly? inicio, TimeOnly? fim) =>
        string.IsNullOrWhiteSpace(feito) || feito.Trim().Length > MaxFeito ? "Descrição em falta ou demasiado longa."
        : inicio is null || fim is null || fim < inicio ? "Horas inválidas."
        : null;

    public static string? ValidarMaterial(string? design, decimal? qtt, decimal? epv) =>
        string.IsNullOrWhiteSpace(design) || design.Trim().Length > RegrasManutencao.TamanhoDesignMaterial ? "Material em falta ou com nome demasiado longo."
        : qtt is null or <= 0 or > 999_999 ? "Quantidade inválida."
        : epv is < 0 or > 999_999_999 ? "Preço inválido."
        : qtt * (epv ?? 0) > 9_999_999_999_999m ? "O valor total do material é demasiado grande."
        : null;

    // Fotos da intervenção: JPEG (a app comprime antes de enviar), no máximo 10 por ficha e 3 MB cada.
    public const int MaxFotos = 10, MaxBytesFoto = 3 * 1024 * 1024, TamanhoNomeFoto = 150;

    public static bool EhJpeg(byte[] dados) => dados.Length > 4 && dados[0] == 0xFF && dados[1] == 0xD8 && dados[2] == 0xFF;

    // O nome só guarda letras, números, ponto, hífen e sublinhado (sem caminhos); sem nome válido usa-se um por defeito.
    public static string NomeFoto(string? nome, DateTime agora)
    {
        var limpo = new string((nome ?? "").Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray()).Trim('.');
        if (limpo.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) limpo = limpo[..^4];
        if (limpo.Length == 0) limpo = $"foto_{agora:yyyyMMdd_HHmmss}";
        return limpo.Length > TamanhoNomeFoto - 4 ? limpo[..(TamanhoNomeFoto - 4)] : limpo;
    }
}

// Tudo é pedido em nome de um técnico (número do PHC); um técnico só mexe no que é seu ou ainda não tem dono.
public interface IRepositorioIntervencoes
{
    Task<IReadOnlyList<IntervencaoDto>> ListarAsync(TecnicoAtual tecnico, CancellationToken ct);
    Task<IntervencaoDto?> ObterAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    // Fichas do técnico fechadas (concluídas, validadas ou canceladas) nos últimos dias, para a cache offline da app.
    Task<IReadOnlyList<IntervencaoDto>> ListarRecentesAsync(TecnicoAtual tecnico, int dias, CancellationToken ct);
    // Equipamentos (sem viaturas), só leitura: pesquisa da avaria e lista; a ficha traz tarefas planeadas e as últimas 50 intervenções.
    // Pesquisa leve da avaria (20 no máximo, só id, nome e zona) e lista com agregados (devolve até MaxEquipamentos + 1 para a UI saber que foi cortada).
    Task<IReadOnlyList<EquipamentoDto>> PesquisarEquipamentosAsync(string texto, CancellationToken ct);
    Task<IReadOnlyList<EquipamentoDto>> ListarEquipamentosAsync(FiltroEquipamentos filtro, CancellationToken ct);
    Task<FichaEquipamentoDto?> ObterEquipamentoAsync(string id, int tecno, CancellationToken ct);
    // Com opId, repetir o pedido devolve a ficha já criada em vez de criar outra.
    Task<(ResultadoIntervencao, IntervencaoDto?)> RegistarAvariaAsync(string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct, string? opId = null);
    Task<(ResultadoIntervencao, IntervencaoDto?)> IniciarAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> ConcluirAsync(string id, TecnicoAtual tecnico, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct);
    // Pausa da ficha em curso (o estado não muda): repetir o pedido é idempotente; concluir em pausa fecha a pausa.
    Task<(ResultadoIntervencao, IntervencaoDto?)> PausarAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> RetomarAsync(string id, TecnicoAtual tecnico, CancellationToken ct);

    // Supervisor: fichas concluídas por validar, validação e devolução (volta a "em curso" com o motivo).
    Task<IReadOnlyList<IntervencaoDto>> ListarPorValidarAsync(TecnicoAtual supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> ValidarAsync(string id, TecnicoAtual supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> DevolverAsync(string id, string motivo, TecnicoAtual supervisor, CancellationToken ct);

    // Supervisor: planeadas e em curso de todos, atribuição de técnico (tecno 0 = sem dono) e geração das planeadas.
    Task<IReadOnlyList<IntervencaoDto>> ListarPlaneadasAsync(CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> AtribuirAsync(string id, int tecno, string nome, TecnicoAtual supervisor, CancellationToken ct);
    // Técnicos adicionais (substitui a lista); só os números novos têm de estar em `ativos` (TecnicoInvalido se não); o principal é ignorado e só valem fichas por fazer e não fechadas, com técnico principal.
    Task<(ResultadoIntervencao, IntervencaoDto?)> DefinirAdicionaisAsync(string id, IReadOnlyList<int> tecnos, IReadOnlySet<int> ativos, TecnicoAtual supervisor, CancellationToken ct);
    Task<RelatorioGeracao> GerarPlaneadasAsync(TecnicoAtual supervisor, CancellationToken ct);

    // Supervisor: todas as fichas, filtradas e paginadas (PodeAbrir diz se o supervisor pode abrir a ficha no detalhe); configuração (u_configman).
    Task<PaginaFichasDto> ListarTodasAsync(FiltroFichas filtro, TecnicoAtual supervisor, CancellationToken ct);
    Task<ConfigManutencao> ObterConfiguracaoAsync(CancellationToken ct);
    Task GuardarConfiguracaoAsync(ConfigManutencao configuracao, TecnicoAtual supervisor, CancellationToken ct);

    // Materiais usados (u_matinterv): só se vêem e mexem nas fichas do técnico; só se lançam com a ficha em curso.
    Task<IReadOnlyList<MaterialDto>?> ListarMateriaisAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    // Com supervisor = true vale a regra do supervisor: qualquer ficha que não seja final (Validada/Cancelada), tenha ou não o seu número.
    Task<(ResultadoIntervencao, MaterialDto?)> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, TecnicoAtual tecnico, CancellationToken ct, bool supervisor = false);
    Task<ResultadoIntervencao> ApagarMaterialAsync(string id, string materialId, TecnicoAtual tecnico, CancellationToken ct, bool supervisor = false);
    Task<(ResultadoIntervencao, MaterialDto?)> EditarMaterialAsync(string id, string materialId, string design, decimal qtt, decimal epv, TecnicoAtual quem, bool supervisor, CancellationToken ct);

    // Ficha completa (para o supervisor qualquer uma; para o técnico só as suas ou sem dono), edição, cancelamento, ficha manual e fecho das fichas antigas.
    // Edição: nomeTecnico só serve ao supervisor quando muda o técnico (o endpoint resolve-o); o estado nunca muda.
    Task<FichaDetalheDto?> ObterDetalheAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, FichaDetalheDto?)> GuardarFichaAsync(string id, PedidoFicha pedido, string? nomeTecnico, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> CancelarAsync(string id, string motivo, TecnicoAtual supervisor, CancellationToken ct);
    // Nasce em curso, com o utilizador como técnico; com opId, repetir o pedido devolve a ficha já criada.
    Task<(ResultadoIntervencao, IntervencaoDto?)> CriarFichaManualAsync(PedidoFichaManual pedido, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, FichaDetalheDto?)> FecharAntigaAsync(string id, bool supervisao, bool fechada, TecnicoAtual supervisor, CancellationToken ct);

    // Mão de obra interna (u_tempinterv): custo = horas x custo/hora. Mesmas regras de acesso do material; o nome do técnico escolhido vem resolvido do endpoint.
    Task<IReadOnlyList<TempoDto>?> ListarMaoDeObraAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, TempoDto?)> AdicionarMaoDeObraAsync(string id, int tecno, string nome, decimal horas, decimal custoHora, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, TempoDto?)> EditarMaoDeObraAsync(string id, string tempoId, int tecno, string nome, decimal horas, decimal custoHora, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<ResultadoIntervencao> ApagarMaoDeObraAsync(string id, string tempoId, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    // Custo/hora do último registo do técnico (0 se não houver).
    Task<decimal> CustoHoraSugeridoAsync(int tecno, CancellationToken ct);

    // Compras ligadas à ficha (fn), só leitura; sem compras na configuração a lista vem vazia. Fornecedores (fl) para escolher na ficha.
    Task<IReadOnlyList<CompraLigadaDto>?> ListarComprasAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<IReadOnlyList<FornecedorDto>> PesquisarFornecedoresAsync(string texto, CancellationToken ct);

    // Contadores do menu do supervisor: planeadas atrasadas (data passada ou fora de prazo, como o KPI do ecrã) e fichas por validar.
    Task<ContadoresMenuDto> ContarMenuAsync(CancellationToken ct);

    // Fotos (anexos do PHC ligados à ficha): o técnico vê e mexe nas da sua ficha, só com a ficha em curso; o supervisor vê as de qualquer ficha (só leitura).
    Task<IReadOnlyList<FotoDto>?> ListarFotosAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<FotoConteudo?> ObterFotoAsync(string id, string fotoId, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, FotoDto?)> AdicionarFotoAsync(string id, string nome, byte[] dados, TecnicoAtual tecnico, CancellationToken ct);
    Task<ResultadoIntervencao> ApagarFotoAsync(string id, string fotoId, TecnicoAtual tecnico, CancellationToken ct);
}

public static partial class EndpointsIntervencoes
{
    public static void MapIntervencoes(this WebApplication app)
    {
        var g = app.MapGroup("").RequireAuthorization();
        // Qualquer escrita nas fichas avisa já os aparelhos ligados (a gravação na base acabou antes de aqui chegar).
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var resultado = await next(ctx);
            var metodo = ctx.HttpContext.Request.Method;
            if (!HttpMethods.IsGet(metodo) && !HttpMethods.IsHead(metodo))
            {
                try { await Microsoft.AspNetCore.SignalR.ClientProxyExtensions.SendAsync(ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<AvisosHub>>().Clients.All, AvisosHub.Evento); }
                catch (Exception) { /* o aviso em tempo real é um extra: a verificação periódica apanha a mudança */ }
                // Escritas recusadas (4xx/5xx) não mudaram nada: não acordam os aparelhos.
                if (resultado is not Microsoft.AspNetCore.Http.IStatusCodeHttpResult { StatusCode: >= 400 })
                try
                {
                    var u = ctx.HttpContext.User;
                    ctx.HttpContext.RequestServices.GetRequiredService<INotificadorPush>().Notificar(u.FindFirstValue("sub") ?? u.FindFirstValue(ClaimTypes.NameIdentifier));
                }
                catch (Exception) { /* o push é um extra e nunca pode falhar o pedido */ }
            }
            return resultado;
        });
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var rotas = ctx.HttpContext.Request.RouteValues;
            if ((rotas.TryGetValue("id", out var id) && id?.ToString()?.Length > 25)
                || (rotas.TryGetValue("materialId", out var mat) && mat?.ToString()?.Length > 25)
                || (rotas.TryGetValue("fotoId", out var foto) && foto?.ToString()?.Length > 25)
                || (rotas.TryGetValue("tempoId", out var tempo) && tempo?.ToString()?.Length > 25)) return Results.NotFound();
            try { return await ComNomesAsync(await next(ctx), ctx.HttpContext); }
            catch (RepositorioIndisponivelException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });

        g.MapGet("/intervencoes", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => Results.Ok(await repo.ListarAsync(t, ct))));

        g.MapGet("/intervencoes/recentes", async Task<IResult> (int? dias, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (dias is < RegrasIntervencao.MinDiasRecentes or > RegrasIntervencao.MaxDiasRecentes) return Results.BadRequest();
            return await ComoTecnico(eu, utilizadores, ct, async t => Results.Ok(await repo.ListarRecentesAsync(t, dias ?? RegrasIntervencao.DiasRecentesOmissao, ct)));
        });

        g.MapGet("/intervencoes/{id}", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ObterAsync(id, t, ct) is { } i ? Results.Ok(i) : Results.NotFound()));

        // leve=true é a pesquisa da avaria (2 letras, 20 resultados, sem agregados). Sem leve: com texto pedem-se 2 letras; sem texto lista-se tudo, com os filtros.
        g.MapGet("/equipamentos", async Task<IResult> (string? q, string? tipo, string? local, bool? obsoletos, bool? leve,
            ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (await Ativo(eu, utilizadores, ct) is null) return Results.Forbid();
            var texto = (q ?? "").Trim();
            tipo = (tipo ?? "").Trim();
            local = (local ?? "").Trim();
            if (texto.Length is 1 or > RegrasManutencao.MaxTextoPesquisa || tipo.Length > RegrasManutencao.TamanhoTipo || local.Length > RegrasManutencao.TamanhoLocal
                || (leve == true && texto.Length == 0))
                return Results.Ok(Array.Empty<EquipamentoDto>());
            if (leve == true) return Results.Ok(await repo.PesquisarEquipamentosAsync(texto, ct));
            return Results.Ok(await repo.ListarEquipamentosAsync(new FiltroEquipamentos(texto, tipo, local, obsoletos ?? false), ct));
        });

        g.MapGet("/equipamentos/{id}", async Task<IResult> (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (await Ativo(eu, utilizadores, ct) is not { } u) return Results.Forbid();
            return await repo.ObterEquipamentoAsync(id, u.Tecno, ct) is { } ficha
                ? Results.Ok(RestringirHistorico(ficha, u.Tecno, u.Perfil == Perfil.Supervisor))
                : Results.NotFound();
        });

        // Todas as fichas (supervisor): estado, intervalo de datas, texto e técnico (número do PHC); 20 por página.
        // Também: situacao (abertas, fechadas, todas), porSupervisionar, tipo (preventiva, curativa), ordem (numero, data, equipamento, tipo, total) e desc.
        g.MapGet("/intervencoes/todas", async Task<IResult> (string? estado, DateOnly? desde, DateOnly? ate, string? q, int? tecno, int? pagina,
            string? situacao, bool? porSupervisionar, string? tipo, string? ordem, bool? desc,
            ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            var sit = SituacaoFichas.Todas;
            var ord = OrdemFichas.Data;
            TipoIntervencao? tipoFiltro = null;
            if (!string.IsNullOrWhiteSpace(situacao) && (!Enum.TryParse(situacao.Trim(), true, out sit) || !Enum.IsDefined(sit))) return Results.BadRequest();
            if (!string.IsNullOrWhiteSpace(ordem) && (!Enum.TryParse(ordem.Trim(), true, out ord) || !Enum.IsDefined(ord))) return Results.BadRequest();
            if (!string.IsNullOrWhiteSpace(tipo))
            {
                if (!Enum.TryParse<TipoIntervencao>(tipo.Trim(), true, out var t) || !Enum.IsDefined(t)) return Results.BadRequest();
                tipoFiltro = t;
            }
            EstadoIntervencao? estadoFiltro = null;
            if (!string.IsNullOrWhiteSpace(estado))
            {
                if (!Enum.TryParse<EstadoIntervencao>(estado.Trim(), true, out var e) || !Enum.IsDefined(e)) return Results.BadRequest();
                estadoFiltro = e;
            }
            var texto = (q ?? "").Trim();
            if (texto.Length > RegrasManutencao.MaxTextoPesquisa || tecno is < 0 or > 99999 || pagina is < 1 or > 500 || (desde > ate)) return Results.BadRequest();
            if (ForaDoIntervalo(desde) || ForaDoIntervalo(ate))
                return Results.Json(new { erro = "pedido_invalido", mensagem = "As datas têm de estar entre 01/01/1900 e 31/12/2999." }, statusCode: StatusCodes.Status400BadRequest);

            return await ComoSupervisor(eu, utilizadores, ct, async s =>
                Results.Ok(await repo.ListarTodasAsync(new FiltroFichas(estadoFiltro, desde, ate, texto, tecno ?? 0, pagina ?? 1, sit, porSupervisionar ?? false, tipoFiltro, ord, desc ?? true), s, ct)));
        }).RequireAuthorization("Supervisor");

        g.MapGet("/configuracao", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => Results.Ok(await repo.ObterConfiguracaoAsync(ct)))).RequireAuthorization("Supervisor");

        g.MapPut("/configuracao", async Task<IResult> (PedidoConfiguracao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, ILogger<Program> log, CancellationToken ct) =>
        {
            if (RegrasManutencao.ValidarConfiguracao(p) is { } mensagem)
                return Results.Json(new { erro = "pedido_invalido", mensagem }, statusCode: StatusCodes.Status400BadRequest);

            var nova = new ConfigManutencao(p.Antecedencia!.Value, p.GerarPlaneadas!.Value, p.AvancaOrdem!.Value, p.ValidarPropria!.Value, p.ExtExigeDoc!.Value, p.ComprasFichaInt!.Value);
            return await ComoSupervisor(eu, utilizadores, ct, async s =>
            {
                await repo.GuardarConfiguracaoAsync(nova, s, ct);
                log.LogInformation("Configuração da manutenção alterada por {Supervisor}: antecedência {Antecedencia}, gerar planeadas {Gerar}, avança ordem {Ordem}, validar própria {Propria}, externa exige documento {Doc}, compras na ficha {Compras}.",
                    s.Codigo, nova.Antecedencia, nova.GerarPlaneadas, nova.AvancaOrdem, nova.ValidarPropria, nova.ExtExigeDoc, nova.ComprasFichaInt);
                // Devolve o que ficou guardado.
                return Results.Ok(await repo.ObterConfiguracaoAsync(ct));
            });
        }).RequireAuthorization("Supervisor");

        g.MapPost("/intervencoes/avaria", (PedidoAvaria p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarAvaria(p.EquipamentoId, p.Anomalia, p.OpId) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.RegistarAvariaAsync(p.EquipamentoId!, p.Anomalia!.Trim(), t, ct, p.OpId))));

        g.MapPost("/intervencoes/{id}/iniciar", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.IniciarAsync(id, t, ct))));

        g.MapPost("/intervencoes/{id}/pausar", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.PausarAsync(id, t, ct))));

        g.MapPost("/intervencoes/{id}/retomar", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.RetomarAsync(id, t, ct))));

        g.MapPost("/intervencoes/{id}/concluir", (string id, PedidoConclusao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarConclusao(p.Feito, p.Inicio, p.Fim) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.ConcluirAsync(id, t, p.Feito!.Trim(), p.Inicio!.Value, p.Fim!.Value, ct))));

        g.MapGet("/intervencoes/{id}/materiais", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ListarMateriaisAsync(id, t, ct) is { } m ? Results.Ok(m) : Results.NotFound()));

        g.MapPost("/intervencoes/{id}/materiais", (string id, PedidoMaterial p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarMaterial(p.Design, p.Qtt, p.Epv) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) => Resposta(await repo.AdicionarMaterialAsync(id, p.Design!.Trim(), p.Qtt!.Value, p.Epv ?? 0, t, ct, sup))));

        g.MapPut("/intervencoes/{id}/materiais/{materialId}", (string id, string materialId, PedidoMaterial p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarMaterial(p.Design, p.Qtt, p.Epv) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) => Resposta(await repo.EditarMaterialAsync(id, materialId, p.Design!.Trim(), p.Qtt!.Value, p.Epv ?? 0, t, sup, ct))));

        g.MapDelete("/intervencoes/{id}/materiais/{materialId}", (string id, string materialId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) => await repo.ApagarMaterialAsync(id, materialId, t, ct, sup) is var r && r == ResultadoIntervencao.Ok
                ? Results.NoContent()
                : Resposta<object>((r, null))));

        // Ficha manual (operador e supervisor com número de técnico), ficha completa, edição, cancelamento e fecho das fichas antigas.
        g.MapPost("/intervencoes", (PedidoFichaManual p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasFicha.ValidarFichaManual(p) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, supervisor) =>
                {
                    if (t.Tecno <= 0) return Results.Json(new { erro = "sem_tecno" }, statusCode: StatusCodes.Status403Forbidden);
                    // O técnico só abre avarias (curativas, internas, sem periodicidade): as preventivas nascem da geração e são do supervisor.
                    if (!supervisor && p.Tipo == TipoIntervencao.Preventiva) return Results.Json(new { erro = "tipo_nao_permitido" }, statusCode: StatusCodes.Status403Forbidden);
                    var pedido = supervisor ? p : p with { Tipo = TipoIntervencao.Curativa, TipoManut = "Interna", Periodicidade = "" };
                    return Resposta(await repo.CriarFichaManualAsync(pedido, t, ct));
                }));

        g.MapGet("/intervencoes/contadores", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => Results.Ok(await repo.ContarMenuAsync(ct)))).RequireAuthorization("Supervisor");

        g.MapGet("/intervencoes/{id}/detalhe", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (q, sup) => await repo.ObterDetalheAsync(id, q, sup, ct) is { } d ? Results.Ok(d) : Results.NotFound()));

        g.MapPut("/intervencoes/{id}", async Task<IResult> (string id, PedidoFicha p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (RegrasFicha.ValidarPedido(p) is { } erro) return PedidoInvalido(erro);
            return await ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (q, sup) =>
            {
                // Sem nome (técnico inexistente ou inativo) o repositório só recusa se o técnico mudar de facto.
                var nome = sup && p.Tecno is { } tecno ? await NomeTecnicoAtivoAsync(utilizadores, tecno, ct) : null;
                return Resposta(await repo.GuardarFichaAsync(id, p, nome, q, sup, ct));
            });
        });

        g.MapPost("/intervencoes/{id}/cancelar", (string id, PedidoCancelamento p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasFicha.ValidarCancelamento(p.Motivo) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.CancelarAsync(id, p.Motivo!.Trim(), s, ct)))).RequireAuthorization("Supervisor");

        g.MapPut("/intervencoes/{id}/antiga", (string id, PedidoFechoAntiga p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            p.Supervisao is null || p.Fechada is null
                ? Task.FromResult(PedidoInvalido("Indique se está supervisionada e se está fechada."))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.FecharAntigaAsync(id, p.Supervisao.Value, p.Fechada.Value, s, ct)))).RequireAuthorization("Supervisor");

        // Mão de obra interna: técnico e horas obrigatórios; o custo é horas x custo/hora.
        g.MapGet("/intervencoes/{id}/mao-de-obra", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (q, sup) => await repo.ListarMaoDeObraAsync(id, q, sup, ct) is { } l ? Results.Ok(RegrasFicha.OcultarCustos(l, sup, q.Tecno)) : Results.NotFound()));

        g.MapPost("/intervencoes/{id}/mao-de-obra", async Task<IResult> (string id, PedidoTempo p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (RegrasFicha.ValidarTempo(p.Tecno, p.Horas, p.CustoHora) is { } erro) return PedidoInvalido(erro);
            return await ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (q, sup) =>
                await NomeTecnicoAtivoAsync(utilizadores, p.Tecno!.Value, ct) is not { } nome
                    ? Resposta<TempoDto>((ResultadoIntervencao.TecnicoInvalido, null))
                    : Resposta(Esconder(await repo.AdicionarMaoDeObraAsync(id, p.Tecno.Value, nome, p.Horas!.Value, p.CustoHora ?? 0, q, sup, ct), sup, q.Tecno)));
        });

        g.MapPut("/intervencoes/{id}/mao-de-obra/{tempoId}", async Task<IResult> (string id, string tempoId, PedidoTempo p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (RegrasFicha.ValidarTempo(p.Tecno, p.Horas, p.CustoHora) is { } erro) return PedidoInvalido(erro);
            return await ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (q, sup) =>
                await NomeTecnicoAtivoAsync(utilizadores, p.Tecno!.Value, ct) is not { } nome
                    ? Resposta<TempoDto>((ResultadoIntervencao.TecnicoInvalido, null))
                    : Resposta(Esconder(await repo.EditarMaoDeObraAsync(id, tempoId, p.Tecno.Value, nome, p.Horas!.Value, p.CustoHora ?? 0, q, sup, ct), sup, q.Tecno)));
        });

        g.MapDelete("/intervencoes/{id}/mao-de-obra/{tempoId}", (string id, string tempoId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (q, sup) => await repo.ApagarMaoDeObraAsync(id, tempoId, q, sup, ct) is var r && r == ResultadoIntervencao.Ok
                ? Results.NoContent()
                : Resposta<object>((r, null))));

        g.MapGet("/tecnicos/{tecno:int}/custo-hora", async Task<IResult> (int tecno, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (await Ativo(eu, utilizadores, ct) is not { } u) return Results.Forbid();
            if (tecno is < 1 or > RegrasIntervencao.MaxTecnoPhc) return Results.BadRequest();
            // Só o próprio técnico e o supervisor vêem o custo/hora de alguém; para os outros é 0.
            return Results.Ok(u.Perfil == Perfil.Supervisor || u.Tecno == tecno ? await repo.CustoHoraSugeridoAsync(tecno, ct) : 0m);
        });

        // Compras ligadas à ficha (só leitura, só supervisor) e pesquisa de fornecedores (2 letras no mínimo).
        g.MapGet("/intervencoes/{id}/compras", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async s => await repo.ListarComprasAsync(id, s, true, ct) is { } l ? Results.Ok(l) : Results.NotFound())).RequireAuthorization("Supervisor");

        g.MapGet("/fornecedores", async Task<IResult> (string? q, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (await Ativo(eu, utilizadores, ct) is null) return Results.Forbid();
            var texto = (q ?? "").Trim();
            return texto.Length is < 2 or > RegrasManutencao.MaxTextoPesquisa ? Results.Ok(Array.Empty<FornecedorDto>()) : Results.Ok(await repo.PesquisarFornecedoresAsync(texto, ct));
        });

        // Fotos: a lista e cada foto servem o técnico da ficha e qualquer supervisor; juntar e apagar só o técnico, com a ficha em curso.
        g.MapGet("/intervencoes/{id}/fotos", async Task<IResult> (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            await ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (q, sup) => await repo.ListarFotosAsync(id, q, sup, ct) is { } l ? Results.Ok(l) : Results.NotFound()));

        g.MapGet("/intervencoes/{id}/fotos/{fotoId}", async Task<IResult> (string id, string fotoId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            await ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (q, sup) => await repo.ObterFotoAsync(id, fotoId, q, sup, ct) is { } f
                ? Results.File(f.Dados, "image/jpeg") : Results.NotFound()));

        // Corpo = o JPEG em bruto (Content-Type image/jpeg); o nome vai na query.
        g.MapPost("/intervencoes/{id}/fotos", async Task<IResult> (string id, string? nome, HttpRequest pedido, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (pedido.ContentLength > RegrasIntervencao.MaxBytesFoto) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var memoria = new MemoryStream();
            var buffer = new byte[81920];
            int lidos;
            while ((lidos = await pedido.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (memoria.Length + lidos > RegrasIntervencao.MaxBytesFoto) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                memoria.Write(buffer, 0, lidos);
            }
            var dados = memoria.ToArray();
            if (!RegrasIntervencao.EhJpeg(dados)) return Results.BadRequest();
            return await ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.AdicionarFotoAsync(id, RegrasIntervencao.NomeFoto(nome, DateTime.Now), dados, t, ct)));
        });

        g.MapDelete("/intervencoes/{id}/fotos/{fotoId}", (string id, string fotoId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ApagarFotoAsync(id, fotoId, t, ct) is var r && r == ResultadoIntervencao.Ok
                ? Results.NoContent()
                : Resposta<object>((r, null))));

        g.MapGet("/validacao", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async s => Results.Ok(await repo.ListarPorValidarAsync(s, ct)))).RequireAuthorization("Supervisor");

        g.MapPost("/validacao/{id}", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.ValidarAsync(id, s, ct)))).RequireAuthorization("Supervisor");

        g.MapPost("/validacao/{id}/devolver", (string id, PedidoDevolucao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(p.Motivo) || p.Motivo.Trim().Length > RegrasManutencao.TamanhoMotivo
                ? Task.FromResult(Results.BadRequest())
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.DevolverAsync(id, p.Motivo.Trim(), s, ct)))).RequireAuthorization("Supervisor");

        g.MapGet("/planeadas", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => Results.Ok(await repo.ListarPlaneadasAsync(ct)))).RequireAuthorization("Supervisor");

        g.MapPost("/planeadas/gerar", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async s => Results.Ok(await repo.GerarPlaneadasAsync(s, ct)))).RequireAuthorization("Supervisor");

        g.MapPut("/planeadas/{id}/tecnico", (string id, PedidoAtribuicao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            p.Tecno is null or < 0 or > 99999
                ? Task.FromResult(Results.BadRequest())
                : ComoSupervisor(eu, utilizadores, ct, async s =>
                {
                    var nome = "";
                    if (p.Tecno > 0)
                    {
                        var alvo = (await utilizadores.ListarAsync(ct)).FirstOrDefault(u => u.Ativo && u.Tecno == p.Tecno);
                        if (alvo is null) return Results.NotFound();
                        nome = Truncar(alvo.Nome);
                    }
                    return Resposta(await repo.AtribuirAsync(id, p.Tecno!.Value, nome, s, ct));
                })).RequireAuthorization("Supervisor");

        g.MapPut("/planeadas/{id}/tecnicos", (string id, PedidoAdicionais p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            p.Tecnos is null || p.Tecnos.Count > RegrasIntervencao.MaxAdicionais || p.Tecnos.Any(t => t is < 1 or > RegrasIntervencao.MaxTecnoPhc)
                ? Task.FromResult(Results.BadRequest())
                : ComoSupervisor(eu, utilizadores, ct, async s =>
                {
                    var pedidos = p.Tecnos.Distinct().ToList();
                    var ativos = (await utilizadores.ListarAsync(ct)).Where(u => u.Ativo && u.Tecno > 0).Select(u => u.Tecno).ToHashSet();
                    return Resposta(await repo.DefinirAdicionaisAsync(id, pedidos, ativos, s, ct));
                })).RequireAuthorization("Supervisor");
    }

    // Os nomes dos adicionais resolvem-se aqui (a ficha só guarda os números).
    private static async Task<object?> ComNomesAsync(object? resultado, HttpContext http)
    {
        if (resultado is not IValueHttpResult { Value: { } valor }) return resultado;
        static bool Tem(IntervencaoDto i) => i.Adicionais is { Count: > 0 };
        var precisa = valor switch
        {
            IntervencaoDto i => Tem(i),
            IReadOnlyList<IntervencaoDto> l => l.Any(Tem),
            PaginaFichasDto pg => pg.Itens.Any(Tem),
            FichaEquipamentoDto f => f.Historico.Any(Tem),
            FichaDetalheDto d => d.Adicionais is { Count: > 0 },
            _ => false
        };
        if (!precisa) return resultado;

        var nomes = (await http.RequestServices.GetRequiredService<IRepositorioUtilizadores>().ListarAsync(http.RequestAborted))
            .Where(u => u.Tecno > 0).GroupBy(u => u.Tecno).ToDictionary(g => g.Key, g => Truncar(g.First().Nome));
        IntervencaoDto N(IntervencaoDto i) => i.Adicionais is null ? i
            : i with { Adicionais = i.Adicionais.Select(a => a with { Nome = nomes.GetValueOrDefault(a.Tecno, "PHC " + a.Tecno) }).ToList() };
        return valor switch
        {
            IntervencaoDto i => Results.Ok(N(i)),
            IReadOnlyList<IntervencaoDto> l => Results.Ok((IReadOnlyList<IntervencaoDto>)l.Select(N).ToList()),
            PaginaFichasDto pg => Results.Ok(pg with { Itens = pg.Itens.Select(N).ToList() }),
            FichaEquipamentoDto f => Results.Ok(f with { Historico = f.Historico.Select(N).ToList() }),
            FichaDetalheDto d => Results.Ok(d with { Adicionais = d.Adicionais!.Select(a => a with { Nome = nomes.GetValueOrDefault(a.Tecno, "PHC " + a.Tecno) }).ToList() }),
            _ => resultado
        };
    }

    // Só técnicos ativos com número do PHC podem ser escolhidos numa ficha ou na mão de obra.
    private static async Task<string?> NomeTecnicoAtivoAsync(IRepositorioUtilizadores utilizadores, int tecno, CancellationToken ct) =>
        (await utilizadores.ListarAsync(ct)).FirstOrDefault(u => u.Ativo && u.Tecno == tecno) is { } alvo ? Truncar(alvo.Nome) : null;

    private static (ResultadoIntervencao, TempoDto?) Esconder((ResultadoIntervencao Resultado, TempoDto? Tempo) r, bool supervisor, int tecno) =>
        (r.Resultado, r.Tempo is { } t ? RegrasFicha.OcultarCusto(t, supervisor, tecno) : null);

    private static IResult PedidoInvalido(string mensagem) =>
        Results.Json(new { erro = "pedido_invalido", mensagem }, statusCode: StatusCodes.Status400BadRequest);

    private static async Task<UtilizadorApp?> Ativo(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct)
    {
        var codigo = eu.FindFirstValue("sub") ?? eu.FindFirstValue(ClaimTypes.NameIdentifier);
        return codigo is null ? null : await utilizadores.ObterAsync(codigo, ct) is { Ativo: true, PinProvisorio: false } u ? u : null;
    }

    // Sem utilizador ativo não há acesso; sem número de técnico do PHC não há fichas (a app explica e pede ao supervisor).
    private static async Task<IResult> ComoTecnico(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct, Func<TecnicoAtual, Task<IResult>> acao)
    {
        if (await Ativo(eu, utilizadores, ct) is not { } u) return Results.Forbid();
        if (u.Tecno <= 0) return Results.Json(new { erro = "sem_tecno" }, statusCode: StatusCodes.Status403Forbidden);
        return await acao(new TecnicoAtual(u.Codigo, u.Tecno, Truncar(u.Nome)));
    }

    // Supervisor (mesmo sem número de técnico) ou técnico com número; o supervisor lê qualquer ficha.
    private static async Task<IResult> ComoTecnicoOuSupervisor(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct, Func<TecnicoAtual, bool, Task<IResult>> acao)
    {
        if (await Ativo(eu, utilizadores, ct) is not { } u) return Results.Forbid();
        var supervisor = u.Perfil == Perfil.Supervisor;
        if (!supervisor && u.Tecno <= 0) return Results.Json(new { erro = "sem_tecno" }, statusCode: StatusCodes.Status403Forbidden);
        return await acao(new TecnicoAtual(u.Codigo, u.Tecno, Truncar(u.Nome)), supervisor);
    }

    // O token dura um turno; as ações do supervisor confirmam na base que continua ativo e supervisor.
    private static async Task<IResult> ComoSupervisor(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct, Func<TecnicoAtual, Task<IResult>> acao)
    {
        if (await Ativo(eu, utilizadores, ct) is not { Perfil: Perfil.Supervisor } u) return Results.Forbid();
        return await acao(new TecnicoAtual(u.Codigo, u.Tecno, Truncar(u.Nome)));
    }

    private static bool ForaDoIntervalo(DateOnly? data) => data is { } d && (d < new DateOnly(1900, 1, 1) || d > new DateOnly(2999, 12, 31));

    // Pressuposto: o operador só vê o conteúdo (problema, o que foi feito, técnico, horas) das fichas suas; nas dos outros só data, tipo e estado.
    // O supervisor vê tudo. PodeAbrir já vem calculado do repositório e o número do técnico sai, para não identificar ninguém.
    internal static FichaEquipamentoDto RestringirHistorico(FichaEquipamentoDto ficha, int tecno, bool supervisor) =>
        supervisor ? ficha with { Historico = ficha.Historico.Select(h => h with { PodeAbrir = true }).ToList() } : ficha with
        {
            Historico = ficha.Historico.Select(h => RegrasIntervencao.Envolve(h, tecno)
                ? h
                : h with { Anomalia = null, Feito = null, Tecnico = null, Tecno = 0, HoraInicio = null, HoraFim = null, Motivo = null, Adicionais = null }).ToList()
        };

    // Na ficha, o nome do técnico cabe em 45 caracteres.
    internal static string Truncar(string nome) => nome.Length > 45 ? nome[..45] : nome;

    private static IResult Conflito(string codigo) => Results.Json(new { erro = codigo }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Resposta<T>((ResultadoIntervencao Resultado, T? Dados) r) => r.Resultado switch
    {
        ResultadoIntervencao.Ok => Results.Ok(r.Dados),
        ResultadoIntervencao.NaoExiste => Results.NotFound(),
        ResultadoIntervencao.TecnicoInvalido => Results.Json(new { erro = "tecnico_invalido" }, statusCode: StatusCodes.Status404NotFound),
        ResultadoIntervencao.ForaDeOrdem => Conflito("fora_de_ordem"),
        ResultadoIntervencao.ExigeDocumento => Conflito("externa_sem_documento"),
        ResultadoIntervencao.Propria => Conflito("propria"),
        ResultadoIntervencao.LimiteFotos => Conflito("limite_fotos"),
        ResultadoIntervencao.SemPermissao => Results.Json(new { erro = "sem_permissao" }, statusCode: StatusCodes.Status403Forbidden),
        ResultadoIntervencao.Invalido => Results.Json(new { erro = "pedido_invalido", mensagem = "Algum dos valores indicados não é válido." }, statusCode: StatusCodes.Status400BadRequest),
        ResultadoIntervencao.ProblemaLongo => Results.Json(new { erro = "problema_longo", mensagem = "O problema é demasiado longo para esta ficha (leva um identificador da operação)." }, statusCode: StatusCodes.Status400BadRequest),
        ResultadoIntervencao.RefDuplicada => Conflito("ref_duplicada"),
        ResultadoIntervencao.PlaneadasMudaram => Conflito("planeadas_mudaram"),
        ResultadoIntervencao.TarefaComFichas => Conflito("tarefa_com_fichas"),
        ResultadoIntervencao.InstrucaoObsoleta => Conflito("instrucao_obsoleta"),
       
        ResultadoIntervencao.PeriodicidadeInvalida => Results.Json(new { erro = "periodicidade_invalida", mensagem = "Essa periodicidade não existe neste equipamento." }, statusCode: StatusCodes.Status400BadRequest),
        _ => Conflito("conflito")
    };
}
