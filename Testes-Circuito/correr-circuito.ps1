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
  [switch]$Ux,   # em vez do circuito, corre a auditoria de UI/UX (só leitura) na app real
  [string]$Script,        # em vez do circuito, corre este .mjs só contra a API (sem app nem limpeza), p. ex. popular-dados.mjs
  [string]$AppSettings,   # ficheiro appsettings de onde se carregam ConnectionStrings.PHCConn e Pin.Pepper para variáveis do processo (nunca se imprimem)
  [switch]$CriarUtilizadores,  # cria 1 supervisor e 1 técnico de teste (e2e.sup4/e2e.tec4, PINs de 4 números aleatórios) em u_manutil e apaga-os no fim; dispensa PIN_TECNICO/PIN_SUPERVISOR
  [string]$Circuito = 'circuito.mjs',  # o .mjs a correr com a app (p. ex. circuitos-funcionais.mjs)

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
  if (-not $env:PIN_TECNICO) { $env:PIN_TECNICO = '4829' }
  if (-not $env:PIN_SUPERVISOR) { $env:PIN_SUPERVISOR = '7953' }
  # Chaves só para esta execução (o desenvolvimento gera-as sozinho; o pepper tem de ser definido de forma estável).
  Remove-Item Env:\Pin__Pepper -ErrorAction SilentlyContinue
} else {
  if ($AppSettings) {
    $cfg = Get-Content $AppSettings -Raw -Encoding UTF8 | ConvertFrom-Json
    $env:ConnectionStrings__PHCConn = $cfg.ConnectionStrings.PHCConn
    $env:Pin__Pepper = $cfg.Pin.Pepper
  }
  $obrigatorias = @('ConnectionStrings__PHCConn', 'Pin__Pepper'); if (-not $CriarUtilizadores) { $obrigatorias += 'PIN_TECNICO', 'PIN_SUPERVISOR' }
  foreach ($v in $obrigatorias) {
    if (-not [Environment]::GetEnvironmentVariable($v)) { Falhar "Falta a variável de ambiente $v (ver o cabeçalho do script)." }
  }
  $base = Consultar 'SELECT DB_NAME()'
  if ($base -ne $BaseEsperada) { Falhar "A ligação aponta a '$base', não a '$BaseEsperada'. Nada foi feito." }
  Write-Host "Base confirmada: $base" -ForegroundColor Green

  if ($CriarUtilizadores) {
    # Utilizadores só deste teste: PIN de 4 números sem sequências, hash como a API (HMAC-SHA256 com o pepper). Nunca se imprimem os PINs.
    function HashPin([string]$pin) {
      $h = New-Object System.Security.Cryptography.HMACSHA256 (, [Text.Encoding]::UTF8.GetBytes($env:Pin__Pepper))
      return ([BitConverter]::ToString($h.ComputeHash([Text.Encoding]::UTF8.GetBytes($pin)))).Replace('-', '')
    }
    function PinNovo {
      while ($true) {
        $p = '{0:D4}' -f (Get-Random -Minimum 0 -Maximum 10000)
        $d = 1..3 | ForEach-Object { [int]$p[$_] - [int]$p[$_ - 1] }
        if (($d | Sort-Object -Unique).Count -eq 1) { continue }   # 0000, 1234, 4321...
        if ((Consultar 'SELECT COUNT(*) FROM u_manutil WHERE pinhash = @h' @{ '@h' = (HashPin $p) }) -eq 0) { return $p }
      }
    }
    foreach ($u in @(@('e2e.sup4', 'Teste E2E Supervisor', 'SUPERVISOR', 99003), @('e2e.tec4', 'Teste E2E Tecnico', 'OPERADOR', 99004))) {
      if ((Consultar 'SELECT COUNT(*) FROM u_manutil WHERE utilizador = @u' @{ '@u' = $u[0] }) -ne 0) { [void](Consultar 'DELETE FROM u_manutil WHERE utilizador = @u; SELECT 0' @{ '@u' = $u[0] }) }
      $pin = PinNovo
      [void](Consultar "INSERT INTO u_manutil (u_manutilstamp, utilizador, nome, perfil, ativo, pinhash, pinmudar, tecno, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora) VALUES (LEFT('E2E' + REPLACE(CAST(NEWID() AS varchar(40)), '-', ''), 25), @u, @n, @p, 1, @h, 0, @t, 'E2E', CONVERT(date, GETDATE()), '00:00:00', 'E2E', CONVERT(date, GETDATE()), '00:00:00'); SELECT 0" @{ '@u' = $u[0]; '@n' = $u[1]; '@p' = $u[2]; '@h' = (HashPin $pin); '@t' = $u[3] })
      if ($u[2] -eq 'SUPERVISOR') { $env:PIN_SUPERVISOR = $pin } else { $env:PIN_TECNICO = $pin }
    }
    $utilizadoresCriados = $true
    Write-Host 'Utilizadores de teste criados (e2e.sup4, e2e.tec4).' -ForegroundColor Green
  }
}
if ($ComPlaneada) { $env:E2E_PLANEADA = '1' } elseif ($Memoria) { $env:E2E_PLANEADA = '1' }

