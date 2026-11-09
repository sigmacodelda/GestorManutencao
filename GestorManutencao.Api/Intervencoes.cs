using System.Security.Claims;
using System.Text.Json.Serialization;

namespace GestorManutencao.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoIntervencao { Planeada, EmCurso, Concluida, Validada, Cancelada }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoIntervencao { Preventiva, Curativa }

// Última, Cancelada e DataPrevista só vêm nas planeadas. Tecno é o número de utilizador do PHC de quem tem a ficha (0 = sem dono). PodeValidar só conta na lista de validação.
// Motivo só vem nas fichas devolvidas pelo supervisor (em curso com observação gravada).
public sealed record IntervencaoDto(
    string Id, string Equipamento, string Zona, TipoIntervencao Tipo, DateOnly DataPlaneada, EstadoIntervencao Estado,
    string? Anomalia = null, string? Feito = null, TimeOnly? HoraInicio = null, TimeOnly? HoraFim = null,
    string? Tecnico = null, int Tecno = 0, bool PodeValidar = true,
    DateOnly? UltimaIntervencao = null, DateOnly? UltimaCancelada = null, DateOnly? DataPrevista = null,
    bool PodeAbrir = false, string? Motivo = null);

// Id, Nome e Zona bastam à pesquisa da avaria; o resto serve à lista e à ficha de equipamentos.
public sealed record EquipamentoDto(
    string Id, string Nome, string Zona, string Ref = "", int NoInterno = 0, string Tipo = "", string Marca = "", string Modelo = "", string Serie = "",
    DateOnly? UltimaIntervencao = null, int FichasAbertas = 0, bool ForaDeServico = false, bool Obsoleto = false);

public sealed record TarefaPlaneadaDto(string Id, string Descricao, string Periodicidade, DateOnly? UltimaIntervencao, DateOnly? ProximaData);

public sealed record FichaEquipamentoDto(EquipamentoDto Equipamento, IReadOnlyList<TarefaPlaneadaDto> Tarefas, IReadOnlyList<IntervencaoDto> Historico);

public sealed record FiltroEquipamentos(string Texto, string Tipo, string Local, bool Obsoletos);

public sealed record FiltroFichas(EstadoIntervencao? Estado, DateOnly? Desde, DateOnly? Ate, string Texto, int Tecno, int Pagina);

public sealed record PaginaFichasDto(IReadOnlyList<IntervencaoDto> Itens, bool TemMais);

// ExigeMaterial ausente conta como falso (clientes antigos não o enviam).
public sealed record PedidoConfiguracao(int? Antecedencia, bool? GerarPlaneadas, bool? AvancaOrdem, bool? ValidarPropria, bool? ExtExigeDoc, bool? ComprasFichaInt, bool? ExigeMaterial = null);

public sealed record MaterialDto(string Id, string Design, decimal Qtt, decimal Epv, decimal Custo);

// Foto anexada à intervenção (tabela anexos do PHC); os bytes só vêm no pedido da própria foto.
public sealed record FotoDto(string Id, string Nome, int Bytes);

public sealed record FotoConteudo(string Nome, byte[] Dados);

// OpId (opcional) identifica a operação para a repetição do pedido não criar outra ficha; vai gravado no fim do texto do problema.
public sealed record PedidoAvaria(string? EquipamentoId, string? Anomalia, string? OpId = null);

public sealed record PedidoConclusao(string? Feito, TimeOnly? Inicio, TimeOnly? Fim);

public sealed record PedidoMaterial(string? Design, decimal? Qtt, decimal? Epv);

public sealed record PedidoDevolucao(string? Motivo);

public sealed record PedidoAtribuicao(int? Tecno);

public sealed record RelatorioGeracao(int Criadas, int JaTinham, int SemPeriodicidade, int Erros);

public enum ResultadoIntervencao { Ok, Conflito, NaoExiste, ForaDeOrdem, ExigeDocumento, Propria, SemMaterial, LimiteFotos }

// O nome vai em tecnome (45); nas colunas de auditoria vai o código (30); a identidade da pessoa nas fichas é o Tecno.
public sealed record TecnicoAtual(string Codigo, int Tecno, string Nome);

public static class RegrasIntervencao
{
    public const int MaxAnomalia = 200, MaxFeito = 254;

    // Marcador gravado no fim do problema: " [op:" + OpId + "]". Com o OpId máximo o texto fica em 200 - 6 - 32 = 162 caracteres.
    public const int MinOpId = 8, MaxOpId = 32, MaxAnomaliaComOpId = MaxAnomalia - 6 - MaxOpId;

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

