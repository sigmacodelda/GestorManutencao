using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Gestão sobre as tabelas do PHC, como a LandeiroMaui (ManutencaoService): u_equip, u_itmanut, u_itmanutli, u_periocid, u_histstats, u_marcaequip e fl.
// Escritas em transação, com auditoria; a chave de versão é usrdata + usrhora (formato yyyyMMdd + HH:mm:ss).
public sealed partial class SqlRepositorioIntervencoes : IRepositorioGestao
{
    private const string Concorrencia = "convert(varchar(8), {0}.usrdata, 112) + isnull({0}.usrhora, '')";

    private static readonly string ConcorrenciaE = string.Format(Concorrencia, "e"), ConcorrenciaI = string.Format(Concorrencia, "i"), ConcorrenciaL = string.Format(Concorrencia, "l"), ConcorrenciaP = string.Format(Concorrencia, "p");

    private static readonly string SqlEquipGestao =
        $"""
        select e.u_equipstamp, isnull(e.nointerno, 0), isnull(e.ref, ''), isnull(e.design, ''), isnull(e.tipo, ''), isnull(e.marca, ''), isnull(e.modelo, ''), isnull(e.serie, ''),
               isnull(cast(e.anoaq as int), 0), isnull(e.[aquisiçao], 0), isnull(e.nofornec, 0), isnull(e.nomeforn, ''), e.data, isnull(e.[local], ''), isnull(e.status, ''), e.obsoleto,
               isnull(e.manpdf, ''), isnull(e.email, ''), {ConcorrenciaE}
        from u_equip e with (nolock)
        where e.u_equipstamp = @id and isnull(e.tipo, '') <> @veiculos
        """;

    private const string SqlRefRepetida = "select count(*) from u_equip with (updlock, holdlock) where ref = @ref and u_equipstamp <> @id";

    private const string SqlProximoNoInterno = "select isnull(max(nointerno), 0) + 1 from u_equip with (updlock, holdlock)";

    private const string SqlNovoEquip =
        """
        insert into u_equip (u_equipstamp, nointerno, ref, design, tipo, marca, modelo, serie, anoaq, [aquisiçao], nofornec, nomeforn, data, [local], status, obsoleto, manpdf, email,
                             ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        values (@id, @no, @ref, @design, @tipo, @marca, @modelo, @serie, @ano, @valor, @nofornec, @nomeforn, @data, @local, @status, @obsoleto, @manpdf, @email,
                @codigo, @dia, @hora, @codigo, @dia, @hora);
        insert into u_itmanut (u_itmanutstamp, u_equipstamp, ref, design, data, versao, obsoleto, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        values (@instrucao, @id, @ref, @design, @dia, '', 0, @codigo, @dia, @hora, @codigo, @dia, @hora);
        """;

    private static readonly string SqlEditarEquip =
        $"""
        update u_equip set ref = @ref, design = @design, tipo = @tipo, marca = @marca, modelo = @modelo, serie = @serie, anoaq = @ano, [aquisiçao] = @valor,
               nofornec = @nofornec, nomeforn = @nomeforn, data = @data, [local] = @local, status = @status, obsoleto = @obsoleto, manpdf = @manpdf, email = @email,
               usrinis = @codigo, usrdata = @dia, usrhora = @hora
        from u_equip e
        where e.u_equipstamp = @id and isnull(e.tipo, '') <> @veiculos and (@versao = '' or {ConcorrenciaE} = @versao)
        """;

    private const string SqlHistorico =
        """
        insert into u_histstats (u_histstatsstamp, u_equipstamp, data, status, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        values (@hist, @id, @dia, @status, @codigo, @dia, @hora, @codigo, @dia, @hora)
        """;

    private const string SqlListaHistorico =
        "select data, isnull(status, ''), isnull(ousrinis, '') from u_histstats with (nolock) where u_equipstamp = @id order by data desc, ousrhora desc";

    private const string SqlPlaneadasDoEquipLeitura = "select count(*) from u_fichaint with (nolock) where u_equipstamp = @id and estado = 'Planeada'";

    private const string SqlEquipExiste = "select count(*) from u_equip with (nolock) where u_equipstamp = @id and isnull(tipo, '') <> @veiculos";

    private const string SqlTipos = "select distinct ltrim(rtrim(tipo)) from u_equip with (nolock) where ltrim(rtrim(tipo)) <> '' and tipo <> @veiculos order by 1";

    private const string SqlMarcas = "select distinct ltrim(rtrim(marca)) from u_marcaequip with (nolock) where ltrim(rtrim(marca)) <> '' and marca not like '-%' order by 1";

