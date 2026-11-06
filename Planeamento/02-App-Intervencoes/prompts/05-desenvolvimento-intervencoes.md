# Desenvolvimento das intervenções na app (lista, alerta, criar, foto, avançar ordem)
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 05 (P2-T05) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO e espera a minha confirmação; depois autónomo até ao GATE)
Papel do agente: desenvolvedor (implementa o que está no prompt, sem análises nem relatórios além de uma linha de contexto)
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 2: a app GestorManutencao já está construída com login só por PIN (`u_manutil`, campo novo `tecno`), perfis operador e supervisor; o técnico é o utilizador PHC (`us.userno`) gravado em `tecno`/`tecnome`. Login SQL por utilizador e PIN local (D-03/D-06/D-10 antigos) deixam de valer. Ecrãs Planeadas e Material usado na ficha estão em construção.
Entradas: `docs/manutencao/p2/desenho.md` (ecrãs, regras de ordem, notificações); código de P2-T03 e P2-T04; `IIntervencaoService` e `IManutencaoConfigService` da Parte 1
Saídas: ecrãs `Components/Pages/Manutencao/`, serviços e helpers de regras com testes xUnit; `docs/DEPENDENCIES.md`; `docs/tasks/00-global.md` atualizado
Critérios de conclusão:
1. Lista (APP-04): planeadas, em curso, atrasadas, devolvidas e por enviar, com filtro simples, estado de sincronização por registo e botão "+" no canto superior direito; abre em menos de 2 s com cache.
2. Criar intervenção (APP-05): equipamento (pesquisa na cache), preventiva ou curativa, hora de início e fim, técnico (por defeito o utilizador), anomalia, intervenção feita, limpeza do local, foto da câmara; funciona offline e grava na fila; validações claras.
3. Iniciar e concluir uma planeada (a curativa nasce em curso); avançar ordem (APP-06) conforme a configuração da cache, com cadeado e razão para as bloqueadas; curativas nunca bloqueadas.
4. Alerta (APP-03): notificações locais agendadas a partir da cache com a antecedência configurada, reagendadas após sincronizar, reiniciar o telemóvel e mudar a configuração; pedido de permissão de notificações tratado; funciona sem rede.
5. Fotos: captura, compressão (limites do desenho), pasta privada, envio pela fila com a intervenção; permissão de câmara negada tratada sem bloquear o registo.
6. Ponto de ligação à Parte 3 (APP-08): secção "Peças usadas" atrás de uma interface (`IPecasIntervencao`) com implementação vazia e a funcionalidade desligada até à task P3-T05; testes xUnit a passar e roteiro manual no telemóvel escrito (declarado não corrido se não houver aparelho).
Gate: manual
Agentes: `sc-dev` (implementação), `sc-revisor` (revisão obrigatória: mais de 5 ficheiros)

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
- Situação atual e problema: depois das tasks 03 (sessão e PIN) e 04 (cache e fila) falta o que o operador vê e usa. O desenho (P2-T02) fixa os ecrãs; o protótipo clicável aprovado em 11/11 é a referência visual.
- Interpretação do esboço do quadro (Suposição, a validar na reunião): linha de campos do ecrã "Intr." = hora de início, técnico e hora de fim; caixa de texto = anomalia e intervenção; botão de ação = guardar e concluir.
- Regra de ordem (Suposição, a confirmar com a Landeiro): as planeadas ordenam-se por data planeada e depois por `u_ordem`; sem permissão de avançar, só se inicia a primeira por executar.
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
Objetivo: o operador consegue ver o que está planeado, ser avisado, criar e concluir intervenções, com foto, mesmo sem rede.
Âmbito:
- Fase 1, regras puras com testes primeiro: `RegraOrdemHelper` (quais as planeadas iniciáveis dado o perfil e a configuração), `PlaneadorAlertas` (dada a lista, a antecedência, a hora local e "agora", devolve a lista de notificações a agendar; função pura), `ValidacaoIntervencao` (hora fim não anterior à de início, campos obrigatórios por tipo, limite de fotos).
- Fase 2, lista e filtros sobre a cache; faixa de alerta no topo ("N planeadas nos próximos D dias"); marca de estado; cadeado nas bloqueadas.
- Fase 3, criar e editar: ecrã do esboço, equipamento por pesquisa na cache, tipo, tempos (com atalho "agora"), técnico, textos, limpeza, foto; guardar rascunho e concluir; curativa e planeada iniciada geram operações na fila (task 04); stamp gerado no telemóvel.
- Fase 4, foto: `MediaPicker` do MAUI Essentials, compressão e redimensionamento, pasta privada, miniatura, operação `AnexarFoto` dependente da intervenção; limpeza dos ficheiros depois de enviados e com a purga.
- Fase 5, alertas: serviço de notificações locais (biblioteca a justificar em `docs/DEPENDENCIES.md`, com documentação oficial atual e compatibilidade com .NET 9 verificada), permissão `POST_NOTIFICATIONS`, canal de notificação, reagendar nos eventos do desenho, abrir a intervenção ao tocar na notificação.
- Fase 6, ligação à Parte 3: interface `IPecasIntervencao` e secção "Peças usadas" escondida por interruptor.
Fora de âmbito: criar ou validar a ficha no backoffice (Parte 1); lista de peças, leitura de código de barras e stock (Parte 3, task P3-T05); sincronização com a app fechada; iOS.
Solução escolhida e porquê: regras puras em helpers com testes (as de ordem e de alertas são as que mais falham em campo), ecrãs finos sobre os serviços da task 04.
</task>

