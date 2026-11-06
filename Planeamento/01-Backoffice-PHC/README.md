> **Base = LandeiroMaui (2026-11-06):** as tabelas e regras de manutenção já existem na LandeiroMaui; ver `../00-Comum/base-landeiromaui.md` (prevalece). Onde as decisões abaixo citam `u_manplano`, `u_manconfig`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini` ou `u_tecnico`, leia-se `u_itmanutli`, `u_configman`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini` e `tecno`/`tecnome`.

# Parte 1: Backoffice e PHC (Gestor de Manutenção, Carnes Landeiro)

Versão 0.1 · 05/11/2026. Parte do planeamento em `Planeamento/`; base comum em `../00-Comum/` (fonte de verdade).

## 1. O que a parte faz

Passa para o módulo Manutenção do LandeiroMaui (ecrãs Windows, decisão D-07) tudo o que acontece no escritório: registo de equipamentos (reaproveita o ecrã Infraestruturas do PHC), planos e periodicidade por equipamento com carga inicial do Word P.05-03, geração das intervenções planeadas (D-05: fichas com estado Planeada), configuração da antecedência do alerta e da permissão de avançar fora de ordem por perfil, validação pela supervisão (Concluída → Validada, ficha Fechada), dashboard de análises e alertas, manutenção externa (fornecedor, doc. externo / folha de serviço), e a especificação das tabelas e campos a criar no PHC e dos contratos de serviço que as Partes 2 e 3 consomem. Requisitos BO-01 a BO-10.

## 2. Ficheiros desta pasta

| Ficheiro | Para quê |
|---|---|
| `requisitos.md` | BO-01 a BO-10 com critérios verificáveis, casos limite, perguntas abertas; matriz perfil × permissão |
| `fluxogramas.md` | Mermaid: visão geral, estados, algoritmo de geração, sequência, validação, carga do Word, navegação, dependências |
| `tarefas.md` | As 6 tasks em subtarefas, com horas (intervalo) e dependências |
| `prompts/00-prompt-mestre.md` | Prompt de projeto grande (Fase 0 de orçamentação e fases 1 a 6) |
| `prompts/01` a `06` | Um prompt por task (01 pronto a usar; 02 a 06 em rascunho: rever quando a anterior fechar) |
| `mockups/backoffice.html` | Mockup clicável (ficheiro único, marca SigmaCode, dados fictícios) para mostrar à Landeiro; base do protótipo de P1-T02 |

## 3. Ordem de execução e dependências entre partes

`P1-T01` → `P1-T02` → `P1-T03` → `P1-T04` → `P1-T05` → `P1-T06` (sequencial, GATE do utilizador em cada).

| Quem | Precisa de | Quando |
|---|---|---|
| P1-T01 | acesso de leitura a `TESTES_LAND2` (utilizador preenche o `appsettings`) e o Word do Luís | arranque |
| P1-T03 | tabelas, campos, índice único e roles criados no PHC de testes pelo utilizador | depois de P1-T02 |
| **P2-T02 e P2-T03, P3-T02** | **contratos publicados em `docs/contratos.md` (critério de conclusão de P1-T02)** | fim de P1-T02 |
| P2-T04 | serviços de P1-T03 | fim de P1-T03 |
| P1-T05 (opcional) | alerta de rutura de P3-T05 (sem ele o mosaico mostra "indisponível") | quando existir |

P1-T02 é o ponto crítico das 3 partes: enquanto os contratos não estiverem publicados e confirmados por P2 e P3, essas partes não passam do levantamento.

## 4. Quem faz o quê

| Quem | O quê |
|---|---|
| Utilizador (SigmaCode) | preenche `appsettings` de testes; cria no PHC as tabelas, campos, índice único e roles; confirma D-02, D-04, D-07; dá o GATE de cada task |
| Landeiro (Luís, Rui, informática) | Word P.05-03; circuito PHC em detalhe; nomes e membros dos perfis; confirma Suposições |
| Agente orquestrador | executa o prompt-mestre, escreve os briefs e valida a evidência |
| `sc-leitor-bd` / `sc-leitor` | consultas SELECT e excertos do LandeiroMaui |
| `sc-dev` / `sc-doc` / `sc-revisor` | código e testes / documentos / revisão obrigatória em T03, T04, T05 e final |

## 5. Como arrancar

1. Utilizador: preencher a connection string de `TESTES_LAND2` no `appsettings` do LandeiroMaui e guardar o Word em local conhecido.
2. Colar `prompts/00-prompt-mestre.md` e responder "começa" (Fase 0, orçamentação); depois `APROVADO`.
3. Colar `prompts/01-levantamento.md` e correr P1-T01 (pode correr ao mesmo tempo que P2-T01 e P3-T01).
4. Para a reunião de 11/11/2026: abrir `mockups/backoffice.html` (já navegável) e, depois de P1-T01, o protótipo refinado de P1-T02.

Nota de prazos (risco R-01): até 17/11/2026 a Parte 1 não chega a código; a solução provisória é registar no ecrã PHC atual com o plano exportado do Word.

