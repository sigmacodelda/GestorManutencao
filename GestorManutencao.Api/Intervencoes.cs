using System.Data;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoIntervencao { Planeada, EmCurso, Concluida, Validada, Cancelada }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoIntervencao { Preventiva, Curativa }

public sealed record IntervencaoDto(
    string Id, string Equipamento, string Zona, TipoIntervencao Tipo, DateOnly DataPlaneada, EstadoIntervencao Estado,
    string? Anomalia = null, string? Feito = null, TimeOnly? HoraInicio = null, TimeOnly? HoraFim = null, string? Tecnico = null);

public sealed record EquipamentoDto(string Id, string Nome, string Zona);

public sealed record PedidoAvaria(string? EquipamentoId, string? Anomalia);

public sealed record PedidoConclusao(string? Feito, TimeOnly? Inicio, TimeOnly? Fim);

public enum ResultadoIntervencao { Ok, Conflito, NaoExiste }

// O nome vai em tecnome (45); nas colunas de auditoria vai o código (30).
public sealed record TecnicoAtual(string Codigo, string Nome);

public static class RegrasIntervencao
{
    public const int MaxAnomalia = 200, MaxFeito = 254;

    public static string? ValidarAvaria(string? equipamentoId, string? anomalia) =>
        string.IsNullOrWhiteSpace(equipamentoId) || equipamentoId.Length > 25 ? "Equipamento em falta."
        : string.IsNullOrWhiteSpace(anomalia) || anomalia.Trim().Length > MaxAnomalia ? "Descrição em falta ou demasiado longa."
        : null;

    public static string? ValidarConclusao(string? feito, TimeOnly? inicio, TimeOnly? fim) =>
        string.IsNullOrWhiteSpace(feito) || feito.Trim().Length > MaxFeito ? "Descrição em falta ou demasiado longa."
        : inicio is null || fim is null || fim < inicio ? "Horas inválidas."
        : null;
}

