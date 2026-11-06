# Desenvolvimento do login da app (login só por PIN contra `u_manutil`, técnico PHC, sessão)
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 03 (P2-T03) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO e espera a minha confirmação; depois autónomo até ao GATE)
Papel do agente: desenvolvedor (implementa o que está no prompt, sem análises nem relatórios além de uma linha de contexto)
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 2: a app GestorManutencao já está construída com login só por PIN (`u_manutil`, campo novo `tecno`), perfis operador e supervisor; o técnico é o utilizador PHC (`us.userno`) gravado em `tecno`/`tecnome`. Login SQL por utilizador e PIN local (D-03/D-06/D-10 antigos) deixam de valer. Ecrãs Planeadas e Material usado na ficha estão em construção.
Entradas: `docs/manutencao/p2/levantamento.md`, `docs/manutencao/p2/desenho.md` (secções de login e PIN); contratos de P1-T02; a app GestorManutencao já construída (login por PIN, `u_manutil`)
Saídas: confirmação e, se faltar, completamento do login por PIN, do campo `tecno` e da sessão na app GestorManutencao, com testes xUnit; `docs/DEPENDENCIES.md` se houver dependências novas; `docs/tasks/00-global.md` atualizado
Critérios de conclusão:
1. Ligação confirmada: a app lê `u_manutil` em `TESTES_LAND2` pelo caminho da arquitetura em vigor (SELECT leve, depois de `SELECT DB_NAME()`) a partir do emulador ou do telemóvel, e o resultado fica no relatório; se falhar, `BLOQUEIO:` com 2 a 4 propostas. O spike S0 do desenho antigo só é preciso se o telemóvel ligar diretamente ao SQL Server (por confirmar; a app tem uma API).
2. A app GestorManutencao (já construída; não se cria `ManutencaoMobile`) compila sem avisos e arranca; o LandeiroMaui só entra se for tocado (R-06).
3. Login só por PIN contra `u_manutil` (utilizador, nome, perfil, pinhash, ativo, pinprov, versao, marcada), a distinguir "PIN inválido", "utilizador inativo ou bloqueado" e "sem rede"; sem rede e sem sessão guardada, mensagem clara e nada de login falso. Não há login SQL por utilizador nem password na app. O campo NOVO `tecno` (nº PHC do técnico, numérico, por defeito 0) é lido para a sessão e gravado em `u_fichaint.tecno`/`tecnome` ao iniciar (só se `tecno` = 0 na ficha).
4. PIN: 1.º acesso com PIN provisório (`pinprov`) e definição do PIN definitivo, desbloqueio, bloqueio ao atingir o máximo de tentativas (`u_configman.pinmaxtent`), contador persistente; o PIN nunca em claro, só `pinhash` (esquema de hash e sal da app existente: confirmar); falhas de escrita e leitura nunca em silêncio.
5. Sessão longa e logout: sem expiração por tempo; PIN ao abrir e após X minutos em segundo plano; logout com pendentes bloqueado (a fila é da task 04: usar a interface `IFilaPendentes` com implementação provisória); perfil (operador ou supervisor) e `ativo` recarregados em `u_manutil` quando há rede; testes xUnit a passar e o roteiro manual no telemóvel escrito (declarado não corrido se não houver aparelho).
Gate: manual
Agentes: `sc-dev` (implementação), `sc-revisor` (revisão obrigatória: segurança)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid (Android), SQLite, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto e cliente: Gestor de Manutenção da Carnes Landeiro. Passa o circuito de manutenção (hoje papel, Word e PHC) para a plataforma .NET MAUI da Landeiro. Esta é a Parte 2: app de telemóvel para registo de intervenções (GestorManutencao, já construída). A Parte 1 (backoffice Windows, dados e regras no PHC) e a Parte 3 (peças e armazém) correm em paralelo.
- Repositórios: `C:\_SigmaRepos\Landeiro-Desmancha` (código; `LandeiroMaui` é a base) e `C:\_SigmaRepos\GestorManutencao` (planeamento); branch `dev-landeiro-manutencao-p2`. Documentos desta parte em `docs/manutencao/p2/` do repositório de código.
- Requisitos da Parte 2:
  - APP-01 Login só por PIN (tabela PHC `u_manutil`; app GestorManutencao já construída; o login SQL por utilizador do LandeiroMaui não é a base); a sessão mantém-se o máximo de tempo possível; o técnico é o utilizador PHC (`us.userno`) em `tecno`/`tecnome`.
  - APP-02 **Obrigatório trabalhar offline**: cache local SQLite, fila de saída e sincronização.
  - APP-03 Alerta de intervenções planeadas com a antecedência definida no backoffice (funciona offline).
  - APP-04 Lista de planeadas e pendentes, com botão "+" para criar.
  - APP-05 Ecrã simples e prático para criar a intervenção: equipamento, preventiva ou curativa, tempos, técnico, descrição, foto.
  - APP-06 Avançar intervenções fora de ordem só se a configuração do backoffice o permitir.
  - APP-07 Sincronização sem duplicados e com resolução de conflitos (ganha a validação do backoffice).
  - APP-08 Peças usadas na intervenção (liga à Parte 3, 2.ª fase).
