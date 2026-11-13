# Instalação: API no IIS e APK no telemóvel

Endereço da API: `https://192.168.1.71:5055/` (dentro da app, em `GestorManutencao.App/Resources/Raw/appsettings.json`, chave `ApiUrl`). É o IP Wi-Fi do PC onde corre o IIS; reservar este IP no router (DHCP), senão a app deixa de ligar quando ele mudar e o APK tem de ser refeito.

**Neste PC (caso atual):** correr numa PowerShell como administrador `powershell -NoProfile -ExecutionPolicy Bypass -File docs\ativar-api-iis-neste-pc.ps1`. Copia o certificado para o computador, põe o binding `*:5055` no site do IIS, abre a porta na firewall e testa `/saude`. Os passos abaixo valem para instalar noutro servidor.

## 1. A API no IIS (num servidor, como administrador)

Pasta pronta a copiar: `publicar-api/` (gerada com `dotnet publish GestorManutencao.Api -c Release -o publicar-api`). Não leva segredos: o `appsettings.json` nunca vai dentro dela (e por isso republicar por cima não o apaga).

1. **Instalar o .NET 10 Hosting Bundle** no servidor (dá o runtime e o módulo `AspNetCoreModuleV2` ao IIS). Depois de instalar: `iisreset`.
2. **Copiar** o conteúdo de `publicar-api/` para, por exemplo, `C:\inetpub\GestorManutencaoApi`.
3. **Criar** `appsettings.json` nessa pasta, a partir de `configuracao.exemplo.json` (vem na pasta publicada; sem valores), e preencher. É o único ficheiro de configuração que a API lê (não lê `appsettings.Production.json` nem `Development`); para passar de testes a real, troca-se o conteúdo deste ficheiro e reinicia-se o site:
   - `ConnectionStrings:PHCConn` (ligação à base, cifrada como na LandeiroMaui: cola-se a mesma string cifrada, a chave já está dentro da API; em claro também funciona).
   - `Jwt:Chave` (texto aleatório com 64 ou mais caracteres).
   - `Pin:Pepper` (tem de ser o mesmo com que foram criados os PINs em `u_manutil`; se mudar, ninguém entra).
4. **Certificado HTTPS**: a app só confia no certificado `CN=GestorManutencao` (IPs 192.168.1.71 e 192.168.18.168 e nome `gestor.local`, válido até 11/11/2028, impressão digital `CDF9C00151CC3B1719B26155C8FC236213036EAB`). O IIS tem de usar esse mesmo certificado, senão a app recusa a ligação. A chave privada está no PC de desenvolvimento (utilizador que o criou). O certificado antigo (`4934FB90...`, só 192.168.18.168) já não serve a esta versão da app. Exportar lá, escolhendo uma palavra-passe:
   ```powershell
   Export-PfxCertificate -Cert Cert:\CurrentUser\My\CDF9C00151CC3B1719B26155C8FC236213036EAB -FilePath .\gestor.pfx -Password (Read-Host -AsSecureString)   # sigma-ok: a palavra-passe é pedida no momento
   ```
   Importar no servidor (mesma palavra-passe) e apagar o `.pfx` depois:
   ```powershell
   Import-PfxCertificate -FilePath .\gestor.pfx -CertStoreLocation Cert:\LocalMachine\My -Password (Read-Host -AsSecureString)   # sigma-ok: a palavra-passe é pedida no momento
   ```
5. **Site no IIS**: novo site apontado à pasta do passo 2, com um pool "Sem código gerido" (No Managed Code). Binding: `https`, todos os IPs (`*`), porta **5055**, certificado `GestorManutencao`. Não ligar só a um adaptador (no PC de desenvolvimento estava ligado ao adaptador virtual do Hyper-V e o telemóvel não chegava).
6. **Firewall do Windows**: permitir a entrada TCP na porta 5055.
7. **Testar**: no browser do servidor e depois do telemóvel (ligado à mesma rede), abrir `https://192.168.1.71:5055/saude` (não pelo `192.168.176.1`, que é o adaptador virtual do Hyper-V e dá `ERR_CERT_COMMON_NAME_INVALID`). Deve responder 200. No browser do telemóvel aparece o aviso do certificado: é normal, a app é que o aceita.

Notas
- O ambiente é `Production` por omissão no IIS. Sem ligação à base configurada, a API recusa arrancar (não usa dados fictícios).
- **Tempo real (SignalR, rota `/hub/avisos`):** no IIS é preciso a funcionalidade **WebSocket Protocol** (Server Manager > Add Roles and Features > Web Server > Application Development > WebSocket Protocol), seguida de `iisreset`. Sem ela a app continua a funcionar, mas volta à verificação de 5 em 5 segundos. O aviso não leva dados: cada aparelho volta a pedir as fichas com o seu token.
- Em IIS "in-process" não é preciso configurar `Proxy:IpsConfiaveis`.
- Os limites de tentativas de PIN (por endereço e totais) estão em `appsettings.json`, secção `Limites`.

## 2. O APK

Ficheiro: `publicar-apk/v1.1/pt.sigmacode.gestormanutencao-Signed.apk` (versão 1.1, código 2). Fica fora do git.

1. Copiar o APK para o telemóvel (cabo, partilha de ficheiros ou e-mail).
2. No Android, permitir "instalar apps de fontes desconhecidas" para a aplicação que vai abrir o ficheiro.
3. Abrir o APK e instalar. Se já havia a versão anterior assinada com a mesma chave, atualiza por cima; se disser que não pode instalar, desinstalar a anterior primeiro (perde-se a fila offline que não tenha sido enviada).
4. O telemóvel tem de estar no mesmo Wi-Fi do PC (192.168.1.x). A rede do PC não pode estar classificada como "Pública" no Windows.
5. Entrar com o PIN de **4 números**.

Cuidados
- Utilizadores com PIN antigo de 6 números deixam de entrar pelo teclado do ecrã (só tem 4). O supervisor atribui-lhes um PIN novo em Equipa, que dá um provisório de 4 números.
- O APK é assinado com a chave de depuração, o que serve para instalar à mão. Para a loja ou para distribuição gerida seria preciso uma chave própria de produção.
- A app só aceita ligações seguras a este endereço e só confia no certificado `gestor.cer` incluído (e nos do sistema).
