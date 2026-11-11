<#
  Passa a API do IIS deste PC a servir em https://192.168.1.71:5055 com o certificado novo (CN=GestorManutencao, IP 192.168.1.71).
  Correr numa PowerShell COMO ADMINISTRADOR:   powershell -NoProfile -ExecutionPolicy Bypass -File docs\ativar-api-iis-neste-pc.ps1

  O que faz:
   1. Copia o certificado do utilizador atual (CurrentUser\My) para o computador (LocalMachine\My), com uma palavra-passe temporária só em memória.
   2. Encontra o site do IIS com binding https na porta 5055, tira-lhe o binding antigo (192.168.176.1) e põe um para todos os IPs (*:5055) com o certificado novo.
   3. Abre a porta 5055 na firewall (redes Privada e Domínio).
   4. Testa https://192.168.1.71:5055/saude.
  Não escreve nenhum segredo em ficheiros.
#>
param([string]$Thumbprint = 'CDF9C00151CC3B1719B26155C8FC236213036EAB', [int]$Porta = 5055, [string]$Ip = '192.168.1.71')
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  Write-Host 'ERRO: corra esta PowerShell como administrador.' -ForegroundColor Red; exit 2
}
Import-Module WebAdministration

# 1. Certificado para o computador
$origem = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $Thumbprint | Select-Object -First 1
$destino = Get-ChildItem Cert:\LocalMachine\My | Where-Object Thumbprint -eq $Thumbprint | Select-Object -First 1
if (-not $destino) {
  if (-not $origem) { Write-Host "ERRO: não encontrei o certificado $Thumbprint em CurrentUser\My. Corra este script com o utilizador que o criou." -ForegroundColor Red; exit 2 }
  $pw = ConvertTo-SecureString ([Guid]::NewGuid().ToString('N')) -AsPlainText -Force
  $tmp = Join-Path $env:TEMP ('gestor-' + [Guid]::NewGuid().ToString('N') + '.pfx')
  try {
    Export-PfxCertificate -Cert $origem -FilePath $tmp -Password $pw | Out-Null   # sigma-ok: palavra-passe aleatória só em memória
    Import-PfxCertificate -FilePath $tmp -CertStoreLocation Cert:\LocalMachine\My -Password $pw | Out-Null   # sigma-ok: palavra-passe aleatória só em memória
  } finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
  Write-Host 'Certificado copiado para LocalMachine\My.' -ForegroundColor Green
} else { Write-Host 'O certificado já está em LocalMachine\My.' -ForegroundColor Green }

# 2. Binding do IIS
$site = Get-Website | Where-Object { $_.bindings.Collection | Where-Object { $_.protocol -eq 'https' -and $_.bindingInformation -match ":$Porta`:" } } | Select-Object -First 1
if (-not $site) { Write-Host "ERRO: não há nenhum site do IIS com binding https na porta $Porta." -ForegroundColor Red; exit 2 }
Write-Host "Site: $($site.Name)  pasta: $($site.PhysicalPath)"
foreach ($b in @(Get-WebBinding -Name $site.Name -Protocol https | Where-Object { $_.bindingInformation -match ":$Porta`:" })) {
  $ipAntigo = ($b.bindingInformation -split ':')[0]
  Remove-WebBinding -Name $site.Name -Protocol https -Port $Porta -IPAddress $ipAntigo
  if ($ipAntigo -ne '*') { & netsh http delete sslcert ipport="$($ipAntigo):$Porta" | Out-Null }
}
New-WebBinding -Name $site.Name -Protocol https -Port $Porta -IPAddress '*'
(Get-WebBinding -Name $site.Name -Protocol https -Port $Porta).AddSslCertificate($Thumbprint, 'My')
Write-Host "Binding https *:$Porta com o certificado novo." -ForegroundColor Green
if (-not (Test-Path (Join-Path $site.PhysicalPath 'appsettings.json'))) {
  Write-Host "AVISO: falta appsettings.json em $($site.PhysicalPath) (ligação à base, Jwt:Chave, Pin:Pepper)." -ForegroundColor Yellow
}

# 3. Firewall
if (-not (Get-NetFirewallRule -DisplayName "Gestor Manutencao API $Porta" -ErrorAction SilentlyContinue)) {
  New-NetFirewallRule -DisplayName "Gestor Manutencao API $Porta" -Direction Inbound -Protocol TCP -LocalPort $Porta -Action Allow -Profile Private, Domain | Out-Null
  Write-Host "Firewall: porta $Porta aberta nas redes Privada e Domínio." -ForegroundColor Green
}
$perfil = (Get-NetConnectionProfile | Where-Object { (Get-NetIPAddress -InterfaceIndex $_.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue).IPAddress -contains $Ip }).NetworkCategory
Write-Host "A rede do Wi-Fi está classificada como: $perfil (se for Public, passe-a a Private ou o telemóvel não chega)."

# 4. Teste
iisreset /noforce | Out-Null
Start-Sleep -Seconds 3
$codigo = & curl.exe -sk -o NUL -w '%{http_code}' "https://$($Ip):$Porta/saude"
Write-Host "GET https://$($Ip):$Porta/saude -> $codigo (200 = a API responde)"
