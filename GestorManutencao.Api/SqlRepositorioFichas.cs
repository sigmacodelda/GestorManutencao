using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Ficha completa, edição, cancelamento, ficha manual, mão de obra (u_tempinterv), compras ligadas (fn), fornecedores (fl) e contadores do menu.
// Mesmas tabelas e campos da LandeiroMaui (ver docs/paridade-backoffice-landeiro.md).
public sealed partial class SqlRepositorioIntervencoes
{
    private const string SqlDetalhe =
        $"""
        select f.u_fichaintstamp, f.no, isnull(f.u_equipstamp, ''), case when f.design <> '' then f.design else isnull(e.design, '') end, isnull(e.[local], ''),
               isnull(f.ref, ''), isnull(f.tipointerv, ''), isnull(f.tipomanut, ''), isnull(f.periodo, ''), f.data, rtrim(f.estado), f.fechada, f.supervisao,
               f.tecno, isnull(f.tecnome, ''), {TecnosSel}, f.hini, f.hfim, isnull(f.horasfrio, 0), isnull(f.horasconf, 0),
               isnull(f.problema, ''), isnull(f.resolucao, ''), isnull(cast(f.obs as varchar(max)), ''), f.limrem,
               isnull(f.nofornec, 0), isnull(f.nomefornec, ''), isnull(f.docexterno, ''),
               isnull(f.mointerna, 0), isnull(f.moexterna, 0), isnull(f.matinterno, 0), isnull(f.matexterno, 0), isnull(f.totmo, 0), isnull(f.totref, 0), isnull(f.valobs, ''),
               convert(varchar(8), f.usrdata, 112) + isnull(f.usrhora, '')
        from u_fichaint f with (nolock) left join u_equip e with (nolock) on e.u_equipstamp = f.u_equipstamp
        where f.u_fichaintstamp = @id and f.obsoleto = 0 and e.tipoequip = 1
        """;

    // Só grava o que mudou (o SET vem de SetsFicha). Protege o técnico lido (reatribuição concorrente) e a versão lida (usrdata + usrhora): se diferirem, não grava.
    private const string SqlGuardarFichaWhere =
        """
         where u_fichaintstamp = @id and obsoleto = 0 and rtrim(estado) = @estado and tecno = @tecnoLido
           and (@versao = '' or convert(varchar(8), usrdata, 112) + isnull(usrhora, '') = @versao)
        """;

    private const string SqlFornecedor = "select top 1 nome from fl with (nolock) where no = @no and inactivo = 0";

    private const string SqlCancelar =
        """
        update u_fichaint set estado = 'Cancelada', valobs = @motivo, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and obsoleto = 0 and rtrim(estado) = @de
        """;

    private const string SqlFecharAntiga =
        """
        update u_fichaint set supervisao = @supervisao, fechada = @fechada, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and obsoleto = 0 and rtrim(estado) = ''
        """;

    // Como a avaria, mas com tipo, tipo de manutenção e periodicidade; a periodicidade tem de ser de uma tarefa do equipamento.
    private const string SqlFichaManual =
        """
        insert into u_fichaint (u_fichaintstamp, no, ref, design, tipointerv, tipomanut, periodo, problema, data, estado, u_equipstamp, hini, tecno, tecnome, opid,
                                ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        select @stamp, @no, left(e.ref, 20), left(e.design, 45), @tipo, @tipomanut, @periodo, @problema, @dia, 'EmCurso', e.u_equipstamp, @agora, @tecno, @tecnome, @opid,
               @codigo, @dia, @hora, @codigo, @dia, @hora
        from u_equip e with (nolock)
        where e.u_equipstamp = @equipamento and e.obsoleto = 0 and e.tipoequip = 1
          and (@periodo = '' or exists (select 1 from u_itmanutli l with (nolock) join u_itmanut i with (nolock) on i.u_itmanutstamp = l.u_itmanutstamp
                                        where i.u_equipstamp = e.u_equipstamp and i.obsoleto = 0 and ltrim(rtrim(l.periodo)) = @periodo))
        """;

    private const string SqlEquipamentoValido =
        "select count(*) from u_equip with (nolock) where u_equipstamp = @equipamento and obsoleto = 0 and tipoequip = 1";

