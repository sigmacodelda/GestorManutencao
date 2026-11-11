# Tabela de utilizadores da app (a criar no PHC por quem administra)

Quem cria a tabela é o utilizador ou a Landeiro; o Claude Code não cria tabelas nem campos no PHC. A API (`SqlRepositorioUtilizadores`) já está escrita para esta estrutura, mas ainda não foi testada contra uma base real.

## Tabela `u_manutil`

| Campo | Tipo | Obrigatório | Para quê |
|---|---|---|---|
| `u_manutilstamp` | char(25) | sim (chave) | Identificador do registo (padrão PHC) |
| `utilizador` | varchar(30) | sim, único | Código curto da pessoa, em minúsculas (ex.: `jsilva`) |
| `nome` | varchar(60) | sim | Nome mostrado na app |
| `perfil` | varchar(12) | sim | `OPERADOR` ou `SUPERVISOR` |
| `ativo` | bit | sim, por defeito 1 | Desativar em vez de apagar |
| `pinhash` | varchar(64) | sim, **único** | PIN cifrado (HMAC-SHA256 em maiúsculas hexadecimais). Nunca o PIN em claro |
| `pinmudar` | bit | sim, por defeito 1 | 1 = PIN provisório, a pessoa tem de escolher outro ao entrar |
| `pinalteradopor` | varchar(30) | não | Quem definiu o último PIN |
| `pinalteradoem` | datetime | não | Quando |
| `tecno` | numeric(5,0) | sim, por defeito 0 | **Número de utilizador do PHC** (`us.userno`) da pessoa. É a identidade dela nas fichas de intervenção (`u_fichaint.tecno`), como na LandeiroMaui. 0 = ainda sem número: a pessoa entra, mas não vê fichas até o supervisor o indicar na Equipa. Cada número só pode estar numa pessoa (a app verifica; não há índice único porque o 0 repete-se) |
| `ousrinis` varchar(30), `ousrdata` datetime, `ousrhora` varchar(8), `usrinis` varchar(30), `usrdata` datetime, `usrhora` varchar(8) | padrão PHC | sim | Campos de auditoria habituais |

O script de criação está em `docs/criar-u_manutil.sql` (a correr por quem administra, primeiro numa base de testes). **A tabela que já existe na base de testes foi criada antes do campo `tecno`**: o mesmo script tem o `ALTER TABLE` que o acrescenta (ou criar o campo `tecno`, numérico 5,0, por defeito 0, no PHC). A API não arranca a ler utilizadores sem este campo.

Índices únicos: `utilizador` e `pinhash`. O segundo é o que impede dois utilizadores de terem o mesmo PIN (necessário porque se entra só com o PIN).

Os supervisores criam-se diretamente na tabela por quem administra (`perfil = 'SUPERVISOR'`, `pinmudar = 1`). A app só cria operadores. Para gerar o PIN provisório e o `pinhash` do primeiro supervisor, com o `Pin:Pepper` já configurado:

`dotnet run --project GestorManutencao.Api -- gerar-pin`

Imprime o PIN (para entregar à pessoa) e o `pinhash` (para gravar na tabela). O PIN nunca é guardado.

## Regras

- Entra-se só com o PIN: 4 números (o login ainda aceita PINs antigos de 4 a 6), sem sequências óbvias (0000, 1234, 4321, 123456...).
- O supervisor cria o operador e a app mostra-lhe o PIN provisório uma única vez. O mesmo ao repor um PIN.
- Com PIN provisório a pessoa só consegue escolher o PIN definitivo.
- O supervisor não repõe o PIN de outro supervisor.
- A API usa um login SQL próprio, só com `SELECT`, `INSERT` e `UPDATE` em `u_manutil`.

## Configuração da API (a preencher por quem instala)

- `ConnectionStrings:PHCConn`: ligação com o login próprio da API.
- Neste computador a ligação à base `LAND2_DEMO` está em `GestorManutencao.Api/appsettings.json`, o único ficheiro de configuração da API, que o git ignora (nunca vai para o repositório). Para outra base, substituir o valor nesse ficheiro. Em Development a API usa utilizadores em memória e não precisa da base.
- `Jwt:Chave`: mínimo 32 caracteres.
- `Pin:Pepper`: mínimo 32 caracteres e diferente da chave do token. Segredo do servidor com que o PIN é cifrado; **se se perder, todos os PINs ficam inválidos**, e se mudar, é preciso repô-los a todos. Guardar uma cópia segura, fora da base de dados e da pasta partilhada com o PHC (variáveis de ambiente ou cifrado com `tools/segredos/sc-segredos.ps1`).

- `Pin:Dev` (opcional): PIN de configuração de 4 números, só para criar utilizadores (incluindo o primeiro supervisor). Vazio ou ausente = desligado. Fica só no `appsettings.json` do servidor, nunca na app nem no git. Quem entra com ele não tem utilizador na tabela: só vê o ecrã Equipa (listar e criar utilizadores, repor PIN a operadores, mudar o número do PHC), o token dura 30 minutos e deixa de valer se o `Pin:Dev` mudar ou for retirado. É o único acesso que cria supervisores. Cada entrada fica no registo da API (só o IP). Depois de criar o primeiro supervisor, esvaziar o valor e reiniciar a API.
- `Proxy:IpsConfiaveis`: se a API está atrás de proxy ou balanceador, listar os seus IPs. Sem isto o limite de tentativas vê todos os técnicos como um só endereço.
- `Limites:LoginPorMinuto` (por endereço, por defeito 10) e `Limites:LoginGlobalPorMinuto` (teto total, por defeito 600): ajustar ao número de técnicos e ao NAT da fábrica.

## Sessões

- O token dura 10 horas, mas deixa de valer logo que o PIN da pessoa muda ou que ela é desativada.
- Os registos da API guardam o endereço IP das tentativas falhadas (para detetar ataques), nunca o PIN. Informar o cliente (RGPD).

## Limite conhecido

Com entrada só por PIN não é possível bloquear uma pessoa em concreto. A proteção é por limites de tentativas por endereço e no total (valores acima). Um atacante com muitos endereços pode, a longo prazo, acertar num PIN válido; por isso os limites de tentativas são a proteção principal (4 números são poucos) e os óbvios são recusados.
