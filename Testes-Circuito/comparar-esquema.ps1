<#
  Compara o esquema das tabelas que a app usa entre a base de testes e a base real, e confirma se os campos que o código
  referencia existem em cada uma. SÓ LÊ: só SELECT em INFORMATION_SCHEMA e SELECT DB_NAME(); nunca toca em dados, nunca imprime a ligação.

  Variáveis (definidas por si na sessão, nunca em ficheiros):
    ConnectionStrings__PHCConn        base de testes
    ConnectionStrings__PHCConn_REAL   base real (produção)
  As ligações podem ser o texto cifrado do appsettings (então define também Seguranca__ChaveLigacao) ou em claro.
  Uso:  .\comparar-esquema.ps1 [-Esperado .\esquema-esperado.json] [-BaseTestes testes_land2] [-BaseReal land2]
  Saída: resultados-esquema\<data>\comparacao-esquema.md
#>
param(
  [string]$Esperado = (Join-Path $PSScriptRoot 'esquema-esperado.json'),
  [string]$BaseTestes = 'testes_land2',
  [string]$BaseReal = 'land2'
)
$ErrorActionPreference = 'Stop'
if (-not $env:ConnectionStrings__PHCConn) { Write-Host 'ERRO: falta ConnectionStrings__PHCConn (testes).' -ForegroundColor Red; exit 2 }
if (-not $env:ConnectionStrings__PHCConn_REAL) { Write-Host 'ERRO: falta ConnectionStrings__PHCConn_REAL (real).' -ForegroundColor Red; exit 2 }
# Aceita a ligação em claro (com ';') ou cifrada como no appsettings (TripleDES/ECB, chave = MD5 de Seguranca__ChaveLigacao, base64).
function Ligacao([string]$valor) {
  if ($valor.Contains(';')) { return $valor }
  $chave = $env:Seguranca__ChaveLigacao
  if (-not $chave) { Write-Host 'ERRO: ligação cifrada: falta Seguranca__ChaveLigacao.' -ForegroundColor Red; exit 2 }
  $md5 = [System.Security.Cryptography.MD5]::Create()
  $td = [System.Security.Cryptography.TripleDES]::Create()
  $td.Key = $md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($chave)); $td.Mode = 'ECB'; $td.Padding = 'PKCS7'
  $bytes = [Convert]::FromBase64String($valor.Trim())
  $txt = [Text.Encoding]::UTF8.GetString($td.CreateDecryptor().TransformFinalBlock($bytes, 0, $bytes.Length)).Trim()
  if ($txt -like 'Server=*') { return $txt }
  $p = @($txt -split "`n" | ForEach-Object { $_.Replace("`r", '').Trim() } | Where-Object { $_ })
  if ($p.Count -lt 5) { Write-Host 'ERRO: ligação desencriptada incompleta.' -ForegroundColor Red; exit 2 }
  return "Server=$($p[0]);Database=$($p[1]);User Id=$($p[3]);Password=$($p[4]);TrustServerCertificate=True;"   # sigma-ok: monta o texto com os campos desencriptados, sem valor real
}
$esp = Get-Content $Esperado -Raw -Encoding UTF8 | ConvertFrom-Json
$tabelas = @($esp.PSObject.Properties | Where-Object { $_.Name -notlike '_*' } | ForEach-Object { $_.Name })
foreach ($t in $tabelas) { if ($t -notmatch '^[a-z0-9_]+$') { Write-Host "ERRO: nome de tabela inesperado: $t" -ForegroundColor Red; exit 2 } }
$lista = ($tabelas | ForEach-Object { "'$_'" }) -join ','

function LerEsquema([string]$ligacao, [string]$esperadaDb, [string]$rotulo) {
  $c = New-Object System.Data.SqlClient.SqlConnection $ligacao
  $c.Open()
  try {
    $cmd = $c.CreateCommand(); $cmd.CommandText = 'SELECT DB_NAME()'
    $db = [string]$cmd.ExecuteScalar()
    if ($db -ne $esperadaDb) { throw "A ligação '$rotulo' aponta a '$db', não a '$esperadaDb'. Nada foi lido." }
    Write-Host "$rotulo confirmada: $db" -ForegroundColor Green
    $cmd.CommandText = "SELECT LOWER(TABLE_NAME), LOWER(COLUMN_NAME), DATA_TYPE, ISNULL(CHARACTER_MAXIMUM_LENGTH,-9), ISNULL(NUMERIC_PRECISION,-9), ISNULL(NUMERIC_SCALE,-9), IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE LOWER(TABLE_NAME) IN ($lista)"
    $r = $cmd.ExecuteReader()
    $m = @{}
    while ($r.Read()) {
      $t = $r.GetString(0)
      if (-not $m.ContainsKey($t)) { $m[$t] = @{} }
      $m[$t][$r.GetString(1)] = "$($r.GetString(2))($($r.GetValue(3)),$($r.GetValue(4)),$($r.GetValue(5))) $(if ($r.GetString(6) -eq 'YES') {'null'} else {'not null'})"
    }
    $r.Close()
    return $m
  } finally { $c.Dispose() }
}