    private const string SqlLocais = "select distinct ltrim(rtrim([local])) from u_equip with (nolock) where ltrim(rtrim([local])) <> '' and [local] not like '-%' and tipo <> @veiculos order by 1";

    private static readonly string SelInstrucao =
        $"""
        select i.u_itmanutstamp, i.u_equipstamp, isnull(i.versao, ''), i.data, i.obsoleto, isnull(cast(i.obs as varchar(max)), ''),
               (select count(*) from u_itmanutli x with (nolock) where x.u_itmanutstamp = i.u_itmanutstamp), {ConcorrenciaI}
        from u_itmanut i with (nolock) join u_equip e with (nolock) on e.u_equipstamp = i.u_equipstamp
        where isnull(e.tipo, '') <> @veiculos and
        """;

    private static readonly string SqlInstrucoesDoEquip = SelInstrucao + " i.u_equipstamp = @id order by i.obsoleto, i.data desc, i.versao desc";

    private static readonly string SqlInstrucaoUma = SelInstrucao + " i.u_itmanutstamp = @id";

    private const string SqlNovaInstrucao =
        """
        insert into u_itmanut (u_itmanutstamp, u_equipstamp, ref, design, versao, data, obsoleto, obs, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        select @id, e.u_equipstamp, e.ref, e.design, @versao, @data, @obsoleto, @obs, @codigo, @dia, @hora, @codigo, @dia, @hora
        from u_equip e where e.u_equipstamp = @equip and isnull(e.tipo, '') <> @veiculos
        """;

    private static readonly string SqlEditarInstrucao =
        $"""
        update u_itmanut set versao = @versao, data = @data, obsoleto = @obsoleto, obs = @obs, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        from u_itmanut i
        where i.u_itmanutstamp = @id and (@versao_lida = '' or {ConcorrenciaI} = @versao_lida)
        """;

    private static readonly string SelTarefa =
        $"""
        select l.u_itmanutlistamp, l.u_itmanutstamp, isnull(l.periodo, ''), isnull(l.responsabilidade, ''), isnull(l.responsavel, ''),
               isnull(cast(l.descricao as varchar(max)), ''), isnull(l.tipomanut, ''), l.ativo, {ConcorrenciaL}
        from u_itmanutli l with (nolock)
        join u_itmanut i with (nolock) on i.u_itmanutstamp = l.u_itmanutstamp
        join u_equip e with (nolock) on e.u_equipstamp = i.u_equipstamp
        where isnull(e.tipo, '') <> @veiculos and
        """;

    private static readonly string SqlTarefasDaInstrucao = SelTarefa + " i.u_itmanutstamp = @id order by l.periodo, l.ousrdata, l.ousrhora";

    private static readonly string SqlTarefaUma = SelTarefa + " l.u_itmanutlistamp = @id";

    private const string SqlNovaTarefa =
        """
        insert into u_itmanutli (u_itmanutlistamp, u_itmanutstamp, periodo, responsabilidade, responsavel, descricao, ref, design, no, tipomanut, ativo,
                                 ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        select @id, i.u_itmanutstamp, @periodo, @responsabilidade, @responsavel, @descricao, i.ref, i.design, 0, @tipomanut, @ativo, @codigo, @dia, @hora, @codigo, @dia, @hora
        from u_itmanut i where i.u_itmanutstamp = @instrucao and i.obsoleto = 0
        """;

    private static readonly string SqlEditarTarefa =
        $"""
        update u_itmanutli set periodo = @periodo, responsabilidade = @responsabilidade, responsavel = @responsavel, descricao = @descricao, tipomanut = @tipomanut,
               ativo = @ativo, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        from u_itmanutli l
        where l.u_itmanutlistamp = @id and (@versao = '' or {ConcorrenciaL} = @versao)
        """;

    private const string SqlPeriodoConhecido =
        """
        select count(*) from (select left(ltrim(rtrim(cast(periodo as varchar(254)))), 20) as p from u_periocid with (nolock)
                              union select ltrim(rtrim(periodo)) from u_itmanutli with (nolock)) x
        where p = @periodo
        """;

    // Dentro da transação do apagar, sem bloqueios: basta saber se existe alguma ficha feita.
    private const string SqlFichasFeitasDaTarefa =
        "select count(*) from u_fichaint with (nolock) where uitmanutlistamp = @id and estado not in ('Planeada', 'Cancelada')";

    private const string SqlApagarTarefa = "delete from u_itmanutli where u_itmanutlistamp = @id";