// Tudo é pedido em nome de um técnico (o nome gravado nas fichas); um técnico só mexe no que é seu ou ainda não tem dono.
public interface IRepositorioIntervencoes
{
    Task<IReadOnlyList<IntervencaoDto>> ListarAsync(TecnicoAtual tecnico, CancellationToken ct);
    Task<IntervencaoDto?> ObterAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    Task<IReadOnlyList<EquipamentoDto>> PesquisarEquipamentosAsync(string texto, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> RegistarAvariaAsync(string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> IniciarAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> ConcluirAsync(string id, TecnicoAtual tecnico, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct);
    // Fichas concluídas por validar (de qualquer técnico). Quem concluiu não valida a própria ficha.
    Task<IReadOnlyList<IntervencaoDto>> ListarPorValidarAsync(CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> ValidarAsync(string id, TecnicoAtual supervisor, CancellationToken ct);
}

public static class EndpointsIntervencoes
{
    public static void MapIntervencoes(this WebApplication app)
    {
        var g = app.MapGroup("").RequireAuthorization();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            if (ctx.HttpContext.Request.RouteValues.TryGetValue("id", out var id) && id?.ToString()?.Length > 25) return Results.NotFound();
            try { return await next(ctx); }
            catch (RepositorioIndisponivelException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });

        g.MapGet("/intervencoes", async (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            await Tecnico(eu, utilizadores, ct) is { } t ? Results.Ok(await repo.ListarAsync(t, ct)) : Results.Forbid());

        g.MapGet("/intervencoes/{id}", async (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (await Tecnico(eu, utilizadores, ct) is not { } t) return Results.Forbid();
            return await repo.ObterAsync(id, t, ct) is { } i ? Results.Ok(i) : Results.NotFound();
        });

        g.MapGet("/equipamentos", async (string? q, IRepositorioIntervencoes repo, CancellationToken ct) =>
            Results.Ok(string.IsNullOrWhiteSpace(q) || q.Trim().Length is < 2 or > 60 ? [] : await repo.PesquisarEquipamentosAsync(q.Trim(), ct)));

        g.MapPost("/intervencoes/avaria", async (PedidoAvaria p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (RegrasIntervencao.ValidarAvaria(p.EquipamentoId, p.Anomalia) is not null) return Results.BadRequest();
            if (await Tecnico(eu, utilizadores, ct) is not { } t) return Results.Forbid();
            return Resposta(await repo.RegistarAvariaAsync(p.EquipamentoId!, p.Anomalia!.Trim(), t, ct));
        });

        g.MapPost("/intervencoes/{id}/iniciar", async (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            await Tecnico(eu, utilizadores, ct) is { } t ? Resposta(await repo.IniciarAsync(id, t, ct)) : Results.Forbid());

        g.MapGet("/validacao", async (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            await Supervisor(eu, utilizadores, ct) is not null ? Results.Ok(await repo.ListarPorValidarAsync(ct)) : Results.Forbid())
            .RequireAuthorization("Supervisor");

        g.MapPost("/validacao/{id}", async (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            await Supervisor(eu, utilizadores, ct) is { } s ? Resposta(await repo.ValidarAsync(id, s, ct)) : Results.Forbid())
            .RequireAuthorization("Supervisor");

        g.MapPost("/intervencoes/{id}/concluir", async (string id, PedidoConclusao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
        {
            if (RegrasIntervencao.ValidarConclusao(p.Feito, p.Inicio, p.Fim) is not null) return Results.BadRequest();
            if (await Tecnico(eu, utilizadores, ct) is not { } t) return Results.Forbid();
            return Resposta(await repo.ConcluirAsync(id, t, p.Feito!.Trim(), p.Inicio!.Value, p.Fim!.Value, ct));
        });
    }

    // Nome do técnico tal como está na tabela de utilizadores; sem utilizador ativo não há acesso.
    private static async Task<TecnicoAtual?> Tecnico(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct)
    {
        var codigo = eu.FindFirstValue("sub") ?? eu.FindFirstValue(ClaimTypes.NameIdentifier);
        if (codigo is null) return null;
        var u = await utilizadores.ObterAsync(codigo, ct);
        return u is { Ativo: true, PinProvisorio: false } ? new TecnicoAtual(u.Codigo, Truncar(u.Nome)) : null;
    }

    // O token dura um turno; a validação confirma na base que o supervisor continua ativo.
    private static async Task<TecnicoAtual?> Supervisor(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct)
    {
        var codigo = eu.FindFirstValue("sub") ?? eu.FindFirstValue(ClaimTypes.NameIdentifier);
        var u = codigo is null ? null : await utilizadores.ObterAsync(codigo, ct);
        return u is { Ativo: true, Perfil: Perfil.Supervisor, PinProvisorio: false } ? new TecnicoAtual(u.Codigo, Truncar(u.Nome)) : null;
    }

    // Na ficha, o nome do técnico cabe em 45 caracteres.
    internal static string Truncar(string nome) => nome.Length > 45 ? nome[..45] : nome;

    private static IResult Resposta((ResultadoIntervencao Resultado, IntervencaoDto? Dados) r) => r.Resultado switch
    {
        ResultadoIntervencao.Ok => Results.Ok(r.Dados),
        ResultadoIntervencao.NaoExiste => Results.NotFound(),
        _ => Results.Conflict()
    };
}

// Para desenvolvimento e testes.
public sealed class RepositorioIntervencoesMemoria(TimeProvider relogio) : IRepositorioIntervencoes
{
    private readonly object _trinco = new();
    private static readonly EquipamentoDto[] Equipamentos =
    [
        new("E1", "Compressor de ar 02", "Produção"), new("E2", "Empilhador 05", "Armazém"), new("E3", "Câmara de frio 01", "Frio")
    ];
    private readonly List<(IntervencaoDto I, string? Dono)> _lista =
    [
        (new("F1", "Compressor de ar 02", "Produção", TipoIntervencao.Preventiva, DateOnly.FromDateTime(DateTime.Today), EstadoIntervencao.Planeada), null),
        (new("F2", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, DateOnly.FromDateTime(DateTime.Today), EstadoIntervencao.Planeada), "Outra Pessoa"),
        (new("F3", "Balança 03", "Expedição", TipoIntervencao.Curativa, DateOnly.FromDateTime(DateTime.Today), EstadoIntervencao.Concluida,
            "Mostrador apagado", "Trocado o cabo", new TimeOnly(8, 0), new TimeOnly(9, 0)), "Ana Silva")
    ];

    private static bool Meu(string? dono, TecnicoAtual t) => dono is null || dono == t.Nome;

    public Task<IReadOnlyList<IntervencaoDto>> ListarAsync(TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<IntervencaoDto>>(_lista
                .Where(x => Meu(x.Dono, tecnico) && x.I.Estado is EstadoIntervencao.Planeada or EstadoIntervencao.EmCurso).Select(x => x.I).ToList());
    }

    public Task<IntervencaoDto?> ObterAsync(string id, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (_trinco) return Task.FromResult(_lista.Where(x => x.I.Id == id && Meu(x.Dono, tecnico)).Select(x => x.I).FirstOrDefault());
    }

    public Task<IReadOnlyList<EquipamentoDto>> PesquisarEquipamentosAsync(string texto, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EquipamentoDto>>(Equipamentos.Where(e => e.Nome.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<(ResultadoIntervencao, IntervencaoDto?)> RegistarAvariaAsync(string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct)
    {
        var e = Equipamentos.FirstOrDefault(x => x.Id == equipamentoId);
        if (e is null) return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.NaoExiste, null));
        var agora = relogio.GetLocalNow();
        var nova = new IntervencaoDto(Guid.NewGuid().ToString("N")[..25], e.Nome, e.Zona, TipoIntervencao.Curativa, DateOnly.FromDateTime(agora.DateTime),
            EstadoIntervencao.EmCurso, anomalia, null, TimeOnly.FromDateTime(agora.DateTime));
        lock (_trinco) _lista.Add((nova, tecnico.Nome));
        return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, nova));
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> IniciarAsync(string id, TecnicoAtual tecnico, CancellationToken ct) =>
        Alterar(id, tecnico, EstadoIntervencao.Planeada, i => i with { Estado = EstadoIntervencao.EmCurso, HoraInicio = TimeOnly.FromDateTime(relogio.GetLocalNow().DateTime) });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> ConcluirAsync(string id, TecnicoAtual tecnico, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct) =>
        Alterar(id, tecnico, EstadoIntervencao.EmCurso, i => i with { Estado = EstadoIntervencao.Concluida, Feito = feito, HoraInicio = inicio, HoraFim = fim });

    public Task<IReadOnlyList<IntervencaoDto>> ListarPorValidarAsync(CancellationToken ct)
    {
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<IntervencaoDto>>(_lista.Where(x => x.I.Estado == EstadoIntervencao.Concluida)
                .Select(x => x.I with { Tecnico = x.Dono }).ToList());
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> ValidarAsync(string id, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (_trinco)
        {
            var n = _lista.FindIndex(x => x.I.Id == id);
            if (n < 0) return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.NaoExiste, null));
            if (_lista[n].I.Estado != EstadoIntervencao.Concluida || _lista[n].Dono == supervisor.Nome)
                return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Conflito, null));
            _lista[n] = (_lista[n].I with { Estado = EstadoIntervencao.Validada }, _lista[n].Dono);
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, _lista[n].I with { Tecnico = _lista[n].Dono }));
        }
    }

    private Task<(ResultadoIntervencao, IntervencaoDto?)> Alterar(string id, TecnicoAtual tecnico, EstadoIntervencao de, Func<IntervencaoDto, IntervencaoDto> muda)
    {
        lock (_trinco)
        {
            var n = _lista.FindIndex(x => x.I.Id == id && Meu(x.Dono, tecnico));
            if (n < 0) return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.NaoExiste, null));
            if (_lista[n].I.Estado != de) return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Conflito, null));
            _lista[n] = (muda(_lista[n].I), tecnico.Nome);
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, _lista[n].I));
        }
    }
}

