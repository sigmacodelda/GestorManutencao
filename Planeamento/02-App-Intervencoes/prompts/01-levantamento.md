# Levantamento da app de intervenções (LandeiroMaui, Android, dispositivos, offline)
Versão 1.0 · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 01 (P2-T01) · Tipo: levantamento
Task global: `docs/tasks/00-global.md`
Modo: Autónomo (só leitura; para no GATE)
Papel do agente: analista (lê e conclui com evidência; só escreve `docs/manutencao/p2/levantamento.md` e o `00-global.md`; não escreve código nem altera o LandeiroMaui)
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 2: a app GestorManutencao já está construída com login só por PIN (`u_manutil`, campo novo `tecno`), perfis operador e supervisor; o técnico é o utilizador PHC (`us.userno`) gravado em `tecno`/`tecnome`. Login SQL por utilizador e PIN local (D-03/D-06/D-10 antigos) deixam de valer. Ecrãs Planeadas e Material usado na ficha estão em construção.
Entradas: no LandeiroMaui (`C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui`): `Components/Pages/Login/Login.razor`, `Services/UserStateService.cs`, `Services/SafeSecureStorage.cs`, `CLAUDE.md`, `ARQUITETURA.md` (secção 3), `docs/plano-modo-offline-cache.md`, `LandeiroMaui.csproj` (só as propriedades de alvo e pacotes), `Platforms/Android/`, `Helpers/Perfis.cs`, `Helpers/FalhaServidorHelper.cs`, `LandeiroMaui.Testes/LandeiroMaui.Testes.csproj`. Nunca leias `appsettings.json`, `bin/` nem `obj/`.
Saídas: `docs/manutencao/p2/levantamento.md`; `docs/tasks/00-global.md` atualizado
Critérios de conclusão:
1. Tabela reaproveitável / adaptar / não serve, com evidência (ficheiro e símbolo) para: `Login.razor`, `UserStateService`, `SafeSecureStorage`, `CrashLogService`, `DbConfig` e `EncriptacaoHelper`, `Perfis`, UI partilhada (css, `ModalBase`, `Numpad`, teclado virtual), plano offline, ZXing.Net, `sqlite-net-pcl` e `LandeiroMaui.Testes`.
2. Estado real do alvo Android no LandeiroMaui e comparação da decisão D-02 (alvo Android no LandeiroMaui vs projeto novo `ManutencaoMobile`) com recomendação, impacto no build e na publicação dos postos e no risco R-06.
3. Lista de permissões Android e de dependências candidatas (câmara, notificações locais, SQLite), com versões e documentação oficial atual consultadas; o que não foi possível confirmar fica marcado "Suposição:".
4. Análise de segurança e rede (R-03): que credencial usam os serviços depois do login, o que ficaria dentro do APK, suporte do `Microsoft.Data.SqlClient` em Android, e as opções de credencial com recomendação.
5. Lista única de perguntas e pendências (dispositivos, rede, perfis, desativação de utilizador, filtro das planeadas, fotos, distribuição do APK), cada uma com dono e se bloqueia, e o GATE.
Gate: manual
Agentes: `sc-leitor`

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
- Situação atual e problema: o LandeiroMaui só compila para Windows (`net9.0-windows10.0.19041.0`), foi pensado para postos táteis 1024x768 com teclado virtual, e o alvo Android foi retirado (restam vestígios). A app de telemóvel precisa de alvo Android, ecrã vertical e teclado nativo. O plano de modo offline do LandeiroMaui está só em documento: não há `LocalDb`, `FilaSaida` nem `LigacaoEstadoService` no código (verificado em 05/11/2026; confirma de novo).
- Factos já lidos (confirma-os, não os dês como certos): `LoginBDAsync` abre uma ligação com o utilizador e password SQL do próprio utilizador e depois lê `us` e os perfis `DES_*` (`pf`/`pfu`); o `catch` devolve `false` sem distinguir rede de credenciais inválidas; `RecarregarRolesAsync` usa a cadeia de ligação da app; o login não guarda a password; `SafeSecureStorage.SetAsync` engole falhas de escrita; o plano offline diz "login offline" fora de âmbito.
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
Objetivo: dizer, com evidência do código, o que se reaproveita do LandeiroMaui para a app de telemóvel, o que não serve, e que decisões e dados faltam antes de desenhar.
Âmbito:
- Fase A, código: tabela dos critérios 1 e 2; para cada item diz o que se leva tal como está, o que se adapta (e o quê) e o que se abandona.
- Fase B, plataforma: Android (permissões, versão mínima, notificações locais, câmara, bateria e Doze, distribuição e atualização do APK sem Google Play, hipótese de partilha de bibliotecas por ficheiros ligados como em `LandeiroMaui.Testes`), e como o plano offline existente se aplica: para cada conceito (`LigacaoEstadoService`, `LocalDb`, cache incremental, pares anti-duplicação, `fila_saida`, decisão D7 do logout, "login offline fora de âmbito") diz se aplica, o ajuste, e porquê (aqui não há "par": a unidade é a intervenção com os seus movimentos e fotos).
- Fase C, segurança e rede: R-03, a credencial, `SqlClient` em Android, rede interna ou VPN.
- Fase D, perguntas: tudo o que falta ao utilizador e à Landeiro, numa só lista, com a opção recomendada por pergunta.
Fora de âmbito: escrever código, editar o LandeiroMaui, ler a base de dados (não é preciso nesta task), decidir D-02 (propões; o utilizador decide), instalar workloads ou SDKs (só `dotnet workload list` e `dotnet --list-sdks`, se quiseres saber o que existe).
Solução escolhida e porquê: levantamento por leitura de excertos (Grep, Read com intervalo), delegando a leitura larga ao `sc-leitor`, para não gastar tokens do modelo principal.
</task>

