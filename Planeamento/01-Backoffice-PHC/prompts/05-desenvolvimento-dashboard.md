# Dashboard e alertas de manutenção
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 05 (P1-T05) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md` (em `C:\_SigmaRepos\GestorManutencao\docs\tasks\`)
Modo: Plan Mode para o PLANO; depois Autónomo
Papel do agente: desenvolvedor: implementa o que está no prompt e no desenho, sem análises nem relatórios além de uma linha de contexto
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 1: estados, iniciar/concluir/validar/devolver/cancelar, geração das planeadas e config já estão na LandeiroMaui; o desenho não os reinventa, mapeia-os e propõe só o que falta (por confirmar).
Entradas: `C:\_SigmaRepos\GestorManutencao\docs\desenho.md`, `docs\contratos.md` (`IDashboardManutencaoService`, `IAlertaManutencaoService`); ecrãs da P1-T04; dashboards existentes do LandeiroMaui (`Components/Pages/Desmancha/Dashboard/`)
Saídas: `Components/Pages/Manutencao/Dashboard/`, `Services/Manutencao/DashboardManutencaoService.cs`, `Interfaces/Manutencao/`, estilos em `wwwroot/css/manutencao.css`, testes em `LandeiroMaui.Testes`
Critérios de conclusão:
1. Cada KPI do dashboard (planeadas, realizadas, atrasadas, % cumprimento por mês, preventivas vs curativas, por equipamento e zona, custo de mão de obra e de peças, equipamentos com mais avarias) bate com uma consulta manual na base de testes, diferença zero, em pelo menos 3 períodos.
2. Filtros (período, departamento, zona, equipamento, plano, tipo) combinam-se e reduzem todos os KPIs de forma coerente; alertas (atrasadas, a vencer na janela de antecedência, por validar, planos sem próxima data, equipamentos sem plano) navegam para o ecrã certo.
3. Estrutura como os dashboards do LandeiroMaui (separadores por role, `PainelBase`, modo Visualizar com recarga a 30 s sem custos), cores D-09, abertura em menos de 3 s com 12 meses de dados (medido) e mosaico "peças em rutura" que mostra "indisponível" sem a Parte 3 e nunca dá erro.
4. Compilação sem avisos, `verificar-segredos.ps1` limpo e revisão `sc-revisor` sem pendências críticas.
Gate: manual
Agentes: `sc-leitor` (padrão dos dashboards), `sc-dev` (código), `sc-leitor-bd` (conferir os números), `sc-revisor` (SQL, obrigatório)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto: Gestor de Manutenção da Carnes Landeiro. O dashboard dá à supervisão e à administração a visão do que está planeado, feito, atrasado e quanto custa, mais os alertas.
- Dashboards existentes do LandeiroMaui (lê com `sc-leitor`, excertos): separadores por role `DES_DASHBOARD_*`, `PainelBase` (com `Visualizar`), `PainelResumo` (só números grandes), modo Visualizar em ecrã inteiro (`.db-tv`, recarrega a cada 30 s com `Versao++`, relógio `DbRelogio`), `TemDashboard`. Repete o padrão em `Components/Pages/Manutencao/Dashboard/`, com roles `MAN_*` (matriz do desenho) e **cores segundo D-09**: paleta SigmaCode (`--man-*` em `manutencao.css`), salvo a frase `[PREENCHER: vazio = SigmaCode; ou "Cliente: Landeiro, usar cores do cliente"]`.
- Definições (copiadas do desenho; não as alteres sem registar): **Planeadas** = fichas com origem plano e `u_dataplan` no período, sem Cancelada. **Realizadas** = dessas, Concluida ou Validada. **Atrasadas** = Planeada ou EmCurso com `u_dataplan` < hoje (instantâneo a hoje, não por período). **A vencer** = Planeada com `u_dataplan` entre hoje e hoje + antecedência efetiva. **% cumprimento do mês** = realizadas ÷ planeadas com `u_dataplan` no mês (sem canceladas); mês sem planeadas = "n/a", nunca 0 %. **Preventivas vs curativas** = contagem de fichas por Tipo Intervenção por mês (não Canceladas). **Por equipamento e zona** = a zona é a localização da infraestrutura. **Custo** = soma de Mão de Obra e de Materiais/Peças das fichas Concluida e Validada, por mês e por equipamento (nomes de campos reais do levantamento). **Mais avarias** = top 10 de curativas por equipamento no período. **Peças em rutura** = só quando a Parte 3 existir (ver abaixo). Fichas legado (`u_estado` vazio) entram nas curativas e custos pelos campos existentes mas nunca em planeadas.
- Alertas: atrasadas; a vencer na janela; Concluidas por validar; planos ativos sem próxima data; equipamentos ativos sem plano (útil na carga inicial).
- Peças em rutura: a Parte 3 regista (DI opcional, `GetService`) uma `IAlertaRuturaProvider` com `ListarAsync()`; sem ela o mosaico mostra "indisponível".
- Anexos: entradas acima.
Nota: sem credenciais, tokens nem connection strings.
</context>

<environment>
- PHC: edição Evolution (Ultimate), versão 2025.04. Cliente: Carnes Landeiro. Alojamento: servidor do cliente [CONFIRMAR].
- SQL Server: versão [CONFIRMAR]. Nível de compatibilidade da base real: assumir 100 [CONFIRMAR]. `LAND2_DEMO` está em 150 e não prova compatibilidade com 100.
- Bases: `TESTES_LAND2` (testes) e `LAND2_DEMO` (demo) são as únicas onde se pode escrever, depois de `SELECT DB_NAME();` e de confirmar o nome. `LAND2` (produção) é só leitura. A connection string vive no `appsettings`; nunca a escrevas nem a leias.
- Podes criar views (`sc_v_<Nome>`) só na base de testes e com confirmação por lote, se uma consulta pesar (justifica numa frase). Nunca crias tabelas nem campos, nem alteras triggers existentes. Índices só com autorização explícita registada em <decisions>.
- Triggers existentes nas tabelas envolvidas: `[PREENCHER com a lista do levantamento]`.
- Projeto de código base: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui`. Lê `CLAUDE.md` e `ARQUITETURA.md` antes de escrever código. Nesse repositório não faças commits, branches nem push: só editas ficheiros.
</environment>

