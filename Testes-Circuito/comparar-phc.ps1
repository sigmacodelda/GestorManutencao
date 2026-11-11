<#
  Compara o que a API devolveu (snapshot-api.json de popular-dados.mjs) com as linhas gravadas nas tabelas do PHC da base de testes.
  SÓ LÊ: só SELECT com (nolock), depois de confirmar SELECT DB_NAME() = testes_land2. Nunca imprime a ligação nem dados pessoais.

  Uso:  .\comparar-phc.ps1 -Pasta .\resultados-dados\2026-11-10_abc123 [-AppSettings <ficheiro>]
  Variável: ConnectionStrings__PHCConn (ou -AppSettings para a ler do ficheiro).
  Saída (na pasta): comparacao-phc.md (resumo e diferenças), comparacao-completa.csv (campo a campo), phc-linhas.json (todas as colunas das linhas lidas).
#>
param(
  [Parameter(Mandatory)][string]$Pasta,
  [string]$AppSettings,
  [string]$BaseEsperada = 'testes_land2'
)
$ErrorActionPreference = 'Stop'
$inv = [System.Globalization.CultureInfo]::InvariantCulture
if ($AppSettings) { $env:ConnectionStrings__PHCConn = (Get-Content $AppSettings -Raw -Encoding UTF8 | ConvertFrom-Json).ConnectionStrings.PHCConn }
if (-not $env:ConnectionStrings__PHCConn) { Write-Host 'ERRO: falta ConnectionStrings__PHCConn.' -ForegroundColor Red; exit 2 }
$cri = Get-Content (Join-Path $Pasta 'dados-criados.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$snap = Get-Content (Join-Path $Pasta 'snapshot-api.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$marca = $cri.marca
if ($marca -notmatch '^TESTE-DADOS-[a-z0-9]{6}$') { Write-Host 'ERRO: marcador inesperado.' -ForegroundColor Red; exit 2 }

$c = New-Object System.Data.SqlClient.SqlConnection $env:ConnectionStrings__PHCConn
$c.Open()
function Linhas([string]$sql, [hashtable]$p = @{}) {
  $cmd = $c.CreateCommand(); $cmd.CommandText = $sql
  foreach ($k in $p.Keys) { [void]$cmd.Parameters.AddWithValue($k, $p[$k]) }
  $r = $cmd.ExecuteReader()
  $cols = @(0..($r.FieldCount - 1) | ForEach-Object { $r.GetName($_) })
  $out = New-Object System.Collections.ArrayList
  while ($r.Read()) {
    $h = @{}
    for ($i = 0; $i -lt $r.FieldCount; $i++) {
      $v = $r.GetValue($i)
      if ($v -is [DBNull]) { $v = $null } elseif ($v -is [byte[]]) { $v = "<$($v.Length) bytes>" }
      $h[$cols[$i]] = $v
    }
    [void]$out.Add($h)
  }
  $r.Close()
  return [pscustomobject]@{ Cols = $cols; Rows = $out }
}
$base = (Linhas 'SELECT DB_NAME() as db').Rows[0]['db']
if ($base -ne $BaseEsperada) { Write-Host "ERRO: a ligação aponta a '$base', não a '$BaseEsperada'. Nada foi feito." -ForegroundColor Red; $c.Dispose(); exit 2 }
Write-Host "Base confirmada: $base" -ForegroundColor Green

# ---------------------------------------------------------------- normalização
function Norm($v) {
  if ($null -eq $v) { return '' }
  if ($v -is [bool]) { return $(if ($v) { 'true' } else { 'false' }) }
  if ($v -is [DateTime]) {
    if ($v.Year -le 1900) { return '' }
    if ($v.TimeOfDay.Ticks -eq 0) { return $v.ToString('yyyy-MM-dd', $inv) }
    return $v.ToString('yyyy-MM-ddTHH:mm:ss', $inv)
  }
  if ($v -is [decimal] -or $v -is [double] -or $v -is [int] -or $v -is [long] -or $v -is [single]) { return ([decimal]$v).ToString('0.######', $inv) }
  $s = ([string]$v).Trim()
  if ($s -match '^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d') { $s = $s.Substring(0, 19); if ($s.StartsWith('1900-01-01')) { return '' }; if ($s.EndsWith('T00:00:00')) { return $s.Substring(0, 10) }; return $s }
  if ($s -match '^1900-01-01') { return '' }
  $d = [decimal]0
  if ($s -match '^-?\d+([.,]\d+)?$' -and [decimal]::TryParse($s.Replace(',', '.'), [System.Globalization.NumberStyles]::Float, $inv, [ref]$d)) { return $d.ToString('0.######', $inv) }
  return $s
}
function Bruto($v) {
  if ($null -eq $v) { return 'NULL' }
  if ($v -is [DateTime]) { return $v.ToString('yyyy-MM-ddTHH:mm:ss.fff', $inv) }
  if ($v -is [string]) { return "'" + $v.Trim() + "'" }
  return [string]$v
}

$todas = New-Object System.Collections.ArrayList   # linhas campo a campo
function Cmp([string]$entidade, [string]$rotulo, $api, [string]$campoApi, $phc, [string]$colPhc, [string]$nota = '') {
  $na = Norm $api; $np = Norm $phc
  $res = if ($na -ceq $np) { 'OK' } else { 'DIF' }
  [void]$todas.Add([pscustomobject]@{ Entidade = $entidade; Ref = $rotulo; Campo = "$campoApi -> $colPhc"; API = $(if ($null -eq $api) { 'null' } else { Bruto $api }); PHC = (Bruto $phc); Resultado = $res; Nota = $nota })
}
function Info([string]$entidade, [string]$rotulo, [string]$campo, [string]$api, [string]$phc, [string]$nota) {
  [void]$todas.Add([pscustomobject]@{ Entidade = $entidade; Ref = $rotulo; Campo = $campo; API = $api; PHC = $phc; Resultado = 'INFO'; Nota = $nota })
}
function Col($row, [string]$padrao) { foreach ($k in $row.Keys) { if ($k -like $padrao) { return $row[$k] } }; return $null }
function TemCol($row, [string]$nome) { return $row.ContainsKey($nome) }

# ---------------------------------------------------------------- leitura das tabelas (só linhas de teste)
$like = "%$marca%"
function Em([string[]]$ids, [string]$prefixo) {
  $n = 0; $nomes = @(); $q = @{}
  foreach ($i in $ids) { $nomes += "@$prefixo$n"; $q["@$prefixo$n"] = $i; $n++ }
  if ($nomes.Count -eq 0) { return [pscustomobject]@{ Sql = "(select 'x' where 1=0)"; P = @{} } }
  return [pscustomobject]@{ Sql = '(' + ($nomes -join ',') + ')'; P = $q }
}
$eqIds = @($cri.equipamentos | ForEach-Object { $_.id })
$ei = Em $eqIds 'e'
$p = @{ '@like' = $like }; foreach ($k in $ei.P.Keys) { $p[$k] = $ei.P[$k] }
$tEquip = Linhas "select * from u_equip with (nolock) where u_equipstamp in $($ei.Sql)" $p
$tInst = Linhas "select * from u_itmanut with (nolock) where u_equipstamp in $($ei.Sql)" $p
$tTar = Linhas "select l.* from u_itmanutli l with (nolock) join u_itmanut i with (nolock) on i.u_itmanutstamp = l.u_itmanutstamp where i.u_equipstamp in $($ei.Sql)" $p
$tHist = Linhas "select * from u_histstats with (nolock) where u_equipstamp in $($ei.Sql) order by ousrdata, ousrhora" $p
$tFicha = Linhas "select * from u_fichaint with (nolock) where design like @like or problema like @like or u_equipstamp in $($ei.Sql) order by no" $p
$fstamps = @($tFicha.Rows | ForEach-Object { $_['u_fichaintstamp'] })
$fi = Em $fstamps 'f'
$pf = @{}; foreach ($k in $fi.P.Keys) { $pf[$k] = $fi.P[$k] }
$tMat = Linhas "select * from u_matinterv with (nolock) where u_fichaintstamp in $($fi.Sql)" $pf
$tTemp = Linhas "select * from u_tempinterv with (nolock) where u_fichaintstamp in $($fi.Sql)" $pf
# anexos sem o conteúdo binário (bdados): só os metadados
$colsAnexos = (Linhas "select top 0 * from anexos with (nolock)").Cols | Where-Object { $_ -ne 'bdados' }
$tAnex = Linhas ("select " + (($colsAnexos | ForEach-Object { "[$_]" }) -join ', ') + ", datalength(bdados) as bdados_bytes from anexos with (nolock) where recstamp in $($fi.Sql)") $pf
$perIds = @($cri.periodicidadesAlteradas | ForEach-Object { $_.id })
$pi = Em $perIds 'p'
$tPer = Linhas "select * from u_periocid with (nolock) where u_periocidstamp in $($pi.Sql)" $pi.P
$tCfg = Linhas 'select top 1 * from u_configman with (nolock) order by ousrdata, ousrhora'

$porStamp = @{}
function Indexar($tabela, [string]$chave) { $h = @{}; foreach ($r in $tabela.Rows) { $h[([string]$r[$chave]).Trim()] = $r }; return $h }
$ixEquip = Indexar $tEquip 'u_equipstamp'; $ixInst = Indexar $tInst 'u_itmanutstamp'; $ixTar = Indexar $tTar 'u_itmanutlistamp'; $ixFicha = Indexar $tFicha 'u_fichaintstamp'
$ixMat = Indexar $tMat 'u_matintervstamp'; $ixTemp = Indexar $tTemp 'u_tempintervstamp'

# ---------------------------------------------------------------- equipamentos (u_equip)
$mapaEq = @(
  @('noInterno', 'nointerno'), @('ref', 'ref'), @('design', 'design'), @('tipo', 'tipo'), @('marca', 'marca'), @('modelo', 'modelo'), @('serie', 'serie'),
  @('ano', 'anoaq'), @('valor', 'aquisi*'), @('noFornecedor', 'nofornec'), @('nomeFornecedor', 'nomeforn'), @('data', 'data'), @('local', 'local'),
  @('estado', 'status'), @('obsoleto', 'obsoleto'), @('manualPdf', 'manpdf'), @('email', 'email'))
foreach ($e in $cri.equipamentos) {
  $api = $snap.equipamentos.($e.id); $row = $ixEquip[$e.id]
  if (-not $row) { Info 'u_equip' $e.ref 'linha' 'existe' 'NAO ENCONTRADA' 'equipamento da API sem linha no PHC'; continue }
  foreach ($m in $mapaEq) { Cmp 'u_equip' $e.ref $api.($m[0]) $m[0] (Col $row $m[1]) $m[1] }
  Info 'u_equip' $e.ref 'auditoria' '' ("ousrinis={0} ousrdata={1} ousrhora={2} usrinis={3}" -f (Bruto $row['ousrinis']), (Norm $row['ousrdata']), (Bruto $row['ousrhora']), (Bruto $row['usrinis'])) 'criação pelo supervisor de teste'
}

# ---------------------------------------------------------------- histórico de estado (u_histstats)
foreach ($e in $cri.equipamentos) {
  $api = @($snap.historico.($e.id)); $db = @($tHist.Rows | Where-Object { ([string]$_['u_equipstamp']).Trim() -eq $e.id })
  Cmp 'u_histstats' $e.ref $api.Count 'nº de linhas do histórico' $db.Count 'linhas'
  $apiOrd = @($api | Sort-Object { $_.estado }); $dbOrd = @($db | Sort-Object { ([string]$_['status']).Trim() })
  for ($i = 0; $i -lt [Math]::Min($apiOrd.Count, $dbOrd.Count); $i++) { Cmp 'u_histstats' $e.ref $apiOrd[$i].estado "estado[$i]" $dbOrd[$i]['status'] 'status' }
}

# ---------------------------------------------------------------- instruções (u_itmanut) e tarefas (u_itmanutli)
foreach ($i in $snap.instrucoes.PSObject.Properties) {
  $api = $i.Value; $row = $ixInst[$i.Name]; $rot = "instrucao $($api.versao)"
  if (-not $row) { Info 'u_itmanut' $rot 'linha' 'existe' 'NAO ENCONTRADA' ''; continue }
  foreach ($m in @(@('versao', 'versao'), @('data', 'data'), @('obsoleta', 'obsoleto'), @('obs', 'obs'), @('equipamentoId', 'u_equipstamp'))) { Cmp 'u_itmanut' $rot $api.($m[0]) $m[0] $row[$m[1]] $m[1] }
  Cmp 'u_itmanut' $rot $api.numTarefas 'numTarefas' (@($tTar.Rows | Where-Object { ([string]$_['u_itmanutstamp']).Trim() -eq $i.Name }).Count) 'count(u_itmanutli)'
  Info 'u_itmanut' $rot 'ref/design (copiados do equipamento)' '' ("ref={0} design={1}" -f (Bruto $row['ref']), (Bruto $row['design'])) ''
}
foreach ($t in $snap.tarefas.PSObject.Properties) {
  $api = $t.Value; $row = $ixTar[$t.Name]; $rot = "tarefa $($api.periodo)"
  if (-not $row) { Info 'u_itmanutli' $rot 'linha' 'existe' 'NAO ENCONTRADA' ''; continue }
  foreach ($m in @(@('periodo', 'periodo'), @('responsabilidade', 'responsabilidade'), @('responsavel', 'responsavel'), @('descricao', 'descricao'), @('tipoManut', 'tipomanut'), @('instrucaoId', 'u_itmanutstamp'))) { Cmp 'u_itmanutli' $rot $api.($m[0]) $m[0] $row[$m[1]] $m[1] }
  Info 'u_itmanutli' $rot 'ref/design/no' '' ("ref={0} design={1} no={2}" -f (Bruto $row['ref']), (Bruto $row['design']), (Bruto $row['no'])) 'copiados da instrução; no = 0 fixo'
}

# ---------------------------------------------------------------- fichas (u_fichaint)
$mapaFicha = @(
  @('numero', 'no'), @('equipamentoId', 'u_equipstamp'), @('ref', 'ref'), @('equipamento', 'design'), @('tipo', 'tipointerv'), @('tipoManut', 'tipomanut'), @('periodicidade', 'periodo'),
  @('data', 'data'), @('estado', 'estado'), @('fechada', 'fechada'), @('supervisao', 'supervisao'), @('tecno', 'tecno'), @('tecnico', 'tecnome'),
  @('horaInicio', 'hini'), @('horaFim', 'hfim'), @('horasFrio', 'horasfrio'), @('horasConf', 'horasconf'), @('problema', 'problema'), @('resolucao', 'resolucao'), @('obs', 'obs'), @('limRem', 'limrem'),
  @('noFornecedor', 'nofornec'), @('nomeFornecedor', 'nomefornec'), @('docExterno', 'docexterno'),
  @('moInterna', 'mointerna'), @('moExterna', 'moexterna'), @('matInterno', 'matinterno'), @('matExterno', 'matexterno'), @('totMo', 'totmo'), @('totRef', 'totref'), @('motivo', 'valobs'))
$rotulos = @{}; foreach ($f in $cri.fichas) { $rotulos[$f.id] = $f.rotulo }
foreach ($row in $tFicha.Rows) {
  $id = ([string]$row['u_fichaintstamp']).Trim()
  $rot = if ($rotulos.ContainsKey($id)) { $rotulos[$id] } else { "extra-$id" }
  $rot = "$rot #$($row['no'])"
  $det = $snap.fichas.$id
  if ($det) {
    foreach ($m in $mapaFicha) {
      if (-not (TemCol $row $m[1])) { Info 'u_fichaint' $rot $m[1] '' '' 'coluna inexistente na tabela'; continue }
      $nota = ''
      if ($m[0] -eq 'motivo') { $nota = 'API só mostra o motivo em fichas EmCurso/Cancelada; no PHC fica em valobs' }
      Cmp 'u_fichaint' $rot $det.($m[0]) $m[0] $row[$m[1]] $m[1] $nota
    }
    # decomposição das horas: a API entrega DateTime, o PHC guarda datetime
    Info 'u_fichaint' $rot 'hini/hfim em bruto' ("{0} | {1}" -f $det.horaInicio, $det.horaFim) ("{0} | {1}" -f (Bruto $row['hini']), (Bruto $row['hfim'])) 'formato gravado'
  } else {
    # fichas geradas ao concluir (sem detalhe): compara só com a lista /intervencoes/todas
    $li = $snap.listaTodas.$id
    if ($li) {
      foreach ($m in @(@('numero', 'no'), @('tipo', 'tipointerv'), @('dataPlaneada', 'data'), @('estado', 'estado'), @('anomalia', 'problema'), @('tecno', 'tecno'), @('fechada', 'fechada'))) { Cmp 'u_fichaint (lista)' $rot $li.($m[0]) $m[0] $row[$m[1]] $m[1] }
    } else { Info 'u_fichaint' $rot 'linha' '(fora do snapshot)' 'existe' 'ficha com o marcador que a API não listou' }
  }
  # lista /intervencoes/todas
  $li = $snap.listaTodas.$id
  if ($li -and $det) {
    foreach ($m in @(@('numero', 'no'), @('tipo', 'tipointerv'), @('dataPlaneada', 'data'), @('estado', 'estado'), @('anomalia', 'problema'), @('feito', 'resolucao'), @('tecno', 'tecno'), @('fechada', 'fechada'))) { Cmp 'lista /intervencoes/todas' $rot $li.($m[0]) $m[0] $row[$m[1]] $m[1] }
    $horaIni = if ($row['hini'] -is [DateTime] -and $row['hini'].Year -gt 1900) { $row['hini'].ToString('HH:mm:ss', $inv) } else { '' }
    Cmp 'lista /intervencoes/todas' $rot $(if ($li.horaInicio) { ([string]$li.horaInicio).Substring(0, [Math]::Min(8, ([string]$li.horaInicio).Length)) } else { $li.horaInicio }) 'horaInicio (hora)' $horaIni 'hini (só hora)'
    $horaFim = if ($row['hfim'] -is [DateTime] -and $row['hfim'].Year -gt 1900) { $row['hfim'].ToString('HH:mm:ss', $inv) } else { '' }
    Cmp 'lista /intervencoes/todas' $rot $li.horaFim 'horaFim (hora)' $horaFim 'hfim (só hora)'
    Cmp 'lista /intervencoes/todas' $rot $li.total 'total' ([decimal]$row['totmo'] + [decimal]$row['totref']) 'totmo+totref'
    $sup = [bool]$row['supervisao']
    Cmp 'lista /intervencoes/todas' $rot $li.porSupervisionar 'porSupervisionar' (-not $sup) 'not supervisao' 'API marca "por supervisionar" também nas planeadas por começar'
  }
  # vista do técnico
  $vt = $snap.vistaTecnico.$id
  if ($vt -and $vt.estado -eq 200) {
    foreach ($m in @(@('estado', 'estado'), @('tecno', 'tecno'), @('anomalia', 'problema'))) { Cmp 'vista do técnico /intervencoes/{id}' $rot $vt.dto.($m[0]) $m[0] $row[$m[1]] $m[1] }
    if ($vt.dto.horaInicio) { Cmp 'vista do técnico /intervencoes/{id}' $rot ([string]$vt.dto.horaInicio).Substring(0, [Math]::Min(8, ([string]$vt.dto.horaInicio).Length)) 'horaInicio' $(if ($row['hini'] -is [DateTime] -and $row['hini'].Year -gt 1900) { $row['hini'].ToString('HH:mm:ss', $inv) } else { '' }) 'hini (só hora)' }
  } elseif ($vt) { Info 'vista do técnico' $rot 'acesso' "HTTP $($vt.estado)" '' 'o técnico não vê esta ficha (não é dele)' }
}

# ---------------------------------------------------------------- material (u_matinterv) e mão de obra (u_tempinterv)
foreach ($f in $cri.fichas) {
  $det = $snap.fichas.($f.id); if (-not $det) { continue }
  foreach ($m in @($det.materiais)) {
    $row = $ixMat[$m.id]; $rot = "$($f.rotulo) / $($m.design)"
    if (-not $row) { Info 'u_matinterv' $rot 'linha' 'existe' 'NAO ENCONTRADA' ''; continue }
    foreach ($mm in @(@('design', 'design'), @('qtt', 'qtt'), @('epv', 'epv'), @('custo', 'custo'), @('', 'u_fichaintstamp'))) {
      if ($mm[0] -eq '') { Cmp 'u_matinterv' $rot $f.id 'ficha' $row['u_fichaintstamp'] 'u_fichaintstamp'; continue }
      Cmp 'u_matinterv' $rot $m.($mm[0]) $mm[0] $row[$mm[1]] $mm[1]
    }
    Info 'u_matinterv' $rot 'ref (vazio) / auditoria' '' ("ref={0} ousrinis={1} ousrdata={2}" -f (Bruto $row['ref']), (Bruto $row['ousrinis']), (Norm $row['ousrdata'])) 'ref fica vazia: sem artigo (st) ligado'
  }
  foreach ($t in @($det.maoDeObra)) {
    $row = $ixTemp[$t.id]; $rot = "$($f.rotulo) / MO $($t.responsavel)"
    if (-not $row) { Info 'u_tempinterv' $rot 'linha' 'existe' 'NAO ENCONTRADA' ''; continue }
    foreach ($mm in @(@('tecno', 'no'), @('responsavel', 'responsavel'), @('horas', 'tempo'), @('custo', 'custo'))) { Cmp 'u_tempinterv' $rot $t.($mm[0]) $mm[0] $row[$mm[1]] $mm[1] }
    Info 'u_tempinterv' $rot 'custo/hora' ("$($t.custoHora)") 'sem coluna (custo / tempo)' 'o custo/hora não é gravado: deduz-se de custo / tempo'
  }
  # material e mão de obra que existem no PHC e a API não devolve
  $ids = @(@($det.materiais) | ForEach-Object { $_.id })
  foreach ($r in @($tMat.Rows | Where-Object { ([string]$_['u_fichaintstamp']).Trim() -eq $f.id -and $ids -notcontains ([string]$_['u_matintervstamp']).Trim() })) { Info 'u_matinterv' $f.rotulo 'linha só no PHC' '' (Bruto $r['design']) 'a API não a devolve' }
}

# ---------------------------------------------------------------- anexos (fotos)
foreach ($pr in $snap.fotos.PSObject.Properties) {
  foreach ($foto in @($pr.Value)) {
    $row = $tAnex.Rows | Where-Object { ([string]$_['anexosstamp']).Trim() -eq $foto.id } | Select-Object -First 1
    $rot = "foto de $($rotulos[$pr.Name])"
    if (-not $row) { Info 'anexos' $rot 'linha' 'existe' 'NAO ENCONTRADA' ''; continue }
    Cmp 'anexos' $rot $foto.bytes 'bytes' $row['bdados_bytes'] 'datalength(bdados)'
    Cmp 'anexos' $rot ($foto.nome + '.jpg') 'nome (+.jpg)' $row['fname'] 'fname' 'a API devolve o nome sem extensão'
    Info 'anexos' $rot 'metadados' '' ("oritable={0} tabnm={1} fext={2} flen={3} tipo={4} origem={5} resumo={6}" -f (Bruto $row['oritable']), (Bruto $row['tabnm']), (Bruto $row['fext']), (Bruto $row['flen']), (Bruto $row['tipo']), (Bruto $row['origem']), (Bruto $row['resumo'])) ''
  }
}

# ---------------------------------------------------------------- periodicidades repostas e configuração
foreach ($pe in @($cri.periodicidadesAlteradas)) {
  if (-not $pe) { continue }
  $row = $tPer.Rows | Where-Object { ([string]$_['u_periocidstamp']).Trim() -eq $pe.id } | Select-Object -First 1
  Cmp 'u_periocid' $pe.periodo $pe.antes.unidade 'unidade original' $row['unidade'] 'unidade' 'reposta no fim do popular-dados'
  Cmp 'u_periocid' $pe.periodo $pe.antes.intervalo 'intervalo original' $row['intervalo'] 'intervalo' 'reposto no fim do popular-dados'
}
if ($tCfg.Rows.Count -gt 0) {
  $row = $tCfg.Rows[0]
  foreach ($m in @(@('antecedencia', 'antecedencia'), @('gerarPlaneadas', 'gerarplaneadas'), @('avancaOrdem', 'avancaordem'), @('validarPropria', 'validarpropria'), @('extExigeDoc', 'extexigedoc'), @('comprasFichaInt', 'comprasfichaint'))) { Cmp 'u_configman' 'configuracao' $snap.configuracao.($m[0]) $m[0] $row[$m[1]] $m[1] }
}

# ---------------------------------------------------------------- consistência entre tabelas (derivadas e chaves)
$cons = New-Object System.Collections.ArrayList
function Cons([string]$regra, [bool]$ok, [string]$detalhe) { [void]$cons.Add([pscustomobject]@{ Regra = $regra; Resultado = $(if ($ok) { 'OK' } else { 'FALHA' }); Detalhe = $detalhe }) }
$m1 = @{}; foreach ($r in $tMat.Rows) { $k = ([string]$r['u_fichaintstamp']).Trim(); $m1[$k] = [decimal]$m1[$k] + [decimal]$r['custo'] }
$t1 = @{}; foreach ($r in $tTemp.Rows) { $k = ([string]$r['u_fichaintstamp']).Trim(); $t1[$k] = [decimal]$t1[$k] + [decimal]$r['custo'] }
$mal = @(); $malTot = @(); $malFech = @(); $semEquip = @(); $semTarefa = @(); $estSujo = @(); $d1900 = @()
foreach ($row in $tFicha.Rows) {
  $id = ([string]$row['u_fichaintstamp']).Trim(); $n = "#$($row['no'])"
  if ([decimal]$row['matinterno'] -ne [decimal]$m1[$id]) { $mal += "$n matinterno=$($row['matinterno']) soma=$([decimal]$m1[$id])" }
  if ([decimal]$row['mointerna'] -ne [decimal]$t1[$id]) { $mal += "$n mointerna=$($row['mointerna']) soma=$([decimal]$t1[$id])" }
  if ([decimal]$row['totref'] -ne [decimal]$row['matinterno'] + [decimal]$row['matexterno']) { $malTot += "$n totref" }
  if ([decimal]$row['totmo'] -ne [decimal]$row['mointerna'] + [decimal]$row['moexterna']) { $malTot += "$n totmo" }
  $est = ([string]$row['estado']).Trim()
  $fechada = [bool]$row['fechada']
  if (($est -eq 'Validada') -ne $fechada) { $malFech += "$n estado=$est fechada=$fechada" }
  if ($est -eq 'Validada' -and -not [bool]$row['supervisao']) { $malFech += "$n Validada sem supervisao" }
  if (-not $ixEquip.ContainsKey(([string]$row['u_equipstamp']).Trim())) { $semEquip += $n }
  if (([string]$row['uitmanutlistamp']).Trim() -ne '' -and -not $ixTar.ContainsKey(([string]$row['uitmanutlistamp']).Trim())) { $semTarefa += $n }
  if (([string]$row['estado']).Length -ne ([string]$row['estado']).TrimEnd().Length) { $estSujo += "$n '$($row['estado'])'" }
  foreach ($col in 'data', 'hini', 'hfim', 'valdata') { if ($row.ContainsKey($col) -and $row[$col] -is [DateTime] -and $row[$col].Year -le 1900) { $d1900 += "$n.$col" } }
}
Cons 'u_fichaint.matinterno / mointerna = soma das linhas (u_matinterv / u_tempinterv)' ($mal.Count -eq 0) ($mal -join '; ')
Cons 'u_fichaint.totref = matinterno + matexterno e totmo = mointerna + moexterna' ($malTot.Count -eq 0) ($malTot -join '; ')
Cons 'Validada <=> fechada = 1 e supervisao = 1' ($malFech.Count -eq 0) ($malFech -join '; ')
Cons 'u_fichaint.u_equipstamp existe em u_equip' ($semEquip.Count -eq 0) ($semEquip -join '; ')
Cons 'u_fichaint.uitmanutlistamp (quando preenchido) existe em u_itmanutli' ($semTarefa.Count -eq 0) ($semTarefa -join '; ')
Cons 'estado sem espaços à direita no PHC' ($estSujo.Count -eq 0) ($estSujo -join '; ')
Cons 'u_itmanutli liga a u_itmanut e este a u_equip' (@($tTar.Rows | Where-Object { -not $ixInst.ContainsKey(([string]$_['u_itmanutstamp']).Trim()) }).Count -eq 0 -and @($tInst.Rows | Where-Object { -not $ixEquip.ContainsKey(([string]$_['u_equipstamp']).Trim()) }).Count -eq 0) ''
$nums = $tFicha.Rows | ForEach-Object { [int]$_['no'] }
Cons 'números de ficha das fichas de teste são únicos e seguidos' ((@($nums | Select-Object -Unique).Count -eq @($nums).Count)) ("números: " + (($nums | Sort-Object) -join ','))
$ano = (Get-Date).Year
$dup = Linhas "select no, count(*) as n from u_fichaint with (nolock) where data >= @a and data < @b and no > 0 and no < 1000000 group by no having count(*) > 1" @{ '@a' = (Get-Date -Year $ano -Month 1 -Day 1).Date; '@b' = (Get-Date -Year ($ano + 1) -Month 1 -Day 1).Date }
Cons "números de ficha repetidos em todo o ano $ano (toda a base, só contagem)" ($dup.Rows.Count -eq 0) ("repetidos: " + (($dup.Rows | ForEach-Object { "$($_['no'])x$($_['n'])" }) -join ','))
$dupEq = @($tEquip.Rows | ForEach-Object { [string]$_['nointerno'] } | Group-Object | Where-Object Count -gt 1)
Cons 'u_equip.nointerno único nos equipamentos de teste' ($dupEq.Count -eq 0) ''
Cons 'datas 1900-01-01 nas fichas (convenção PHC de "vazio")' $true ("campos a 1900-01-01: " + $(if ($d1900.Count) { $d1900.Count } else { 0 }) + " (data/hini/hfim/valdata)")

# ---------------------------------------------------------------- anomalias por tabela: colunas sempre NULL / sempre 1900 / sempre vazias
function Anomalias([string]$nome, $tabela) {
  if ($tabela.Rows.Count -eq 0) { return "- $nome : sem linhas" }
  $nulas = @(); $s1900 = @(); $vazias = @()
  foreach ($col in $tabela.Cols) {
    $vals = @($tabela.Rows | ForEach-Object { $_[$col] })
    if (@($vals | Where-Object { $null -ne $_ }).Count -eq 0) { $nulas += $col; continue }
    if (@($vals | Where-Object { $_ -is [DateTime] -and $_.Year -le 1900 }).Count -eq $vals.Count) { $s1900 += $col; continue }
    if (@($vals | Where-Object { $_ -is [string] -and $_.Trim() -eq '' }).Count -eq $vals.Count) { $vazias += $col }
  }
  return "- **$nome** ($($tabela.Rows.Count) linhas, $($tabela.Cols.Count) colunas)`n  - sempre NULL: $(if ($nulas) { $nulas -join ', ' } else { '-' })`n  - sempre 1900-01-01: $(if ($s1900) { $s1900 -join ', ' } else { '-' })`n  - sempre vazias: $(if ($vazias) { $vazias -join ', ' } else { '-' })"
}
$anom = @(
  (Anomalias 'u_equip' $tEquip), (Anomalias 'u_itmanut' $tInst), (Anomalias 'u_itmanutli' $tTar), (Anomalias 'u_histstats' $tHist),
  (Anomalias 'u_fichaint' $tFicha), (Anomalias 'u_matinterv' $tMat), (Anomalias 'u_tempinterv' $tTemp), (Anomalias 'anexos' $tAnex))

# auditoria: quem gravou
$aud = New-Object System.Collections.ArrayList
foreach ($par in @(@('u_equip', $tEquip), @('u_itmanut', $tInst), @('u_itmanutli', $tTar), @('u_histstats', $tHist), @('u_fichaint', $tFicha), @('u_matinterv', $tMat), @('u_tempinterv', $tTemp), @('anexos', $tAnex))) {
  $t = $par[1]; if ($t.Rows.Count -eq 0) { continue }
  $o = @($t.Rows | ForEach-Object { ([string]$_['ousrinis']).Trim() } | Sort-Object -Unique) -join ','
  $u = @($t.Rows | ForEach-Object { ([string]$_['usrinis']).Trim() } | Sort-Object -Unique) -join ','
  $h = @($t.Rows | Where-Object { ([string]$_['ousrhora']) -notmatch '^\d\d:\d\d:\d\d$' }).Count
  [void]$aud.Add([pscustomobject]@{ Tabela = $par[0]; Linhas = $t.Rows.Count; ousrinis = $o; usrinis = $u; 'ousrhora fora de HH:MM:SS' = $h })
}

# ---------------------------------------------------------------- saídas
$todas | Export-Csv (Join-Path $Pasta 'comparacao-completa.csv') -NoTypeInformation -Encoding UTF8
$dump = [ordered]@{}
foreach ($par in @(@('u_equip', $tEquip), @('u_itmanut', $tInst), @('u_itmanutli', $tTar), @('u_histstats', $tHist), @('u_fichaint', $tFicha), @('u_matinterv', $tMat), @('u_tempinterv', $tTemp), @('anexos', $tAnex), @('u_periocid', $tPer), @('u_configman', $tCfg))) {
  $dump[$par[0]] = @($par[1].Rows | ForEach-Object { $h = [ordered]@{}; foreach ($col in $par[1].Cols) { $h[$col] = Bruto $_[$col] }; $h })
}
$dump | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Pasta 'phc-linhas.json') -Encoding UTF8