<decisions>
- D-01: as 3 partes correm em paralelo; esta task não depende de nenhuma outra.
- D-02 (atualizada 2026-11-06): a app é a GestorManutencao, já construída, que segue a base da LandeiroMaui (sem projeto novo `ManutencaoMobile`). Alternativa: alvo Android dentro do LandeiroMaui, com mais risco para os postos em produção.
- D-03 (atualizada): acesso a dados como a LandeiroMaui (Dapper direto ao SQL Server); login da app GestorManutencao só por PIN contra a tabela PHC `u_manutil` (não login SQL por utilizador). Alternativa (API intermédia) fica como opção se a segurança o exigir.
- D-06 (atualizada): o PIN vive no PHC em `u_manutil` (pinhash, pinprov, versao, ativo; campo novo `tecno` = nº PHC do técnico, por defeito 0, NOVO: campo a criar por pessoa no PHC).
- D-09: a marca do documento é SigmaCode; a app só usa as cores do LandeiroMaui se o pedido disser "Cliente: Landeiro, usar cores do cliente".
</decisions>

<open-points>
| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 5 | Modelos de telemóvel (Android? versão mínima, câmara) | Landeiro | sim: P2-T02 |
| 6 | Wi-Fi cobre todas as zonas? VPN? | Landeiro (informática) | não |
| 10 | Perfis (roles) do módulo: operador, supervisor, administrador | Landeiro | sim: P1-T02 |
| 12 | Confirmar D-02 | Utilizador | sim: P2-T02 |
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
- Só leitura: não alteres nada fora de `docs/manutencao/p2/levantamento.md` e `docs/tasks/00-global.md`. Em cada afirmação sobre o código indica o ficheiro e o símbolo; o que vem de memória ou de inferência marca "Suposição:".
- Documentação oficial: antes de afirmares versões mínimas, permissões ou suporte de bibliotecas em Android, consulta a documentação atual (Microsoft Learn, repositórios oficiais) e cita a fonte. Se não conseguires consultar, diz que não consultaste.
- Não repitas valores de configuração nem trechos de segredos; do `.csproj` só os alvos e os pacotes.
- Se uma conclusão contrariar uma decisão já tomada (por exemplo D-03), escreve "isto está mal, é melhor assim" com as razões e pára nesse ponto com 2 a 4 opções (a recomendada primeiro); continua o resto.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. Cada linha da tabela do critério 1 tem ficheiro e símbolo verificáveis (reabre 3 ao acaso e confirma).
2. Confirmaste, com Grep, se existem `LocalDb`, `FilaSaida`, `LigacaoEstadoService` ou `SQLiteAsyncConnection` no código (resultado escrito no documento).
3. O documento não contém valores de `appsettings`, credenciais nem IPs; corre `verificar-segredos.ps1` sobre `docs/manutencao/p2`.
4. Cada pergunta do critério 5 tem dono, opção recomendada e "bloqueia: sim/não".
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
