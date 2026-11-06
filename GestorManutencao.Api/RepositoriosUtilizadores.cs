using System.Data;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Para desenvolvimento e testes: guarda tudo em memória.
public sealed class RepositorioUtilizadoresMemoria : IRepositorioUtilizadores
{
    private readonly object _trinco = new();
    private readonly Dictionary<string, (UtilizadorApp U, string Hash)> _dados;
    private long _versao = 1;

    public RepositorioUtilizadoresMemoria(IEnumerable<(UtilizadorApp U, string Hash)>? iniciais = null) =>
        _dados = (iniciais ?? []).ToDictionary(x => x.U.Codigo, x => x);

    public void Desativar(string codigo)
    {
        lock (_trinco) _dados[codigo] = (_dados[codigo].U with { Ativo = false, Versao = ++_versao }, _dados[codigo].Hash);
    }

    public Task<UtilizadorApp?> ObterPorPinAsync(string pinHash, CancellationToken ct)
    {
        lock (_trinco) return Task.FromResult(_dados.Values.FirstOrDefault(x => x.Hash == pinHash && x.U.Ativo).U);
    }

    public Task<UtilizadorApp?> ObterAsync(string codigo, CancellationToken ct)
    {
        lock (_trinco) return Task.FromResult(_dados.TryGetValue(codigo, out var x) ? x.U : null);
    }

    public Task<IReadOnlyList<UtilizadorApp>> ListarAsync(CancellationToken ct)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<UtilizadorApp>>(_dados.Values.Select(x => x.U).OrderBy(u => u.Nome).ToList());
    }

    public Task<ResultadoPin> CriarOperadorAsync(string codigo, string nome, string pinHash, string porQuem, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (_dados.ContainsKey(codigo)) return Task.FromResult(ResultadoPin.JaExiste);
            if (_dados.Values.Any(x => x.Hash == pinHash)) return Task.FromResult(ResultadoPin.PinEmUso);
            _dados[codigo] = (new UtilizadorApp(codigo, nome, Perfil.Operador, true, true), pinHash);
            return Task.FromResult(ResultadoPin.Ok);
        }
    }

    public Task<ResultadoPin> DefinirPinAsync(string codigo, string pinHash, bool provisorio, string porQuem, CancellationToken ct, string? pinHashAtual = null)
    {
        lock (_trinco)
        {
            if (!_dados.TryGetValue(codigo, out var x) || (pinHashAtual is not null && x.Hash != pinHashAtual))
                return Task.FromResult(ResultadoPin.NaoExiste);
            if (_dados.Values.Any(o => o.Hash == pinHash && o.U.Codigo != codigo)) return Task.FromResult(ResultadoPin.PinEmUso);
            _dados[codigo] = (x.U with { PinProvisorio = provisorio, Versao = ++_versao }, pinHash);
            return Task.FromResult(ResultadoPin.Ok);
        }
    }
}

