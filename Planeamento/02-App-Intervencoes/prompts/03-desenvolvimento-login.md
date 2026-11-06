# Desenvolvimento do login da app (alvo Android, utilizador e password, PIN, sessão)
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 03 (P2-T03) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO e espera a minha confirmação; depois autónomo até ao GATE)
Papel do agente: desenvolvedor (implementa o que está no prompt, sem análises nem relatórios além de uma linha de contexto)
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 2: a app GestorManutencao já está construída com login só por PIN (`u_manutil`, campo novo `tecno`), perfis operador e supervisor; o técnico é o utilizador PHC (`us.userno`) gravado em `tecno`/`tecnome`. Login SQL por utilizador e PIN local (D-03/D-06/D-10 antigos) deixam de valer. Ecrãs Planeadas e Material usado na ficha estão em construção.
Entradas: `docs/manutencao/p2/levantamento.md`, `docs/manutencao/p2/desenho.md` (secções de login, PIN e credencial D-10); contratos de P1-T02
Saídas: projeto `ManutencaoMobile` com alvo Android a compilar; módulo de sessão e PIN com testes xUnit; `docs/DEPENDENCIES.md`; `docs/tasks/00-global.md` atualizado
Critérios de conclusão:
1. Spike S0 feito e documentado: a app abre uma ligação a `TESTES_LAND2` a partir do emulador ou do telemóvel (SELECT leve) e o resultado fica no relatório; se falhar, `BLOQUEIO:` com 2 a 4 propostas (por exemplo, API fina).
2. (Já construída como app GestorManutencao: confirmar em vez de criar) Projeto `ManutencaoMobile` (net9.0-android, MAUI Blazor Hybrid) compila sem avisos e arranca; o build do LandeiroMaui para Windows continua a compilar como antes (R-06).
3. (Substituído: o login atual é só por PIN contra `u_manutil`) Login por utilizador e password com a base do LandeiroMaui (`LoginBDAsync` adaptado), a distinguir "credenciais inválidas" de "sem rede"; sem rede e sem sessão guardada, mensagem clara e nada de login falso.
4. PIN: definição no 1.º acesso, desbloqueio, bloqueio ao atingir o máximo de tentativas, contador persistente; só hash com sal no armazenamento seguro; falhas de escrita e leitura nunca em silêncio.
5. Sessão longa e logout: sem expiração por tempo; PIN ao abrir e após X minutos em segundo plano; logout com pendentes bloqueado (a fila é da task 04: usar a interface `IFilaPendentes` com implementação provisória); roles recarregados quando há rede; testes xUnit a passar e o roteiro manual no telemóvel escrito (declarado não corrido se não houver aparelho).
Gate: manual
Agentes: `sc-dev` (implementação), `sc-revisor` (revisão obrigatória: segurança)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid (Android), SQLite, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto e cliente: Gestor de Manutenção da Carnes Landeiro. Passa o circuito de manutenção (hoje papel, Word e PHC) para a plataforma .NET MAUI da Landeiro. Esta é a Parte 2: app de telemóvel Android para registo de intervenções. A Parte 1 (backoffice Windows, dados e regras no PHC) e a Parte 3 (peças e armazém) correm em paralelo.
- Repositórios: `C:\_SigmaRepos\Landeiro-Desmancha` (código; `LandeiroMaui` é a base) e `C:\_SigmaRepos\GestorManutencao` (planeamento); branch `dev-landeiro-manutencao-p2`. Documentos desta parte em `docs/manutencao/p2/` do repositório de código.
- Requisitos da Parte 2:
  - APP-01 Login só por PIN (tabela PHC `u_manutil`; app GestorManutencao já construída; o login SQL por utilizador do LandeiroMaui deixou de ser a base); a sessão mantém-se o máximo de tempo possível.
  - APP-02 **Obrigatório trabalhar offline**: cache local SQLite, fila de saída e sincronização.
  - APP-03 Alerta de intervenções planeadas com a antecedência definida no backoffice (funciona offline).
  - APP-04 Lista de planeadas e pendentes, com botão "+" para criar.
  - APP-05 Ecrã simples e prático para criar a intervenção: equipamento, preventiva ou curativa, tempos, técnico, descrição, foto.
  - APP-06 Avançar intervenções fora de ordem só se a configuração do backoffice o permitir.
  - APP-07 Sincronização sem duplicados e com resolução de conflitos (ganha a validação do backoffice).
  - APP-08 Peças usadas na intervenção (liga à Parte 3, 2.ª fase).