$teste = LerEsquema (Ligacao $env:ConnectionStrings__PHCConn) $BaseTestes 'Testes'
$real = LerEsquema (Ligacao $env:ConnectionStrings__PHCConn_REAL) $BaseReal 'Real'

$md = New-Object System.Collections.ArrayList
[void]$md.Add("# Esquema: testes ($BaseTestes) vs real ($BaseReal)")
[void]$md.Add("")
[void]$md.Add("Gerado em $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Só INFORMATION_SCHEMA; nenhum dado lido.")
[void]$md.Add("")
[void]$md.Add("| Tabela | Colunas testes | Colunas real | Em falta (testes) | Em falta (real) | Só nos testes | Só na real | Tipos diferentes |")
[void]$md.Add("|---|---|---|---|---|---|---|---|")
$detalhe = New-Object System.Collections.ArrayList
foreach ($t in $tabelas) {
  $et = @($esp.$t | ForEach-Object { $_.ToLower() })
  $opc = @(); if ($esp._opcionais -and $esp._opcionais.$t) { $opc = @($esp._opcionais.$t | ForEach-Object { $_.ToLower() }) }
  $ct = if ($teste.ContainsKey($t)) { $teste[$t] } else { @{} }
  $cr = if ($real.ContainsKey($t)) { $real[$t] } else { @{} }
  $faltaT = @($et | Where-Object { -not $ct.ContainsKey($_) -and ($opc -notcontains $_) })
  $faltaR = @($et | Where-Object { -not $cr.ContainsKey($_) -and ($opc -notcontains $_) })
  $soT = @($ct.Keys | Where-Object { -not $cr.ContainsKey($_) } | Sort-Object)
  $soR = @($cr.Keys | Where-Object { -not $ct.ContainsKey($_) } | Sort-Object)
  $dif = @($ct.Keys | Where-Object { $cr.ContainsKey($_) -and $cr[$_] -ne $ct[$_] } | Sort-Object)
  $nome = if ($ct.Count -eq 0 -and $cr.Count -eq 0) { "$t (NÃO EXISTE em nenhuma)" } else { $t }
  [void]$md.Add("| $nome | $($ct.Count) | $($cr.Count) | $($faltaT.Count) | $($faltaR.Count) | $($soT.Count) | $($soR.Count) | $($dif.Count) |")
  if ($faltaT.Count + $faltaR.Count + $soT.Count + $soR.Count + $dif.Count + $opc.Count -gt 0) {
    [void]$detalhe.Add("## $t")
    if ($faltaT) { [void]$detalhe.Add("- **Usada pelo código e em falta nos testes:** " + ($faltaT -join ', ')) }
    if ($faltaR) { [void]$detalhe.Add("- **Usada pelo código e em falta na real:** " + ($faltaR -join ', ')) }
    foreach ($o in $opc) { [void]$detalhe.Add("- Opcional ``$o``: testes $(if ($ct.ContainsKey($o)) {'existe'} else {'não existe'}); real $(if ($cr.ContainsKey($o)) {'existe'} else {'não existe'})") }
    if ($soT) { [void]$detalhe.Add("- Só nos testes: " + ($soT -join ', ')) }
    if ($soR) { [void]$detalhe.Add("- Só na real: " + ($soR -join ', ')) }
    foreach ($d in $dif) { [void]$detalhe.Add("- Tipo diferente em ``$d``: testes ``$($ct[$d])`` / real ``$($cr[$d])``") }
    [void]$detalhe.Add("")
  }
}
[void]$md.Add("")
foreach ($l in $detalhe) { [void]$md.Add($l) }
$pasta = Join-Path $PSScriptRoot ("resultados-esquema\" + (Get-Date -Format 'yyyy-MM-dd_HHmm'))
New-Item -ItemType Directory -Force $pasta | Out-Null
$md -join "`r`n" | Set-Content (Join-Path $pasta 'comparacao-esquema.md') -Encoding UTF8
Write-Host "Relatório: $pasta\comparacao-esquema.md" -ForegroundColor Green