// Tabela u_manutil do PHC (ver docs/tabela-utilizadores.md). A cadeia de ligação vem do appsettings.
public sealed class SqlRepositorioUtilizadores(IConfiguration configuracao, TimeProvider relogio, ILogger<SqlRepositorioUtilizadores> log)
    : IRepositorioUtilizadores
{
    private const string Colunas = "utilizador, nome, perfil, ativo, pinmudar, pinalteradoem";

    private const string SqlPorPin = $"select {Colunas} from u_manutil where pinhash = @hash and ativo = 1";
    private const string SqlPorCodigo = $"select {Colunas} from u_manutil where utilizador = @codigo";
    private const string SqlListar = $"select {Colunas} from u_manutil with (nolock) order by nome";

    private const string SqlCriar =
        """
        insert into u_manutil (u_manutilstamp, utilizador, nome, perfil, ativo, pinhash, pinmudar,
                               pinalteradopor, pinalteradoem, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        values (@stamp, @codigo, @nome, 'OPERADOR', 1, @hash, 1, @por, @agora, @por, @dia, @hora, @por, @dia, @hora)
        """;

    private const string SqlDefinirPin =
        """
        update u_manutil
        set pinhash = @hash, pinmudar = @provisorio, pinalteradopor = @por, pinalteradoem = @agora,
            usrinis = @por, usrdata = @dia, usrhora = @hora
        where utilizador = @codigo and (@hashAtual is null or pinhash = @hashAtual)
        """;

    private SqlConnection Abrir()
    {
        var ligacao = configuracao.GetConnectionString("PHCConn");
        if (string.IsNullOrWhiteSpace(ligacao)) throw new RepositorioIndisponivelException();
        return new SqlConnection(ligacao);
    }

    private static UtilizadorApp Ler(SqlDataReader r) => new(
        r.GetString(0).Trim(), r.GetString(1).Trim(),
        string.Equals(r.GetString(2).Trim(), "SUPERVISOR", StringComparison.OrdinalIgnoreCase) ? Perfil.Supervisor : Perfil.Operador,
        r.GetBoolean(3), r.GetBoolean(4), r.IsDBNull(5) ? 0 : r.GetDateTime(5).Ticks);

    private static void Texto(SqlCommand cmd, string nome, int tamanho, string valor) =>
        cmd.Parameters.Add(nome, SqlDbType.VarChar, tamanho).Value = valor;

    private async Task<T> Executar<T>(Func<SqlConnection, Task<T>> trabalho)
    {
        try
        {
            await using var conn = Abrir();
            await conn.OpenAsync();
            return await trabalho(conn);
        }
        catch (SqlException ex) when (ex.Number is not (2601 or 2627))
        {
            // Só o número do erro, nunca a mensagem (pode trazer valores).
            log.LogError("Falha SQL nos utilizadores (erro {Numero}).", ex.Number);
            throw new RepositorioIndisponivelException(ex);
        }
    }

    public Task<UtilizadorApp?> ObterPorPinAsync(string pinHash, CancellationToken ct) => Executar(async conn =>
    {
        await using var cmd = new SqlCommand(SqlPorPin, conn);
        Texto(cmd, "@hash", 64, pinHash);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    });

    public Task<UtilizadorApp?> ObterAsync(string codigo, CancellationToken ct) => Executar(async conn =>
    {
        await using var cmd = new SqlCommand(SqlPorCodigo, conn);
        Texto(cmd, "@codigo", 30, codigo);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    });

    public Task<IReadOnlyList<UtilizadorApp>> ListarAsync(CancellationToken ct) => Executar<IReadOnlyList<UtilizadorApp>>(async conn =>
    {
        await using var cmd = new SqlCommand(SqlListar, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<UtilizadorApp>();
        while (await r.ReadAsync(ct)) lista.Add(Ler(r));
        return lista;
    });

    public async Task<ResultadoPin> CriarOperadorAsync(string codigo, string nome, string pinHash, string porQuem, CancellationToken ct)
    {
        try
        {
            return await Executar(async conn =>
            {
                await using var cmd = new SqlCommand(SqlCriar, conn);
                Texto(cmd, "@stamp", 25, NovoStamp());
                Texto(cmd, "@codigo", 30, codigo);
                Texto(cmd, "@nome", 60, nome);
                Texto(cmd, "@hash", 64, pinHash);
                Auditoria(cmd, porQuem);
                await cmd.ExecuteNonQueryAsync(ct);
                return ResultadoPin.Ok;
            });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Duas restrições únicas (utilizador e PIN): vê-se qual falhou perguntando se o utilizador já existe.
            return await ObterAsync(codigo, ct) is null ? ResultadoPin.PinEmUso : ResultadoPin.JaExiste;
        }
    }

    public async Task<ResultadoPin> DefinirPinAsync(string codigo, string pinHash, bool provisorio, string porQuem, CancellationToken ct, string? pinHashAtual = null)
    {
        try
        {
            return await Executar(async conn =>
            {
                await using var cmd = new SqlCommand(SqlDefinirPin, conn);
                Texto(cmd, "@codigo", 30, codigo);
                Texto(cmd, "@hash", 64, pinHash);
                cmd.Parameters.Add("@provisorio", SqlDbType.Bit).Value = provisorio;
                cmd.Parameters.Add("@hashAtual", SqlDbType.VarChar, 64).Value = (object?)pinHashAtual ?? DBNull.Value;
                Auditoria(cmd, porQuem);
                return await cmd.ExecuteNonQueryAsync(ct) == 0 ? ResultadoPin.NaoExiste : ResultadoPin.Ok;
            });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return ResultadoPin.PinEmUso;
        }
    }

    private void Auditoria(SqlCommand cmd, string porQuem)
    {
        var agora = relogio.GetLocalNow().DateTime;
        Texto(cmd, "@por", 30, porQuem.Length > 30 ? porQuem[..30] : porQuem);
        cmd.Parameters.Add("@agora", SqlDbType.DateTime).Value = agora;
        cmd.Parameters.Add("@dia", SqlDbType.DateTime).Value = agora.Date;
        Texto(cmd, "@hora", 8, agora.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string NovoStamp() => Guid.NewGuid().ToString("N")[..25].ToUpperInvariant();
}