- Estados da intervenção: Planeada, EmCurso, Concluida, Validada (supervisão, no backoffice). A curativa nasce EmCurso no telemóvel. A supervisão pode devolver (Concluida para EmCurso) ou cancelar (Planeada para Cancelada). A ficha só fica Fechada quando validada.
- Esboço do quadro (reunião de 04/11/2026): ecrã de lista com botão "+" no canto superior direito, que abre o ecrã "Intr." com uma linha de campos de tempo e pessoa, uma caixa de texto, uma foto e um botão de ação. Nota ao lado: "configuração no BO que defina se pode avançar ordem intervenções". "Obrigatório trabalhar" (offline).
- Prazos: protótipo clicável a 11/11/2026; 17/11/2026 (Suposição) primeiras intervenções. Marcos propostos (Suposição): M0 protótipo 11/11; M1 login e lista online; M2 offline; M3 alertas e fotos.
- Situação atual e problema: o login do LandeiroMaui é um login SQL do próprio utilizador (`UserStateService.LoginBDAsync`: `SqlConnectionStringBuilder` com `UserID` e `Password`, `OpenAsync`, depois `LoadUser` em `us` e `LoadUserRoles` em `pf`/`pfu` com `left(resumo,4)='DES_'`), guarda `username`, `userno`, `usernome`, `iniciais` e `roles` em `SafeSecureStorage` e nunca a password. Problemas para o telemóvel: o `catch` devolve `false` para tudo, `SafeSecureStorage.SetAsync` engole falhas, o ecrã usa teclado virtual e `Environment.Exit`, e o login offline não existe.
- Decisão D-02 deve estar confirmada (ver `00-global.md`); se não estiver, `BLOQUEIO:`.
- Anexos: nenhum.
Nota: sem credenciais, tokens nem connection strings.
</context>

<environment>
- PHC: edição Evolution (Ultimate), versão 2025.04. Cliente: Carnes Landeiro. Alojamento: servidor do cliente [CONFIRMAR].
- SQL Server: versão [CONFIRMAR]. Nível de compatibilidade da base real: assumir 100 [CONFIRMAR]. `LAND2_DEMO` está em 150 e não prova compatibilidade com 100.
- Bases: `TESTES_LAND2` (testes) e `LAND2_DEMO` (demo) são as únicas onde se pode escrever, depois de `SELECT DB_NAME();` e de confirmar o nome. `LAND2` (produção) é só leitura. A connection string vive no `appsettings` do projeto, preenchida pelo utilizador; nunca a escrevas em código, prompts, logs nem documentação.
- Podes criar: SPs (`sc_<Entidade><Acao>`, nunca `sp_`), views (`sc_v_<Nome>`) e triggers novos, só na base de testes e com confirmação por lote. Se o padrão do projeto for SQL inline com Dapper, segue o padrão (é o do LandeiroMaui).
- Nunca crias tabelas nem campos (nem CREATE TABLE, nem ALTER TABLE), nem alteras, desativas ou apagas triggers existentes (incluindo encriptados). Tabelas e campos novos: especifica-os (nome, tipo, tamanho, nulo, valor por defeito, regra) para o utilizador os criar no PHC. Índices, funções e tipos só com autorização explícita registada em <decisions>.
- Triggers existentes nas tabelas envolvidas: [a ler no levantamento da Parte 1].
- Projeto de código base: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (.NET 9, MAUI Blazor Hybrid, Dapper). Lê `CLAUDE.md` e `ARQUITETURA.md` desse projeto antes de escrever código e cumpre as suas regras. Nesse repositório não faças commits, branches nem push: só editas ficheiros.
- Telemóvel Android: modelos e versão mínima [CONFIRMAR, pendência 5]; Suposição: Android 8.0 (API 26) ou superior. Rede: Wi-Fi da fábrica ou VPN [CONFIRMAR, pendência 6]; o telemóvel só liga ao SQL Server dentro da rede interna ou por VPN.
- Máquina de desenvolvimento: Windows com Visual Studio e as cargas de trabalho .NET MAUI e Android (SDK, JDK, emulador) [CONFIRMAR]. Não instalas nada: se faltar alguma peça, listas o que falta e continuas o que não depende dela.
</environment>