try {
  if (-not $SemBuild) {
    dotnet build $api -v q | Out-Null; if ($LASTEXITCODE) { Falhar 'A API não compilou.' }
    dotnet build $app -f net10.0-windows10.0.19041.0 -v q | Out-Null; if ($LASTEXITCODE) { Falhar 'A app Windows não compilou.' }
  }
  if (-not $Script) {
    if (-not (Test-Path $exe)) { Falhar "Não encontrei a app: $exe" }
    Write-Host "App: $exe ($((Get-Item $exe).LastWriteTime))"
  }
  if (-not (Test-Path (Join-Path $PSScriptRoot 'node_modules'))) { Push-Location $PSScriptRoot; npm install --no-audit --no-fund | Out-Null; Pop-Location }

  # ---- API
  if (Get-NetTCPConnection -LocalPort $PortaApi -State Listen -ErrorAction SilentlyContinue) { Falhar "A porta $PortaApi já está ocupada." }
  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ASPNETCORE_URLS = $apiUrl
  $env:Limites__LoginPorMinuto = '500'   # o circuito entra várias vezes por minuto no mesmo endereço
  # Log de cada pedido (verbo, rota, estado, ms) para o circuito mostrar o que a API recebeu a cada clique.
  [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics', 'Information')
  $env:GM_API_LOG = Join-Path $env:TEMP 'gm-api.log'
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

  if ($Script) {
    $env:API_URL = $apiUrl
    Push-Location $PSScriptRoot
    node $Script
    $codigo = $LASTEXITCODE
    Pop-Location
    exit $codigo
  }

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
  if ($Ux) { node auditoria-ux.mjs } else { node $Circuito }
  $codigo = $LASTEXITCODE
  Pop-Location

  # ---- Limpeza: só as fichas criadas por esta execução e só com o marcador do teste, na base de testes confirmada
  if (-not $Memoria -and -not $SemLimpeza -and -not $Ux) {
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

    # Equipamentos, instruções, tarefas, fichas e operadores criados pelos circuitos funcionais (só com o marcador TESTE-E2E-).
    $criadosJson = Join-Path $ultimo.FullName 'criados.json'
    if (Test-Path $criadosJson) {
      $k = Get-Content $criadosJson -Raw -Encoding UTF8 | ConvertFrom-Json
      $eqApagados = 0
      foreach ($id in $k.equipamentos) {
        if ((Consultar "SELECT COUNT(*) FROM u_equip WHERE u_equipstamp = @id AND ref LIKE 'TESTE-E2E-%'" @{ '@id' = $id }) -ne 1) { continue }
        $sub = "SELECT u_fichaintstamp FROM u_fichaint WHERE u_equipstamp = @id"
        foreach ($t in 'u_matinterv', 'u_tempinterv') { [void](Consultar "DELETE FROM $t WHERE u_fichaintstamp IN ($sub); SELECT 0" @{ '@id' = $id }) }
        [void](Consultar "DELETE FROM u_fichaint WHERE u_equipstamp = @id; SELECT 0" @{ '@id' = $id })
        [void](Consultar "DELETE FROM u_itmanutli WHERE u_itmanutstamp IN (SELECT u_itmanutstamp FROM u_itmanut WHERE u_equipstamp = @id); SELECT 0" @{ '@id' = $id })
        [void](Consultar "DELETE FROM u_itmanut WHERE u_equipstamp = @id; SELECT 0" @{ '@id' = $id })
        [void](Consultar "DELETE FROM u_histstats WHERE u_equipstamp = @id; SELECT 0" @{ '@id' = $id })
        [void](Consultar "DELETE FROM u_equip WHERE u_equipstamp = @id AND ref LIKE 'TESTE-E2E-%'; SELECT 0" @{ '@id' = $id })
        $eqApagados++
      }
      foreach ($u in $k.utilizadores) {
        if ($u -match '^e2e\.n[a-z0-9]{6}$') { [void](Consultar 'DELETE FROM u_manutil WHERE utilizador = @u; SELECT 0' @{ '@u' = $u }) }
      }
      Write-Host "Limpeza: $eqApagados equipamento(s) de teste (com instruções, tarefas e fichas) e $(@($k.utilizadores).Count) operador(es) apagados."
    }
  }
  exit $codigo
} finally {
  # Só os processos que este script arrancou (e os filhos deles), nunca uma app que o utilizador já tenha aberta.
  foreach ($p in $processos) { if ($p -and -not $p.HasExited) { taskkill /T /F /PID $p.Id | Out-Null } }
  if ($utilizadoresCriados) {
    try {
      if ((Consultar 'SELECT DB_NAME()') -eq $BaseEsperada) {
        [void](Consultar "DELETE FROM u_manutil WHERE utilizador IN ('e2e.sup4', 'e2e.tec4'); SELECT 0")
        Write-Host 'Utilizadores de teste apagados.' -ForegroundColor Green
      }
    } catch { Write-Host 'AVISO: não consegui apagar e2e.sup4/e2e.tec4 de u_manutil: apague à mão.' -ForegroundColor Yellow }
  }
  if ($dados -and (Test-Path $dados)) { Remove-Item $dados -Recurse -Force -ErrorAction SilentlyContinue }
}