- Estados da intervenção (`u_fichaint.estado`, texto): Planeada → EmCurso → Concluida → Validada, mais Cancelada; vazio = legado. Atrasada e A vencer são derivados, nunca gravados. Estado inicial da curativa criada em Registar avaria: por confirmar no código da LandeiroMaui (o planeamento antigo assumia EmCurso). A supervisão pode devolver (Concluida para EmCurso, motivo em `valobs`) ou cancelar (Planeada/EmCurso para Cancelada). A ficha só fica Fechada quando validada.
- Esboço do quadro (reunião de 04/11/2026): ecrã de lista com botão "+" no canto superior direito, que abre o ecrã "Intr." com uma linha de campos de tempo e pessoa, uma caixa de texto, uma foto e um botão de ação. Nota ao lado: "configuração no BO que defina se pode avançar ordem intervenções". "Obrigatório trabalhar" (offline).
- Prazos: protótipo clicável a 11/11/2026; 17/11/2026 (Suposição) primeiras intervenções. Marcos propostos (Suposição): M0 protótipo 11/11; M1 login e lista online; M2 offline; M3 alertas e fotos.
- Contexto do LandeiroMaui (NÃO é o login da app; fica só como referência do que não se reaproveita): o login do LandeiroMaui é um login SQL do próprio utilizador (`UserStateService.LoginBDAsync`: `SqlConnectionStringBuilder` com `UserID` e `Password`, `OpenAsync`, depois `LoadUser` em `us` e `LoadUserRoles` em `pf`/`pfu` com `left(resumo,4)='DES_'`), guarda `username`, `userno`, `usernome`, `iniciais` e `roles` em `SafeSecureStorage` e nunca a password. Por isso não serve ao telemóvel: o `catch` devolve `false` para tudo, `SafeSecureStorage.SetAsync` engole falhas, o ecrã usa teclado virtual e `Environment.Exit`, e o login offline não existe. A app GestorManutencao usa em vez disso o PIN contra `u_manutil`.
- Decisão D-02 (atualizada 2026-11-06) já está fixada: a app é a GestorManutencao.
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
Objetivo: confirmar e completar o acesso à app GestorManutencao (PIN, técnico PHC, sessão longa), pronta para a cache e a fila da task 04.
Âmbito:
- Fase 1, ligação: confirmar como o telemóvel acede aos dados (API da app ou ligação direta, por confirmar) e provar um SELECT leve em `u_manutil` na base de testes, depois de `SELECT DB_NAME()`. O spike S0 do desenho antigo só se aplica à ligação direta.
- Fase 2, confirmar a app: estrutura, `appsettings` com o campo da ligação vazio para o utilizador preencher, permissões estritamente necessárias (rede; câmara e notificações só entram na task 05), CSS móvel com tokens de marca (SigmaCode por defeito, trocável num só ficheiro). Não se cria `ManutencaoMobile`.
- Fase 3, técnico e sessão: ler `tecno` (campo NOVO em `u_manutil`, por defeito 0) para a sessão; ao iniciar uma ficha gravar `tecno`/`tecnome` só se `tecno` da ficha = 0; resultado de login tipado (Ok, PinInvalido, Bloqueado, Inativo, SemRede, ErroServidor); armazenamento seguro estrito (lança ou devolve resultado em falhas de escrita); `IFilaPendentes.ContarAsync` com implementação provisória.
- Fase 4, PIN: confirmar `PinService` e hash contra a tabela `u_manutil` (pinhash, pinprov, versao, ativo, marcada), PIN provisório no 1.º acesso, política de bloqueio com `u_configman.pinmaxtent`, contador persistente. Corrige só o que falhar os testes.
- Fase 5, ecrãs: login por PIN, definir PIN, desbloquear (teclado numérico na própria página ou nativo, conforme o desenho), landing por perfil (operador e supervisor).
- Fase 6, regras de sessão: sem expiração por tempo; PIN ao abrir e após X minutos em segundo plano (local, defeito 5); logout bloqueado com pendentes; ao haver rede, recarregar perfil e `ativo`; utilizador inativo ou sem perfil termina a sessão.
Fora de âmbito: cache SQLite e fila (task 04); lista, câmara e notificações (task 05); alterar o LandeiroMaui; login SQL por utilizador; guardar password; PIN só local sem `u_manutil` (modelo antigo).
Solução escolhida e porquê: manter o login por PIN que a app já tem (tabela `u_manutil` no PHC) e só acrescentar `tecno`, para o técnico ser o utilizador PHC sem pedir password ao telemóvel.
</task>

<decisions>
- D-02 (atualizada 2026-11-06): a app é a GestorManutencao, já construída, que segue a base da LandeiroMaui (sem projeto novo `ManutencaoMobile`). 
- D-03 (atualizada): acesso a dados como na LandeiroMaui (Dapper direto ao SQL Server) ou pela API da app, conforme a arquitetura em vigor (por confirmar). D-10 antiga (login SQL por utilizador, password no cofre do Android) deixou de valer; esta task esconde o acesso atrás de interfaces.
- D-06 (atualizada): o PIN vive no PHC em `u_manutil` (pinhash, pinprov, versao, ativo; campo novo `tecno` = nº PHC do técnico, por defeito 0, NOVO: campo a criar por pessoa no PHC).
- D-09: cores da marca SigmaCode por defeito, em tokens CSS num ficheiro único.
</decisions>

