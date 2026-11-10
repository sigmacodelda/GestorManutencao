using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Fichas de intervenção do PHC (u_fichaint), equipamentos (u_equip) e materiais (u_matinterv), com o mesmo circuito da LandeiroMaui:
// estado em texto (Planeada, EmCurso, Concluida, Validada, Cancelada), técnico = tecno + tecnome. Ver docs/ligacao-phc-intervencoes.md.
public sealed partial class SqlRepositorioIntervencoes(IConfiguration configuracao, TimeProvider relogio, ILogger<SqlRepositorioIntervencoes> log)
    : IRepositorioIntervencoes
{
    // Um único semáforo de numeração (estático): geração de planeadas, avarias e a planeada seguinte nunca escolhem o mesmo `no` em simultâneo.
    private static readonly SemaphoreSlim Numeracao = new(1, 1);
    // Serializa a procura e a gravação da avaria com OpId, para dois pedidos iguais em simultâneo não criarem duas fichas.
    private static readonly SemaphoreSlim AvariaOp = new(1, 1);
    private static readonly DateTime SemData = new(1900, 1, 1);

    // Técnicos adicionais: memo tecnos (";12;45;"). O LIKE com ";n;" não confunde o 12 com o 123.
    private const string ColunaTecnos = RegrasIntervencao.ColunaTecnos;
    private const string TecnosSel = "isnull(cast(f." + ColunaTecnos + " as varchar(max)), '')";
    private const string EmTecnos = "cast(f." + ColunaTecnos + " as varchar(max)) like @padrao";
    private const string EmTecnosSemAlias = "cast(" + ColunaTecnos + " as varchar(max)) like @padrao";

    private const string Selecao =
        $"""
        select f.u_fichaintstamp, case when f.design <> '' then f.design else isnull(e.design, '') end, isnull(e.[local], ''),
               f.tipointerv, f.data, rtrim(f.estado), f.problema, f.resolucao, f.hini, f.hfim, f.tecnome, f.tecno, isnull(f.valobs, ''), {TecnosSel}
        from u_fichaint f with (nolock) left join u_equip e with (nolock) on e.u_equipstamp = f.u_equipstamp
        """;

    // Do técnico, principal ou adicional (ou sem dono e já dentro da antecedência); em curso e planeadas, sem viaturas.
    private const string SqlListar = Selecao +
        $"""

        where f.estado in ('Planeada', 'EmCurso') and f.fechada = 0 and f.obsoleto = 0 and isnull(e.tipo, '') <> @veiculos
          and (f.tecno = @tecno or {EmTecnos} or (f.tecno = 0 and f.data <= @ate))
        order by f.data, f.ordem, f.no
        """;

    private const string SqlObter = Selecao +
        $"""

        where f.u_fichaintstamp = @id and f.obsoleto = 0 and isnull(e.tipo, '') <> @veiculos and (f.tecno = @tecno or f.tecno = 0 or {EmTecnos})
        """;

    private const string SqlObterQualquer = Selecao +
        """

        where f.u_fichaintstamp = @id and f.obsoleto = 0 and isnull(e.tipo, '') <> @veiculos
        """;

    // Como a LandeiroMaui: última feita da tarefa (ou, nas fichas antigas, do equipamento com a mesma periodicidade), última cancelada e periodicidade.
    private const string SqlPlaneadas =
        $"""
        select f.u_fichaintstamp, case when f.design <> '' then f.design else isnull(e.design, '') end, isnull(e.[local], ''),
               f.tipointerv, f.data, rtrim(f.estado), f.problema, f.resolucao, f.hini, f.hfim, f.tecnome, f.tecno, isnull(f.valobs, ''), {TecnosSel},
               coalesce(c.feita, lg.ultima), c.cancelada, isnull(pp.unidade, ''), isnull(pp.intervalo, 0)
        from u_fichaint f with (nolock)
        left join u_equip e with (nolock) on e.u_equipstamp = f.u_equipstamp
        left join u_itmanutli l with (nolock) on l.u_itmanutlistamp = f.uitmanutlistamp
        outer apply (select top 1 ltrim(rtrim(p.unidade)) as unidade, p.intervalo
                     from u_periocid p with (nolock)
                     where left(ltrim(rtrim(cast(p.periodo as varchar(254)))), 20) = ltrim(rtrim(isnull(l.periodo, f.periodo)))
                     order by p.intervalo desc) pp
        outer apply (select max(case when x.estado in ('Concluida', 'Validada') then x.data end) as feita,
                            max(case when x.estado = 'Cancelada' then x.data end) as cancelada
                     from u_fichaint x with (nolock)
                     where f.uitmanutlistamp <> '' and x.uitmanutlistamp = f.uitmanutlistamp
                       and x.estado in ('Concluida', 'Validada', 'Cancelada')) c
        outer apply (select max(x.data) as ultima
                     from u_fichaint x with (nolock)
                     where x.u_equipstamp = f.u_equipstamp and x.estado = '' and x.obsoleto = 0 and x.data <= getdate()
                       and ltrim(rtrim(x.periodo)) = ltrim(rtrim(f.periodo)) and ltrim(rtrim(f.periodo)) <> '') lg
        where f.estado in ('Planeada', 'EmCurso') and f.obsoleto = 0 and isnull(e.tipo, '') <> @veiculos
        order by f.data, f.ordem, f.no
        """;

    private const string SqlPorValidar = Selecao +
        """

        where f.estado = 'Concluida' and f.obsoleto = 0 and isnull(e.tipo, '') <> @veiculos
        order by f.data, f.no
        """;

    private const string SqlAtual =
        $"""
        select rtrim(f.estado), f.uitmanutlistamp, f.tipomanut, f.nofornec, f.docexterno, f.tecno, isnull(e.[local], ''), f.hini, {TecnosSel}
        from u_fichaint f with (nolock) left join u_equip e with (nolock) on e.u_equipstamp = f.u_equipstamp
        where f.u_fichaintstamp = @id and f.obsoleto = 0 and isnull(e.tipo, '') <> @veiculos
        """;

    private const string SqlConfiguracao =
        """
        select top 1 antecedencia, gerarplaneadas, avancaordem, validarpropria, extexigedoc, comprasfichaint
        from u_configman with (nolock) order by ousrdata, ousrhora
        """;

    // matobrig (bit) é o campo novo de u_configman ("exigir material ao concluir"): tem de ser criado no PHC; enquanto não existir, lê-se como desligado.
    private const string SqlConfiguracaoMaterial =
        """
        select top 1 antecedencia, gerarplaneadas, avancaordem, validarpropria, extexigedoc, comprasfichaint, matobrig
        from u_configman with (nolock) order by ousrdata, ousrhora
        """;

    private static volatile bool _campoMaterialExiste;

    private const string SqlMateriaisDaFicha = "select count(*) from u_matinterv with (nolock) where u_fichaintstamp = @id";

    // Anexos do PHC ligados à ficha. oritable e tabnm identificam a tabela de origem (Suposição: 'U_FICHAINT' / 'Fichas de Intervenção', por confirmar
    // com um anexo real da ficha no PHC); origem identifica as fotos desta app, que são as únicas que a app lista e apaga.
    private const string OrigemAnexos = "GestorManutencao", OriTableFicha = "U_FICHAINT", TabNmFicha = "Fichas de Intervenção";

    private const string SqlFotos =
        """
        select anexosstamp, fname, flen from anexos with (nolock)
        where oritable = @oritable and recstamp = @id and cast(origem as varchar(50)) = @origem
        order by ousrdata, ousrhora, anexosid
        """;

    private const string SqlFoto =
        """
        select fname, bdados from anexos with (nolock)
        where anexosstamp = @foto and oritable = @oritable and recstamp = @id and cast(origem as varchar(50)) = @origem
        """;

    private const string SqlContarFotos =
        "select count(*) from anexos with (nolock) where oritable = @oritable and recstamp = @id and cast(origem as varchar(50)) = @origem";

    // anexosid é IDENTITY (não entra no INSERT); as restantes colunas ficam com o valor por omissão da tabela.
    private const string SqlNovaFoto =
        """
        insert into anexos (anexosstamp, oritable, tabnm, recstamp, resumo, descricao, bdados, fullname, fname, fext, flen, tipo, origem, tpdos, tpdoc,
                            ausrinis, ausrdata, ausrhora, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        values (@stamp, @oritable, @tabnm, @id, @resumo, @descricao, @dados, @fullname, @fname, 'jpg', @flen, 1, @origem, 0, 0,
                @codigo, @dia, @hora, @codigo, @dia, @hora, @codigo, @dia, @hora)
        """;

    private const string SqlApagarFoto =
        "delete from anexos where anexosstamp = @foto and oritable = @oritable and recstamp = @id and cast(origem as varchar(50)) = @origem";

    // Como a LandeiroMaui: última feita = fichas antigas (sem estado), concluídas ou validadas; abertas = antigas por fechar, em curso e por validar (as planeadas não contam).
    private const string SelecaoEquipamento =
        """
        select top 501 e.u_equipstamp, isnull(e.design, ''), isnull(e.[local], ''), isnull(e.ref, ''), isnull(e.nointerno, 0), isnull(e.tipo, ''), isnull(e.marca, ''),
               isnull(e.modelo, ''), isnull(e.serie, ''), isnull(e.status, ''), e.obsoleto, f.ultima, isnull(f.abertas, 0)
        from u_equip e with (nolock)
        left join (select u_equipstamp,
                          max(case when estado in ('', 'Concluida', 'Validada') and data <= getdate() then data end) as ultima,
                          sum(case when fechada = 0 and obsoleto = 0 and estado in ('', 'EmCurso', 'Concluida') then 1 else 0 end) as abertas
                   from u_fichaint with (nolock) where u_equipstamp <> '' group by u_equipstamp) f on f.u_equipstamp = e.u_equipstamp
        """;

    private const string SqlEquipamentos = SelecaoEquipamento +
        """

        where isnull(e.tipo, '') <> @veiculos and (@obsoletos = 1 or e.obsoleto = 0)
          and (@tipo = '' or e.tipo = @tipo) and (@local = '' or e.[local] = @local)
          and (@texto = '' or e.design like @texto escape '\' or e.ref like @texto escape '\' or e.marca like @texto escape '\' or e.modelo like @texto escape '\')
        order by e.design, e.nointerno
        """;

    private const string SqlEquipamento = SelecaoEquipamento +
        """

        where e.u_equipstamp = @id and isnull(e.tipo, '') <> @veiculos
        """;

    // Tarefas ativas das instruções de manutenção do equipamento, com a periodicidade e as datas necessárias à próxima.
    private const string SqlTarefasEquipamento =
        """
        select l.u_itmanutlistamp, isnull(left(cast(l.descricao as varchar(max)), 400), ''), ltrim(rtrim(l.periodo)), isnull(p.unidade, ''), isnull(p.intervalo, 0),
               c.feita, c.cancelada, lg.ultima, c.aberta
        from u_itmanutli l with (nolock)
        join u_itmanut i with (nolock) on i.u_itmanutstamp = l.u_itmanutstamp
        outer apply (select top 1 ltrim(rtrim(pp.unidade)) as unidade, pp.intervalo
                     from u_periocid pp with (nolock)
                     where left(ltrim(rtrim(cast(pp.periodo as varchar(254)))), 20) = ltrim(rtrim(l.periodo))
                     order by pp.intervalo desc) p
        left join (select uitmanutlistamp,
                          max(case when estado in ('Concluida', 'Validada') then data end) as feita,
                          max(case when estado = 'Cancelada' then data end) as cancelada,
                          min(case when estado in ('Planeada', 'EmCurso') then data end) as aberta
                   from u_fichaint with (nolock) where uitmanutlistamp <> '' and u_equipstamp = @id group by uitmanutlistamp) c on c.uitmanutlistamp = l.u_itmanutlistamp
        left join (select ltrim(rtrim(periodo)) as periodo, max(data) as ultima
                   from u_fichaint with (nolock) where u_equipstamp = @id and estado = '' and obsoleto = 0 and data <= getdate() and ltrim(rtrim(periodo)) <> ''
                   group by ltrim(rtrim(periodo))) lg on lg.periodo = ltrim(rtrim(l.periodo))
        where i.u_equipstamp = @id and l.ativo = 1 and i.obsoleto = 0
        order by ltrim(rtrim(l.periodo)), l.u_itmanutlistamp
        """;

    // As fichas antigas do PHC não têm estado: fechadas contam como validadas, as que ficaram abertas como concluídas.
    private const string EstadoEfetivo = "case when rtrim(f.estado) <> '' then rtrim(f.estado) when f.fechada = 1 then 'Validada' else 'Concluida' end";

    private const string CamposFichaBase =
        " f.u_fichaintstamp, case when f.design <> '' then f.design else isnull(e.design, '') end, isnull(e.[local], ''), f.tipointerv, f.data, " + EstadoEfetivo +
        ", f.problema, f.resolucao, f.hini, f.hfim, f.tecnome, f.tecno, isnull(f.valobs, ''), " + TecnosSel;

    private const string FromFicha = " from u_fichaint f with (nolock) left join u_equip e with (nolock) on e.u_equipstamp = f.u_equipstamp";

    private const string CamposFicha = CamposFichaBase + FromFicha;

    // Lista do supervisor: mais número, total, fechada, supervisão, fora de ordem e ficha antiga (estado vazio).
    private const string CamposTodas = CamposFichaBase +
        ", f.no, isnull(f.totmo, 0) + isnull(f.totref, 0), f.fechada, f.supervisao, f.foraordem, case when rtrim(f.estado) = '' then 1 else 0 end" + FromFicha;

    // Histórico do equipamento: as planeadas (ainda por fazer) ficam nas tarefas.
    private const string SqlHistoricoEquipamento = "select top 50" + CamposFicha +
        " where f.u_equipstamp = @id and f.obsoleto = 0 and f.estado <> 'Planeada' order by f.data desc, f.no desc";

    // Sem OFFSET (nível 100): lê-se até ao fim da página pedida mais uma ficha (para saber se há mais) e saltam-se as anteriores.
    private const string SqlTodasBase = "select top (@topo)" + CamposTodas +
        " where isnull(e.tipo, '') <> @veiculos and f.obsoleto = 0 and (@estado = '' or " + EstadoEfetivo + " = @estado)" +
        " and f.data >= @desde and f.data < @ate and (@tecno = 0 or f.tecno = @tecno or " + EmTecnos + ")" +
        " and (@situacao = 'Todas' or (@situacao = 'Abertas' and f.fechada = 0 and rtrim(f.estado) <> 'Cancelada') or (@situacao = 'Fechadas' and f.fechada = 1))" +
        " and (@soPorSupervisionar = 0 or f.supervisao = 0) and (@tipo = '' or rtrim(f.tipointerv) = @tipo)" +
        " and (@texto = '' or f.design like @texto escape '\\' or e.design like @texto escape '\\' or f.ref like @texto escape '\\'" +
        " or f.problema like @texto escape '\\' or f.resolucao like @texto escape '\\' or f.tecnome like @texto escape '\\' or cast(f.no as varchar(20)) = @exato)";

    // A ordem vem de uma lista fechada (nunca de texto do utilizador); o desempate é sempre o mesmo.
    public static string OrdenarTodas(OrdemFichas ordem, bool descendente)
    {
        var coluna = ordem switch
        {
            OrdemFichas.Numero => "f.no",
            OrdemFichas.Equipamento => "case when f.design <> '' then f.design else isnull(e.design, '') end",
            OrdemFichas.Tipo => "f.tipointerv",
            OrdemFichas.Total => "isnull(f.totmo, 0) + isnull(f.totref, 0)",
            _ => "f.data"
        };
        var sentido = descendente ? " desc" : "";
        return $" order by {coluna}{sentido}, f.no{sentido}, f.u_fichaintstamp{sentido}";
    }

    // Cache offline: fichas fechadas do técnico (principal ou adicional) nas últimas semanas, sem viaturas.
    private const string SqlRecentes = "select top 500" + CamposFicha +
        " where (f.tecno = @tecno or " + EmTecnos + ") and f.obsoleto = 0 and isnull(e.tipo, '') <> @veiculos and " + EstadoEfetivo + " in ('Concluida', 'Validada', 'Cancelada')" +
        " and f.data >= @desde order by f.data desc, f.no desc, f.u_fichaintstamp desc";

    // Procura a avaria já gravada com o marcador do OpId no fim do problema (sem LIKE, por causa dos parênteses retos).
    private const string SqlAvariaPorOp =
        """
        select top 1 f.u_fichaintstamp from u_fichaint f with (nolock)
        where f.tecno = @tecno and f.obsoleto = 0 and right(rtrim(cast(f.problema as varchar(254))), @tam) = @marcador
        order by f.data desc, f.no desc
        """;

    // Pesquisa leve da avaria: sem o agregado das fichas.
    private const string SqlPesquisaAvaria =
        """
        select top 20 u_equipstamp, isnull(design, ''), isnull([local], '') from u_equip with (nolock)
        where obsoleto = 0 and isnull(tipo, '') <> @veiculos and (design like @texto escape '\' or ref like @texto escape '\')
        order by design
        """;

    private const string SqlGuardarConfiguracao =
        """
        set xact_abort on;
        begin transaction;
        -- UPDLOCK + HOLDLOCK: duas gravações em simultâneo não criam duas linhas.
        if exists (select 1 from u_configman with (updlock, holdlock))
            update u_configman set antecedencia = @antecedencia, gerarplaneadas = @gerar, avancaordem = @avanca, validarpropria = @propria,
                   extexigedoc = @ext, comprasfichaint = @compras, usrinis = @codigo, usrdata = @dia, usrhora = @hora;
        else
            insert into u_configman (u_configmanstamp, antecedencia, gerarplaneadas, avancaordem, validarpropria, extexigedoc, pinmaxtent, comprasfichaint,
                                     ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            values (@stamp, @antecedencia, @gerar, @avanca, @propria, @ext, 5, @compras, @codigo, @dia, @hora, @codigo, @dia, @hora);
        commit transaction;
        """;

    private const string SqlNumerosDoAno = "select no from u_fichaint with (nolock) where data >= @ini and data < @fim";

    private const string SqlAvaria =
        """
        insert into u_fichaint (u_fichaintstamp, no, ref, design, tipointerv, tipomanut, problema, data, estado, u_equipstamp, hini, tecno, tecnome,
                                ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        select @stamp, @no, left(e.ref, 20), left(e.design, 45), 'Curativa', 'Interna', @anomalia, @dia, 'EmCurso', e.u_equipstamp, @agora, @tecno, @tecnome,
               @codigo, @dia, @hora, @codigo, @dia, @hora
        from u_equip e where e.u_equipstamp = @equipamento and e.obsoleto = 0 and isnull(e.tipo, '') <> @veiculos
        """;

    private const string SqlPrimeiraDaZona =
        """
        select top 1 f.u_fichaintstamp
        from u_fichaint f with (nolock) left join u_equip e with (nolock) on e.u_equipstamp = f.u_equipstamp
        where f.estado = 'Planeada' and isnull(e.[local], '') = @local
        order by f.data, f.ordem, f.no
        """;

    // O UPDATE só acontece se o estado ainda for o lido: se alguém mudou entretanto, não grava.
    private const string SqlIniciar =
        $"""
        update u_fichaint set estado = 'EmCurso',
               hini = case when hini <= '19000101' then @agora else hini end,
               tecno = case when tecno = 0 then @tecno else tecno end,
               tecnome = case when tecno = 0 then @tecnome else tecnome end,
               foraordem = @fora, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and fechada = 0 and obsoleto = 0 and estado = 'Planeada' and (tecno = @tecno or tecno = 0 or {EmTecnosSemAlias})
        """;

    private const string SqlConcluir =
        $"""
        update u_fichaint set estado = 'Concluida', resolucao = @feito, hini = @inicio, hfim = @fim, data = @dataficha,
               usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and fechada = 0 and obsoleto = 0 and estado = 'EmCurso' and (tecno = @tecno or tecno = 0 or {EmTecnosSemAlias})
        """;

    private const string SqlValidar =
        """
        update u_fichaint set estado = 'Validada', fechada = 1, supervisao = 1, valinis = @codigo, valdata = @agora,
               usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and estado = 'Concluida'
        """;

    // Ao concluir já se gerou a seguinte; ao devolver, essa planeada ainda por tocar sai (uma só em aberto por tarefa).
    private const string SqlApagarSeguinte =
        """
        delete from u_fichaint
        where uitmanutlistamp = @tarefa and estado = 'Planeada' and u_fichaintstamp <> @id and tecno = 0
          and not exists (select 1 from u_tempinterv t where t.u_fichaintstamp = u_fichaint.u_fichaintstamp)
          and not exists (select 1 from u_matinterv m where m.u_fichaintstamp = u_fichaint.u_fichaintstamp)
        """;

    private const string SqlDevolver =
        """
        update u_fichaint set estado = 'EmCurso', valobs = @motivo, hfim = '19000101', usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and estado = 'Concluida'
        """;

    // Mudar o principal tira-o da lista de adicionais (o texto novo já vem calculado); só grava se os adicionais não mudaram entretanto.
    private const string SqlAtribuir =
        $"""
        update u_fichaint set tecno = @novo, tecnome = @tecnome, {ColunaTecnos} = @tecnos, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and fechada = 0 and obsoleto = 0 and estado in ('Planeada', 'EmCurso')
          and isnull(cast({ColunaTecnos} as varchar(max)), '') = @antes
        """;

    // Só com técnico principal, se este continua o mesmo e os adicionais não mudaram entretanto.
    private const string SqlDefinirAdicionais =
        $"""
        update u_fichaint set {ColunaTecnos} = @tecnos, usrinis = @codigo, usrdata = @dia, usrhora = @hora
        where u_fichaintstamp = @id and fechada = 0 and obsoleto = 0 and estado in ('Planeada', 'EmCurso')
          and tecno = @principal and isnull(cast({ColunaTecnos} as varchar(max)), '') = @antes
        """;

    private const string SqlMateriais =
        "select u_matintervstamp, design, qtt, epv, custo from u_matinterv with (nolock) where u_fichaintstamp = @id order by ousrdata, ousrhora";

    private const string SqlNovoMaterial =
        """
        insert into u_matinterv (u_matintervstamp, u_fichaintstamp, design, qtt, epv, custo, ref, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        values (@stamp, @id, @design, @qtt, @epv, @custo, '', @codigo, @dia, @hora, @codigo, @dia, @hora)
        """;

    private const string SqlApagarMaterial = "delete from u_matinterv where u_matintervstamp = @material and u_fichaintstamp = @id";

    // Mesma regra "Actualiza Totais" da LandeiroMaui; os externos só vêm das compras quando a configuração usa compras e há compras ligadas.
    private const string SqlTotais =
        """
        declare @linhas int = 0, @moe numeric(19,6) = 0, @mate numeric(19,6) = 0;
        if @usaCompras = 1
            select @linhas = count(*),
                   @moe = isnull(sum(case when fn.u_tpint = @mo then case when cm1.debito = 1 then -fn.etiliquido else fn.etiliquido end end), 0),
                   @mate = isnull(sum(case when fn.u_tpint = @pecas then case when cm1.debito = 1 then -fn.etiliquido else fn.etiliquido end end), 0)
            from fn left join fo on fo.fostamp = fn.fostamp left join cm1 on cm1.cm = fo.doccode
            where fn.u_fichaint = @id;

        update u_fichaint set
            mointerna = isnull((select sum(t.custo) from u_tempinterv t where t.u_fichaintstamp = @id), 0),
            matinterno = isnull((select sum(m.custo) from u_matinterv m where m.u_fichaintstamp = @id), 0),
            moexterna = case when @linhas > 0 then @moe else moexterna end,
            matexterno = case when @linhas > 0 then @mate else matexterno end
        where u_fichaintstamp = @id;

        update u_fichaint set totmo = mointerna + moexterna, totref = matinterno + matexterno where u_fichaintstamp = @id;
        """;

    private const string SqlCandidatas =
        """
        select l.u_itmanutlistamp, i.u_equipstamp, e.ref, e.design, ltrim(rtrim(l.periodo)), ltrim(rtrim(l.tipomanut)), ltrim(rtrim(l.responsabilidade)),
               left(cast(l.descricao as varchar(max)), 400), isnull(p.unidade, ''), isnull(p.intervalo, 0),
               c.feita, c.cancelada, lg.ultima, isnull(c.aberta, 0)
        from u_itmanutli l with (nolock)
        join u_itmanut i with (nolock) on i.u_itmanutstamp = l.u_itmanutstamp
        join u_equip e with (nolock) on e.u_equipstamp = i.u_equipstamp
        outer apply (select top 1 ltrim(rtrim(pp.unidade)) as unidade, pp.intervalo
                     from u_periocid pp with (nolock)
                     where left(ltrim(rtrim(cast(pp.periodo as varchar(254)))), 20) = ltrim(rtrim(l.periodo))
                     order by pp.intervalo desc) p
        left join (select uitmanutlistamp,
                          max(case when estado in ('Concluida', 'Validada') then data end) as feita,
                          max(case when estado = 'Cancelada' then data end) as cancelada,
                          max(case when estado in ('Planeada', 'EmCurso') then 1 else 0 end) as aberta
                   from u_fichaint with (nolock) where uitmanutlistamp <> '' group by uitmanutlistamp) c on c.uitmanutlistamp = l.u_itmanutlistamp
        left join (select u_equipstamp, ltrim(rtrim(periodo)) as periodo, max(data) as ultima
                   from u_fichaint with (nolock) where estado = '' and obsoleto = 0 and data <= getdate() and ltrim(rtrim(periodo)) <> ''
                   group by u_equipstamp, ltrim(rtrim(periodo))) lg on lg.u_equipstamp = i.u_equipstamp and lg.periodo = ltrim(rtrim(l.periodo))
        where l.ativo = 1 and i.obsoleto = 0 and e.obsoleto = 0 and e.tipo <> @veiculos and (@tarefa = '' or l.u_itmanutlistamp = @tarefa)
        """;

    // Se outra pessoa gerou entretanto, não duplica. Sem nolock de propósito: é a verificação que protege a escrita. Revalida no insert que o equipamento,
    // a tarefa e a instrução continuam ativos (podem ter ficado obsoletos entre a leitura dos candidatos e a escrita).
    private const string SqlGerar =
        """
        insert into u_fichaint (u_fichaintstamp, no, u_equipstamp, ref, design, data, tipomanut, tipointerv, periodo, problema,
                                estado, uitmanutlistamp, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        select @stamp, @no, @equip, @ref, @design, @data, @tipomanut, 'Preventiva', @periodo, @problema,
               'Planeada', @tarefa, @codigo, @dia, @hora, @codigo, @dia, @hora
        where not exists (select 1 from u_fichaint where uitmanutlistamp = @tarefa and estado in ('Planeada', 'EmCurso'))
          and exists (select 1 from u_equip where u_equipstamp = @equip and obsoleto = 0)
          and exists (select 1 from u_itmanutli l join u_itmanut i on i.u_itmanutstamp = l.u_itmanutstamp where l.u_itmanutlistamp = @tarefa and l.ativo = 1 and i.obsoleto = 0)
        """;

    private sealed record Atual(string Estado, string Tarefa, string TipoManut, decimal NoFornecedor, string DocExterno, decimal Tecno, string Local, DateTime Hini, string Tecnos);

    private sealed record Candidata(string Stamp, string EquipStamp, string Ref, string Design, string Periodo, string TipoManut, string Responsabilidade,
        string Descricao, string Unidade, decimal Intervalo, DateTime? UltimaFeita, DateTime? UltimaCancelada, DateTime? UltimaLegado, bool Aberta);

    private sealed record Momento(DateTime Agora)
    {
        public DateTime Dia => Agora.Date;
        public string Hora => Agora.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private SqlConnection Abrir()
    {
        var ligacao = LigacaoPhc.Obter(configuracao);
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

    private static void Quando(SqlCommand cmd, string nome, DateTime valor) =>
        cmd.Parameters.Add(nome, SqlDbType.DateTime).Value = valor;

    private static void Numero(SqlCommand cmd, string nome, decimal valor, byte precisao = 16, byte escala = 0)
    {
        var p = cmd.Parameters.Add(nome, SqlDbType.Decimal);
        p.Precision = precisao; p.Scale = escala; p.Value = valor;
    }

    private static void Veiculos(SqlCommand cmd) =>
        cmd.Parameters.Add("@veiculos", SqlDbType.NVarChar, 30).Value = RegrasManutencao.TipoVeiculos;

    // As datas e horas gravadas são as do servidor SQL (como a LandeiroMaui), por isso não dependem do fuso da API.
    private static async Task<Momento> AgoraAsync(SqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("select getdate()", conn);
        return new Momento((DateTime)(await cmd.ExecuteScalarAsync(ct))!);
    }

    private static void Auditoria(SqlCommand cmd, string codigo, Momento m)
    {
        Texto(cmd, "@codigo", 30, codigo);
        Quando(cmd, "@agora", m.Agora);
        Quando(cmd, "@dia", m.Dia);
        Texto(cmd, "@hora", 8, m.Hora);
    }

    // Só depois de o campo existir é que passa a ser lido e gravado; a verificação repete-se até existir (sem reiniciar a API).
    private static async Task<bool> CampoMaterialAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_campoMaterialExiste) return true;
        await using var cmd = new SqlCommand("select col_length('u_configman', 'matobrig')", conn);
        return _campoMaterialExiste = await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
    }

    private static async Task<ConfigManutencao> LerConfiguracaoAsync(SqlConnection conn, CancellationToken ct)
    {
        var comMaterial = await CampoMaterialAsync(conn, ct);
        await using var cmd = new SqlCommand(comMaterial ? SqlConfiguracaoMaterial : SqlConfiguracao, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new ConfigManutencao((int)r.GetDecimal(0), r.GetBoolean(1), r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4), r.GetBoolean(5),
                comMaterial && !r.IsDBNull(6) && Convert.ToBoolean(r.GetValue(6), CultureInfo.InvariantCulture))
            : new ConfigManutencao();
    }

    private static IntervencaoDto Ler(SqlDataReader r, bool podeValidar = true)
    {
        var tipo = string.Equals(r.GetString(3).Trim(), "Curativa", StringComparison.OrdinalIgnoreCase) ? TipoIntervencao.Curativa : TipoIntervencao.Preventiva;
        var estado = Enum.TryParse<EstadoIntervencao>(r.GetString(5).Trim(), out var e) ? e : EstadoIntervencao.Planeada;
        var hini = r.GetDateTime(8);
        var hfim = r.GetDateTime(9);
        return new IntervencaoDto(
            r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim(), tipo, DateOnly.FromDateTime(r.GetDateTime(4)), estado,
            Vazio(RegrasIntervencao.SemMarcador(r.GetString(6))), Vazio(r.GetString(7)),
            hini > SemData ? TimeOnly.FromDateTime(hini) : null, hfim > SemData ? TimeOnly.FromDateTime(hfim) : null,
            Vazio(r.GetString(10)), (int)r.GetDecimal(11), podeValidar,
            Motivo: estado == EstadoIntervencao.EmCurso ? Vazio(r.GetString(12)) : null,
            Adicionais: RegrasIntervencao.AdicionaisDe(r.GetString(13), (int)r.GetDecimal(11)));
    }

    // Data prevista = a mais recente entre a última feita e a última cancelada (cancelar salta uma ocorrência) + periodicidade.
    private static IntervencaoDto LerPlaneada(SqlDataReader r)
    {
        var feita = r.IsDBNull(14) ? (DateTime?)null : r.GetDateTime(14);
        var cancelada = r.IsDBNull(15) ? (DateTime?)null : r.GetDateTime(15);
        var baseData = cancelada.HasValue && (!feita.HasValue || cancelada > feita) ? cancelada : feita;
        var prevista = baseData.HasValue
            ? RegrasManutencao.ProximaDepoisDaUltima(baseData.Value, RegrasManutencao.LerUnidade(r.GetString(16)), (int)r.GetDecimal(17))
            : null;
        return Ler(r) with
        {
            UltimaIntervencao = feita.HasValue ? DateOnly.FromDateTime(feita.Value) : null,
            UltimaCancelada = cancelada.HasValue ? DateOnly.FromDateTime(cancelada.Value) : null,
            DataPrevista = prevista.HasValue ? DateOnly.FromDateTime(prevista.Value) : null
        };
    }

    private static TimeOnly? Segundos(TimeOnly? hora) => hora is { } h ? new TimeOnly(h.Hour, h.Minute, h.Second) : null;

    private static string? Vazio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static async Task<Atual?> LerAtualAsync(SqlConnection conn, string id, CancellationToken ct, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand(SqlAtual, conn, tx);
        Texto(cmd, "@id", 25, id);
        Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new Atual(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim(), r.GetDecimal(3), r.GetString(4), r.GetDecimal(5), r.GetString(6).Trim(), r.GetDateTime(7), r.GetString(8))
            : null;
    }

    private static async Task<IntervencaoDto?> LerUmaAsync(SqlConnection conn, string id, CancellationToken ct, TecnicoAtual? tecnico = null)
    {
        await using var cmd = new SqlCommand(tecnico is null ? SqlObterQualquer : SqlObter, conn);
        Texto(cmd, "@id", 25, id);
        Veiculos(cmd);
        if (tecnico is not null) { Numero(cmd, "@tecno", tecnico.Tecno, 5); ParametroTecnos(cmd, tecnico.Tecno); }
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    }

    private static bool Minha(Atual a, TecnicoAtual t) => RegrasIntervencao.PodeMexer((int)a.Tecno, a.Tecnos, t.Tecno);

    private static bool Pertence(Atual a, int tecno) => RegrasIntervencao.PertenceAFicha((int)a.Tecno, a.Tecnos, tecno);

    private static void ParametroTecnos(SqlCommand cmd, int tecno) => Texto(cmd, "@padrao", 12, RegrasIntervencao.PadraoTecno(tecno));

    public Task<IReadOnlyList<IntervencaoDto>> ListarAsync(TecnicoAtual tecnico, CancellationToken ct) => Executar<IReadOnlyList<IntervencaoDto>>(async conn =>
    {
        var cfg = await LerConfiguracaoAsync(conn, ct);
        var hoje = relogio.GetLocalNow().Date;
        await using var cmd = new SqlCommand(SqlListar, conn);
        Numero(cmd, "@tecno", tecnico.Tecno, 5);
        ParametroTecnos(cmd, tecnico.Tecno);
        Quando(cmd, "@ate", hoje.AddDays(cfg.Antecedencia));
        Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<IntervencaoDto>();
        while (await r.ReadAsync(ct)) lista.Add(Ler(r));
        return lista;
    });

    public Task<IReadOnlyList<IntervencaoDto>> ListarRecentesAsync(TecnicoAtual tecnico, int dias, CancellationToken ct) => Executar<IReadOnlyList<IntervencaoDto>>(async conn =>
    {
        await using var cmd = new SqlCommand(SqlRecentes, conn);
        Numero(cmd, "@tecno", tecnico.Tecno, 5);
        ParametroTecnos(cmd, tecnico.Tecno);
        Quando(cmd, "@desde", relogio.GetLocalNow().Date.AddDays(-dias));
        Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<IntervencaoDto>();
        while (await r.ReadAsync(ct)) lista.Add(Ler(r));
        return lista;
    });

    public Task<IntervencaoDto?> ObterAsync(string id, TecnicoAtual tecnico, CancellationToken ct) => Executar(conn => LerUmaAsync(conn, id, ct, tecnico));

    // Os curingas do LIKE escapam-se para o texto do utilizador valer só como texto.
    private static string Padrao(string texto) =>
        texto.Length == 0 ? "" : "%" + texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";

    private static void Parametro(SqlCommand cmd, string nome, SqlDbType tipo, int tamanho, string valor) =>
        cmd.Parameters.Add(nome, tipo, tamanho).Value = valor;

    private static EquipamentoDto LerEquipamento(SqlDataReader r) => new(
        r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim(), r.GetString(3).Trim(), Convert.ToInt32(r.GetValue(4), CultureInfo.InvariantCulture),
        r.GetString(5).Trim(), r.GetString(6).Trim(), r.GetString(7).Trim(), r.GetString(8).Trim(),
        r.IsDBNull(11) || r.GetDateTime(11) <= SemData ? null : DateOnly.FromDateTime(r.GetDateTime(11)), r.GetInt32(12),
        string.Equals(r.GetString(9).Trim(), "Fora de Serviço", StringComparison.OrdinalIgnoreCase), r.GetBoolean(10));

    public Task<IReadOnlyList<EquipamentoDto>> PesquisarEquipamentosAsync(string texto, CancellationToken ct) => Executar<IReadOnlyList<EquipamentoDto>>(async conn =>
    {
        await using var cmd = new SqlCommand(SqlPesquisaAvaria, conn);
        Veiculos(cmd);
        Texto(cmd, "@texto", 130, Padrao(texto));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<EquipamentoDto>();
        while (await r.ReadAsync(ct)) lista.Add(new(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim()));
        return lista;
    });

    public Task<IReadOnlyList<EquipamentoDto>> ListarEquipamentosAsync(FiltroEquipamentos filtro, CancellationToken ct) => Executar<IReadOnlyList<EquipamentoDto>>(async conn =>
    {
        await using var cmd = new SqlCommand(SqlEquipamentos, conn);
        Veiculos(cmd);
        cmd.Parameters.Add("@obsoletos", SqlDbType.Bit).Value = filtro.Obsoletos;
        Parametro(cmd, "@tipo", SqlDbType.NVarChar, RegrasManutencao.TamanhoTipo, filtro.Tipo);
        Parametro(cmd, "@local", SqlDbType.NVarChar, RegrasManutencao.TamanhoLocal, filtro.Local);
        Texto(cmd, "@texto", 130, Padrao(filtro.Texto));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<EquipamentoDto>();
        while (await r.ReadAsync(ct)) lista.Add(LerEquipamento(r));
        return lista;
    });

    public Task<FichaEquipamentoDto?> ObterEquipamentoAsync(string id, int tecno, CancellationToken ct) => Executar<FichaEquipamentoDto?>(async conn =>
    {
        EquipamentoDto? equipamento;
        await using (var cmd = new SqlCommand(SqlEquipamento, conn))
        {
            Texto(cmd, "@id", 25, id);
            Veiculos(cmd);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            equipamento = await r.ReadAsync(ct) ? LerEquipamento(r) : null;
        }
        if (equipamento is null) return null;

        var tarefas = new List<TarefaPlaneadaDto>();
        await using (var cmd = new SqlCommand(SqlTarefasEquipamento, conn))
        {
            Texto(cmd, "@id", 25, id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var feita = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5);
                var cancelada = r.IsDBNull(6) ? (DateTime?)null : r.GetDateTime(6);
                var antiga = r.IsDBNull(7) ? (DateTime?)null : r.GetDateTime(7);
                var aberta = r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8);
                var ultima = feita ?? antiga;
                var proxima = RegrasManutencao.ProximaDaTarefa(feita, cancelada, antiga, RegrasManutencao.LerUnidade(r.GetString(3)), (int)r.GetDecimal(4), aberta);
                tarefas.Add(new TarefaPlaneadaDto(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim(),
                    ultima.HasValue && ultima.Value > SemData ? DateOnly.FromDateTime(ultima.Value) : null,
                    proxima.HasValue ? DateOnly.FromDateTime(proxima.Value) : null));
            }
        }

        var historico = new List<IntervencaoDto>();
        await using (var cmd = new SqlCommand(SqlHistoricoEquipamento, conn))
        {
            Texto(cmd, "@id", 25, id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) historico.Add(ComPermissao(Ler(r), tecno));
        }
        return new FichaEquipamentoDto(equipamento, tarefas, historico);
    });

    // Como no detalhe da ficha (SqlObter): só abre quem tem número de técnico e a ficha é sua (principal ou adicional) ou ainda não tem dono.
    private static IntervencaoDto ComPermissao(IntervencaoDto ficha, int tecno) =>
        ficha with { PodeAbrir = tecno > 0 && (ficha.Tecno == 0 || RegrasIntervencao.Envolve(ficha, tecno)) };

    public Task<PaginaFichasDto> ListarTodasAsync(FiltroFichas filtro, TecnicoAtual supervisor, CancellationToken ct) => Executar(async conn =>
    {
        var tamanho = RegrasManutencao.FichasPorPagina;
        var saltar = (filtro.Pagina - 1) * tamanho;
        await using var cmd = new SqlCommand(SqlTodasBase + OrdenarTodas(filtro.Ordem, filtro.Descendente), conn);
        cmd.Parameters.Add("@topo", SqlDbType.Int).Value = filtro.Pagina * tamanho + 1;
        Parametro(cmd, "@situacao", SqlDbType.VarChar, 10, filtro.Situacao.ToString());
        cmd.Parameters.Add("@soPorSupervisionar", SqlDbType.Bit).Value = filtro.SoPorSupervisionar;
        Parametro(cmd, "@tipo", SqlDbType.VarChar, 20, filtro.Tipo?.ToString() ?? "");
        Texto(cmd, "@exato", 20, filtro.Texto.Length <= 20 ? filtro.Texto : "");
        Veiculos(cmd);
        Parametro(cmd, "@estado", SqlDbType.VarChar, 20, filtro.Estado?.ToString() ?? "");
        Quando(cmd, "@desde", filtro.Desde?.ToDateTime(TimeOnly.MinValue) ?? SemData);
        Quando(cmd, "@ate", filtro.Ate?.AddDays(1).ToDateTime(TimeOnly.MinValue) ?? new DateTime(2999, 12, 31));
        Numero(cmd, "@tecno", filtro.Tecno, 5);
        ParametroTecnos(cmd, filtro.Tecno);
        Texto(cmd, "@texto", 130, Padrao(filtro.Texto));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var itens = new List<IntervencaoDto>();
        var lidas = 0;
        while (await r.ReadAsync(ct))
        {
            if (lidas++ < saltar) continue;
            itens.Add(ComPermissao(Ler(r), supervisor.Tecno) with
            {
                Numero = (int)Dec(r, 14), Total = Dec(r, 15), Fechada = Bool(r, 16), PorSupervisionar = !Bool(r, 17), ForaDeOrdem = Bool(r, 18), Antiga = Dec(r, 19) == 1
            });
        }
        // A ficha a mais lida (topo = página + 1) só serve para saber se há mais.
        var temMais = itens.Count > tamanho;
        return new PaginaFichasDto(temMais ? itens.Take(tamanho).ToList() : itens, temMais);
    });

    public Task<ConfigManutencao> ObterConfiguracaoAsync(CancellationToken ct) => Executar(conn => LerConfiguracaoAsync(conn, ct));

    public Task GuardarConfiguracaoAsync(ConfigManutencao configuracao, TecnicoAtual supervisor, CancellationToken ct) => Executar(async conn =>
    {
        var comMaterial = await CampoMaterialAsync(conn, ct);
        if (!comMaterial && configuracao.ExigeMaterial)
            log.LogWarning("Falta o campo matobrig (bit) em u_configman: 'exigir material ao concluir' não foi gravado. Criar o campo no PHC.");
        var sql = !comMaterial ? SqlGuardarConfiguracao : SqlGuardarConfiguracao
            .Replace("comprasfichaint = @compras,", "comprasfichaint = @compras, matobrig = @material,")
            .Replace("pinmaxtent, comprasfichaint,", "pinmaxtent, comprasfichaint, matobrig,")
            .Replace("5, @compras,", "5, @compras, @material,");
        await using var cmd = new SqlCommand(sql, conn);
        if (comMaterial) cmd.Parameters.Add("@material", SqlDbType.Bit).Value = configuracao.ExigeMaterial;
        Texto(cmd, "@stamp", 25, Guid.NewGuid().ToString("N")[..25].ToUpperInvariant());
        Numero(cmd, "@antecedencia", configuracao.Antecedencia, 5);
        cmd.Parameters.Add("@gerar", SqlDbType.Bit).Value = configuracao.GerarPlaneadas;
        cmd.Parameters.Add("@avanca", SqlDbType.Bit).Value = configuracao.AvancaOrdem;
        cmd.Parameters.Add("@propria", SqlDbType.Bit).Value = configuracao.ValidarPropria;
        cmd.Parameters.Add("@ext", SqlDbType.Bit).Value = configuracao.ExtExigeDoc;
        cmd.Parameters.Add("@compras", SqlDbType.Bit).Value = configuracao.ComprasFichaInt;
        Auditoria(cmd, supervisor.Codigo, await AgoraAsync(conn, ct));
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    });

    private static async Task<decimal> ProximoNumeroAsync(SqlConnection conn, int ano, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlNumerosDoAno, conn);
        Quando(cmd, "@ini", new DateTime(ano, 1, 1));
        Quando(cmd, "@fim", new DateTime(ano + 1, 1, 1));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var numeros = new List<decimal>();
        while (await r.ReadAsync(ct)) numeros.Add(r.GetDecimal(0));
        return RegrasManutencao.ProximoNumeroFicha(numeros);
    }

    public async Task<(ResultadoIntervencao, IntervencaoDto?)> RegistarAvariaAsync(string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct, string? opId = null)
    {
        if (opId is null) return await ComNumeracaoAsync(() => Executar(conn => GravarAvariaAsync(conn, equipamentoId, anomalia, tecnico, ct)), ct);
        await AvariaOp.WaitAsync(ct);
        try { return await ComNumeracaoAsync(() => Executar(conn => GravarAvariaAsync(conn, equipamentoId, anomalia, tecnico, ct, opId)), ct); }
        finally { AvariaOp.Release(); }
    }

    // A numeração lê o último `no` e grava a seguir: tem de ser atómica em relação a qualquer outra que numere.
    private static async Task<T> ComNumeracaoAsync<T>(Func<Task<T>> acao, CancellationToken ct)
    {
        await Numeracao.WaitAsync(ct);
        try { return await acao(); }
        finally { Numeracao.Release(); }
    }

    private static async Task<(ResultadoIntervencao, IntervencaoDto?)> GravarAvariaAsync(SqlConnection conn, string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct, string? opId = null)
    {
        if (opId is not null)
        {
            var marcador = RegrasIntervencao.Marcador(opId);
            await using var procura = new SqlCommand(SqlAvariaPorOp, conn);
            Numero(procura, "@tecno", tecnico.Tecno, 5);
            procura.Parameters.Add("@tam", SqlDbType.Int).Value = marcador.Length;
            Texto(procura, "@marcador", 40, marcador);
            if (await procura.ExecuteScalarAsync(ct) is string existente && await LerUmaAsync(conn, existente.Trim(), ct) is { } ficha)
                return (ResultadoIntervencao.Ok, ficha);
            anomalia += marcador;
        }
        var agora = await AgoraAsync(conn, ct);
        var no = await ProximoNumeroAsync(conn, agora.Agora.Year, ct);
        var stamp = Guid.NewGuid().ToString("N")[..25].ToUpperInvariant();
        await using var cmd = new SqlCommand(SqlAvaria, conn);
        Texto(cmd, "@stamp", 25, stamp);
        Numero(cmd, "@no", no, 10);
        Texto(cmd, "@anomalia", 200, anomalia);
        Texto(cmd, "@equipamento", 25, equipamentoId);
        Veiculos(cmd);
        Numero(cmd, "@tecno", tecnico.Tecno, 5);
        Texto(cmd, "@tecnome", 45, tecnico.Nome);
        Auditoria(cmd, tecnico.Codigo, agora);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) return (ResultadoIntervencao.NaoExiste, null);
        return (ResultadoIntervencao.Ok, await LerUmaAsync(conn, stamp, ct));
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> IniciarAsync(string id, TecnicoAtual tecnico, CancellationToken ct) => Executar<(ResultadoIntervencao, IntervencaoDto?)>(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a || !Minha(a, tecnico)) return (ResultadoIntervencao.NaoExiste, null);
        // Repetição do mesmo pedido (a resposta perdeu-se): já está em curso e é do mesmo técnico.
        if (a.Estado == "EmCurso" && Pertence(a, tecnico.Tecno)) return (ResultadoIntervencao.Ok, await LerUmaAsync(conn, id, ct));
        if (a.Estado != "Planeada") return (ResultadoIntervencao.Conflito, null);

        // A ficha deve ser a primeira planeada da fila da zona, salvo se a configuração deixar avançar fora de ordem.
        var cfg = await LerConfiguracaoAsync(conn, ct);
        await using var fila = new SqlCommand(SqlPrimeiraDaZona, conn);
        Texto(fila, "@local", 65, a.Local);
        var primeira = (await fila.ExecuteScalarAsync(ct)) as string;
        var foraOrdem = !string.IsNullOrEmpty(primeira) && primeira.Trim() != id.Trim();
        if (foraOrdem && !cfg.AvancaOrdem) return (ResultadoIntervencao.ForaDeOrdem, null);

        var agora = await AgoraAsync(conn, ct);
        await using var cmd = new SqlCommand(SqlIniciar, conn);
        Texto(cmd, "@id", 25, id);
        Numero(cmd, "@tecno", tecnico.Tecno, 5);
        ParametroTecnos(cmd, tecnico.Tecno);
        Texto(cmd, "@tecnome", 45, tecnico.Nome);
        cmd.Parameters.Add("@fora", SqlDbType.Bit).Value = foraOrdem;
        Auditoria(cmd, tecnico.Codigo, agora);
        return await Resultado(conn, cmd, id, ct);
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> ConcluirAsync(string id, TecnicoAtual tecnico, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct) => Executar<(ResultadoIntervencao, IntervencaoDto?)>(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a || !Minha(a, tecnico)) return (ResultadoIntervencao.NaoExiste, null);
        // Repetição do mesmo pedido: já concluída pelo mesmo técnico com o mesmo texto e as mesmas horas (ao segundo); não gera outra seguinte.
        if (a.Estado == "Concluida" && Pertence(a, tecnico.Tecno) && await LerUmaAsync(conn, id, ct) is { } feita
            && feita.Feito == feito && Segundos(feita.HoraInicio) == Segundos(inicio) && Segundos(feita.HoraFim) == Segundos(fim))
            return (ResultadoIntervencao.Ok, feita);
        if (a.Estado != "EmCurso") return (ResultadoIntervencao.Conflito, null);

        var cfg = await LerConfiguracaoAsync(conn, ct);
        if (cfg.ExtExigeDoc && RegrasManutencao.FaltaParaFechar(a.TipoManut, a.NoFornecedor, a.DocExterno) is not null) return (ResultadoIntervencao.ExigeDocumento, null);
        if (cfg.ExigeMaterial)
        {
            await using var contar = new SqlCommand(SqlMateriaisDaFicha, conn);
            Texto(contar, "@id", 25, id);
            if (Convert.ToInt32(await contar.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 0) return (ResultadoIntervencao.SemMaterial, null);
        }

        // As horas ficam no dia em que a ficha foi iniciada; a data da ficha passa a ser esse dia (base da próxima).
        var agora = await AgoraAsync(conn, ct);
        var dia = a.Hini > SemData ? a.Hini.Date : agora.Dia;
        await using var cmd = new SqlCommand(SqlConcluir, conn);
        Texto(cmd, "@id", 25, id);
        Numero(cmd, "@tecno", tecnico.Tecno, 5);
        ParametroTecnos(cmd, tecnico.Tecno);
        Texto(cmd, "@feito", 254, feito);
        Quando(cmd, "@inicio", dia + inicio.ToTimeSpan());
        Quando(cmd, "@fim", dia + fim.ToTimeSpan());
        Quando(cmd, "@dataficha", dia);
        Auditoria(cmd, tecnico.Codigo, agora);
        var resultado = await Resultado(conn, cmd, id, ct);
        if (resultado.Item1 == ResultadoIntervencao.Ok) await GerarSeguinteAsync(conn, a.Tarefa, tecnico.Codigo, ct);
        return resultado;
    });

    private async Task<(ResultadoIntervencao, IntervencaoDto?)> Resultado(SqlConnection conn, SqlCommand cmd, string id, CancellationToken ct)
    {
        if (await cmd.ExecuteNonQueryAsync(ct) > 0) return (ResultadoIntervencao.Ok, await LerUmaAsync(conn, id, ct));
        return (await LerAtualAsync(conn, id, ct) is null ? ResultadoIntervencao.NaoExiste : ResultadoIntervencao.Conflito, null);
    }

    public Task<IReadOnlyList<IntervencaoDto>> ListarPorValidarAsync(TecnicoAtual supervisor, CancellationToken ct) => Executar<IReadOnlyList<IntervencaoDto>>(async conn =>
    {
        var cfg = await LerConfiguracaoAsync(conn, ct);
        await using var cmd = new SqlCommand(SqlPorValidar, conn);
        Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<IntervencaoDto>();
        while (lista.Count < 300 && await r.ReadAsync(ct))
        {
            var ficha = Ler(r);
            lista.Add(ficha with { PodeValidar = cfg.ValidarPropria || supervisor.Tecno <= 0 || !RegrasIntervencao.Envolve(ficha, supervisor.Tecno) });
        }
        return lista;
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> ValidarAsync(string id, TecnicoAtual supervisor, CancellationToken ct) => Executar<(ResultadoIntervencao, IntervencaoDto?)>(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a) return (ResultadoIntervencao.NaoExiste, null);
        if (a.Estado != "Concluida") return (ResultadoIntervencao.Conflito, null);

        var cfg = await LerConfiguracaoAsync(conn, ct);
        if (!cfg.ValidarPropria && Pertence(a, supervisor.Tecno)) return (ResultadoIntervencao.Propria, null);
        if (cfg.ExtExigeDoc && RegrasManutencao.FaltaParaFechar(a.TipoManut, a.NoFornecedor, a.DocExterno) is not null) return (ResultadoIntervencao.ExigeDocumento, null);

        await using var cmd = new SqlCommand(SqlValidar, conn);
        Texto(cmd, "@id", 25, id);
        Auditoria(cmd, supervisor.Codigo, await AgoraAsync(conn, ct));
        return await Resultado(conn, cmd, id, ct);
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> DevolverAsync(string id, string motivo, TecnicoAtual supervisor, CancellationToken ct) => Executar<(ResultadoIntervencao, IntervencaoDto?)>(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a) return (ResultadoIntervencao.NaoExiste, null);
        if (a.Estado != "Concluida") return (ResultadoIntervencao.Conflito, null);

        var agora = await AgoraAsync(conn, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        if (a.Tarefa.Length > 0)
        {
            await using var apagar = new SqlCommand(SqlApagarSeguinte, conn, tx);
            Texto(apagar, "@tarefa", 25, a.Tarefa);
            Texto(apagar, "@id", 25, id);
            await apagar.ExecuteNonQueryAsync(ct);
        }
        await using var cmd = new SqlCommand(SqlDevolver, conn, tx);
        Texto(cmd, "@id", 25, id);
        Texto(cmd, "@motivo", 254, RegrasManutencao.Cortar(motivo, RegrasManutencao.TamanhoMotivo));
        Auditoria(cmd, supervisor.Codigo, agora);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return (ResultadoIntervencao.Conflito, null); }
        await tx.CommitAsync(ct);
        return (ResultadoIntervencao.Ok, await LerUmaAsync(conn, id, ct));
    });

    public Task<IReadOnlyList<IntervencaoDto>> ListarPlaneadasAsync(CancellationToken ct) => Executar<IReadOnlyList<IntervencaoDto>>(async conn =>
    {
        await using var cmd = new SqlCommand(SqlPlaneadas, conn);
        Veiculos(cmd);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<IntervencaoDto>();
        while (lista.Count < 500 && await r.ReadAsync(ct)) lista.Add(LerPlaneada(r));
        return lista;
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> AtribuirAsync(string id, int tecno, string nome, TecnicoAtual supervisor, CancellationToken ct) => Executar<(ResultadoIntervencao, IntervencaoDto?)>(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a) return (ResultadoIntervencao.NaoExiste, null);
        await using var cmd = new SqlCommand(SqlAtribuir, conn);
        Texto(cmd, "@id", 25, id);
        Numero(cmd, "@novo", tecno, 5);
        Texto(cmd, "@tecnome", 45, nome);
        cmd.Parameters.Add("@tecnos", SqlDbType.VarChar, -1).Value = tecno == 0 ? "" : RegrasIntervencao.EscreverTecnos(RegrasIntervencao.LerTecnos(a.Tecnos), tecno);
        cmd.Parameters.Add("@antes", SqlDbType.VarChar, -1).Value = a.Tecnos;
        Auditoria(cmd, supervisor.Codigo, await AgoraAsync(conn, ct));
        return await Resultado(conn, cmd, id, ct);
    });

    public Task<(ResultadoIntervencao, IntervencaoDto?)> DefinirAdicionaisAsync(string id, IReadOnlyList<int> tecnos, IReadOnlySet<int> ativos, TecnicoAtual supervisor, CancellationToken ct) => Executar<(ResultadoIntervencao, IntervencaoDto?)>(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a) return (ResultadoIntervencao.NaoExiste, null);
        var principal = (int)a.Tecno;
        if (principal == 0) return (ResultadoIntervencao.Conflito, null);
        var atuais = RegrasIntervencao.LerTecnos(a.Tecnos);
        if (tecnos.Any(t => t != principal && !atuais.Contains(t) && !ativos.Contains(t))) return (ResultadoIntervencao.TecnicoInvalido, null);
        await using var cmd = new SqlCommand(SqlDefinirAdicionais, conn);
        Texto(cmd, "@id", 25, id);
        Numero(cmd, "@principal", principal, 5);
        cmd.Parameters.Add("@tecnos", SqlDbType.VarChar, -1).Value = RegrasIntervencao.EscreverTecnos(tecnos, principal);
        cmd.Parameters.Add("@antes", SqlDbType.VarChar, -1).Value = a.Tecnos;
        Auditoria(cmd, supervisor.Codigo, await AgoraAsync(conn, ct));
        return await Resultado(conn, cmd, id, ct);
    });

    public Task<RelatorioGeracao> GerarPlaneadasAsync(TecnicoAtual supervisor, CancellationToken ct) => Executar(async conn =>
    {
        await Numeracao.WaitAsync(ct);
        try { return await GerarAsync(conn, supervisor.Codigo, "", ct); }
        finally { Numeracao.Release(); }
    });

    // A seguinte nunca falha a conclusão: se a geração der erro, fica registada e a ficha concluída mantém-se.
    private async Task GerarSeguinteAsync(SqlConnection conn, string tarefa, string codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tarefa)) return;
        await Numeracao.WaitAsync(ct);
        try
        {
            if ((await LerConfiguracaoAsync(conn, ct)).GerarPlaneadas) await GerarAsync(conn, codigo, tarefa, ct);
        }
        catch (Exception ex) { log.LogError(ex, "Falha ao gerar a planeada seguinte."); }
        finally { Numeracao.Release(); }
    }

    // Como a LandeiroMaui: a data da seguinte = última feita + periodicidade, nunca antes de hoje; uma só planeada aberta por tarefa.
    private static async Task<RelatorioGeracao> GerarAsync(SqlConnection conn, string codigo, string tarefa, CancellationToken ct)
    {
        int criadas = 0, jaTinham = 0, semPeriodicidade = 0, erros = 0;
        var candidatas = new List<Candidata>();
        await using (var cmd = new SqlCommand(SqlCandidatas, conn))
        {
            Texto(cmd, "@tarefa", 25, tarefa);
            Veiculos(cmd);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                candidatas.Add(new Candidata(
                    r.GetString(0).Trim(), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                    r.IsDBNull(7) ? "" : r.GetString(7), r.GetString(8), r.GetDecimal(9),
                    r.IsDBNull(10) ? null : r.GetDateTime(10), r.IsDBNull(11) ? null : r.GetDateTime(11), r.IsDBNull(12) ? null : r.GetDateTime(12),
                    r.GetInt32(13) == 1));
        }

        var agora = await AgoraAsync(conn, ct);
        var hoje = agora.Dia;
        var proximoNumero = new Dictionary<int, decimal>();

        foreach (var c in candidatas)
        {
            if (c.Aberta) { jaTinham++; continue; }
            var unidade = RegrasManutencao.LerUnidade(c.Unidade);
            if (unidade == UnidadePeriodo.Nenhum || c.Intervalo <= 0) { semPeriodicidade++; continue; }

            var feita = c.UltimaFeita ?? c.UltimaLegado;
            var intervalo = (int)c.Intervalo;
            DateTime data;
            if (c.UltimaCancelada.HasValue && (!feita.HasValue || c.UltimaCancelada.Value > feita.Value))
                data = RegrasManutencao.ProximaDepoisDaUltima(c.UltimaCancelada.Value, unidade, intervalo) ?? hoje;
            else
                data = feita.HasValue ? RegrasManutencao.ProximaDepoisDaUltima(feita.Value, unidade, intervalo) ?? hoje : hoje;
            if (data < hoje) data = hoje;

            try
            {
                if (!proximoNumero.TryGetValue(data.Year, out var no)) no = await ProximoNumeroAsync(conn, data.Year, ct);
                var tipomanut = c.TipoManut.Length > 0 ? c.TipoManut
                    : string.Equals(c.Responsabilidade, "Empresa Externa", StringComparison.OrdinalIgnoreCase) ? "Externa" : "Interna";

                await using var ins = new SqlCommand(SqlGerar, conn);
                Texto(ins, "@stamp", 25, Guid.NewGuid().ToString("N")[..25].ToUpperInvariant());
                Numero(ins, "@no", no, 10);
                Texto(ins, "@equip", 25, c.EquipStamp);
                Texto(ins, "@ref", 20, RegrasManutencao.Cortar(c.Ref, 20));
                Texto(ins, "@design", 45, RegrasManutencao.Cortar(c.Design, 45));
                Quando(ins, "@data", data);
                Texto(ins, "@tipomanut", 20, RegrasManutencao.Cortar(tipomanut, 20));
                Texto(ins, "@periodo", 20, RegrasManutencao.Cortar(c.Periodo, 20));
                Texto(ins, "@problema", 200, RegrasManutencao.Cortar(c.Descricao.Replace("\r", " ").Replace("\n", " "), 200));
                Texto(ins, "@tarefa", 25, c.Stamp);
                Auditoria(ins, codigo, agora);
                if (await ins.ExecuteNonQueryAsync(ct) > 0) { criadas++; proximoNumero[data.Year] = no + 1; }
                else jaTinham++;
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { jaTinham++; }
            catch (SqlException) { erros++; }
        }
        return new RelatorioGeracao(criadas, jaTinham, semPeriodicidade, erros);
    }

    public Task<IReadOnlyList<MaterialDto>?> ListarMateriaisAsync(string id, TecnicoAtual tecnico, CancellationToken ct) => Executar<IReadOnlyList<MaterialDto>?>(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a || !Minha(a, tecnico)) return null;
        await using var cmd = new SqlCommand(SqlMateriais, conn);
        Texto(cmd, "@id", 25, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<MaterialDto>();
        while (await r.ReadAsync(ct)) lista.Add(new MaterialDto(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4)));
        return lista;
    });

    // Acesso à ficha para mexer no material, na mão de obra e nos dados: NaoExiste se não a vê, Conflito se já não se pode alterar.
    private static ResultadoIntervencao? AcessoEdicao(Atual? a, TecnicoAtual quem, bool supervisor) =>
        a is null || (!supervisor && !Minha(a, quem)) ? ResultadoIntervencao.NaoExiste
        : !RegrasFicha.PodeEditar(a.Estado, supervisor, Pertence(a, quem.Tecno)) ? ResultadoIntervencao.Conflito
        : null;

    public Task<(ResultadoIntervencao, MaterialDto?)> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, TecnicoAtual tecnico, CancellationToken ct, bool supervisor = false) => Executar<(ResultadoIntervencao, MaterialDto?)>(async conn =>
    {
        var a = await LerAtualAsync(conn, id, ct);
        if (AcessoEdicao(a, tecnico, supervisor) is { } negado) return (negado, null);

        var material = new MaterialDto(Guid.NewGuid().ToString("N")[..25].ToUpperInvariant(), RegrasManutencao.Cortar(design, RegrasManutencao.TamanhoDesignMaterial),
            qtt, epv, RegrasManutencao.CustoMaterial(qtt, epv));
        await using var cmd = new SqlCommand(SqlNovoMaterial, conn);
        Texto(cmd, "@stamp", 25, material.Id);
        Texto(cmd, "@id", 25, id);
        Texto(cmd, "@design", 35, material.Design);
        Numero(cmd, "@qtt", material.Qtt, 16, 3);
        Numero(cmd, "@epv", material.Epv, 16, 3);
        Numero(cmd, "@custo", material.Custo, 16, 3);
        Auditoria(cmd, tecnico.Codigo, await AgoraAsync(conn, ct));
        var usaCompras = (await LerConfiguracaoAsync(conn, ct)).ComprasFichaInt;
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        cmd.Transaction = tx;
        await cmd.ExecuteNonQueryAsync(ct);
        await RecalcularTotaisAsync(conn, id, usaCompras, ct, tx);
        await tx.CommitAsync(ct);
        return (ResultadoIntervencao.Ok, material);
    });

    public Task<ResultadoIntervencao> ApagarMaterialAsync(string id, string materialId, TecnicoAtual tecnico, CancellationToken ct, bool supervisor = false) => Executar(async conn =>
    {
        if (AcessoEdicao(await LerAtualAsync(conn, id, ct), tecnico, supervisor) is { } negado) return negado;

        await using var cmd = new SqlCommand(SqlApagarMaterial, conn);
        Texto(cmd, "@id", 25, id);
        Texto(cmd, "@material", 25, materialId);
        var usaCompras = (await LerConfiguracaoAsync(conn, ct)).ComprasFichaInt;
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        cmd.Transaction = tx;
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return ResultadoIntervencao.NaoExiste; }
        await RecalcularTotaisAsync(conn, id, usaCompras, ct, tx);
        await tx.CommitAsync(ct);
        return ResultadoIntervencao.Ok;
    });

    private static void ParametrosAnexo(SqlCommand cmd, string id)
    {
        Texto(cmd, "@oritable", 25, OriTableFicha);
        Texto(cmd, "@id", 25, id);
        Texto(cmd, "@origem", 50, OrigemAnexos);
    }

    // O supervisor lê qualquer ficha; o técnico só as suas (ou sem dono).
    private static async Task<Atual?> FichaVisivelAsync(SqlConnection conn, string id, TecnicoAtual quem, bool supervisor, CancellationToken ct) =>
        await LerAtualAsync(conn, id, ct) is { } a && (supervisor || Minha(a, quem)) ? a : null;

    public Task<IReadOnlyList<FotoDto>?> ListarFotosAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct) => Executar<IReadOnlyList<FotoDto>?>(async conn =>
    {
        if (await FichaVisivelAsync(conn, id, quem, supervisor, ct) is null) return null;
        await using var cmd = new SqlCommand(SqlFotos, conn);
        ParametrosAnexo(cmd, id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<FotoDto>();
        while (await r.ReadAsync(ct)) lista.Add(new FotoDto(r.GetString(0).Trim(), r.GetString(1).Trim(), Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture)));
        return lista;
    });

    public Task<FotoConteudo?> ObterFotoAsync(string id, string fotoId, TecnicoAtual quem, bool supervisor, CancellationToken ct) => Executar<FotoConteudo?>(async conn =>
    {
        if (await FichaVisivelAsync(conn, id, quem, supervisor, ct) is null) return null;
        await using var cmd = new SqlCommand(SqlFoto, conn);
        ParametrosAnexo(cmd, id);
        Texto(cmd, "@foto", 25, fotoId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new FotoConteudo(r.GetString(0).Trim(), (byte[])r.GetValue(1)) : null;
    });

    public Task<(ResultadoIntervencao, FotoDto?)> AdicionarFotoAsync(string id, string nome, byte[] dados, TecnicoAtual tecnico, CancellationToken ct) => Executar<(ResultadoIntervencao, FotoDto?)>(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a || !Minha(a, tecnico)) return (ResultadoIntervencao.NaoExiste, null);
        if (a.Estado != "EmCurso" || !Pertence(a, tecnico.Tecno)) return (ResultadoIntervencao.Conflito, null);

        await using (var contar = new SqlCommand(SqlContarFotos, conn))
        {
            ParametrosAnexo(contar, id);
            if (Convert.ToInt32(await contar.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) >= RegrasIntervencao.MaxFotos) return (ResultadoIntervencao.LimiteFotos, null);
        }

        var foto = new FotoDto(Guid.NewGuid().ToString("N")[..25].ToUpperInvariant(), nome, dados.Length);
        await using var cmd = new SqlCommand(SqlNovaFoto, conn);
        ParametrosAnexo(cmd, id);
        Texto(cmd, "@stamp", 25, foto.Id);
        Texto(cmd, "@tabnm", 50, TabNmFicha);
        Texto(cmd, "@resumo", 80, RegrasManutencao.Cortar(nome.Replace(' ', '_'), 80));
        cmd.Parameters.Add("@descricao", SqlDbType.VarChar, -1).Value = "Foto da intervenção";
        cmd.Parameters.Add("@dados", SqlDbType.VarBinary, -1).Value = dados;
        Texto(cmd, "@fullname", 254, nome + ".jpg");
        Texto(cmd, "@fname", 150, nome);
        Numero(cmd, "@flen", dados.Length, 12);
        Auditoria(cmd, tecnico.Codigo, await AgoraAsync(conn, ct));
        await cmd.ExecuteNonQueryAsync(ct);
        return (ResultadoIntervencao.Ok, foto);
    });

    public Task<ResultadoIntervencao> ApagarFotoAsync(string id, string fotoId, TecnicoAtual tecnico, CancellationToken ct) => Executar(async conn =>
    {
        if (await LerAtualAsync(conn, id, ct) is not { } a || !Minha(a, tecnico)) return ResultadoIntervencao.NaoExiste;
        if (a.Estado != "EmCurso" || !Pertence(a, tecnico.Tecno)) return ResultadoIntervencao.Conflito;

        await using var cmd = new SqlCommand(SqlApagarFoto, conn);
        ParametrosAnexo(cmd, id);
        Texto(cmd, "@foto", 25, fotoId);
        return await cmd.ExecuteNonQueryAsync(ct) == 0 ? ResultadoIntervencao.NaoExiste : ResultadoIntervencao.Ok;
    });

    private static async Task RecalcularTotaisAsync(SqlConnection conn, string id, CancellationToken ct) =>
        await RecalcularTotaisAsync(conn, id, (await LerConfiguracaoAsync(conn, ct)).ComprasFichaInt, ct);

    // Com transação, a configuração lê-se antes (todos os comandos da ligação têm de levar a transação).
    private static async Task RecalcularTotaisAsync(SqlConnection conn, string id, bool usaCompras, CancellationToken ct, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand(SqlTotais, conn, tx);
        Texto(cmd, "@id", 25, id);
        cmd.Parameters.Add("@usaCompras", SqlDbType.Bit).Value = usaCompras;
        cmd.Parameters.Add("@mo", SqlDbType.NVarChar, 30).Value = "Mão de Obra";
        cmd.Parameters.Add("@pecas", SqlDbType.NVarChar, 30).Value = "Peças";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
