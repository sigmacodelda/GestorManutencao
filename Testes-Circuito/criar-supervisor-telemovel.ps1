<#
  Cria na base de TESTES (testes_land2) um supervisor para experimentar a app no telemóvel, com um PIN provisório de 4 números
  que só aparece neste ecrã (a app obriga a trocá-lo no primeiro acesso). Não imprime ligações nem segredos.

  Uso:      .\criar-supervisor-telemovel.ps1
  Remover:  .\criar-supervisor-telemovel.ps1 -Remover

  Lê a ligação e o pepper do appsettings.json do site (por omissão C:\Sigma\LandeiroManutencao).
  Só escreve se SELECT DB_NAME() devolver testes_land2.
#>
param(
  [switch]$Remover,
  [string]$AppSettings = 'C:\Sigma\LandeiroManutencao\appsettings.json',
  [string]$BaseEsperada = 'testes_land2',
  [string]$Codigo = 'tel.sup',
  [int]$Tecno = 99010
)
$ErrorActionPreference = 'Stop'
$cfg = Get-Content $AppSettings -Raw -Encoding UTF8 | ConvertFrom-Json
$ligacao = "$($cfg.ConnectionStrings.PHCConn)"
$pepper = "$($cfg.Pin.Pepper)"
if (-not $ligacao.Contains(';') -or -not $pepper) { Write-Host 'ERRO: ligação em claro e pepper têm de estar preenchidos no appsettings.' -ForegroundColor Red; exit 2 }

$conn = New-Object System.Data.SqlClient.SqlConnection $ligacao
$conn.Open()
function Executar-Sql([string]$sql, [hashtable]$parametros = @{}) {
  $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
  foreach ($chave in $parametros.Keys) { [void]$cmd.Parameters.AddWithValue($chave, $parametros[$chave]) }
  return $cmd.ExecuteScalar()
}
function Calcular-HashPin([string]$pin) {
  $hmac = New-Object System.Security.Cryptography.HMACSHA256 (, [Text.Encoding]::UTF8.GetBytes($pepper))
  return ([BitConverter]::ToString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($pin)))).Replace('-', '')
}
try {
  $base = Executar-Sql 'SELECT DB_NAME()'
  if ($base -ne $BaseEsperada) { Write-Host "ERRO: a ligação aponta a '$base', não a '$BaseEsperada'. Nada foi feito." -ForegroundColor Red; exit 2 }
  Write-Host "Base confirmada: $base" -ForegroundColor Green

  if ($Remover) {
    [void](Executar-Sql 'DELETE FROM u_manutil WHERE utilizador = @u; SELECT 0' @{ '@u' = $Codigo })
    Write-Host "Utilizador $Codigo apagado." -ForegroundColor Green
    return
  }
  if ((Executar-Sql 'SELECT COUNT(*) FROM u_manutil WHERE utilizador = @u' @{ '@u' = $Codigo }) -ne 0) {
    Write-Host "Já existe o utilizador $Codigo. Corra com -Remover primeiro se quiser outro PIN." -ForegroundColor Yellow; exit 1
  }
  do {
    $pin = '{0:D4}' -f (Get-Random -Minimum 0 -Maximum 10000)
    $passos = 1..3 | ForEach-Object { [int]$pin[$_] - [int]$pin[$_ - 1] }
    $fraco = (($passos | Sort-Object -Unique).Count -eq 1)
    $emUso = (Executar-Sql 'SELECT COUNT(*) FROM u_manutil WHERE pinhash = @h' @{ '@h' = (Calcular-HashPin $pin) }) -ne 0
  } while ($fraco -or $emUso)

  [void](Executar-Sql "INSERT INTO u_manutil (u_manutilstamp, utilizador, nome, perfil, ativo, pinhash, pinmudar, tecno, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora) VALUES (LEFT('TEL' + REPLACE(CAST(NEWID() AS varchar(40)), '-', ''), 25), @u, 'Teste Telemovel', 'SUPERVISOR', 1, @h, 1, @t, 'TEL', CONVERT(date, GETDATE()), '00:00:00', 'TEL', CONVERT(date, GETDATE()), '00:00:00'); SELECT 0" @{ '@u' = $Codigo; '@h' = (Calcular-HashPin $pin); '@t' = $Tecno })
  Write-Host ''
  Write-Host "Supervisor criado: $Codigo" -ForegroundColor Green
  Write-Host "PIN provisório (só aparece agora): $pin" -ForegroundColor Cyan
  Write-Host 'No telemóvel, escreva estes 4 números; a app pede logo um PIN novo de 4 números.'
} finally { $conn.Dispose() }
