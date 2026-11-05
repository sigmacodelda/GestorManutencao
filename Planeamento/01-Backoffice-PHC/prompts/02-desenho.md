# Desenho do backoffice: modelo de dados, contratos, ecrãs e protótipo
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 02 (P1-T02) · Tipo: desenho
Task global: `docs/tasks/00-global.md` (em `C:\_SigmaRepos\GestorManutencao\docs\tasks\`)
Modo: Autónomo
Papel do agente: analista: desenha e documenta; não escreve código da aplicação nem toca na base
Entradas: `C:\_SigmaRepos\GestorManutencao\docs\levantamento.md` (P1-T01); opcional: mockup inicial `[PREENCHER: caminho de mockups/backoffice.html, se existir]`
Saídas: `docs\desenho.md`, `docs\contratos.md`, `docs\sql\01_verificar_manutencao.sql`, `docs\prototipo\backoffice.html` (todos em `C:\_SigmaRepos\GestorManutencao\`)
Critérios de conclusão:
1. Modelo de dados final em `<phc-tables>` (tabelas novas, campos novos na Ficha, índices propostos) já corrigido pelo levantamento, com o script de verificação só leitura, e a lista ordenada do que o utilizador tem de criar no PHC (tabelas, campos, roles, índice).
2. Lista única de periodicidades e algoritmo de geração de planeadas escritos sem ambiguidade, com pelo menos 12 casos de teste (data de hoje, plano, resultado esperado).
3. Matriz de perfis (operador, supervisor, administrador de manutenção), permissões e chaves de `u_manconfig`, coerente com a matriz de partida (secção Âmbito, ponto 3) e com as roles reais.
4. `docs/contratos.md` publicado (interfaces, DTOs, exemplos, erros) e confirmado como suficiente para as Partes 2 e 3 (o utilizador valida com os agentes P2 e P3).
5. Wireframes dos 6 ecrãs (Equipamentos, Planos, Planeadas, Validação, Configuração, Dashboard) e protótipo clicável com a marca SigmaCode, validado pelo utilizador antes de 11/11/2026.
Gate: manual
Agentes: `sc-leitor` (excertos do LandeiroMaui), `sc-doc` (se delegares escrita), `sc-leitor-bd` (só se faltar um facto do levantamento)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto: Gestor de Manutenção da Carnes Landeiro, 3 partes em paralelo: P1 backoffice e PHC (esta), P2 app móvel offline, P3 peças e armazém. **As Partes 2 e 3 só avançam depois dos teus contratos** (P2-T02 e P3-T02 dependem de P1-T02).
- Backoffice = ecrãs Windows do módulo `Manutencao` no LandeiroMaui (`C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui`: Dapper, `NOLOCK`, `Perfis`, `UserStateService`, `ModalBase`, `tabela-moderna`, dashboards com separadores e modo Visualizar). Estrutura: `Components/Pages/Manutencao/`, `Interfaces/Manutencao/`, `Services/Manutencao/`, `Models/Manutencao/`.
- Requisitos da parte (resumo): BO-01 equipamentos (reaproveita a infraestrutura do PHC), BO-02 plano por equipamento (periodicidade, interna ou externa, meses), BO-03 geração das planeadas (decisão D-05: fichas com estado Planeada), BO-04 antecedência do alerta (global e por plano), BO-05 avançar fora de ordem por perfil, BO-06 validação (supervisão: Concluída → Validada, ficha Fechada), BO-07 dashboard e alertas, BO-08 tabelas, campos e contratos, BO-09 carga inicial do Word P.05-03, BO-10 manutenção externa (fornecedor, doc. externo/folha de serviço).
- Estados: Planeada, EmCurso, Concluida, Validada, Cancelada (o modelo candidato original omitia Cancelada; o diagrama geral tem-na). Atrasada e A vencer são derivados.
- Ecrãs do PHC reaproveitados (nomes reais vêm do levantamento): Infraestruturas e Ficha de Intervenção, com Movimentos Internos e Externos.
- Anexos: o levantamento (entrada).
Nota: sem credenciais, tokens nem connection strings.
</context>

<environment>
- PHC: edição Evolution (Ultimate), versão 2025.04. Cliente: Carnes Landeiro. Alojamento: servidor do cliente [CONFIRMAR].
- SQL Server: versão e nível de compatibilidade: os do levantamento (assumir 100 se não confirmados). `LAND2_DEMO` está em 150.
- Bases: `TESTES_LAND2` e `LAND2_DEMO` são as únicas onde se pode escrever; `LAND2` só leitura. Nesta task não escreves na base.
- Nunca crias tabelas nem campos, nem alteras triggers existentes. Especificas para o utilizador criar. Índices, roles e funções só com autorização explícita do utilizador.
- Triggers existentes nas tabelas envolvidas: os do levantamento.
- Código base: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui`; lê excertos de `CLAUDE.md` e `ARQUITETURA.md`; nunca `appsettings.json`, `bin/` nem `obj/`; sem git nesse repositório.
</environment>

<phc-tables>
Nota: modelo candidato, a corrigir com o levantamento (nomes reais, tipos, convenções de nulos e datas das tabelas `u_` existentes). O Claude Code NUNCA cria estes objetos: o utilizador cria-os no PHC. Campos standard do PHC (stamp `u_<nome>stamp`, `ousrinis`, `ousrdata`, `ousrhora`, `usrinis`, `usrdata`, `usrhora`, `marcada`) não estão listados. Convenção assumida: sem nulos; data vazia = `19000101`; se o levantamento mostrar outra, segue-a. Se a edição não permitir tabelas `u_`: alternativa D-04, base própria `MANUTENCAO`.

Tabela `u_manperiod` (lista única de periodicidades, risco R-08): `codigo` varchar(30) = texto exato da lista da Ficha; `descricao` varchar(60); `tipocalc` char(1) D (dias), M (meses) ou N (nenhum, só meses marcados); `intervalo` int; `geraauto` bit (0 para "Sempre que necessário"); `ordem` int; `inativo` bit. Valores iniciais: os 10 da lista (Diária 1 D, Semanal 7 D, Quinzenal 14 D, Mensal 1 M, Trimestral 3 M, Semestral 6 M, Anual 12 M, Regular N, De acordo com procedimento N, Sempre que necessário N com `geraauto` 0).

Tabela `u_manplano` (plano por equipamento; vários por equipamento): `infrastamp` char(25) (chave da infraestrutura, nome real a confirmar); `descricao` varchar(100); `periodicidade` varchar(30) (→ `u_manperiod.codigo`); `tipoman` varchar(10) Interna ou Externa; `tipoint` varchar(10) defeito Preventiva; `meses` char(12) de 0/1, defeito `000000000000` (vazio = todos); `diames` int defeito 1; `datainicio` datetime; `ultimadata` datetime (última data planeada concluída); `proxdata` datetime (cache da próxima data); `antecedencia` int (-1 = usa o global); `fornecedor` numeric(10,0) (nº do fornecedor por defeito, 0 = nenhum); `ativo` bit defeito 1; `origem` varchar(10) (WORD ou MANUAL); `obs` varchar(254). Chave lógica: (`infrastamp`, `descricao`).

Tabela `u_manconfig` (chave/valor): `chave` varchar(40) única; `valor` varchar(100); `descricao` varchar(100). Chaves e valores de origem: `ALERTA_ANTECEDENCIA_DIAS` 7; `HORIZONTE_GERACAO_DIAS` 60; `GERAR_ATRASADAS_MAX` 1; `AVANCAR_ORDEM_MAN_OPERADOR` 0; `AVANCAR_ORDEM_MAN_SUPERVISOR` 1; `AVANCAR_ORDEM_MAN_ADMIN` 1; `VALIDAR_PROPRIA_INTERVENCAO` 0; `PIN_MAX_TENTATIVAS` 5 (usada pela Parte 2).

Campos novos na tabela da Ficha de Intervenção (nome real do levantamento): `u_estado` varchar(12) defeito '' (vazio = legado: Fechada → Validada, aberta → Concluida; o legado fica fora do dashboard anterior à data de arranque); `u_planostamp` char(25); `u_dataplan` datetime; `u_hini` datetime; `u_hfim` datetime; `u_tecnico` varchar(30); `u_ordem` int; `u_origem` varchar(10) (PLANO, MANUAL, APP); `u_extid` varchar(36) (id gerado no dispositivo, anti-duplicados da Parte 2; confirmar com P2-T02); `u_valinis` varchar(3); `u_valdata` datetime; `u_valobs` varchar(254) (motivo da devolução ou nota).

Índices propostos (a aprovar; não se criam sozinhos): único filtrado em `u_manconfig(chave)`; único filtrado na Ficha em (`u_planostamp`, `u_dataplan`) onde `u_planostamp` <> '' e `u_estado` <> 'Cancelada' (impede planeadas duplicadas se duas pessoas gerarem ao mesmo tempo; filtered index existe desde o SQL Server 2008, confirmar no nível 100); `u_manplano(infrastamp)`.
Roles a criar pelo utilizador (autorização explícita): `MAN_OPERADOR`, `MAN_SUPERVISOR`, `MAN_ADMIN` (nomes a confirmar, pendência 10) com SELECT, INSERT e UPDATE nas tabelas `u_man*` e na Ficha.
Verificação: `01_verificar_manutencao.sql`, só leitura (`INFORMATION_SCHEMA` e `sys.*`), falha com `RAISERROR` (severidade 16) se faltar tabela, campo, tipo ou tamanho; se falhar, a task dependente pára e escreve `BLOQUEIO:` com a lista do que falta.
</phc-tables>

<task>
Objetivo: fechar o desenho da Parte 1 e publicar os contratos de que as Partes 2 e 3 dependem.
Âmbito:
1. Modelo de dados final (acima), corrigido pelo levantamento; decide e justifica se a Ficha serve as planeadas (D-05) e se os Movimentos servem as peças (senão `u_manintpec`, Parte 3). Diz como `Validada` mapeia o campo `Fechada?`.
2. Algoritmo de geração (a regra): `desde` = maior entre `ultimadata` e a maior `u_dataplan` das fichas do plano não canceladas, ou `datainicio` menos 1 dia se não houver; datas até hoje + `HORIZONTE_GERACAO_DIAS` (nunca menor que a maior antecedência); `tipocalc` D soma N dias e filtra pelos meses marcados; M sem meses soma N meses; com meses marcados, ou `tipocalc` N, usa `diames` (limitado ao último dia) nos meses marcados; das datas passadas só se mantêm as últimas `GERAR_ATRASADAS_MAX`, as outras vão para o relatório como saltadas; idempotente por (plano, data); máximo 24 ocorrências por plano e execução; erro num plano não pára os outros; atualiza `proxdata`; `u_ordem` pela data e nº do equipamento; planos inativos, equipamentos obsoletos e `geraauto` 0 nunca geram. Exemplos obrigatórios (hoje 2026-11-05, até 2027-01-04): Mensal dia 10, última 2026-09-10 → 10-10, 11-10, 12-10; Quinzenal, última 2026-10-30 → 11-13, 11-27, 12-11, 12-25; Semestral JAN e JUL, última 2026-07-15 → nada, próxima 2027-01-15; dia 31 em fevereiro; plano novo com 12 meses de atraso; segunda execução sem criar nada.
3. Matriz de perfis × permissão, consolidada com as roles reais e o modo de verificação (no menu e no serviço). Ponto de partida (Suposição; S sim, N não, C configurável com valor de origem): operador, supervisor, admin. Ver equipamentos e planos S S S; criar ou editar equipamento e tornar obsoleto N N S; criar ou editar plano N S S; gerar e cancelar planeadas N S S; criar curativa S S S; iniciar e concluir a própria S S S; avançar fora de ordem C(0) C(1) C(1); editar concluída antes de validar, validar e devolver N S S; validar a própria ficha C(0) para supervisor e admin; alterar configurações N N S; ver dashboard e custos N S S; registar fornecedor e doc. externo S S S. Regra de ordem (BO-05): Suposição de fila por zona, por `u_dataplan` e `u_ordem` (sem permissão só se inicia a primeira da fila de cada zona); diz se o levantamento sugere outra.
4. Contratos em `docs/contratos.md`. Ponto de partida (decide e fixa): `IEquipamentoService` (listar com filtro, obter, gravar, listar zonas); `IPlanoManutencaoService` (listar, gravar, calcular próximas datas como função pura, gerar planeadas, periodicidades); `IManutencaoConfigService` (obter, gravar, antecedência efetiva por plano, pode avançar fora de ordem por role); `IIntervencaoService` (listar com filtro, obter, validar, devolver, cancelar; Parte 1) e `IExecucaoIntervencaoService` (iniciar, concluir, criar curativa; implementa a Parte 2, Suposição de separação para não haver duas equipas na mesma classe); `IAlertaManutencaoService` (planeadas a vencer, atrasadas, por validar) e `IDashboardManutencaoService` (KPIs). Para cada: assinatura C#, DTOs (campos, tipos, obrigatórios), erros possíveis (`ResultadoOperacao` com código e mensagem), perfil exigido, exemplos de pedido e resposta, e o que a Parte 2 guarda em cache. Acrescenta o que P2 pede (alerta offline, ordem, anti-duplicados) e P3 pede (peças por plano via `u_planostamp`, alerta de rutura).
5. Wireframes (ASCII em `desenho.md`) e protótipo clicável `docs/prototipo/backoffice.html` (ficheiro único, marca SigmaCode, dados fictícios, desktop e largura de telemóvel, troca de perfil) com: lista de equipamentos, plano de manutenção com grelha de meses, intervenções planeadas (filtro Departamento → Zona ou Máquina → Plano, gerar), validação (validar e devolver), configuração (antecedência, ordem por perfil), dashboard. Nenhuma biblioteca além de Google Fonts.
6. Plano de carga inicial (BO-09): como o CSV do Word entra, relatório de correspondência e script idempotente (só em testes).
Fora de âmbito: código da aplicação, criar objetos na base, Partes 2 e 3 (só os contratos), viaturas.
Solução escolhida e porquê: Dapper e SQL inline com `NOLOCK` (padrão do LandeiroMaui); sem SPs nem triggers salvo justificação de uma frase por objeto.
</task>

<decisions>
- D-05: planeadas = fichas Planeada. Alternativa rejeitada: calcular em memória, porque a app offline precisa de linhas com stamp.
- D-07 (Suposição a confirmar): backoffice = ecrãs do LandeiroMaui. Alternativa: painéis no PHC, fora do que o PAT 2 diz.
- D-09: protótipos e mockups com a paleta SigmaCode (#5BB572, #3466AF, #604696, #1A1731, #CAC9C9; Exo 2 e Open Sans) salvo pedido com "Cliente: Landeiro, usar cores do cliente".
- D-04 (por decidir) e D-02 (projeto novo para a app): não afetam este desenho, mas os contratos não podem depender do alvo.
</decisions>

<open-points>

| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 4 | Criar tabelas, campos, índices e roles no PHC: quem e quando | Utilizador / Landeiro | sim (P1-T03) |
| 10 | Nomes e membros dos perfis | Landeiro | sim (matriz final) |
| 12 | Confirmar D-02, D-04 e D-07 | Utilizador | sim (contratos finais) |
| 1 | Word do plano | Luís | não (carga em P1-T03) |
</open-points>

<rules>
- Segue os padrões que já existem no repositório. Prefere a solução mais simples que seja correta. Não introduzas dependências sem justificação (`docs/DEPENDENCIES.md`).
- Estrutura por módulo `Manutencao`; DTOs separados dos modelos de dados; registo manual dos serviços em `MauiProgram.cs`; roles novas = constante em `Helpers/Perfis`; entrada de menu dentro de `@if (UserStateService.HasRole(Perfis.X))`.
- Dados: Dapper cru, `WITH (NOLOCK)`, parâmetros nomeados; nunca `UPDLOCK`, `HOLDLOCK`, `XLOCK`, `TABLOCK`, `ROWLOCK`, `SERIALIZABLE`, `READPAST`. Nível 100: sem STRING_SPLIT, OPENJSON, JSON_VALUE, TVP. Comentários SQL em `u_sqlexec` só `/* */`.
- Erros nunca em silêncio (`CrashLogService.Registar` e aviso). Comentários o mínimo, em português. Sem credenciais; dados pessoais só o necessário.
- Antes de entregar corre `C:\_SigmaRepos\Claude-The-ALLKNOWING\tools\verificar-segredos.ps1 -Caminho C:\_SigmaRepos\GestorManutencao\docs`.
- Git: sem commit nem push (no LandeiroMaui nunca).
- Contratos: depois de os escreveres, lista as perguntas que P2 e P3 têm de confirmar; uma alteração posterior a `contratos.md` regista versão e quem é afetado.
- Revisão crítica do teu desenho antes de entregar.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado, saídas, critérios ([x] só com evidência), decisões, pendências e riscos novos, linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho e continua o que não depende dela.
</rules>

<tests>
1. Cada campo do `<phc-tables>` final tem nome, tipo, tamanho, nulo, defeito e regra; o script de verificação cobre todos.
2. Os 12 casos do algoritmo foram calculados à mão e batem com a regra (mostra a conta de 3).
3. Cada requisito BO-01 a BO-10 aponta para um ecrã, um contrato ou uma tabela (matriz de cobertura no `desenho.md`).
4. Cada método de contrato tem perfil exigido, erros e exemplo; nada expõe linhas cruas do PHC.
5. O protótipo abre sem erros na consola, navega pelos 6 ecrãs, troca de perfil e funciona em 390 px de largura.
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