$linhasMd = New-Object System.Collections.ArrayList
[void]$linhasMd.Add("# Comparacao API vs PHC ($marca)")
[void]$linhasMd.Add('')
[void]$linhasMd.Add("Base: $base. Linhas lidas: u_equip $($tEquip.Rows.Count), u_itmanut $($tInst.Rows.Count), u_itmanutli $($tTar.Rows.Count), u_histstats $($tHist.Rows.Count), u_fichaint $($tFicha.Rows.Count), u_matinterv $($tMat.Rows.Count), u_tempinterv $($tTemp.Rows.Count), anexos $($tAnex.Rows.Count).")
[void]$linhasMd.Add('')
[void]$linhasMd.Add('## Resumo por entidade (campos comparados)')
[void]$linhasMd.Add('')
[void]$linhasMd.Add('| Entidade | Campos | Iguais | Diferentes |')
[void]$linhasMd.Add('|---|---|---|---|')
foreach ($g in ($todas | Where-Object { $_.Resultado -ne 'INFO' } | Group-Object Entidade)) {
  [void]$linhasMd.Add("| $($g.Name) | $($g.Count) | $(@($g.Group | Where-Object Resultado -eq 'OK').Count) | $(@($g.Group | Where-Object Resultado -eq 'DIF').Count) |")
}
[void]$linhasMd.Add('')
[void]$linhasMd.Add('## Diferencas campo a campo (API vs PHC)')
[void]$linhasMd.Add('')
$difs = @($todas | Where-Object Resultado -eq 'DIF')
if ($difs.Count -eq 0) { [void]$linhasMd.Add('Nenhuma.') } else {
  [void]$linhasMd.Add('| Entidade | Ref | Campo API -> coluna | API | PHC | Nota |'); [void]$linhasMd.Add('|---|---|---|---|---|---|')
  foreach ($d in $difs) { [void]$linhasMd.Add("| $($d.Entidade) | $($d.Ref) | $($d.Campo) | $(([string]$d.API) -replace '\|','/') | $(([string]$d.PHC) -replace '\|','/') | $($d.Nota) |") }
}
[void]$linhasMd.Add('')
[void]$linhasMd.Add('## Observacoes (INFO)')
[void]$linhasMd.Add('')
[void]$linhasMd.Add('| Entidade | Ref | Campo | API | PHC | Nota |'); [void]$linhasMd.Add('|---|---|---|---|---|---|')
foreach ($d in @($todas | Where-Object Resultado -eq 'INFO' | Select-Object -First 400)) { [void]$linhasMd.Add("| $($d.Entidade) | $($d.Ref) | $($d.Campo) | $(([string]$d.API) -replace '\|','/') | $(([string]$d.PHC) -replace '\|','/') | $($d.Nota) |") }
[void]$linhasMd.Add('')
[void]$linhasMd.Add('## Consistencia entre tabelas')
[void]$linhasMd.Add('')
[void]$linhasMd.Add('| Regra | Resultado | Detalhe |'); [void]$linhasMd.Add('|---|---|---|')
foreach ($x in $cons) { [void]$linhasMd.Add("| $($x.Regra) | $($x.Resultado) | $($x.Detalhe) |") }
[void]$linhasMd.Add('')
[void]$linhasMd.Add('## Colunas sempre vazias nas linhas de teste')
[void]$linhasMd.Add('')
foreach ($a in $anom) { [void]$linhasMd.Add($a) }
[void]$linhasMd.Add('')
[void]$linhasMd.Add('## Auditoria (quem gravou)')
[void]$linhasMd.Add('')
[void]$linhasMd.Add('| Tabela | Linhas | ousrinis | usrinis | ousrhora fora de HH:MM:SS |'); [void]$linhasMd.Add('|---|---|---|---|---|')
foreach ($a in $aud) { [void]$linhasMd.Add("| $($a.Tabela) | $($a.Linhas) | $($a.ousrinis) | $($a.usrinis) | $($a.'ousrhora fora de HH:MM:SS') |") }
$linhasMd -join "`r`n" | Set-Content (Join-Path $Pasta 'comparacao-phc.md') -Encoding UTF8
$c.Dispose()
Write-Host ("Comparado: {0} campos, {1} iguais, {2} diferentes. Ficheiros em {3}" -f @($todas | Where-Object Resultado -ne 'INFO').Count, @($todas | Where-Object Resultado -eq 'OK').Count, $difs.Count, $Pasta)
