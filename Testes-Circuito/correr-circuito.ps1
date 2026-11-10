<#
  Circuito de testes ponta a ponta do Gestor de Manutenção.
  Arranca a API local e a app Windows (WebView2 com depuração remota) e corre circuito.mjs:
  a API (supervisor e técnico) e os botões da app (Playwright por CDP) são testados em simultâneo.

  Modos
    -Memoria   API com utilizadores e fichas em memória (sem base de dados). Serve para validar o próprio circuito.
    (omissão)  API ligada à base de testes: só corre se SELECT DB_NAME() devolver testes_land2.

  Variáveis de ambiente (definidas por si na sessão; este script nunca as imprime nem grava):
    ConnectionStrings__PHCConn   ligação à testes_land2
    Pin__Pepper                  o mesmo pepper com que foram criados os PINs em u_manutil
    PIN_TECNICO, PIN_SUPERVISOR  PINs definitivos de um técnico e de um supervisor da base de testes
    (em -Memoria os PINs de teste da API são usados por omissão)
#>
param(
  [switch]$Memoria,
  [switch]$SemBuild,
  [switch]$SemLimpeza,
  [switch]$ComPlaneada,
  [string]$BaseEsperada = 'testes_land2',
  [int]$PortaApi = 7257,
  [int]$PortaCdp = 9222
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$api = Join-Path $raiz 'GestorManutencao.Api'
$app = Join-Path $raiz 'GestorManutencao.App'
$exe = Join-Path $app 'bin\Debug\net10.0-windows10.0.19041.0\win-x64\GestorManutencao.App.exe'
$apiUrl = "https://localhost:$PortaApi"
$processos = @()

function Falhar([string]$m) { Write-Host "ERRO: $m" -ForegroundColor Red; exit 2 }

function Consultar([string]$sql, [hashtable]$p = @{}) {
  $c = New-Object System.Data.SqlClient.SqlConnection $env:ConnectionStrings__PHCConn
  try {
    $c.Open()
    $cmd = $c.CreateCommand(); $cmd.CommandText = $sql
    foreach ($k in $p.Keys) { [void]$cmd.Parameters.AddWithValue($k, $p[$k]) }
    return $cmd.ExecuteScalar()
  } finally { $c.Dispose() }
}

# ---- segurança: só se escreve na base de testes confirmada
if ($Memoria) {
  $env:ConnectionStrings__PHCConn = ' '
  if (-not $env:PIN_TECNICO) { $env:PIN_TECNICO = '482913' }
  if (-not $env:PIN_SUPERVISOR) { $env:PIN_SUPERVISOR = '795316' }
  # Chaves só para esta execução (o desenvolvimento gera-as sozinho; o pepper tem de ser definido de forma estável).
  Remove-Item Env:\Pin__Pepper -ErrorAction SilentlyContinue
} else {
  foreach ($v in 'ConnectionStrings__PHCConn', 'Pin__Pepper', 'PIN_TECNICO', 'PIN_SUPERVISOR') {
    if (-not [Environment]::GetEnvironmentVariable($v)) { Falhar "Falta a variável de ambiente $v (ver o cabeçalho do script)." }
  }
  $base = Consultar 'SELECT DB_NAME()'
  if ($base -ne $BaseEsperada) { Falhar "A ligação aponta a '$base', não a '$BaseEsperada'. Nada foi feito." }
  Write-Host "Base confirmada: $base" -ForegroundColor Green
}
if ($ComPlaneada) { $env:E2E_PLANEADA = '1' } elseif ($Memoria) { $env:E2E_PLANEADA = '1' }

try {
  if (-not $SemBuild) {
    dotnet build $api -v q | Out-Null; if ($LASTEXITCODE) { Falhar 'A API não compilou.' }
    dotnet build $app -f net10.0-windows10.0.19041.0 -v q | Out-Null; if ($LASTEXITCODE) { Falhar 'A app Windows não compilou.' }
  }
  if (-not (Test-Path $exe)) { Falhar "Não encontrei a app: $exe" }
  Write-Host "App: $exe ($((Get-Item $exe).LastWriteTime))"
  if (-not (Test-Path (Join-Path $PSScriptRoot 'node_modules'))) { Push-Location $PSScriptRoot; npm install --no-audit --no-fund | Out-Null; Pop-Location }

  # ---- API
  if (Get-NetTCPConnection -LocalPort $PortaApi -State Listen -ErrorAction SilentlyContinue) { Falhar "A porta $PortaApi já está ocupada." }
  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ASPNETCORE_URLS = $apiUrl
  $env:Limites__LoginPorMinuto = '500'   # o circuito entra várias vezes por minuto no mesmo endereço
  if ($Memoria) { $env:Pin__Pepper = $null }
  $processos += Start-Process dotnet -ArgumentList @('run', '--project', $api, '--no-build', '--no-launch-profile') -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $env:TEMP 'gm-api.log') -RedirectStandardError (Join-Path $env:TEMP 'gm-api.err')
  $pronta = $false
  for ($i = 0; $i -lt 60 -and -not $pronta; $i++) {
    Start-Sleep -Seconds 1
    # curl.exe: o Windows PowerShell 5.1 não tem -SkipCertificateCheck e o certificado de desenvolvimento não é de confiança.
    $pronta = (curl.exe -sk -o NUL -w '%{http_code}' "$apiUrl/saude") -eq '200'
  }
  if (-not $pronta) { Falhar 'A API não arrancou (ver %TEMP%\gm-api.err).' }
  Write-Host "API a correr em $apiUrl" -ForegroundColor Green

  # ---- App Windows com a porta de depuração do WebView2
  $dados = Join-Path $env:TEMP "gm-webview2-$PID"
  $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$PortaCdp"
  $env:WEBVIEW2_USER_DATA_FOLDER = $dados
  $env:GESTOR_API_URL = $apiUrl
  $processos += Start-Process $exe -PassThru
  $cdp = $false
  for ($i = 0; $i -lt 60 -and -not $cdp; $i++) {
    Start-Sleep -Seconds 1
    try { $cdp = (Invoke-WebRequest "http://127.0.0.1:$PortaCdp/json/version" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { }
  }
  if (-not $cdp) { Falhar 'A app não abriu a porta de depuração do WebView2.' }
  Write-Host 'App ligada por CDP.' -ForegroundColor Green

  # ---- Circuito
  $env:API_URL = $apiUrl
  $env:CDP_URL = "http://127.0.0.1:$PortaCdp"
  Push-Location $PSScriptRoot
  node circuito.mjs
  $codigo = $LASTEXITCODE
  Pop-Location

  # ---- Limpeza: só as fichas criadas por esta execução e só com o marcador do teste, na base de testes confirmada
  if (-not $Memoria -and -not $SemLimpeza) {
    $ultimo = Get-ChildItem (Join-Path $PSScriptRoot 'resultados') -Directory | Sort-Object Name | Select-Object -Last 1
    $f = Get-Content (Join-Path $ultimo.FullName 'fichas-criadas.json') -Raw | ConvertFrom-Json
    if ((Consultar 'SELECT DB_NAME()') -ne $BaseEsperada) { Falhar 'A base mudou: limpeza cancelada.' }
    $apagadas = 0
    foreach ($id in $f.ids) {
      $n = Consultar "SELECT COUNT(*) FROM u_fichaint WHERE u_fichaintstamp = @id AND problema LIKE 'TESTE-E2E-%'" @{ '@id' = $id }
      if ($n -ne 1) { continue }   # planeadas reais e fichas sem o marcador não se tocam
      foreach ($t in 'u_matinterv', 'u_tempinterv') { [void](Consultar "DELETE FROM $t WHERE u_fichaintstamp = @id; SELECT 0" @{ '@id' = $id }) }
      [void](Consultar "DELETE FROM u_fichaint WHERE u_fichaintstamp = @id AND problema LIKE 'TESTE-E2E-%'; SELECT 0" @{ '@id' = $id })
      $apagadas++
    }
    Write-Host "Limpeza: $apagadas ficha(s) de teste apagada(s) em $BaseEsperada."
  }
  exit $codigo
} finally {
  # Só os processos que este script arrancou (e os filhos deles), nunca uma app que o utilizador já tenha aberta.
  foreach ($p in $processos) { if ($p -and -not $p.HasExited) { taskkill /T /F /PID $p.Id | Out-Null } }
  if ($dados -and (Test-Path $dados)) { Remove-Item $dados -Recurse -Force -ErrorAction SilentlyContinue }
}