<task>
Objetivo: dashboard de manutenção com KPIs, filtros e alertas, no padrão dos dashboards do LandeiroMaui.
Âmbito (por fases):
1. `DashboardManutencaoService`: uma consulta por KPI (Dapper, `NOLOCK`, parâmetros nomeados, filtros opcionais sem concatenar texto), devolve DTOs do contrato; contas de cumprimento e agrupamentos de datas em funções puras com teste xUnit.
2. Separadores (cada um com o seu role ou o perfil do desenho): Resumo (números grandes do mês), Planeamento (planeadas, realizadas, atrasadas, % por mês), Equipamentos (por equipamento e zona, mais avarias, preventivas vs curativas), Custos (mão de obra e peças; só supervisor e admin), Alertas.
3. Gráficos sem biblioteca nova: o mesmo mecanismo do dashboard existente; se este usar uma biblioteca, reutiliza-a; senão SVG ou HTML e CSS. Cada gráfico com valores em texto e legenda; nunca só cor.
4. Filtros e alertas clicáveis (levam a Planeadas ou Validação com o filtro aplicado).
5. Modo Visualizar: ecrã inteiro, recarga a 30 s sem "a carregar", sem custos nem toques; relógio.
6. Mosaico "peças em rutura" com `IAlertaRuturaProvider` opcional.
7. Dados de teste: se a base de testes tiver poucos dados, prepara um lote com `u_origem = 'TESTE'` (12 meses, vários equipamentos e zonas) e um script de limpeza; ambos só depois de `SELECT DB_NAME()` e de confirmação por lote. Mede o tempo de abertura (cronómetro no serviço, 3 execuções, regista a mediana).
8. Reconciliação: para 3 períodos, compara cada KPI com a consulta manual (via `sc-leitor-bd`) e regista a tabela.
Fora de âmbito: previsão de avarias, MTBF e MTTR, relatórios em PDF e Excel, e-mails, a lógica de rutura da Parte 3, alterar dados.
Solução escolhida e porquê: consultas diretas de leitura com `NOLOCK` (padrão do projeto); view só se uma consulta medida passar o orçamento de tempo.
</task>