    private const string SqlTempos = "select u_tempintervstamp, no, responsavel, tempo, custo from u_tempinterv with (nolock) where u_fichaintstamp = @id order by ousrdata, ousrhora";

    private const string SqlNovoTempo =
        """
        insert into u_tempinterv (u_tempintervstamp, u_fichaintstamp, no, responsavel, tempo, custo, ref, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        values (@tempo, @id, @no, @responsavel, @horas, @custo, '', @codigo, @dia, @hora, @codigo, @dia, @hora)
        """;

    private const string SqlEditarTempo =
        """
        update u_tempinterv set no = @no, responsavel = @responsavel, tempo = @horas, custo = @custo, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_tempintervstamp = @tempo and u_fichaintstamp = @id
        """;

    private const string SqlLinhaTempo = "select custo, tempo from u_tempinterv with (nolock) where u_tempintervstamp = @tempo and u_fichaintstamp = @id";

    private const string SqlApagarTempo = "delete from u_tempinterv where u_tempintervstamp = @tempo and u_fichaintstamp = @id";

    // Custo/hora do último registo do técnico (sugestão).
    private const string SqlCustoHora =
        """
        select top 1 cast(custo / tempo as decimal(16,3)) from u_tempinterv with (nolock)
        where no = @no and tempo > 0 and custo > 0
        order by ousrdata desc, ousrhora desc
        """;

    private const string SqlEditarMaterial =
        """
        update u_matinterv set design = @design, qtt = @qtt, epv = @epv, custo = @custo, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_matintervstamp = @material and u_fichaintstamp = @id
        """;

    private const string SqlCompras =
        """
        select fo.data, ltrim(rtrim(fo.docnome)) + ' ' + ltrim(rtrim(fo.adoc)), fo.nome, fn.ref, fn.design, fn.qtt,
               case when cm1.debito = 1 then -fn.etiliquido else fn.etiliquido end, fn.u_tpint
        from fn with (nolock)
        join fo with (nolock) on fo.fostamp = fn.fostamp
        left join cm1 with (nolock) on cm1.cm = fo.doccode
        where fn.u_fichaint = @id
        order by fo.data, fo.adoc, fn.lordem
        """;

    private const string SqlFornecedores =
        """
        select distinct top 30 no, nome from fl with (nolock)
        where inactivo = 0 and (nome like @texto escape '\' or cast(no as varchar(20)) = @exato)
        order by nome
        """;

    private const string SqlContarPorValidar =
        """
        select count(*) from u_fichaint f with (nolock) left join u_equip e with (nolock) on e.u_equipstamp = f.u_equipstamp
        where f.estado = 'Concluida' and f.obsoleto = 0 and e.tipoequip = 1
        """;