## 6. Pressupostos novos (Suposição, a validar com a Landeiro)

1. Um equipamento tem vários planos (não um só); chave lógica equipamento + descrição.
2. Lista única de 10 periodicidades guardada em `u_manperiod` (acrescenta Trimestral e Anual às 8 da Ficha); a lista da Ficha no PHC alinha-se.
3. Calendário fixo: a próxima data conta a partir da data planeada anterior, não da data de execução.
4. Datas só como dias; dia do mês por plano (defeito 1) nos planos com meses marcados; mês curto usa o último dia.
5. Geração (base LandeiroMaui): a planeada seguinte da tarefa gera-se ao concluir (`u_configman.gerarplaneadas`), última feita + periodicidade; uma só planeada/em curso por tarefa; geração diária ao abrir as Planeadas; nunca se gera planeada já atrasada (fica para hoje). O horizonte de 60 dias e `GERAR_ATRASADAS_MAX` do desenho antigo ficam só se a Landeiro os pedir (NOVO: campo a criar por pessoa em `u_configman`).
6. Índice único filtrado em (tarefa `u_itmanutli`, data) proposto (por confirmar se a base já garante unicidade; antes era `u_planostamp`) para impedir duplicados em gerações simultâneas (precisa de autorização).
7. Fichas antigas sem `estado` (`u_fichaint.estado` vazio) são "legado" (já assim na base): Fechada = Validada, aberta = Concluída; não entram nas planeadas.
8. Estado `Cancelada` existe (o diagrama geral tinha-o, o modelo candidato não).
9. "Avançar fora de ordem": fila por zona (`u_equip.local`), por data, ordem e nº; regra `u_configman.avancaordem` (por confirmar se por perfil; origem antiga: operador não, supervisor e admin sim); quem concluiu não valida a própria ficha (`validarpropria`).
10. Perfis: a app GestorManutencao tem operador e supervisor em `u_manutil`; as roles `MAN_*` do desenho antigo ficam por confirmar (a base não controla perfis no código de manutenção).
11. Externa exige fornecedor e doc. externo para concluir e validar.
12. Carga inicial do Word: Word → CSV → relatório de correspondência → script idempotente (só testes pelo agente; produção pelo utilizador); linhas a vermelho entram inativas; sem ecrã de importação na v1.
13. Criar e editar a infraestrutura mantém-se no PHC se a escrita direta não for segura (decide o levantamento).
14. `IIntervencaoService` divide-se: consulta e validação (Parte 1) e `IExecucaoIntervencaoService` (Parte 2), mais `IAlertaManutencaoService` e `IDashboardManutencaoService`.
15. Documentos de trabalho (levantamento, desenho, contratos, tasks) em `C:\_SigmaRepos\GestorManutencao\docs\`; documentação final em `LandeiroMaui\docs\manutencao\`.
16. Os ecrãs usam paleta SigmaCode em `manutencao.css` (D-09), mesmo dentro de uma app com `tokens.css` vermelho Landeiro; a diferença é assumida até a Landeiro decidir.
17. Dados de teste marcados com `u_origem = 'TESTE'`.

## 7. Contradições encontradas

1. Estados: o modelo candidato (`arquitetura-e-modelo-de-dados.md`) lista Planeada, EmCurso, Concluida, Validada; o diagrama geral tem também Cancelada. Segui o diagrama (5 estados).
2. BO-02 lista trimestral e anual; a lista da Ficha não as tem. Resolvido com a lista única (R-08).
3. `u_manplano`, `u_manconfig` e `u_manperiod` deixam de ser propostos: a base já tem `u_itmanutli`, `u_configman` e `u_periocid` (2026-11-06, ver `../00-Comum/base-landeiromaui.md`). Os números 1, 2 e 3 acima ficam por rever contra essas tabelas.
4. `00-global.md` pede branch por parte (`dev-landeiro-manutencao-p1`); o `CLAUDE.md` do LandeiroMaui e os blocos comuns proíbem git ali. Segui a norma do projeto (só editar ficheiros); o utilizador cria o branch fora do agente.
5. D-09 (paleta SigmaCode por defeito) contra o `tokens.css` vermelho do LandeiroMaui onde os ecrãs vivem: segui D-09 (norma) e registei a diferença.
6. Saídas: `00-global.md` indica `docs/desenho.md` para P1-T02; acrescentei `docs/contratos.md` (exigido como critério), `docs/sql/` e `docs/prototipo/`.

## 8. Perguntas que bloqueiam

1. Pendência 4: o PHC permite criar tabelas e campos `u_`? Quem cria e quando (bloqueia P1-T03)?
2. Pendência 1: Word P.05-03 do Luís (bloqueia a carga do plano em P1-T01 e P1-T03).
3. Pendência 2: acesso de leitura a `TESTES_LAND2` (bloqueia P1-T01).
4. Pendência 10: nomes e membros dos perfis (bloqueia a matriz final).
5. Pendência 12: confirmação de D-02, D-04 e D-07.
