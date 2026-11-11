<#
  Apaga os dados criados por popular-dados.mjs na base de testes (só linhas com o marcador TESTE-DADOS-<run> ou ligadas aos ids de dados-criados.json).
  Só corre se SELECT DB_NAME() devolver testes_land2. Por omissão só MOSTRA o que apagaria; para apagar mesmo use -Apagar.

  Uso
    .\limpar-dados-teste.ps1 -Pasta .\resultados-dados\2026-11-10_abc123            (só mostra)
    .\limpar-dados-teste.ps1 -Pasta .\resultados-dados\2026-11-10_abc123 -Apagar    (apaga)

  Variáveis: ConnectionStrings__PHCConn (ou -AppSettings <ficheiro> para a ler de lá). Nunca se imprime a ligação.
  Também repõe as periodicidades alteradas pelo popular-dados.mjs (unidade/intervalo originais), caso o script tenha sido interrompido.
#>
param(
  [Parameter(Mandatory)][string]$Pasta,
  [switch]$Apagar,
  [string]$AppSettings,
  [string]$BaseEsperada = 'testes_land2'
)
$ErrorActionPreference = 'Stop'
if ($AppSettings) { $env:ConnectionStrings__PHCConn = (Get-Content $AppSettings -Raw -Encoding UTF8 | ConvertFrom-Json).ConnectionStrings.PHCConn }
if (-not $env:ConnectionStrings__PHCConn) { Write-Host 'ERRO: falta ConnectionStrings__PHCConn.' -ForegroundColor Red; exit 2 }
$d = Get-Content (Join-Path $Pasta 'dados-criados.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$marca = $d.marca
if ($marca -notmatch '^TESTE-DADOS-[a-z0-9]{6}$') { Write-Host 'ERRO: marcador inesperado.' -ForegroundColor Red; exit 2 }

$c = New-Object System.Data.SqlClient.SqlConnection $env:ConnectionStrings__PHCConn
$c.Open()
function Sql([string]$sql, [hashtable]$p = @{}) {
  $cmd = $c.CreateCommand(); $cmd.CommandText = $sql
  foreach ($k in $p.Keys) { [void]$cmd.Parameters.AddWithValue($k, $p[$k]) }
  return $cmd.ExecuteScalar()
}
$base = Sql 'SELECT DB_NAME()'
if ($base -ne $BaseEsperada) { Write-Host "ERRO: a ligação aponta a '$base', não a '$BaseEsperada'. Nada foi feito." -ForegroundColor Red; $c.Dispose(); exit 2 }
Write-Host "Base confirmada: $base" -ForegroundColor Green

$like = "%$marca%"
$eqIds = @($d.equipamentos | ForEach-Object { $_.id })
$tarIds = @($d.tarefas | ForEach-Object { $_.id })
$insIds = @($d.instrucoes | ForEach-Object { $_.id })
$fichaIds = @($d.fichas | ForEach-Object { $_.id })

# lista de ids (stamps da própria execução) como parâmetros de uma cláusula IN
function Em([string[]]$ids, [string]$prefixo) {
  $n = 0; $nomes = @(); $q = @{}
  foreach ($i in $ids) { $nomes += "@$prefixo$n"; $q["@$prefixo$n"] = $i; $n++ }
  if ($nomes.Count -eq 0) { return [pscustomobject]@{ Sql = "(select 'x' where 1=0)"; P = @{} } }
  return [pscustomobject]@{ Sql = '(' + ($nomes -join ',') + ')'; P = $q }
}
$fi = Em $fichaIds 'f'; $ti = Em $tarIds 't'; $ei = Em $eqIds 'e'; $ii = Em $insIds 'i'
$p = @{ '@like' = $like }
foreach ($x in $fi, $ti, $ei, $ii) { foreach ($k in $x.P.Keys) { $p[$k] = $x.P[$k] } }

# Fichas a apagar: as do equipamento de teste (design com o marcador), as ligadas às tarefas registadas e as com o marcador no problema
# (inclui a planeada seguinte, gerada ao concluir).
$selFichas = "select u_fichaintstamp from u_fichaint with (nolock) where problema like @like or design like @like or uitmanutlistamp in $($ti.Sql) or u_equipstamp in $($ei.Sql)"
$contagens = [ordered]@{
  'u_fichaint'   = "select count(*) from u_fichaint with (nolock) where u_fichaintstamp in ($selFichas)"
  'u_matinterv'  = "select count(*) from u_matinterv with (nolock) where u_fichaintstamp in ($selFichas)"
  'u_tempinterv' = "select count(*) from u_tempinterv with (nolock) where u_fichaintstamp in ($selFichas)"
  'anexos'       = "select count(*) from anexos with (nolock) where recstamp in ($selFichas) and oritable = 'U_FICHAINT'"
  'u_itmanutli'  = "select count(*) from u_itmanutli with (nolock) where u_itmanutlistamp in $($ti.Sql) or (u_itmanutstamp in $($ii.Sql) and descricao like @like)"
  'u_itmanut'    = "select count(*) from u_itmanut with (nolock) where u_itmanutstamp in $($ii.Sql)"
  'u_histstats'  = "select count(*) from u_histstats with (nolock) where u_equipstamp in $($ei.Sql)"
  'u_equip'      = "select count(*) from u_equip with (nolock) where u_equipstamp in $($ei.Sql) and design like @like"
}
foreach ($k in $contagens.Keys) { Write-Host ('{0,-14} {1}' -f $k, (Sql $contagens[$k] $p)) }
Write-Host ('periodicidades a repor: {0}' -f @($d.periodicidadesAlteradas | Where-Object { $_ }).Count)

if (-not $Apagar) { Write-Host "`nNada foi apagado (use -Apagar)." -ForegroundColor Yellow; $c.Dispose(); exit 0 }

$tx = $c.BeginTransaction()
function SqlTx([string]$sql) {
  $cmd = $c.CreateCommand(); $cmd.Transaction = $tx; $cmd.CommandText = $sql
  foreach ($k in $p.Keys) { [void]$cmd.Parameters.AddWithValue($k, $p[$k]) }
  return $cmd.ExecuteNonQuery()
}
try {
  $r = [ordered]@{}
  $r['anexos']       = SqlTx "delete from anexos where recstamp in ($selFichas) and oritable = 'U_FICHAINT'"
  $r['u_matinterv']  = SqlTx "delete from u_matinterv where u_fichaintstamp in ($selFichas)"
  $r['u_tempinterv'] = SqlTx "delete from u_tempinterv where u_fichaintstamp in ($selFichas)"
  $r['u_fichaint']   = SqlTx "delete from u_fichaint where u_fichaintstamp in ($selFichas)"
  $r['u_itmanutli']  = SqlTx "delete from u_itmanutli where u_itmanutlistamp in $($ti.Sql) or (u_itmanutstamp in $($ii.Sql) and descricao like @like)"
  $r['u_itmanut']    = SqlTx "delete from u_itmanut where u_itmanutstamp in $($ii.Sql) and not exists (select 1 from u_itmanutli l where l.u_itmanutstamp = u_itmanut.u_itmanutstamp)"
  $r['u_histstats']  = SqlTx "delete from u_histstats where u_equipstamp in $($ei.Sql)"
  $r['u_equip']      = SqlTx "delete from u_equip where u_equipstamp in $($ei.Sql) and design like @like"
  foreach ($pe in @($d.periodicidadesAlteradas)) {
    if (-not $pe) { continue }
    $cmd = $c.CreateCommand(); $cmd.Transaction = $tx
    $cmd.CommandText = 'update u_periocid set unidade = @u, intervalo = @n where u_periocidstamp = @id'
    [void]$cmd.Parameters.AddWithValue('@u', [string]$pe.antes.unidade); [void]$cmd.Parameters.AddWithValue('@n', [decimal]$pe.antes.intervalo); [void]$cmd.Parameters.AddWithValue('@id', [string]$pe.id)
    [void]$cmd.ExecuteNonQuery()
  }
  $tx.Commit()
  Write-Host "`nApagado:" -ForegroundColor Green
  foreach ($k in $r.Keys) { Write-Host ('{0,-14} {1}' -f $k, $r[$k]) }
} catch { $tx.Rollback(); Write-Host "ERRO, nada foi apagado: $($_.Exception.Message)" -ForegroundColor Red; $c.Dispose(); exit 1 }
$c.Dispose()