<task>
Objetivo: pôr de pé a app Android e o acesso a ela (utilizador e password, PIN e sessão longa), pronta para a cache e a fila da task 04.
Âmbito:
- Fase 1, spike S0: provar a ligação SQL Server a partir do Android (`Microsoft.Data.SqlClient` ou alternativa suportada, conforme o levantamento). Só SELECT leve em `TESTES_LAND2`, depois de `SELECT DB_NAME()`.
- Fase 2, projeto: `ManutencaoMobile` (estrutura do LandeiroMaui: `Components/Pages/Manutencao/`, `Interfaces/`, `Services/`, `Models/`, `Helpers/`), `AndroidManifest` com as permissões estritamente necessárias (INTERNET, ACCESS_NETWORK_STATE; câmara e notificações só entram na task 05), ficheiros partilhados com o LandeiroMaui por ligação (como `LandeiroMaui.Testes`) e não por cópia (`Perfis`, `CrashLogService` ou equivalente sem a pasta SMB, modelos de manutenção); `appsettings` com campo da ligação vazio para o utilizador preencher; CSS móvel com tokens de marca (SigmaCode por defeito, trocável num só ficheiro).
- Fase 3, sessão: `SessaoMovelService` (adapta `UserStateService`: mesmos dados e `HasRole`, `Iniciais`; resultado de login tipado: Ok, CredenciaisInvalidas, SemRede, ErroServidor); `ArmazenamentoSeguro` estrito (lança ou devolve resultado em falhas de escrita; nunca o `SafeSecureStorage` tal como está para o PIN); `IFilaPendentes.ContarAsync` com implementação provisória.
- Fase 4, PIN: `PinService` (definir, validar, anular), `PinHasher` (PBKDF2-SHA256 com sal aleatório por PIN, iterações fixadas no desenho, comparação em tempo constante), política de bloqueio (`PIN_MAX_TENTATIVAS`, defeito 5, lido da cache se existir), contador persistente.
- Fase 5, ecrãs: login utilizador e password, definir PIN, desbloquear com PIN (teclado numérico na própria página ou nativo, conforme o desenho), landing `/manutencao`; entrada do módulo só com o role de manutenção (constante em `Perfis`, nome vindo de P1-T02).
- Fase 6, regras de sessão: sem expiração por tempo; PIN ao abrir e após X minutos em segundo plano (local, defeito 5); logout bloqueado com pendentes; ao haver rede, `RecarregarRolesAsync` adaptado, e utilizador desativado ou sem o perfil termina a sessão.
Fora de âmbito: cache SQLite e fila (task 04); lista, câmara e notificações (task 05); alterar o LandeiroMaui além do que a decisão D-02 permitir; guardar a password; PIN central no PHC.
Solução escolhida e porquê: reaproveitar a lógica de `UserStateService` e não o ecrã (o ecrã depende do posto); PIN local com hash PBKDF2 porque não exige campo novo (D-06).
</task>