<decisions>
- D-09: cores SigmaCode por defeito. Alternativa rejeitada: cores do cliente por defeito. A estrutura segue os dashboards do LandeiroMaui.
- D-05: planeadas = fichas Planeada; por isso planeadas, atrasadas e a vencer saem todas da Ficha.
- O custo vem dos totais da Ficha (Mão de Obra, Materiais/Peças), conforme o levantamento (Suposição: confirma que os Movimentos somam para esses totais).
</decisions>

<open-points>

| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| — | Quem vê custos (Suposição: supervisor e admin) | Landeiro | não |
| — | Tolerância em dias para "realizada no prazo" (fora da v1) | Landeiro | não |
| — | Peças em rutura: contrato com a Parte 3 (P3-T05) | Utilizador | não (mosaico indisponível até existir) |
</open-points>

<rules>
- Segue os padrões que já existem no repositório. Prefere a solução mais simples que seja correta. Não introduzas dependências sem justificação (regista em `docs/DEPENDENCIES.md`).
- Estrutura por módulo (`Components/Pages/Manutencao/`, `Interfaces/Manutencao/`, `Services/Manutencao/`, `Models/Manutencao/`); registo manual em `MauiProgram.cs`; DTOs separados dos modelos de dados.
- Dados: Dapper cru, uma `SqlConnection` por método, parâmetros nomeados, `WITH (NOLOCK)` em todo o SELECT/JOIN a tabelas base (não em CTEs). Nunca `UPDLOCK`, `HOLDLOCK`, `XLOCK`, `TABLOCK`, `ROWLOCK`, `SERIALIZABLE`, `READPAST` nem transações longas. Não inventes filtros SQL: só as condições pedidas. Nível 100: sem STRING_SPLIT, OPENJSON, JSON_VALUE, TVP; TRY_CONVERT, IIF, CONCAT, FORMAT, OFFSET…FETCH, THROW, TRY_CAST e STRING_AGG só depois de confirmados em nível 100.
- Erros nunca em silêncio (`CrashLogService.Registar` e aviso). Nada de `catch { }`, `Console.WriteLine` nem `Debug.WriteLine`.
- Acesso: entrada de menu e cada separador dentro de `@if (UserStateService.HasRole(Perfis.X))`, nunca guarda "Acesso negado" dentro da página; os serviços recusam também quem não tem perfil; custos nunca vão no modo Visualizar nem a quem não os pode ver.
- Compilação sem avisos (Nullable): `string` = `""`, listas = `new()`, sem `!` nem `null!`. Compilar e correr só com a base ativa de testes.
- Regras novas em helpers levam teste xUnit. Comentários o mínimo, em português.
- UI: navegação sem `forceLoad`, `touch-action: pan-y`, CSS com `var(--…)`, botões `sal-btn`, tabelas `tabela-moderna`, modais `ModalBase`; alvos tácteis de 44 px; contraste 4,5:1; sem CSS morto.
- Segurança: sem credenciais; dados pessoais só o necessário (nenhum nome de técnico no dashboard sem decisão); `verificar-segredos.ps1 -Caminho C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` antes de entregar.
- Base de dados: só as ações permitidas em <environment>; uma confirmação por lote; guarda de base de dados no início de cada script.
- Git: sem commit nem push (no LandeiroMaui nunca). Revisão crítica da tua implementação antes de entregar.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. Reconciliação: tabela KPI × período (3 períodos) com valor do ecrã, valor da consulta manual e diferença; tudo zero.
2. Filtros: zona + tipo + equipamento reduzem todos os KPIs; limpar os filtros devolve os valores originais.
3. Mês sem planeadas mostra "n/a"; planeada Cancelada não conta; ficha legado nunca entra em planeadas.
4. Peças em rutura: sem `IAlertaRuturaProvider` mostra "indisponível" e a consola não tem erros; com um fornecedor simulado mostra a lista.
5. Perfis: operador não vê o dashboard; supervisor vê custos; o modo Visualizar não mostra custos.
6. Desempenho: abertura em menos de 3 s com 12 meses de dados (mediana de 3 execuções); recarga do modo Visualizar sem piscar.
7. Largura 1024×768 e 1920×1080. O que não puderes verificar declara-se.
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