    private static decimal Dec(SqlDataReader r, int i) => Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);

    private static bool Bool(SqlDataReader r, int i) => Convert.ToBoolean(r.GetValue(i), CultureInfo.InvariantCulture);

    // O marcador da operação (OpId) vai separado: a ficha lida mostra o problema sem ele e ao gravar volta a juntar-se.
    private static (FichaLida Ficha, string Marcador) LerFicha(SqlDataReader r)
    {
        var problema = r.GetString(20);
        var ficha = new FichaLida(
            r.GetString(0).Trim(), (int)Dec(r, 1), r.GetString(2).Trim(), r.GetString(3).Trim(), r.GetString(4).Trim(), r.GetString(5).Trim(),
            r.GetString(6).Trim(), r.GetString(7).Trim(), r.GetString(8).Trim(), r.GetDateTime(9), r.GetString(10).Trim(), Bool(r, 11), Bool(r, 12),
            (int)Dec(r, 13), r.GetString(14).Trim(), r.GetString(15), r.GetDateTime(16), r.GetDateTime(17), Dec(r, 18), Dec(r, 19),
            RegrasIntervencao.SemMarcador(problema), r.GetString(21), r.GetString(22), Bool(r, 23),
            Dec(r, 24), r.GetString(25), r.GetString(26),
            Dec(r, 27), Dec(r, 28), Dec(r, 29), Dec(r, 30), Dec(r, 31), Dec(r, 32), r.GetString(33), r.IsDBNull(34) ? "" : r.GetString(34));
        return (ficha, RegrasIntervencao.ExtrairMarcador(problema));
    }

    private static async Task<(FichaLida Ficha, string Marcador)?> LerFichaAsync(SqlConnection conn, string id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlDetalhe, conn);
        Texto(cmd, "@id", 25, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerFicha(r) : null;
    }

    private static async Task<List<TempoDto>> LerTemposAsync(SqlConnection conn, string id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlTempos, conn);
        Texto(cmd, "@id", 25, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<TempoDto>();
        while (await r.ReadAsync(ct))
        {
            var horas = Dec(r, 3);
            var custo = Dec(r, 4);
            lista.Add(new TempoDto(r.GetString(0).Trim(), (int)Dec(r, 1), r.GetString(2).Trim(), horas, custo, horas > 0 ? Math.Round(custo / horas, 3) : 0));
        }
        return lista;
    }

    private static async Task<List<MaterialDto>> LerMateriaisAsync(SqlConnection conn, string id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlMateriais, conn);
        Texto(cmd, "@id", 25, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<MaterialDto>();
        while (await r.ReadAsync(ct)) lista.Add(new MaterialDto(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4)));
        return lista;
    }

    private static async Task<FichaDetalheDto?> MontarDetalheAsync(SqlConnection conn, string id, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        if (await LerFichaAsync(conn, id, ct) is not { } lida) return null;
        var cfg = await LerConfiguracaoAsync(conn, ct);
        var tempos = await LerTemposAsync(conn, id, ct);
        var materiais = await LerMateriaisAsync(conn, id, ct);
        return RegrasFicha.Montar(lida.Ficha, tempos, materiais, cfg, supervisor, RegrasIntervencao.PertenceAFicha(lida.Ficha.Tecno, lida.Ficha.Tecnos, quem.Tecno), quem.Tecno);
    }

    private static async Task<string?> NomeFornecedorAsync(SqlConnection conn, long no, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlFornecedor, conn);
        Numero(cmd, "@no", no, 16);
        return await cmd.ExecuteScalarAsync(ct) is string nome ? RegrasManutencao.Cortar(nome, RegrasManutencao.TamanhoNomeFornecedor) : null;
    }

    public Task<FichaDetalheDto?> ObterDetalheAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct) => Executar<FichaDetalheDto?>(async conn =>
        await FichaVisivelAsync(conn, id, quem, supervisor, ct) is null ? null : await MontarDetalheAsync(conn, id, quem, supervisor, ct));

    public Task<(ResultadoIntervencao, FichaDetalheDto?)> GuardarFichaAsync(string id, PedidoFicha pedido, string? nomeTecnico, TecnicoAtual quem, bool supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, FichaDetalheDto?)>(async conn =>
        {
            if (AcessoEdicao(await LerAtualAsync(conn, id, ct), quem, supervisor) is { } negado) return (negado, null);
            if (await LerFichaAsync(conn, id, ct) is not { } lida) return (ResultadoIntervencao.NaoExiste, null);
            var cfg = await LerConfiguracaoAsync(conn, ct);
            // Versão lida pelo cliente diferente da gravada: alguém mexeu entretanto.
            if (!string.IsNullOrEmpty(pedido.Versao) && pedido.Versao != lida.Ficha.Versao) return (ResultadoIntervencao.Conflito, null);
            var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);
            var (nova, resultado) = RegrasFicha.Aplicar(lida.Ficha, pedido, supervisor, nomeTecnico, !cfg.ComprasFichaInt, hoje);
            if (nova is null) return (resultado, null);
            if (nova.NoFornec != lida.Ficha.NoFornec && nova.NoFornec > 0)
            {
                if (await NomeFornecedorAsync(conn, (long)nova.NoFornec, ct) is not { } nomeFornecedor) return (ResultadoIntervencao.Invalido, null);
                nova = nova with { NomeFornec = nomeFornecedor };
            }
            else if (nova.NoFornec == 0 && lida.Ficha.NoFornec != 0) nova = nova with { NomeFornec = "" };
            var problema = nova.Problema + lida.Marcador;
            if (problema.Length > RegrasManutencao.TamanhoProblema) return (ResultadoIntervencao.ProblemaLongo, null);

            var antes = lida.Ficha;
            await using var cmd = new SqlCommand { Connection = conn };
            var sets = new List<string>();
            void Mudou(bool mudou, string coluna, string parametro, Action<SqlCommand> valor)
            {
                if (!mudou) return;
                sets.Add($"{coluna} = {parametro}");
                valor(cmd);
            }
            Mudou(nova.Data != antes.Data, "data", "@data", c => Quando(c, "@data", nova.Data.Date));
            Mudou(nova.Tipo != antes.Tipo, "tipointerv", "@tipo", c => Texto(c, "@tipo", RegrasManutencao.TamanhoTipoFicha, nova.Tipo));
            Mudou(nova.TipoManut != antes.TipoManut, "tipomanut", "@tipomanut", c => Texto(c, "@tipomanut", RegrasManutencao.TamanhoTipoFicha, nova.TipoManut));
            Mudou(nova.Periodo != antes.Periodo, "periodo", "@periodo", c => Texto(c, "@periodo", RegrasManutencao.TamanhoPeriodo, nova.Periodo));
            Mudou(nova.Problema != antes.Problema, "problema", "@problema", c => Texto(c, "@problema", RegrasManutencao.TamanhoProblema, problema));
            Mudou(nova.Resolucao != antes.Resolucao, "resolucao", "@resolucao", c => Texto(c, "@resolucao", RegrasManutencao.TamanhoResolucao, nova.Resolucao));
            Mudou(nova.Obs != antes.Obs, "obs", "@obs", c => c.Parameters.Add("@obs", SqlDbType.VarChar, -1).Value = nova.Obs);
            Mudou(nova.LimRem != antes.LimRem, "limrem", "@limrem", c => c.Parameters.Add("@limrem", SqlDbType.Bit).Value = nova.LimRem);
            Mudou(nova.HorasConf != antes.HorasConf, "horasconf", "@horasconf", c => Numero(c, "@horasconf", nova.HorasConf, 16, 2));
            Mudou(nova.HorasFrio != antes.HorasFrio, "horasfrio", "@horasfrio", c => Numero(c, "@horasfrio", nova.HorasFrio, 16, 2));
            Mudou(nova.DocExterno != antes.DocExterno, "docexterno", "@doc", c => Texto(c, "@doc", RegrasManutencao.TamanhoDocExterno, nova.DocExterno));
            Mudou(nova.NoFornec != antes.NoFornec, "nofornec", "@nofornec", c => Numero(c, "@nofornec", nova.NoFornec, 16));
            Mudou(nova.NomeFornec != antes.NomeFornec, "nomefornec", "@nomefornec", c => Texto(c, "@nomefornec", RegrasManutencao.TamanhoNomeFornecedor, nova.NomeFornec));
            Mudou(nova.HIni != antes.HIni, "hini", "@hini", c => Quando(c, "@hini", nova.HIni));
            Mudou(nova.HFim != antes.HFim, "hfim", "@hfim", c => Quando(c, "@hfim", nova.HFim));
            Mudou(nova.Tecno != antes.Tecno, "tecno", "@tecno", c => Numero(c, "@tecno", nova.Tecno, 5));
            Mudou(nova.Tecnome != antes.Tecnome, "tecnome", "@tecnome", c => Texto(c, "@tecnome", RegrasManutencao.TamanhoNomeTecnico, nova.Tecnome));
            Mudou(nova.Tecnos != antes.Tecnos, ColunaTecnos, "@tecnos", c => c.Parameters.Add("@tecnos", SqlDbType.VarChar, -1).Value = nova.Tecnos);
            Mudou(nova.MoExterna != antes.MoExterna, "moexterna", "@moexterna", c => Numero(c, "@moexterna", nova.MoExterna, 19, 6));
            Mudou(nova.MatExterno != antes.MatExterno, "matexterno", "@matexterno", c => Numero(c, "@matexterno", nova.MatExterno, 19, 6));
            if (sets.Count == 0) return (ResultadoIntervencao.Ok, await MontarDetalheAsync(conn, id, quem, supervisor, ct));

            sets.Add("usrinis = @codigo"); sets.Add("usrdata = @dia"); sets.Add("usrhora = @hora");
            cmd.CommandText = "update u_fichaint set " + string.Join(", ", sets) + SqlGuardarFichaWhere;
            Texto(cmd, "@id", 25, id);
            Texto(cmd, "@estado", 20, antes.Estado);
            Numero(cmd, "@tecnoLido", antes.Tecno, 5);
            Texto(cmd, "@versao", 20, pedido.Versao ?? "");
            Auditoria(cmd, quem.Codigo, await AgoraAsync(conn, ct));

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            cmd.Transaction = tx;
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.Conflito, null); }
            await RecalcularTotaisAsync(conn, id, cfg.ComprasFichaInt, ct, tx);
            await tx.CommitAsync(ct);
            return (ResultadoIntervencao.Ok, await MontarDetalheAsync(conn, id, quem, supervisor, ct));
        });

    // Só Planeada ou EmCurso; o UPDATE só acontece se o estado ainda for o lido. Depois gera-se a seguinte da tarefa, que salta esta ocorrência.
    public Task<(ResultadoIntervencao, IntervencaoDto?)> CancelarAsync(string id, string motivo, TecnicoAtual supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, IntervencaoDto?)>(async conn =>
        {
            if (await LerAtualAsync(conn, id, ct) is not { } a) return (ResultadoIntervencao.NaoExiste, null);
            if (!EstadosFicha.PodeMudar(a.Estado, EstadosFicha.Cancelada)) return (ResultadoIntervencao.Conflito, null);

            await using var cmd = new SqlCommand(SqlCancelar, conn);
            Texto(cmd, "@id", 25, id);
            Texto(cmd, "@de", 20, a.Estado);
            Texto(cmd, "@motivo", 254, RegrasManutencao.Cortar("Cancelada: " + motivo.Trim(), RegrasManutencao.TamanhoMotivo));
            Auditoria(cmd, supervisor.Codigo, await AgoraAsync(conn, ct));
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) return (ResultadoIntervencao.Conflito, null);

            await GerarSeguinteAsync(conn, a.Tarefa, supervisor.Codigo, ct);
            return (ResultadoIntervencao.Ok, await LerUmaAsync(conn, id, ct));
        });

    public async Task<(ResultadoIntervencao, IntervencaoDto?)> CriarFichaManualAsync(PedidoFichaManual pedido, TecnicoAtual tecnico, CancellationToken ct)
    {
        if (pedido.OpId is null) return await ComNumeracaoAsync(() => Executar(conn => GravarManualAsync(conn, pedido, tecnico, ct)), ct);
        await AvariaOp.WaitAsync(ct);
        try { return await ComNumeracaoAsync(() => Executar(conn => GravarManualAsync(conn, pedido, tecnico, ct)), ct); }
        finally { AvariaOp.Release(); }
    }

    private static async Task<(ResultadoIntervencao, IntervencaoDto?)> GravarManualAsync(SqlConnection conn, PedidoFichaManual pedido, TecnicoAtual tecnico, CancellationToken ct)
    {
        var problema = (pedido.Problema ?? "").Trim();
        if (pedido.OpId is not null)
        {
            await using var procura = new SqlCommand(SqlAvariaPorOp, conn);
            Numero(procura, "@tecno", tecnico.Tecno, 5);
            Texto(procura, "@opid", RegrasIntervencao.MaxOpId, pedido.OpId);
            if (await procura.ExecuteScalarAsync(ct) is string existente && await LerUmaAsync(conn, existente.Trim(), ct) is { } ficha)
                return (ResultadoIntervencao.Ok, ficha);
        }

        var agora = await AgoraAsync(conn, ct);
        var no = await ProximoNumeroAsync(conn, agora.Agora.Year, ct);
        var stamp = Guid.NewGuid().ToString("N")[..25].ToUpperInvariant();
        await using var cmd = new SqlCommand(SqlFichaManual, conn);
        Texto(cmd, "@stamp", 25, stamp);
        Numero(cmd, "@no", no, 10);
        Texto(cmd, "@equipamento", 25, pedido.EquipamentoId!);
        Texto(cmd, "@tipo", RegrasManutencao.TamanhoTipoFicha, (pedido.Tipo ?? TipoIntervencao.Preventiva).ToString());
        Texto(cmd, "@tipomanut", RegrasManutencao.TamanhoTipoFicha, RegrasFicha.NormalizarTipoManut(pedido.TipoManut) ?? RegrasManutencao.TipoInterna);
        Texto(cmd, "@periodo", RegrasManutencao.TamanhoPeriodo, RegrasManutencao.Cortar(pedido.Periodicidade, RegrasManutencao.TamanhoPeriodo));
        Texto(cmd, "@problema", RegrasManutencao.TamanhoProblema, problema);
        Texto(cmd, "@opid", RegrasIntervencao.MaxOpId, pedido.OpId ?? "");
        Numero(cmd, "@tecno", tecnico.Tecno, 5);
        Texto(cmd, "@tecnome", RegrasManutencao.TamanhoNomeTecnico, tecnico.Nome);
        Auditoria(cmd, tecnico.Codigo, agora);
        if (await cmd.ExecuteNonQueryAsync(ct) > 0) return (ResultadoIntervencao.Ok, await LerUmaAsync(conn, stamp, ct));

        // Sem linha: ou o equipamento não serve (não existe, obsoleto, viatura) ou a periodicidade não é de uma tarefa dele.
        await using var existe = new SqlCommand(SqlEquipamentoValido, conn);
        Texto(existe, "@equipamento", 25, pedido.EquipamentoId!);
        return Convert.ToInt32(await existe.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0
            ? (ResultadoIntervencao.PeriodicidadeInvalida, null)
            : (ResultadoIntervencao.NaoExiste, null);
    }

    // Ficha antiga (estado vazio): só se fecha à mão; ao passar a fechada, a manutenção externa exige fornecedor e documento (se a configuração o pedir).
    public Task<(ResultadoIntervencao, FichaDetalheDto?)> FecharAntigaAsync(string id, bool supervisao, bool fechada, TecnicoAtual supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, FichaDetalheDto?)>(async conn =>
        {
            if (await LerAtualAsync(conn, id, ct) is not { } a) return (ResultadoIntervencao.NaoExiste, null);
            if (!RegrasFicha.PodeFecharAntiga(a.Estado, true)) return (ResultadoIntervencao.Conflito, null);
            if (await LerFichaAsync(conn, id, ct) is not { } lida) return (ResultadoIntervencao.NaoExiste, null);

            var cfg = await LerConfiguracaoAsync(conn, ct);
            if (fechada && !lida.Ficha.Fechada && cfg.ExtExigeDoc && RegrasManutencao.FaltaParaFechar(a.TipoManut, a.NoFornecedor, a.DocExterno) is not null)
                return (ResultadoIntervencao.ExigeDocumento, null);

            await using var cmd = new SqlCommand(SqlFecharAntiga, conn);
            Texto(cmd, "@id", 25, id);
            cmd.Parameters.Add("@supervisao", SqlDbType.Bit).Value = supervisao;
            cmd.Parameters.Add("@fechada", SqlDbType.Bit).Value = fechada;
            Auditoria(cmd, supervisor.Codigo, await AgoraAsync(conn, ct));
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) return (ResultadoIntervencao.Conflito, null);
            return (ResultadoIntervencao.Ok, await MontarDetalheAsync(conn, id, supervisor, true, ct));
        });

    public Task<(ResultadoIntervencao, MaterialDto?)> EditarMaterialAsync(string id, string materialId, string design, decimal qtt, decimal epv, TecnicoAtual quem, bool supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, MaterialDto?)>(async conn =>
        {
            if (AcessoEdicao(await LerAtualAsync(conn, id, ct), quem, supervisor) is { } negado) return (negado, null);
            var material = new MaterialDto(materialId, RegrasManutencao.Cortar(design, RegrasManutencao.TamanhoDesignMaterial), qtt, epv, RegrasManutencao.CustoMaterial(qtt, epv));
            await using var cmd = new SqlCommand(SqlEditarMaterial, conn);
            Texto(cmd, "@id", 25, id);
            Texto(cmd, "@material", 25, materialId);
            Texto(cmd, "@design", 35, material.Design);
            Numero(cmd, "@qtt", material.Qtt, 16, 3);
            Numero(cmd, "@epv", material.Epv, 16, 3);
            Numero(cmd, "@custo", material.Custo, 16, 3);
            Auditoria(cmd, quem.Codigo, await AgoraAsync(conn, ct));
            var usaCompras = (await LerConfiguracaoAsync(conn, ct)).ComprasFichaInt;
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            cmd.Transaction = tx;
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.NaoExiste, null); }
            await RecalcularTotaisAsync(conn, id, usaCompras, ct, tx);
            await tx.CommitAsync(ct);
            return (ResultadoIntervencao.Ok, material);
        });

    public Task<IReadOnlyList<TempoDto>?> ListarMaoDeObraAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct) => Executar<IReadOnlyList<TempoDto>?>(async conn =>
        await FichaVisivelAsync(conn, id, quem, supervisor, ct) is null ? null : await LerTemposAsync(conn, id, ct));

    public Task<(ResultadoIntervencao, TempoDto?)> AdicionarMaoDeObraAsync(string id, int tecno, string nome, decimal horas, decimal custoHora, TecnicoAtual quem, bool supervisor, CancellationToken ct) =>
        GravarTempoAsync(id, null, tecno, nome, horas, custoHora, quem, supervisor, ct);

    public Task<(ResultadoIntervencao, TempoDto?)> EditarMaoDeObraAsync(string id, string tempoId, int tecno, string nome, decimal horas, decimal custoHora, TecnicoAtual quem, bool supervisor, CancellationToken ct) =>
        GravarTempoAsync(id, tempoId, tecno, nome, horas, custoHora, quem, supervisor, ct);

    private Task<(ResultadoIntervencao, TempoDto?)> GravarTempoAsync(string id, string? tempoId, int tecno, string nome, decimal horas, decimal custoHora, TecnicoAtual quem, bool supervisor, CancellationToken ct) =>
        Executar<(ResultadoIntervencao, TempoDto?)>(async conn =>
        {
            var atual = await LerAtualAsync(conn, id, ct);
            if (AcessoEdicao(atual, quem, supervisor) is { } negado) return (negado, null);
            if (!supervisor)
            {
                // O operador só lança horas de quem está na ficha e nunca fixa o custo/hora: usa o gravado na linha ou o último do técnico, lidos aqui.
                if (!RegrasIntervencao.PertenceAFicha((int)atual!.Tecno, atual.Tecnos, tecno)) return (ResultadoIntervencao.TecnicoInvalido, null);
                if (tempoId is null) custoHora = await CustoHoraDoTecnicoAsync(conn, tecno, ct);
                else if (await CustoHoraDaLinhaAsync(conn, id, tempoId, ct) is { } gravado) custoHora = gravado;
                else return (ResultadoIntervencao.NaoExiste, null);
            }
            var tempo = new TempoDto(tempoId ?? Guid.NewGuid().ToString("N")[..25].ToUpperInvariant(), tecno, RegrasManutencao.Cortar(nome, RegrasManutencao.TamanhoNomeTecnico),
                horas, RegrasManutencao.CustoTempo(horas, custoHora), custoHora);
            await using var cmd = new SqlCommand(tempoId is null ? SqlNovoTempo : SqlEditarTempo, conn);
            Texto(cmd, "@id", 25, id);
            Texto(cmd, "@tempo", 25, tempo.Id);
            Numero(cmd, "@no", tecno, 5);
            Texto(cmd, "@responsavel", RegrasManutencao.TamanhoNomeTecnico, tempo.Responsavel);
            Numero(cmd, "@horas", horas, 16, 3);
            Numero(cmd, "@custo", tempo.Custo, 16, 3);
            Auditoria(cmd, quem.Codigo, await AgoraAsync(conn, ct));
            var usaCompras = (await LerConfiguracaoAsync(conn, ct)).ComprasFichaInt;
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            cmd.Transaction = tx;
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.NaoExiste, null); }
            await RecalcularTotaisAsync(conn, id, usaCompras, ct, tx);
            await tx.CommitAsync(ct);
            return (ResultadoIntervencao.Ok, tempo);
        });

    public Task<ResultadoIntervencao> ApagarMaoDeObraAsync(string id, string tempoId, TecnicoAtual quem, bool supervisor, CancellationToken ct) => Executar(async conn =>
    {
        if (AcessoEdicao(await LerAtualAsync(conn, id, ct), quem, supervisor) is { } negado) return negado;
        await using var cmd = new SqlCommand(SqlApagarTempo, conn);
        Texto(cmd, "@id", 25, id);
        Texto(cmd, "@tempo", 25, tempoId);
        var usaCompras = (await LerConfiguracaoAsync(conn, ct)).ComprasFichaInt;
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        cmd.Transaction = tx;
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return ResultadoIntervencao.NaoExiste; }
        await RecalcularTotaisAsync(conn, id, usaCompras, ct, tx);
        await tx.CommitAsync(ct);
        return ResultadoIntervencao.Ok;
    });

    private static async Task<decimal> CustoHoraDoTecnicoAsync(SqlConnection conn, int tecno, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlCustoHora, conn);
        Numero(cmd, "@no", tecno, 5);
        return await cmd.ExecuteScalarAsync(ct) is decimal valor ? valor : 0m;
    }

    // Custo/hora já gravado na linha (null se a linha não existe).
    private static async Task<decimal?> CustoHoraDaLinhaAsync(SqlConnection conn, string id, string tempoId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlLinhaTempo, conn);
        Texto(cmd, "@id", 25, id);
        Texto(cmd, "@tempo", 25, tempoId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var horas = Dec(r, 1);
        return horas > 0 ? Math.Round(Dec(r, 0) / horas, 3) : 0m;
    }

    public Task<decimal> CustoHoraSugeridoAsync(int tecno, CancellationToken ct) => Executar(conn => CustoHoraDoTecnicoAsync(conn, tecno, ct));

    public Task<IReadOnlyList<CompraLigadaDto>?> ListarComprasAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct) => Executar<IReadOnlyList<CompraLigadaDto>?>(async conn =>
    {
        if (await FichaVisivelAsync(conn, id, quem, supervisor, ct) is null) return null;
        var lista = new List<CompraLigadaDto>();
        if (!(await LerConfiguracaoAsync(conn, ct)).ComprasFichaInt) return lista;
        await using var cmd = new SqlCommand(SqlCompras, conn);
        Texto(cmd, "@id", 25, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            lista.Add(new CompraLigadaDto(
                r.IsDBNull(0) || r.GetDateTime(0) <= SemData ? null : DateOnly.FromDateTime(r.GetDateTime(0)),
                r.IsDBNull(1) ? "" : r.GetString(1).Trim(), r.IsDBNull(2) ? "" : r.GetString(2).Trim(), r.IsDBNull(3) ? "" : r.GetString(3).Trim(),
                r.IsDBNull(4) ? "" : r.GetString(4).Trim(), r.IsDBNull(5) ? 0 : Dec(r, 5), r.IsDBNull(6) ? 0 : Dec(r, 6), r.IsDBNull(7) ? "" : r.GetString(7).Trim()));
        return lista;
    });

    public Task<IReadOnlyList<FornecedorDto>> PesquisarFornecedoresAsync(string texto, CancellationToken ct) => Executar<IReadOnlyList<FornecedorDto>>(async conn =>
    {
        await using var cmd = new SqlCommand(SqlFornecedores, conn);
        Texto(cmd, "@texto", 130, Padrao(texto));
        Texto(cmd, "@exato", 20, texto);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<FornecedorDto>();
        while (await r.ReadAsync(ct)) lista.Add(new FornecedorDto((long)Dec(r, 0), r.GetString(1).Trim()));
        return lista;
    });

    // Atrasadas: a mesma regra do KPI do ecrã de planeadas (data passada ou fora de prazo), sobre a mesma lista (até 500).
    public Task<ContadoresMenuDto> ContarMenuAsync(CancellationToken ct) => Executar(async conn =>
    {
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);
        var atrasadas = 0;
        await using (var cmd = new SqlCommand(SqlPlaneadas, conn))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var lidas = 0;
            while (lidas++ < 500 && await r.ReadAsync(ct))
            {
                var f = LerPlaneada(r);
                if (RegrasManutencao.EmAtraso(f.DataPlaneada, f.DataPrevista, hoje)) atrasadas++;
            }
        }
        await using var contar = new SqlCommand(SqlContarPorValidar, conn);
        return new ContadoresMenuDto(atrasadas, Convert.ToInt32(await contar.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture));
    });
}