// Fichas de intervenção do PHC (u_fichaint) e equipamentos (u_equip). Ver docs/ligacao-phc-intervencoes.md.
// O estado "em curso" não existe no PHC: é uma ficha planeada com hora de início preenchida.
public sealed class SqlRepositorioIntervencoes(IConfiguration configuracao, TimeProvider relogio, ILogger<SqlRepositorioIntervencoes> log)
    : IRepositorioIntervencoes
{
    private static readonly DateTime SemData = new(1900, 1, 1);

    private const string Selecao =
        """
        select f.u_fichaintstamp, case when f.design <> '' then f.design else isnull(e.design, '') end, isnull(e.[local], ''),
               f.tipointerv, f.data,
               case when f.valinis <> '' then 'Validada' when f.estado = 'Concluida' then 'Concluida'
                    when f.estado = 'Cancelada' then 'Cancelada' when f.hini > '19000101' then 'EmCurso' else 'Planeada' end,
               f.problema, f.resolucao, f.hini, f.hfim, f.tecnome
        from u_fichaint f left join u_equip e on e.u_equipstamp = f.u_equipstamp
        """;

    // Fichas ainda por fazer, deste técnico ou sem dono, dos últimos 180 dias até aos próximos 14.
    private const string SqlListar = Selecao +
        """

        where f.fechada = 0 and f.obsoleto = 0 and f.estado = 'Planeada' and f.valinis = ''
          and f.data >= @desde and f.data <= @ate and (f.tecnome = @tecnico or (f.tecno = 0 and f.tecnome = ''))
        order by f.data, f.u_fichaintstamp
        """;

    private const string SqlObter = Selecao +
        """

        where f.u_fichaintstamp = @id and f.fechada = 0 and f.obsoleto = 0 and (f.tecnome = @tecnico or (f.tecno = 0 and f.tecnome = ''))
        """;

    private const string SqlObterQualquer = Selecao +
        """

        where f.u_fichaintstamp = @id and f.fechada = 0 and f.obsoleto = 0
        """;

    // Concluídas e ainda sem validação, do último ano; as mais recentes primeiro.
    private const string SqlPorValidar = Selecao +
        """

        where f.fechada = 0 and f.obsoleto = 0 and f.estado = 'Concluida' and f.valinis = '' and f.data >= @desde
        order by f.data desc, f.u_fichaintstamp
        """;

    private const string SqlValidar =
        """
        update u_fichaint set valinis = @codigo, valdata = @agora, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and fechada = 0 and obsoleto = 0 and estado = 'Concluida' and valinis = '' and tecnome <> @tecnico
        """;

    private const string SqlEquipamentos =
        """
        select top 20 u_equipstamp, design, isnull([local], '') from u_equip
        where obsoleto = 0 and (design like @texto escape '\' or ref like @texto escape '\')
        order by design
        """;

    private const string SqlAvaria =
        """
        insert into u_fichaint (u_fichaintstamp, ref, design, tipointerv, tipomanut, problema, data, estado, u_equipstamp, hini, tecnome,
                                ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        select @stamp, left(e.ref, 20), left(e.design, 45), 'Curativa', 'Interna', @anomalia, @dia, 'Planeada', e.u_equipstamp, @agora, @tecnico,
               @codigo, @dia, @hora, @codigo, @dia, @hora
        from u_equip e where e.u_equipstamp = @equipamento and e.obsoleto = 0
        """;

    private const string SqlIniciar =
        """
        update u_fichaint set hini = @agora, tecnome = @tecnico, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and fechada = 0 and obsoleto = 0 and estado = 'Planeada' and valinis = '' and hini <= '19000101'
          and (tecnome = @tecnico or (tecno = 0 and tecnome = ''))
        """;

    private const string SqlConcluir =
        """
        update u_fichaint set estado = 'Concluida', resolucao = @feito, hini = @inicio, hfim = @fim, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and fechada = 0 and obsoleto = 0 and estado = 'Planeada' and valinis = '' and hini > '19000101' and tecnome = @tecnico
        """;

    private SqlConnection Abrir()
    {
        var ligacao = configuracao.GetConnectionString("PHCConn");
        if (string.IsNullOrWhiteSpace(ligacao)) throw new RepositorioIndisponivelException();
        return new SqlConnection(ligacao);
    }

    private async Task<T> Executar<T>(Func<SqlConnection, Task<T>> trabalho)
    {
        try
        {
            await using var conn = Abrir();
            await conn.OpenAsync();
            return await trabalho(conn);
        }
        catch (SqlException ex)
        {
            log.LogError("Falha SQL nas intervenções (erro {Numero}).", ex.Number);
            throw new RepositorioIndisponivelException(ex);
        }
    }

    private static void Texto(SqlCommand cmd, string nome, int tamanho, string valor) =>
        cmd.Parameters.Add(nome, SqlDbType.VarChar, tamanho).Value = valor;

    private static void Momento(SqlCommand cmd, string nome, DateTime valor) =>
        cmd.Parameters.Add(nome, SqlDbType.DateTime).Value = valor;

    private void Auditoria(SqlCommand cmd, TecnicoAtual tecnico, DateTime agora)
    {
        Texto(cmd, "@tecnico", 45, tecnico.Nome);
        Texto(cmd, "@codigo", 30, tecnico.Codigo);
        Momento(cmd, "@agora", agora);
        Momento(cmd, "@dia", agora.Date);
        Texto(cmd, "@hora", 8, agora.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static IntervencaoDto Ler(SqlDataReader r)
    {
        var tipo = string.Equals(r.GetString(3).Trim(), "Curativa", StringComparison.OrdinalIgnoreCase) ? TipoIntervencao.Curativa : TipoIntervencao.Preventiva;
        var estado = Enum.Parse<EstadoIntervencao>(r.GetString(5));
        var hini = r.GetDateTime(8);
        var hfim = r.GetDateTime(9);
        return new IntervencaoDto(
            r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim(), tipo, DateOnly.FromDateTime(r.GetDateTime(4)), estado,
            Vazio(r.GetString(6)), Vazio(r.GetString(7)),
            hini > SemData ? TimeOnly.FromDateTime(hini) : null, hfim > SemData ? TimeOnly.FromDateTime(hfim) : null, Vazio(r.GetString(10)));
    }

    private static string? Vazio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    public Task<IReadOnlyList<IntervencaoDto>> ListarAsync(TecnicoAtual tecnico, CancellationToken ct) => Executar<IReadOnlyList<IntervencaoDto>>(async conn =>
    {
        var hoje = relogio.GetLocalNow().Date;
        await using var cmd = new SqlCommand(SqlListar, conn);
        Texto(cmd, "@tecnico", 45, tecnico.Nome);
        Momento(cmd, "@desde", hoje.AddDays(-180));
        Momento(cmd, "@ate", hoje.AddDays(14));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<IntervencaoDto>();
        while (await r.ReadAsync(ct)) lista.Add(Ler(r));
        return lista;
    });

    public Task<IntervencaoDto?> ObterAsync(string id, TecnicoAtual tecnico, CancellationToken ct) => Executar(conn => LerUmaAsync(conn, id, tecnico, ct));

    private static async Task<IntervencaoDto?> LerUmaAsync(SqlConnection conn, string id, TecnicoAtual tecnico, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlObter, conn);
        Texto(cmd, "@id", 25, id);
        Texto(cmd, "@tecnico", 45, tecnico.Nome);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    }

    public Task<IReadOnlyList<IntervencaoDto>> ListarPorValidarAsync(CancellationToken ct) => Executar<IReadOnlyList<IntervencaoDto>>(async conn =>
    {
        await using var cmd = new SqlCommand(SqlPorValidar, conn);
        Momento(cmd, "@desde", relogio.GetLocalNow().Date.AddDays(-365));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<IntervencaoDto>();
        while (lista.Count < 100 && await r.ReadAsync(ct)) lista.Add(Ler(r));
        return lista;
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> ValidarAsync(string id, TecnicoAtual supervisor, CancellationToken ct) => Executar(async conn =>
    {
        await using var cmd = new SqlCommand(SqlValidar, conn);
        Texto(cmd, "@id", 25, id);
        Auditoria(cmd, supervisor, relogio.GetLocalNow().DateTime);
        var feito = await cmd.ExecuteNonQueryAsync(ct) > 0;
        await using var ler = new SqlCommand(SqlObterQualquer, conn);
        Texto(ler, "@id", 25, id);
        await using var r = await ler.ExecuteReaderAsync(ct);
        var ficha = await r.ReadAsync(ct) ? Ler(r) : null;
        return ficha is null ? (ResultadoIntervencao.NaoExiste, null) : feito ? (ResultadoIntervencao.Ok, ficha) : (ResultadoIntervencao.Conflito, null);
    });

    public Task<IReadOnlyList<EquipamentoDto>> PesquisarEquipamentosAsync(string texto, CancellationToken ct) => Executar<IReadOnlyList<EquipamentoDto>>(async conn =>
    {
        await using var cmd = new SqlCommand(SqlEquipamentos, conn);
        var escapado = texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        Texto(cmd, "@texto", 130, $"%{escapado}%");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<EquipamentoDto>();
        while (await r.ReadAsync(ct)) lista.Add(new(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim()));
        return lista;
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> RegistarAvariaAsync(string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct) => Executar(async conn =>
    {
        var agora = relogio.GetLocalNow().DateTime;
        var stamp = Guid.NewGuid().ToString("N")[..25].ToUpperInvariant();
        await using var cmd = new SqlCommand(SqlAvaria, conn);
        Texto(cmd, "@stamp", 25, stamp);
        Texto(cmd, "@anomalia", 200, anomalia);
        Texto(cmd, "@equipamento", 25, equipamentoId);
        Auditoria(cmd, tecnico, agora);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) return (ResultadoIntervencao.NaoExiste, null);
        return (ResultadoIntervencao.Ok, await LerUmaAsync(conn, stamp, tecnico, ct));
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> IniciarAsync(string id, TecnicoAtual tecnico, CancellationToken ct) => Executar(async conn =>
    {
        await using var cmd = new SqlCommand(SqlIniciar, conn);
        Texto(cmd, "@id", 25, id);
        Auditoria(cmd, tecnico, relogio.GetLocalNow().DateTime);
        return await Resultado(conn, cmd, id, tecnico, ct);
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> ConcluirAsync(string id, TecnicoAtual tecnico, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct) => Executar(async conn =>
    {
        var agora = relogio.GetLocalNow().DateTime;
        // As horas ficam no dia em que a ficha foi iniciada (hoje, se ainda não tiver hora).
        var existente = await LerUmaAsync(conn, id, tecnico, ct);
        var dia = existente is { HoraInicio: not null } ? await DiaDeInicio(conn, id, ct) ?? agora.Date : agora.Date;
        await using var cmd = new SqlCommand(SqlConcluir, conn);
        Texto(cmd, "@id", 25, id);
        Texto(cmd, "@feito", 254, feito);
        Momento(cmd, "@inicio", dia + inicio.ToTimeSpan());
        Momento(cmd, "@fim", dia + fim.ToTimeSpan());
        Auditoria(cmd, tecnico, agora);
        return await Resultado(conn, cmd, id, tecnico, ct);
    });

    private static async Task<DateTime?> DiaDeInicio(SqlConnection conn, string id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("select hini from u_fichaint where u_fichaintstamp = @id", conn);
        Texto(cmd, "@id", 25, id);
        return await cmd.ExecuteScalarAsync(ct) is DateTime d && d > SemData ? d.Date : null;
    }

    private static async Task<(ResultadoIntervencao, IntervencaoDto?)> Resultado(SqlConnection conn, SqlCommand cmd, string id, TecnicoAtual tecnico, CancellationToken ct)
    {
        if (await cmd.ExecuteNonQueryAsync(ct) > 0) return (ResultadoIntervencao.Ok, await LerUmaAsync(conn, id, tecnico, ct));
        return (await LerUmaAsync(conn, id, tecnico, ct) is null ? ResultadoIntervencao.NaoExiste : ResultadoIntervencao.Conflito, null);
    }
}