<open-points>
| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 5 | Modelos de telemóvel e versão mínima | Landeiro | sim: API mínima do manifesto |
| 10 | Perfis do módulo (operador e supervisor; a base não os controla) | P1-T02 / Landeiro | não |
| 12 | Como o telemóvel acede aos dados (API ou direto) | Utilizador | sim: ligação |
| N4 | Utilizador desativado: resolvido por `u_manutil.ativo` (confirmar na leitura) | P2-T01 / P1-T01 | não |
</open-points>

<rules>
### Regras comuns
- Segue os padrões que já existem no repositório. Prefere a solução mais simples que seja correta. Não introduzas dependências sem justificação (regista em `docs/DEPENDENCIES.md`: nome, versão, para quê, alternativa, licença) e confirma a documentação oficial atual antes de usar uma biblioteca.
- Estrutura por módulo (padrão do LandeiroMaui): `Components/Pages/Manutencao/`, `Interfaces/Manutencao/IXxxService.cs`, `Services/Manutencao/XxxService.cs`, `Models/Manutencao/`. Registo manual de cada serviço em `MauiProgram.cs`. DTOs/modelos de ecrã separados dos modelos de dados quando atravessam camadas.
- Dados: Dapper cru, uma `SqlConnection` por método a partir de `DbConfig.ConnectionString`, parâmetros nomeados, `WITH (NOLOCK)` em todo o SELECT/JOIN a tabelas base (não em CTEs nem alvos de UPDATE/DELETE). Nunca `UPDLOCK`, `HOLDLOCK`, `XLOCK`, `TABLOCK`, `ROWLOCK`, `SERIALIZABLE`, `READPAST` nem transações longas com SELECT dentro. Não inventes filtros SQL: só as condições pedidas.
- Compatibilidade SQL nível 100: sem STRING_SPLIT, OPENJSON, JSON_VALUE, TVP nem tipos de tabela; listas como XML com `.nodes()` e `.value()`. TRY_CONVERT, IIF, CONCAT, FORMAT, OFFSET…FETCH, THROW, TRY_CAST e STRING_AGG só depois de confirmados numa base de teste em nível 100. SQL dentro de `u_sqlexec` (xbase): comentários só `/* */`.
- Erros nunca em silêncio: em cada `catch` de um fluxo que grava, imprime, lê código ou sincroniza, `CrashLogService.Registar(ex, "Classe.Metodo (contexto)")` e aviso ao operador. Nada de `catch { }`, `Console.WriteLine` nem `Debug.WriteLine` para erros.
- Acesso: na app GestorManutencao os perfis são operador e supervisor (`u_manutil.perfil`); a entrada de cada ecrã segue o perfil e nunca há guarda "Acesso negado" dentro da página. A base da LandeiroMaui não controla perfis de manutenção.
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
- O PIN nunca se guarda em claro, nem em log, nem no SQLite; só o hash (`pinhash`) em `u_manutil` e, no telemóvel, o que o desenho fixar (contador e hora de desbloqueio) no armazenamento seguro. Falha ao escrever ou ler: registo de erro e aviso, e o PIN não fica ativo.
- Mensagens ao operador curtas e sem termos técnicos; erros de rede e PIN inválido nunca se confundem.
- Teclado nativo (nada de `Teclado` virtual do LandeiroMaui nem `Environment.Exit`); o campo do PIN numérico com o teclado adequado.
- Dependências novas (qualquer pacote) só com registo em `docs/DEPENDENCIES.md` e documentação oficial atual consultada.
- Compilar a app GestorManutencao sem avisos novos; o LandeiroMaui não se altera sem decisão registada.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. xUnit (testes existentes da app GestorManutencao; acrescenta só o que faltar): hash do PIN (validação certa e errada, comparação em tempo constante); política de bloqueio com `pinmaxtent` (falhas até ao máximo, bloqueia, contador persiste após "reinício", sucesso zera); classificação do erro de login (PIN inválido, bloqueado, inativo, sem rede, servidor); `tecno` lido para a sessão e gravado na ficha só se `tecno` da ficha = 0; regra "inativo ou perfil removido termina a sessão".
2. Manual no telemóvel (roteiro escrito; declarar não corrido se faltar aparelho): primeiro acesso com PIN provisório, definir PIN, fechar e abrir, PINs errados até ao bloqueio, login sem rede com e sem sessão, logout com pendentes, utilizador inativo.
3. Compilação da app GestorManutencao sem avisos novos.
4. `verificar-segredos.ps1` sobre a pasta do projeto; o pacote da app sem connection string em claro (confirma onde fica e regista o risco R-03).
5. Ligação: resultado registado com data, base e o que se mediu (tempo de abertura da ligação).
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