<decisions>
- D-02 (atualizada 2026-11-06): a app é a GestorManutencao, já construída, que segue a base da LandeiroMaui (sem projeto novo `ManutencaoMobile`). Se for decidido o alvo Android dentro do LandeiroMaui, o prompt muda e volta ao desenho.
- D-03: acesso a dados como no LandeiroMaui. D-10 (a fechar em P2-T02): credencial de sincronização; esta task só implementa o que o desenho fixar e esconde o acesso atrás de interfaces.
- D-06 (atualizada): o PIN vive no PHC em `u_manutil` (pinhash, pinprov, versao, ativo; campo novo `tecno` = nº PHC do técnico, por defeito 0, NOVO: campo a criar por pessoa no PHC).
- D-09: cores da marca SigmaCode por defeito, em tokens CSS num ficheiro único.
</decisions>

<open-points>
| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 5 | Modelos de telemóvel e versão mínima | Landeiro | sim: API mínima do manifesto |
| 10 | Nome do role do módulo (constante em `Perfis`) | P1-T02 / Landeiro | sim: entrada no módulo |
| 12 | D-02 e D-10 confirmados | Utilizador | sim: arranque |
| N4 | Como detetar utilizador desativado sem a password | P2-T01 / P1-T01 | não (fica a regra "perfil removido") |
</open-points>

<rules>
### Regras comuns
- Segue os padrões que já existem no repositório. Prefere a solução mais simples que seja correta. Não introduzas dependências sem justificação (regista em `docs/DEPENDENCIES.md`: nome, versão, para quê, alternativa, licença) e confirma a documentação oficial atual antes de usar uma biblioteca.
- Estrutura por módulo (padrão do LandeiroMaui): `Components/Pages/Manutencao/`, `Interfaces/Manutencao/IXxxService.cs`, `Services/Manutencao/XxxService.cs`, `Models/Manutencao/`. Registo manual de cada serviço em `MauiProgram.cs`. DTOs/modelos de ecrã separados dos modelos de dados quando atravessam camadas.
- Dados: Dapper cru, uma `SqlConnection` por método a partir de `DbConfig.ConnectionString`, parâmetros nomeados, `WITH (NOLOCK)` em todo o SELECT/JOIN a tabelas base (não em CTEs nem alvos de UPDATE/DELETE). Nunca `UPDLOCK`, `HOLDLOCK`, `XLOCK`, `TABLOCK`, `ROWLOCK`, `SERIALIZABLE`, `READPAST` nem transações longas com SELECT dentro. Não inventes filtros SQL: só as condições pedidas.
- Compatibilidade SQL nível 100: sem STRING_SPLIT, OPENJSON, JSON_VALUE, TVP nem tipos de tabela; listas como XML com `.nodes()` e `.value()`. TRY_CONVERT, IIF, CONCAT, FORMAT, OFFSET…FETCH, THROW, TRY_CAST e STRING_AGG só depois de confirmados numa base de teste em nível 100. SQL dentro de `u_sqlexec` (xbase): comentários só `/* */`.
- Erros nunca em silêncio: em cada `catch` de um fluxo que grava, imprime, lê código ou sincroniza, `CrashLogService.Registar(ex, "Classe.Metodo (contexto)")` e aviso ao operador. Nada de `catch { }`, `Console.WriteLine` nem `Debug.WriteLine` para erros.
- Acesso: entrada do ecrã no menu dentro de `@if (UserStateService.HasRole(Perfis.X))`, nunca guarda "Acesso negado" dentro da página. Roles novos = constante em `Helpers/Perfis`.
- `usrinis`/`ousrinis` = iniciais do utilizador autenticado (`UserStateService.Iniciais`).
- Compilação sem avisos (Nullable ligado): propriedades `string` = `""`, listas = `new()`, sem `!` nem `null!`. Compilar e correr só quando a base ativa for de testes; em produção só editas ficheiros.
- Regras novas em helpers levam teste xUnit em `LandeiroMaui.Testes`.
- Comentários o mínimo possível, sempre em português: só onde o código não se explica sozinho.
- Segurança: sem credenciais em prompts, código, logs ou documentação; consultas parametrizadas; mínimo de permissões. Dados pessoais só o necessário (RGPD). Antes de entregar corre `C:\_SigmaRepos\Claude-The-ALLKNOWING\tools\verificar-segredos.ps1 -Caminho <pasta>`.
- Base de dados: só as ações permitidas em <environment>. Antes de pedir confirmação para instalar scripts, faz a verificação textual (sem CREATE TABLE exceto temporárias, ALTER TABLE, DROP TABLE, TRUNCATE, SELECT INTO para tabela permanente, sp_settriggerorder) e põe a guarda de base de dados no início de cada script. Uma confirmação por lote.
- Triggers: só novos, leves, seguros para várias linhas, e que nunca bloqueiam a gravação no PHC.
- Git: branch próprio; sem commit nem push sem confirmação (no LandeiroMaui nunca).
- Revisão crítica da tua implementação antes de entregar.
### Regras específicas desta task
- O PIN nunca se guarda em claro, nem em log, nem no SQLite; só hash, sal, iterações, contador e hora de desbloqueio, no armazenamento seguro. Falha ao escrever ou ler o armazenamento seguro: `CrashLogService.Registar` e aviso, e o PIN não fica ativo.
- Mensagens ao operador curtas e sem termos técnicos; erros de rede e credenciais nunca se confundem.
- Teclado nativo (nada de `Teclado` virtual do LandeiroMaui nem `Environment.Exit`); campos de utilizador e password com `autocomplete` adequado.
- Dependências novas (qualquer pacote) só com registo em `docs/DEPENDENCIES.md` e documentação oficial atual consultada.
- Compilar o alvo Android e o do LandeiroMaui (Windows) para provar que o segundo não foi afetado, sem alterar nenhum ficheiro do LandeiroMaui sem decisão registada.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. xUnit (projeto `ManutencaoMobile.Testes`, ficheiros ligados): `PinHasher` (hash diferente por sal, validação certa e errada, comparação em tempo constante); política de bloqueio (4 falhas, 5.ª bloqueia, contador persiste após "reinício", sucesso zera); classificação do erro de login (credenciais, sem rede, servidor); regra "perfil removido termina a sessão".
2. Manual no telemóvel (roteiro escrito; declarar não corrido se faltar aparelho): primeiro login, definir PIN, fechar e abrir, 5 PINs errados, login sem rede com e sem sessão, logout com pendentes, perfil removido.
3. Compilação do alvo Android e do alvo Windows do LandeiroMaui sem avisos novos.
4. `verificar-segredos.ps1` sobre a pasta do projeto; APK sem connection string em claro (confirma onde fica e regista o risco R-03).
5. Spike S0: resultado registado com data, base e o que se mediu (tempo de abertura da ligação).
</tests>