    private static readonly string SelPeriodicidade =
        $"""
        select p.u_periocidstamp, left(ltrim(rtrim(cast(p.periodo as varchar(254)))), 254), ltrim(rtrim(p.unidade)), isnull(p.intervalo, 0), isnull(p.analise, 0), {ConcorrenciaP}
        from u_periocid p with (nolock)
        """;

    private static readonly string SqlPeriodicidades = SelPeriodicidade +
        " order by case ltrim(rtrim(p.unidade)) when 'dias' then 0 when 'meses' then 1 else 2 end, p.intervalo, p.periodo";

    private static readonly string SqlPeriodicidadeUma = SelPeriodicidade + " where p.u_periocidstamp = @id";

    private static readonly string SqlEditarPeriodicidade =
        $"""
        update u_periocid set unidade = @unidade, intervalo = @intervalo, analise = @analise, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        from u_periocid p
        where p.u_periocidstamp = @id and (@versao = '' or {ConcorrenciaP} = @versao)
        """;

    private static string NovoStampGestao() => Guid.NewGuid().ToString("N")[..25].ToUpperInvariant();

    private static async Task<List<string>> LerListaAsync(SqlConnection conn, string sql, bool veiculos, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn);
        if (veiculos) Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<string>();
        while (await r.ReadAsync(ct)) lista.Add(r.GetString(0).Trim());
        return lista;
    }

    public Task<OpcoesEquipamentoDto> ObterOpcoesAsync(CancellationToken ct) => Executar(async conn =>
        new OpcoesEquipamentoDto(await LerListaAsync(conn, SqlTipos, true, ct), await LerListaAsync(conn, SqlMarcas, false, ct), await LerListaAsync(conn, SqlLocais, true, ct), RegrasGestao.Estados));

    private static async Task<EquipamentoGestaoDto?> LerEquipamentoGestaoAsync(SqlConnection conn, string id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlEquipGestao, conn);
        Texto(cmd, "@id", 25, id);
        Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new EquipamentoGestaoDto(
            r.GetString(0).Trim(), (int)Dec(r, 1), r.GetString(2).Trim(), r.GetString(3).Trim(), r.GetString(4).Trim(), r.GetString(5).Trim(), r.GetString(6).Trim(), r.GetString(7).Trim(),
            (int)Dec(r, 8), Dec(r, 9), (long)Dec(r, 10), r.GetString(11).Trim(), r.IsDBNull(12) || r.GetDateTime(12) <= SemData ? null : DateOnly.FromDateTime(r.GetDateTime(12)),
            r.GetString(13).Trim(), r.GetString(14).Trim(), Bool(r, 15), r.GetString(16).Trim(), r.GetString(17).Trim(), r.IsDBNull(18) ? "" : r.GetString(18));
    }

    public Task<EquipamentoGestaoDto?> ObterEquipamentoGestaoAsync(string id, CancellationToken ct) => Executar(conn => LerEquipamentoGestaoAsync(conn, id, ct));

    private static async Task<int> ContarAsync(SqlConnection conn, SqlTransaction? tx, string sql, Action<SqlCommand> parametros, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn, tx);
        parametros(cmd);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    public Task<int?> ContarPlaneadasEquipamentoAsync(string id, CancellationToken ct) => Executar<int?>(async conn =>
    {
        if (await ContarAsync(conn, null, SqlEquipExiste, c => { Texto(c, "@id", 25, id); Veiculos(c); }, ct) == 0) return null;
        return await ContarAsync(conn, null, SqlPlaneadasDoEquipLeitura, c => Texto(c, "@id", 25, id), ct);
    });

    public Task<IReadOnlyList<HistoricoEstadoDto>?> ListarHistoricoEstadoAsync(string equipamentoId, CancellationToken ct) => Executar<IReadOnlyList<HistoricoEstadoDto>?>(async conn =>
    {
        if (await ContarAsync(conn, null, SqlEquipExiste, c => { Texto(c, "@id", 25, equipamentoId); Veiculos(c); }, ct) == 0) return null;
        await using var cmd = new SqlCommand(SqlListaHistorico, conn);
        Texto(cmd, "@id", 25, equipamentoId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<HistoricoEstadoDto>();
        while (await r.ReadAsync(ct)) lista.Add(new HistoricoEstadoDto(DateOnly.FromDateTime(r.GetDateTime(0)), r.GetString(1).Trim(), r.GetString(2).Trim()));
        return lista;
    });

    // Planeadas (nunca as em curso) deixam de fazer sentido: equipamento obsoleto, instrução obsoleta, tarefa desligada ou apagada.
    private static async Task<int> CancelarPlaneadasAsync(SqlConnection conn, SqlTransaction tx, string onde, string alvo, string motivo, string codigo, Momento agora, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("update u_fichaint set estado = 'Cancelada', valobs = @motivo, usrinis = @codigo, usrdata = @dia, usrhora = @hora where " + onde + " and estado = 'Planeada'", conn, tx);
        Texto(cmd, "@alvo", 25, alvo);
        Texto(cmd, "@motivo", 254, motivo);
        Auditoria(cmd, codigo, agora);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    // Se o rollback falhar, a exceção que interessa é a original (a que fez desfazer), não a do rollback.
    private static async Task RollbackSemEsconderAsync(SqlTransaction tx)
    {
        try { await tx.RollbackAsync(CancellationToken.None); }
        catch (Exception) { /* a transação já pode ter terminado com a ligação */ }
    }

    private const string OndeEquip = "u_equipstamp = @alvo", OndeTarefa = "uitmanutlistamp = @alvo",
        OndeInstrucao = "uitmanutlistamp in (select u_itmanutlistamp from u_itmanutli where u_itmanutstamp = @alvo)";

    // A ref única e o nº interno (máximo + 1) são uma secção crítica: o mesmo semáforo da numeração das fichas, como em tudo o que numera.
    public async Task<(ResultadoIntervencao, EquipamentoGestaoDto?)> GravarEquipamentoAsync(string? id, PedidoEquipamento pedido, TecnicoAtual supervisor, CancellationToken ct) =>
        await ComNumeracaoAsync(() => GravarEquipamentoInternoAsync(id, pedido, supervisor, ct), ct);

    private Task<(ResultadoIntervencao, EquipamentoGestaoDto?)> GravarEquipamentoInternoAsync(string? id, PedidoEquipamento pedido, TecnicoAtual supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, EquipamentoGestaoDto?)>(async conn =>
        {
            var novo = id is null;
            var agora = await AgoraAsync(conn, ct);
            EquipamentoGestaoDto atual;
            if (novo) atual = RegrasGestao.EquipamentoVazio(DateOnly.FromDateTime(agora.Dia));
            else if (await LerEquipamentoGestaoAsync(conn, id!, ct) is { } lido) atual = lido;
            else return (ResultadoIntervencao.NaoExiste, null);
            if (!novo && !string.IsNullOrEmpty(pedido.Versao) && pedido.Versao != atual.Versao) return (ResultadoIntervencao.Conflito, null);

            var nova = RegrasGestao.AplicarEquipamento(atual, pedido);
            if (nova.Ref.Length == 0 || nova.Design.Length == 0) return (ResultadoIntervencao.Invalido, null);
            if (RegrasGestao.EhViatura(nova.Tipo)) return (ResultadoIntervencao.Viatura, null);
            // O nome do fornecedor vem do número (fl); sem número, fica vazio.
            if (nova.NoFornecedor > 0)
            {
                if (nova.NoFornecedor == atual.NoFornecedor && atual.NomeFornecedor.Length > 0) nova = nova with { NomeFornecedor = atual.NomeFornecedor };
                else if (await NomeFornecedorAsync(conn, nova.NoFornecedor, ct) is { } nomeFornecedor) nova = nova with { NomeFornecedor = nomeFornecedor };
                else return (ResultadoIntervencao.Invalido, null);
            }
            else nova = nova with { NomeFornecedor = "" };

            var stamp = id ?? NovoStampGestao();
            var passaAObsoleto = nova.Obsoleto && !atual.Obsoleto;
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                // A ref não pode repetir (só se verifica no equipamento novo ou quando a ref muda: há refs já repetidas no PHC que têm de continuar a gravar).
                if ((novo || !string.Equals(nova.Ref, atual.Ref, StringComparison.OrdinalIgnoreCase))
                    && await ContarAsync(conn, tx, SqlRefRepetida, c => { Texto(c, "@ref", RegrasGestao.Ref, nova.Ref); Texto(c, "@id", 25, stamp); }, ct) > 0)
                { await tx.RollbackAsync(ct); return (ResultadoIntervencao.RefDuplicada, null); }

                await using var cmd = new SqlCommand(novo ? SqlNovoEquip : SqlEditarEquip, conn, tx);
                Texto(cmd, "@id", 25, stamp);
                Texto(cmd, "@ref", RegrasGestao.Ref, nova.Ref);
                Texto(cmd, "@design", RegrasGestao.Design, nova.Design);
                Texto(cmd, "@tipo", RegrasGestao.Tipo, nova.Tipo);
                Texto(cmd, "@marca", RegrasGestao.Marca, nova.Marca);
                Texto(cmd, "@modelo", RegrasGestao.Modelo, nova.Modelo);
                Texto(cmd, "@serie", RegrasGestao.Serie, nova.Serie);
                Numero(cmd, "@ano", nova.Ano, 5);
                Numero(cmd, "@valor", nova.Valor, 19, 6);
                Numero(cmd, "@nofornec", nova.NoFornecedor, 16);
                Texto(cmd, "@nomeforn", RegrasGestao.NomeFornecedor, nova.NomeFornecedor);
                Quando(cmd, "@data", nova.Data is { } d ? d.ToDateTime(TimeOnly.MinValue) : SemData);
                Texto(cmd, "@local", RegrasGestao.Local, nova.Local);
                Texto(cmd, "@status", RegrasGestao.Estado, nova.Estado);
                cmd.Parameters.Add("@obsoleto", SqlDbType.Bit).Value = nova.Obsoleto;
                Texto(cmd, "@manpdf", RegrasGestao.ManualPdf, nova.ManualPdf);
                Texto(cmd, "@email", RegrasGestao.Email, nova.Email);
                Auditoria(cmd, supervisor.Codigo, agora);
                if (novo)
                {
                    var no = Convert.ToDecimal(await ExecutarEscalarAsync(conn, tx, SqlProximoNoInterno, ct), CultureInfo.InvariantCulture);
                    Numero(cmd, "@no", no, 10);
                    Texto(cmd, "@instrucao", 25, NovoStampGestao());
                }
                else
                {
                    Veiculos(cmd);
                    Texto(cmd, "@versao", 20, pedido.Versao ?? "");
                }
                if (await cmd.ExecuteNonQueryAsync(ct) == 0 && !novo) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.Conflito, null); }

                // Equipamento novo: o estado anterior é vazio, por isso o primeiro estado fica no histórico.
                if (nova.Estado.Length > 0 && !string.Equals(nova.Estado, novo ? "" : atual.Estado, StringComparison.OrdinalIgnoreCase))
                {
                    await using var hist = new SqlCommand(SqlHistorico, conn, tx);
                    Texto(hist, "@hist", 25, NovoStampGestao());
                    Texto(hist, "@id", 25, stamp);
                    Texto(hist, "@status", RegrasGestao.EstadoHistorico, nova.Estado);
                    Auditoria(hist, supervisor.Codigo, agora);
                    await hist.ExecuteNonQueryAsync(ct);
                }

                // Equipamento obsoleto não tem planeadas: em cada gravação com obsoleto as por fazer ficam canceladas (nunca as em curso), como no Landeiro.
                // Ao passar a obsoleto, o nº cancelado tem de ser o que o utilizador confirmou; se não for, desfaz tudo.
                if (nova.Obsoleto)
                {
                    var canceladas = await CancelarPlaneadasAsync(conn, tx, OndeEquip, stamp, RegrasGestao.MotivoObsoleto, supervisor.Codigo, agora, ct);
                    if (passaAObsoleto && (pedido.PlaneadasConfirmadas ?? 0) != canceladas) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.PlaneadasMudaram, null); }
                }
                await tx.CommitAsync(ct);
            }
            catch { await RollbackSemEsconderAsync(tx); throw; }
            return OkOuConflito(await LerEquipamentoGestaoAsync(conn, stamp, ct));
        });

    // Gravou mas a leitura seguinte não encontra o registo (apagado ou alterado entretanto): erro claro em vez de um 200 vazio.
    private static (ResultadoIntervencao, T?) OkOuConflito<T>(T? lido) where T : class => lido is null ? (ResultadoIntervencao.Conflito, null) : (ResultadoIntervencao.Ok, lido);

    private static async Task<object?> ExecutarEscalarAsync(SqlConnection conn, SqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn, tx);
        return await cmd.ExecuteScalarAsync(ct);
    }

    private static async Task<InstrucaoDto?> LerInstrucaoAsync(SqlConnection conn, string id, CancellationToken ct)
    {
        var lista = await LerInstrucoesAsync(conn, SqlInstrucaoUma, id, ct);
        return lista.FirstOrDefault();
    }

    private static async Task<List<InstrucaoDto>> LerInstrucoesAsync(SqlConnection conn, string sql, string id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn);
        Texto(cmd, "@id", 25, id);
        Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<InstrucaoDto>();
        while (await r.ReadAsync(ct))
            lista.Add(new InstrucaoDto(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim(),
                r.GetDateTime(3) <= SemData ? DateOnly.FromDateTime(DateTime.Today) : DateOnly.FromDateTime(r.GetDateTime(3)), Bool(r, 4), r.GetString(5).Trim(),
                (int)Dec(r, 6), r.IsDBNull(7) ? "" : r.GetString(7)));
        return lista;
    }

    public Task<IReadOnlyList<InstrucaoDto>?> ListarInstrucoesAsync(string equipamentoId, CancellationToken ct) => Executar<IReadOnlyList<InstrucaoDto>?>(async conn =>
        await ContarAsync(conn, null, SqlEquipExiste, c => { Texto(c, "@id", 25, equipamentoId); Veiculos(c); }, ct) == 0 ? null : await LerInstrucoesAsync(conn, SqlInstrucoesDoEquip, equipamentoId, ct));

    public Task<(ResultadoIntervencao, InstrucaoDto?)> GravarInstrucaoAsync(string? equipamentoId, string? id, PedidoInstrucao pedido, TecnicoAtual supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, InstrucaoDto?)>(async conn =>
        {
            var agora = await AgoraAsync(conn, ct);
            var versao = RegrasManutencao.Cortar(pedido.Versao, RegrasGestao.Versao);
            var data = (pedido.Data is { } d ? d.ToDateTime(TimeOnly.MinValue) : agora.Dia);
            if (id is null)
            {
                var stamp = NovoStampGestao();
                await using var novo = new SqlCommand(SqlNovaInstrucao, conn);
                Texto(novo, "@id", 25, stamp);
                Texto(novo, "@equip", 25, equipamentoId!);
                Texto(novo, "@versao", RegrasGestao.Versao, versao);
                Quando(novo, "@data", data);
                novo.Parameters.Add("@obsoleto", SqlDbType.Bit).Value = pedido.Obsoleta ?? false;
                novo.Parameters.Add("@obs", SqlDbType.VarChar, -1).Value = (pedido.Obs ?? "").Trim();
                Veiculos(novo);
                Auditoria(novo, supervisor.Codigo, agora);
                if (await novo.ExecuteNonQueryAsync(ct) == 0) return (ResultadoIntervencao.NaoExiste, null);
                return OkOuConflito(await LerInstrucaoAsync(conn, stamp, ct));
            }

            if (await LerInstrucaoAsync(conn, id, ct) is not { } atual) return (ResultadoIntervencao.NaoExiste, null);
            if (!string.IsNullOrEmpty(pedido.Concorrencia) && pedido.Concorrencia != atual.Concorrencia) return (ResultadoIntervencao.Conflito, null);
            var obsoleta = pedido.Obsoleta ?? atual.Obsoleta;
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = new SqlCommand(SqlEditarInstrucao, conn, tx);
                Texto(cmd, "@id", 25, id);
                Texto(cmd, "@versao", RegrasGestao.Versao, pedido.Versao is null ? atual.Versao : versao);
                Quando(cmd, "@data", pedido.Data is { } nd ? nd.ToDateTime(TimeOnly.MinValue) : atual.Data.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.Add("@obsoleto", SqlDbType.Bit).Value = obsoleta;
                cmd.Parameters.Add("@obs", SqlDbType.VarChar, -1).Value = pedido.Obs is null ? atual.Obs : pedido.Obs.Trim();
                Texto(cmd, "@versao_lida", 20, pedido.Concorrencia ?? "");
                Auditoria(cmd, supervisor.Codigo, agora);
                if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.Conflito, null); }
                // Versão obsoleta: as tarefas dela deixam de gerar e as planeadas por fazer ficam canceladas.
                if (obsoleta) await CancelarPlaneadasAsync(conn, tx, OndeInstrucao, id, RegrasGestao.MotivoInstrucaoObsoleta, supervisor.Codigo, agora, ct);
                await tx.CommitAsync(ct);
            }
            catch { await RollbackSemEsconderAsync(tx); throw; }
            return OkOuConflito(await LerInstrucaoAsync(conn, id, ct));
        });

    private const string SqlObsoletosDaTarefa =
        """
        select i.obsoleto, e.obsoleto
        from u_itmanutli l with (nolock)
        join u_itmanut i with (nolock) on i.u_itmanutstamp = l.u_itmanutstamp
        join u_equip e with (nolock) on e.u_equipstamp = i.u_equipstamp
        where l.u_itmanutlistamp = @id
        """;

    private static async Task<(bool Instrucao, bool Equipamento)> ObsoletosDaTarefaAsync(SqlConnection conn, string id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlObsoletosDaTarefa, conn);
        Texto(cmd, "@id", 25, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (Bool(r, 0), Bool(r, 1)) : (false, false);
    }

    private static async Task<List<TarefaGestaoDto>> LerTarefasAsync(SqlConnection conn, string sql, string id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn);
        Texto(cmd, "@id", 25, id);
        Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<TarefaGestaoDto>();
        while (await r.ReadAsync(ct))
            lista.Add(new TarefaGestaoDto(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim(), r.GetString(3).Trim(), r.GetString(4).Trim(),
                r.GetString(5).Trim(), r.GetString(6).Trim(), Bool(r, 7), r.IsDBNull(8) ? "" : r.GetString(8)));
        return lista;
    }

    public Task<IReadOnlyList<TarefaGestaoDto>?> ListarTarefasAsync(string instrucaoId, CancellationToken ct) => Executar<IReadOnlyList<TarefaGestaoDto>?>(async conn =>
        await LerInstrucaoAsync(conn, instrucaoId, ct) is null ? null : await LerTarefasAsync(conn, SqlTarefasDaInstrucao, instrucaoId, ct));

    public Task<(ResultadoIntervencao, TarefaGestaoDto?)> GravarTarefaAsync(string? instrucaoId, string? id, PedidoTarefa pedido, TecnicoAtual supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, TarefaGestaoDto?)>(async conn =>
        {
            var agora = await AgoraAsync(conn, ct);
            TarefaGestaoDto? atual = null;
            if (id is null)
            {
                if (await LerInstrucaoAsync(conn, instrucaoId!, ct) is not { } instrucao) return (ResultadoIntervencao.NaoExiste, null);
                if (instrucao.Obsoleta) return (ResultadoIntervencao.InstrucaoObsoleta, null);
            }
            else
            {
                atual = (await LerTarefasAsync(conn, SqlTarefaUma, id, ct)).FirstOrDefault();
                if (atual is null) return (ResultadoIntervencao.NaoExiste, null);
                if (!string.IsNullOrEmpty(pedido.Concorrencia) && pedido.Concorrencia != atual.Concorrencia) return (ResultadoIntervencao.Conflito, null);
            }

            var periodo = pedido.Periodo is null ? atual?.Periodo ?? "" : RegrasManutencao.Cortar(pedido.Periodo, RegrasGestao.Periodo);
            if (periodo.Length > 0 && !string.Equals(periodo, atual?.Periodo, StringComparison.OrdinalIgnoreCase)
                && await ContarAsync(conn, null, SqlPeriodoConhecido, c => Texto(c, "@periodo", RegrasGestao.Periodo, periodo), ct) == 0)
                return (ResultadoIntervencao.PeriodicidadeInvalida, null);
            var responsabilidade = pedido.Responsabilidade is null ? atual?.Responsabilidade ?? "" : pedido.Responsabilidade.Trim();
            var responsavel = pedido.Responsavel is null ? atual?.Responsavel ?? "" : pedido.Responsavel.Trim();
            var descricao = pedido.Descricao is null ? atual?.Descricao ?? "" : pedido.Descricao.Trim();
            var tipoManut = pedido.TipoManut is null ? atual?.TipoManut ?? "" : RegrasFicha.NormalizarTipoManut(pedido.TipoManut) ?? "";
            var ativa = pedido.Ativa ?? atual?.Ativa ?? true;
            var stamp = id ?? NovoStampGestao();

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = new SqlCommand(id is null ? SqlNovaTarefa : SqlEditarTarefa, conn, tx);
                Texto(cmd, "@id", 25, stamp);
                Texto(cmd, "@periodo", RegrasGestao.Periodo, periodo);
                Texto(cmd, "@responsabilidade", RegrasGestao.Responsabilidade, responsabilidade);
                Texto(cmd, "@responsavel", RegrasGestao.Responsavel, responsavel);
                cmd.Parameters.Add("@descricao", SqlDbType.VarChar, -1).Value = descricao;
                Texto(cmd, "@tipomanut", RegrasGestao.TipoManut, tipoManut);
                cmd.Parameters.Add("@ativo", SqlDbType.Bit).Value = ativa;
                Auditoria(cmd, supervisor.Codigo, agora);
                if (id is null) Texto(cmd, "@instrucao", 25, instrucaoId!);
                else Texto(cmd, "@versao", 20, pedido.Concorrencia ?? "");
                if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.Conflito, null); }
                if (id is not null && !ativa) await CancelarPlaneadasAsync(conn, tx, OndeTarefa, stamp, RegrasGestao.MotivoTarefaDesligada, supervisor.Codigo, agora, ct);
                await tx.CommitAsync(ct);
            }
            catch { await RollbackSemEsconderAsync(tx); throw; }

            // Tarefa ativa: gera já a planeada (se a configuração gera e não há uma em aberto); a falha não desfaz a gravação.
            // Com a instrução ou o equipamento obsoletos fica gravada mas não gera, e diz-se.
            string? aviso = null;
            if (ativa)
            {
                var (instrucaoObsoleta, equipamentoObsoleto) = await ObsoletosDaTarefaAsync(conn, stamp, ct);
                if (instrucaoObsoleta || equipamentoObsoleto) aviso = instrucaoObsoleta ? RegrasGestao.AvisoInstrucaoObsoleta : RegrasGestao.AvisoEquipamentoObsoleto;
                else await GerarSeguinteAsync(conn, stamp, supervisor.Codigo, ct);
            }
            var lida = (await LerTarefasAsync(conn, SqlTarefaUma, stamp, ct)).FirstOrDefault();
            return lida is null ? (ResultadoIntervencao.Conflito, null) : (ResultadoIntervencao.Ok, lida with { Aviso = aviso });
        });

    public Task<ResultadoIntervencao> ApagarTarefaAsync(string id, TecnicoAtual supervisor, CancellationToken ct) => Executar(async conn =>
    {
        if ((await LerTarefasAsync(conn, SqlTarefaUma, id, ct)).FirstOrDefault() is null) return ResultadoIntervencao.NaoExiste;
        var agora = await AgoraAsync(conn, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // Com intervenções feitas a tarefa guarda o histórico da "última feita": não se apaga, desliga-se.
            if (await ContarAsync(conn, tx, SqlFichasFeitasDaTarefa, c => Texto(c, "@id", 25, id), ct) > 0) { await tx.RollbackAsync(ct); return ResultadoIntervencao.TarefaComFichas; }
            await CancelarPlaneadasAsync(conn, tx, OndeTarefa, id, RegrasGestao.MotivoTarefaApagada, supervisor.Codigo, agora, ct);
            await using var cmd = new SqlCommand(SqlApagarTarefa, conn, tx);
            Texto(cmd, "@id", 25, id);
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch { await RollbackSemEsconderAsync(tx); throw; }
        return ResultadoIntervencao.Ok;
    });

    private static async Task<List<PeriodicidadeDto>> LerPeriodicidadesAsync(SqlConnection conn, string sql, string? id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn);
        if (id is not null) Texto(cmd, "@id", 25, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<PeriodicidadeDto>();
        while (await r.ReadAsync(ct))
            lista.Add(new PeriodicidadeDto(r.GetString(0).Trim(), r.GetString(1).Trim(), RegrasManutencao.LerUnidade(r.GetString(2)).ToString().ToLowerInvariant(), (int)Dec(r, 3), Bool(r, 4), r.IsDBNull(5) ? "" : r.GetString(5)));
        return lista;
    }

    public Task<IReadOnlyList<PeriodicidadeDto>> ListarPeriodicidadesAsync(CancellationToken ct) =>
        Executar<IReadOnlyList<PeriodicidadeDto>>(async conn => await LerPeriodicidadesAsync(conn, SqlPeriodicidades, null, ct));

    public Task<(ResultadoIntervencao, PeriodicidadeDto?)> GravarPeriodicidadeAsync(string id, PedidoPeriodicidade pedido, TecnicoAtual supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, PeriodicidadeDto?)>(async conn =>
        {
            if ((await LerPeriodicidadesAsync(conn, SqlPeriodicidadeUma, id, ct)).FirstOrDefault() is not { } atual) return (ResultadoIntervencao.NaoExiste, null);
            if (!string.IsNullOrEmpty(pedido.Concorrencia) && pedido.Concorrencia != atual.Concorrencia) return (ResultadoIntervencao.Conflito, null);
            var (unidade, intervalo) = RegrasGestao.NormalizarPeriodicidade(pedido);
            var agora = await AgoraAsync(conn, ct);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            await using var cmd = new SqlCommand(SqlEditarPeriodicidade, conn, tx);
            Texto(cmd, "@id", 25, id);
            Texto(cmd, "@unidade", 10, unidade);
            Numero(cmd, "@intervalo", intervalo, 6);
            cmd.Parameters.Add("@analise", SqlDbType.Bit).Value = pedido.Aviso ?? atual.Aviso;
            Texto(cmd, "@versao", 20, pedido.Concorrencia ?? "");
            Auditoria(cmd, supervisor.Codigo, agora);
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.Conflito, null); }
            await tx.CommitAsync(ct);
            return OkOuConflito((await LerPeriodicidadesAsync(conn, SqlPeriodicidadeUma, id, ct)).FirstOrDefault());
        });
}
