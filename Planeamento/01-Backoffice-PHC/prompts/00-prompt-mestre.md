# Prompt-mestre da Parte 1: Backoffice e PHC do Gestor de Manutenção (Carnes Landeiro)
Versão 0.1 (rascunho) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: P1-T01 a P1-T06 · Tipo: projeto grande (levantamento, desenho, desenvolvimento, testes e documentação)
Task global: `docs/tasks/00-global.md` (em `C:\_SigmaRepos\GestorManutencao\docs\tasks\`; se não existir, copia-a de `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\00-global.md`)
Modo: Plan Mode nas fases de desenvolvimento; Autónomo nas outras
Papel do agente: orquestrador da Parte 1: decide, escreve os briefs, valida a evidência dos subagentes; delega código a `sc-dev`, leituras a `sc-leitor` e `sc-leitor-bd`, documentos a `sc-doc`, revisão a `sc-revisor`
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 1: estados, iniciar/concluir/validar/devolver/cancelar, geração das planeadas e config já estão na LandeiroMaui; o desenho não os reinventa, mapeia-os e propõe só o que falta (por confirmar).
Gate: manual (um por task)

## COMO COMEÇAR

1. Este prompt orquestra as 6 tasks da Parte 1 sem escrever código na Fase 0. Preenche os marcadores `[ ... ]` (secção PAPEL, OBJETIVO E MARCA): caminho do Word do Luís, se a connection string de `TESTES_LAND2` já está no `appsettings` e se há frase de cliente para as cores.
2. Responde "começa" e o agente executa só a FASE 0 (orçamentação) e termina com `ORÇAMENTO:`.
3. Respondes `APROVADO` (ou pedes ajustes) e arranca a FASE 1. Cada fase termina com um GATE teu; só depois arranca a seguinte.
4. Cada task tem o seu prompt detalhado (`01` a `06`); o utilizador cola-o no início da fase respetiva. Se não o tiveres, pede-o antes de a fase arrancar.

## PAPEL, OBJETIVO E MARCA

- Papel: engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid, Dapper, SQL Server e PHC (Cegid PHC Evolution), em português de Portugal.
- Objetivo: entregar o backoffice do módulo Manutenção no LandeiroMaui sobre as tabelas do PHC: registo de equipamentos (reaproveitando o ecrã Infraestruturas), planos e periodicidade (carga inicial do Word P.05-03), geração de intervenções planeadas (fichas com estado Planeada, decisão D-05), configuração da antecedência do alerta e da permissão de avançar ordem por perfil, validação pela supervisão (Concluida → Validada, ficha Fechada), dashboard de análises e alertas, especificação das tabelas e campos a criar no PHC, contratos de serviço para a app móvel (Parte 2) e para peças e armazém (Parte 3), e manutenção externa (fornecedor, doc. externo ou folha de serviço). Fluxo ponta a ponta: Word → planos → gerar planeadas → app → validação → dashboard.
- Marca: SigmaCode por defeito (verde #5BB572, azul #3466AF, roxo #604696, azul-escuro #1A1731, cinzento #CAC9C9; Exo 2 e Open Sans) em documentos, mockups, protótipos e ecrãs (D-09). Frase de cliente: `[PREENCHER: vazio, ou "Cliente: Landeiro, usar cores do cliente"]`.
- Entradas a preencher: Word do plano `[caminho ou "ainda não chegou"]`; connection string de `TESTES_LAND2` no `appsettings` do LandeiroMaui `[sim/não]` (nunca a escrevas aqui).
- Ambiente: PHC Evolution (Ultimate) 2025.04; SQL Server [CONFIRMAR]; nível de compatibilidade assumido 100 [CONFIRMAR]; escreve-se só em `TESTES_LAND2` e `LAND2_DEMO` (depois de `SELECT DB_NAME()`); `LAND2` só leitura; nunca crias tabelas, campos nem alteras triggers existentes (especificas para o utilizador criar); código em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` sem git.
- Dependências para outras partes: **P1-T02 publica os contratos (`docs/contratos.md`) de que P2-T02, P2-T03 e P3-T02 dependem**; P1-T03 entrega os serviços de que P2-T04 depende.

## FASE 0: ORÇAMENTAÇÃO (obrigatória, antes de qualquer outra coisa)

Não escrevas código nem documentos de resultado. Produz, por esta ordem:
1. Cronometragem real do início da fase, ou "não mensurável".
2. Tabela por task (P1-T01 a P1-T06) e por agente (`sc-leitor-bd`, `sc-leitor`, `sc-dev`, `sc-doc`, `sc-revisor`, orquestrador): tokens de entrada, tokens de saída, total, intervalo mínimo/provável/máximo e tempo estimado; a própria FASE 0 dentro do total.
3. Pressupostos de cada estimativa. Pressupostos de partida (corrige-os se discordares): P1-T01 lê 3 a 4 tabelas, os triggers e o Word (uma tabela); P1-T02 produz 4 tabelas ou grupos de campos, 6 interfaces, cerca de 25 DTOs, 6 wireframes e 1 protótipo HTML; P1-T03 cria 6 serviços, 2 helpers puros e 20 a 30 testes; P1-T04 cria 5 ecrãs e 1 CSS; P1-T05 cria 1 serviço e 5 separadores; P1-T06 escreve 4 documentos e testes de integração; revisão `sc-revisor` obrigatória em T03, T04, T05 e no fim; uma ronda de correções por revisão.
4. Reserva de contingência (mínimo 15%) e margem de segurança de 5% a 15% sobre o máximo, com o **teto** de tokens mostrado à parte. O teto é limite duro: aviso a 80% e paragem ao atingi-lo.
5. Ponto de corte: onde parar se o teto apertar (proposta: parar depois de P1-T03 com os serviços testados e deixar ecrãs e dashboard para segunda ronda).
6. Duas variantes se o total passar o limite: **Completa** (T01 a T06) e **MVP** (T01 a T04 sem dashboard, com validação e geração de planeadas funcionais; caminho mais curto para o piloto).
7. Termina com `ORÇAMENTO:` seguido do quadro resumo e **pára**. Espera `APROVADO` (ou ajustes).

## FASES 1+ (só após APROVADO)

Cada fase: o prompt da task correspondente (colado pelo utilizador), formato ANÁLISE, PLANO, IMPLEMENTAÇÃO, REVISÃO, TESTES, RISCOS, ESTADO DA TASK e AUTOCRÍTICA; fecha com o GATE. Segue as regras comuns: `NOLOCK`, nível 100, `CrashLogService.Registar`, roles em `Perfis` e menu por `HasRole`, DTOs, comentários mínimos, sem segredos (`verificar-segredos.ps1` antes de entregar), sem commit nem push no LandeiroMaui, revisão crítica antes de entregar.

| Fase | Task | O que entrega | Critério de conclusão | Passo seguinte |
|---|---|---|---|---|
| 1 | P1-T01 Levantamento | `docs/levantamento.md`: tabelas reais, triggers, hipótese dos Movimentos, mapeamento do Word, valores reais | Estrutura e triggers com evidência; hipótese confirmada ou refutada; Word mapeado (ou BLOQUEIO só dessa parte); valores reais e correções ao modelo (5 critérios) | P1-T02 só após GATE; sem Word, continua com a estrutura esperada |
| 2 | P1-T02 Desenho | `docs/desenho.md`, `docs/contratos.md`, scripts de verificação, protótipo `docs/prototipo/backoffice.html` | Modelo `<phc-tables>` final; algoritmo com 12 casos; matriz de perfis; **contratos publicados e confirmados por P2 e P3**; wireframes e protótipo validados antes de 11/11/2026 (5 critérios) | Utilizador cria tabelas, campos, índice e roles no PHC de testes; só então P1-T03 |
| 3 | P1-T03 Serviços | Serviços, helpers puros e testes no LandeiroMaui | 6 serviços conforme contrato; 12 casos de datas; geração idempotente na base de testes; validação com perfis; sem avisos e revisão limpa (5 critérios) | P1-T04; avisar P2 de que os serviços existem |
| 4 | P1-T04 Ecrãs | Cinco ecrãs `/Manutencao/...` e `manutencao.css` | Rotas funcionais; menu por role; fluxo plano → planeadas → validar comprovado; estilo D-09; sem avisos (5 critérios) | P1-T05 |
| 5 | P1-T05 Dashboard | Dashboard, alertas e modo Visualizar | KPIs reconciliados em 3 períodos; filtros e alertas; padrão dos dashboards e desempenho; rutura "indisponível" sem a Parte 3 (4 critérios) | P1-T06 |
| 6 | P1-T06 Testes e documentação | Rastreabilidade, testes, 3 documentos e `ARQUITETURA.md` | Matriz sem critério sem teste; suites passam; documentação entregue; revisão final limpa (4 critérios) | Fecho da Parte 1 e relatório de reconciliação |

Regras de coordenação: dentro da parte as tasks são sequenciais (a task NN só arranca com a anterior CONCLUÍDA); `docs/tasks/00-global.md` é atualizado no fim de cada task (estado, critérios com evidência, decisões, pendências, riscos, Registo). Uma alteração a `contratos.md` depois de publicado regista versão e as partes afetadas. Tabelas, campos, índice único e roles são pedidos ao utilizador numa só lista ordenada.

## CONTROLO DURANTE A EXECUÇÃO

- No fim de cada agente e de cada fase: tokens e tempo reais contra os estimados e o desvio em %.
- Aviso a 80% do teto aprovado: diz o que falta e propõe, por opções, o que cortar.
- Paragem obrigatória ao atingir o teto: pára, entrega o estado e responde com propostas (secção seguinte). Nunca continues em silêncio.

## PARAGEM COM PROPOSTAS

Qualquer coisa que trave o trabalho a meio (teto de tokens, credencial ou connection string em falta, tabela ou campo por criar, decisão de negócio como D-02, D-04 ou D-07, falha repetida, conflito entre partes, risco de segurança) obriga a parar e a responder com **2 a 4 propostas**, a recomendada primeiro, cada uma com impacto em tokens, tempo e risco; nunca com pergunta aberta. Mantém-se tudo o que não depende do bloqueio. Escreve `BLOQUEIO:` só nos casos da norma (credencial, decisão de negócio sem alternativa razoável, escrita fora da base de testes, tabela ou campo novo no PHC) e `GATE:` uma vez por task.

## ENTREGÁVEIS E RECONCILIAÇÃO

- Lista fechada: `docs/levantamento.md`, `docs/desenho.md`, `docs/contratos.md`, `docs/sql/01_verificar_manutencao.sql`, `docs/prototipo/backoffice.html` (em `C:\_SigmaRepos\GestorManutencao\`); serviços, ecrãs, dashboard, CSS e testes no LandeiroMaui; `docs/manutencao/` (técnico, manual do supervisor, carga inicial, rastreabilidade) e a secção Manutenção de `ARQUITETURA.md`; `docs/DEPENDENCIES.md` se houver dependências novas.
- Riscos e impactos: lista final (concorrência na geração sem índice único, escrita direta em tabelas do PHC com triggers, cores do módulo diferentes do resto do LandeiroMaui, dependência das tabelas criadas pelo utilizador, Word por receber).
- Quadro orçamento aprovado contra real, por agente e total, com desvios.
- Lições para orçar melhor e `MELHORIA:` (ou `nenhuma`) por task.