<output-format>
Responde por esta ordem, em português de Portugal: ANÁLISE, PLANO, IMPLEMENTAÇÃO, REVISÃO, TESTES, RISCOS e, no fim, ESTADO DA TASK e AUTOCRÍTICA (3 linhas factuais: o que correu bem, o que correu mal, o que farias diferente).
- ANÁLISE: o que encontraste.
- PLANO: passos e ficheiros a alterar. Em Plan Mode, para aqui e espera a minha confirmação.
- IMPLEMENTAÇÃO: o que foi feito, por fase.
- REVISÃO: crítica da tua própria implementação.
- TESTES: o que correu, os resultados e as medições; o que não correu e porquê. Nunca digas que passou sem o verificares.
- RISCOS: riscos e impactos, e o que fica pendente.
- ESTADO DA TASK: (1) task e estado (EM_CURSO, BLOQUEADA ou pronta para gate); (2) critérios cumpridos (n de n) e os que faltam; (3) pendências novas, com dono e se bloqueiam; (4) qual é a task seguinte (só arranca depois da confirmação do gate).
Em tasks sem código (levantamento, desenho), IMPLEMENTAÇÃO é o que foi produzido (documentos) e TESTES é a verificação do que foi produzido. Nunca omitas uma secção: escreve "não aplicável" e porquê. No fim junta `MELHORIA: <...>` (ou `MELHORIA: nenhuma`).
</output-format>