    public static string? ValidarAvaria(string? equipamentoId, string? anomalia, string? opId = null) =>
        string.IsNullOrWhiteSpace(equipamentoId) || equipamentoId.Length > 25 ? "Equipamento em falta."
        : opId is not null && !OpIdValido(opId) ? "Identificador da operação inválido."
        : string.IsNullOrWhiteSpace(anomalia) || anomalia.Trim().Length > (opId is null ? MaxAnomalia : MaxAnomalia - Marcador(opId).Length) ? "Descrição em falta ou demasiado longa."
        : null;

    public static string? ValidarConclusao(string? feito, TimeOnly? inicio, TimeOnly? fim) =>
        string.IsNullOrWhiteSpace(feito) || feito.Trim().Length > MaxFeito ? "Descrição em falta ou demasiado longa."
        : inicio is null || fim is null || fim < inicio ? "Horas inválidas."
        : null;

    public static string? ValidarMaterial(string? design, decimal? qtt, decimal? epv) =>
        string.IsNullOrWhiteSpace(design) || design.Trim().Length > RegrasManutencao.TamanhoDesignMaterial ? "Material em falta ou com nome demasiado longo."
        : qtt is null or <= 0 or > 999_999 ? "Quantidade inválida."
        : epv is < 0 or > 999_999_999 ? "Preço inválido."
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

    // Supervisor: fichas concluídas por validar, validação e devolução (volta a "em curso" com o motivo).
    Task<IReadOnlyList<IntervencaoDto>> ListarPorValidarAsync(TecnicoAtual supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> ValidarAsync(string id, TecnicoAtual supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> DevolverAsync(string id, string motivo, TecnicoAtual supervisor, CancellationToken ct);

    // Supervisor: planeadas e em curso de todos, atribuição de técnico (tecno 0 = sem dono) e geração das planeadas.
    Task<IReadOnlyList<IntervencaoDto>> ListarPlaneadasAsync(CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> AtribuirAsync(string id, int tecno, string nome, TecnicoAtual supervisor, CancellationToken ct);
    Task<RelatorioGeracao> GerarPlaneadasAsync(TecnicoAtual supervisor, CancellationToken ct);

    // Supervisor: todas as fichas, filtradas e paginadas (PodeAbrir diz se o supervisor pode abrir a ficha no detalhe); configuração (u_configman).
    Task<PaginaFichasDto> ListarTodasAsync(FiltroFichas filtro, TecnicoAtual supervisor, CancellationToken ct);
    Task<ConfigManutencao> ObterConfiguracaoAsync(CancellationToken ct);
    Task GuardarConfiguracaoAsync(ConfigManutencao configuracao, TecnicoAtual supervisor, CancellationToken ct);

    // Materiais usados (u_matinterv): só se vêem e mexem nas fichas do técnico; só se lançam com a ficha em curso.
    Task<IReadOnlyList<MaterialDto>?> ListarMateriaisAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, MaterialDto?)> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, TecnicoAtual tecnico, CancellationToken ct);
    Task<ResultadoIntervencao> ApagarMaterialAsync(string id, string materialId, TecnicoAtual tecnico, CancellationToken ct);

    // Fotos (anexos do PHC ligados à ficha): o técnico vê e mexe nas da sua ficha, só com a ficha em curso; o supervisor vê as de qualquer ficha (só leitura).
    Task<IReadOnlyList<FotoDto>?> ListarFotosAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<FotoConteudo?> ObterFotoAsync(string id, string fotoId, TecnicoAtual quem, bool supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, FotoDto?)> AdicionarFotoAsync(string id, string nome, byte[] dados, TecnicoAtual tecnico, CancellationToken ct);
    Task<ResultadoIntervencao> ApagarFotoAsync(string id, string fotoId, TecnicoAtual tecnico, CancellationToken ct);
}

public static class EndpointsIntervencoes
{
    public static void MapIntervencoes(this WebApplication app)
    {
        var g = app.MapGroup("").RequireAuthorization();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var rotas = ctx.HttpContext.Request.RouteValues;
            if ((rotas.TryGetValue("id", out var id) && id?.ToString()?.Length > 25)
                || (rotas.TryGetValue("materialId", out var mat) && mat?.ToString()?.Length > 25)
                || (rotas.TryGetValue("fotoId", out var foto) && foto?.ToString()?.Length > 25)) return Results.NotFound();
            try { return await next(ctx); }
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
        g.MapGet("/intervencoes/todas", async Task<IResult> (string? estado, DateOnly? desde, DateOnly? ate, string? q, int? tecno, int? pagina,
            ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
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
                Results.Ok(await repo.ListarTodasAsync(new FiltroFichas(estadoFiltro, desde, ate, texto, tecno ?? 0, pagina ?? 1), s, ct)));
        }).RequireAuthorization("Supervisor");