<decisions>
- D-05: planeadas são fichas com estado Planeada; a app só as inicia e conclui. A curativa nasce EmCurso no telemóvel.
- D-06: PIN local (task 03). D-09: marca SigmaCode por defeito, em tokens CSS; só muda com "Cliente: Landeiro, usar cores do cliente".
- Notificações: a partir da cache, sem rede; alarmes exatos não são necessários (Suposição: hora de aviso 08:00, uma por dia enquanto atrasada; ajustável no desenho).
- Conflitos e idempotência: as da task 04, não se reabrem aqui.
</decisions>

<open-points>
| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 10 | Perfis e a chave `AVANCAR_ORDEM_<ROLE>` | Landeiro / P1-T02 | sim: critério 3 |
| N2 | Filtro das planeadas (todas, por zona ou por técnico) | Landeiro | sim: critério 1 |
| N3 | Onde ficam as fotos | P1-T01 e P1-T02 | sim: critério 5 (envio) |
| 5 | Modelos de telemóvel (câmara, versão) | Landeiro | não (testes no que houver) |
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
- Interface móvel: alvos de toque de pelo menos 48 dp, texto de pelo menos 16 px, uma coluna, teclado nativo, estados de carregamento e vazio, sem scroll horizontal, margens de segurança do ecrã; mensagens em português simples. Usar `Virtualize` nas listas longas.
- Nunca perder o que o operador escreveu: rascunho local automático ao sair do ecrã ou se a app for morta.
- Permissões (câmara, notificações): pedir só quando se usa, explicar porquê, e nunca bloquear o registo se forem negadas (aviso e alternativa).
- Fotos sem pessoas quando possível (aviso curto no ecrã); limites de tamanho e número do desenho.
- Hora local do telemóvel só para apresentação; guardar datas de forma consistente com a Parte 1 (a definir em P1-T02; nunca assumir fuso).
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. xUnit `RegraOrdemHelper`: com permissão (todas iniciáveis), sem permissão (só a primeira), curativa nunca bloqueada, concluída a primeira liberta a seguinte, empate de datas desempata por ordem.
2. xUnit `PlaneadorAlertas`: antecedência 0, 1 e N dias; passou a hora do dia; atrasada (uma por dia); sem duplicados após reagendar; planeada concluída cancela a notificação.
3. xUnit `ValidacaoIntervencao`: fim antes do início; curativa sem anomalia; mais fotos do que o limite.
4. Manual no telemóvel (roteiro escrito; declarar não corrido se faltar aparelho): criar curativa offline com foto; iniciar planeada; tentar avançar ordem sem permissão; notificação com a app fechada; reiniciar o telemóvel e confirmar alertas; negar câmara e notificações.
5. Medição: abrir a lista com cache em menos de 2 s (5 medições, mediana).
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