        g.MapGet("/configuracao", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => Results.Ok(await repo.ObterConfiguracaoAsync(ct)))).RequireAuthorization("Supervisor");

        g.MapPut("/configuracao", async Task<IResult> (PedidoConfiguracao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, ILogger<Program> log, CancellationToken ct) =>
        {
            if (RegrasManutencao.ValidarConfiguracao(p) is { } mensagem)
                return Results.Json(new { erro = "pedido_invalido", mensagem }, statusCode: StatusCodes.Status400BadRequest);

            var nova = new ConfigManutencao(p.Antecedencia!.Value, p.GerarPlaneadas!.Value, p.AvancaOrdem!.Value, p.ValidarPropria!.Value, p.ExtExigeDoc!.Value, p.ComprasFichaInt!.Value, p.ExigeMaterial ?? false);
            return await ComoSupervisor(eu, utilizadores, ct, async s =>
            {
                await repo.GuardarConfiguracaoAsync(nova, s, ct);
                log.LogInformation("Configuração da manutenção alterada por {Supervisor}: antecedência {Antecedencia}, gerar planeadas {Gerar}, avança ordem {Ordem}, validar própria {Propria}, externa exige documento {Doc}, compras na ficha {Compras}, exige material {Material}.",
                    s.Codigo, nova.Antecedencia, nova.GerarPlaneadas, nova.AvancaOrdem, nova.ValidarPropria, nova.ExtExigeDoc, nova.ComprasFichaInt, nova.ExigeMaterial);
                // Devolve o que ficou guardado: sem o campo no PHC o "exige material" não grava e o ecrã mostra-o desligado.
                return Results.Ok(await repo.ObterConfiguracaoAsync(ct));
            });
        }).RequireAuthorization("Supervisor");

        g.MapPost("/intervencoes/avaria", (PedidoAvaria p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarAvaria(p.EquipamentoId, p.Anomalia, p.OpId) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.RegistarAvariaAsync(p.EquipamentoId!, p.Anomalia!.Trim(), t, ct, p.OpId))));

        g.MapPost("/intervencoes/{id}/iniciar", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.IniciarAsync(id, t, ct))));

        g.MapPost("/intervencoes/{id}/concluir", (string id, PedidoConclusao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarConclusao(p.Feito, p.Inicio, p.Fim) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.ConcluirAsync(id, t, p.Feito!.Trim(), p.Inicio!.Value, p.Fim!.Value, ct))));

        g.MapGet("/intervencoes/{id}/materiais", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ListarMateriaisAsync(id, t, ct) is { } m ? Results.Ok(m) : Results.NotFound()));

        g.MapPost("/intervencoes/{id}/materiais", (string id, PedidoMaterial p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarMaterial(p.Design, p.Qtt, p.Epv) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.AdicionarMaterialAsync(id, p.Design!.Trim(), p.Qtt!.Value, p.Epv ?? 0, t, ct))));

        g.MapDelete("/intervencoes/{id}/materiais/{materialId}", (string id, string materialId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ApagarMaterialAsync(id, materialId, t, ct) is var r && r == ResultadoIntervencao.Ok
                ? Results.NoContent()
                : Resposta<object>((r, null))));

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
    }

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
        supervisor ? ficha : ficha with
        {
            Historico = ficha.Historico.Select(h => tecno > 0 && h.Tecno == tecno
                ? h
                : h with { Anomalia = null, Feito = null, Tecnico = null, Tecno = 0, HoraInicio = null, HoraFim = null, Motivo = null }).ToList()
        };

    // Na ficha, o nome do técnico cabe em 45 caracteres.
    internal static string Truncar(string nome) => nome.Length > 45 ? nome[..45] : nome;

    private static IResult Conflito(string codigo) => Results.Json(new { erro = codigo }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Resposta<T>((ResultadoIntervencao Resultado, T? Dados) r) => r.Resultado switch
    {
        ResultadoIntervencao.Ok => Results.Ok(r.Dados),
        ResultadoIntervencao.NaoExiste => Results.NotFound(),
        ResultadoIntervencao.ForaDeOrdem => Conflito("fora_de_ordem"),
        ResultadoIntervencao.ExigeDocumento => Conflito("externa_sem_documento"),
        ResultadoIntervencao.Propria => Conflito("propria"),
        ResultadoIntervencao.SemMaterial => Conflito("sem_material"),
        ResultadoIntervencao.LimiteFotos => Conflito("limite_fotos"),
        _ => Conflito("conflito")
    };
}
