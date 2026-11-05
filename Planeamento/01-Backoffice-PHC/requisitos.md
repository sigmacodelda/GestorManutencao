# Parte 1 (Backoffice e PHC): requisitos

Versão 0.1 · 05/11/2026. Fonte: `00-Comum/requisitos-gerais.md` (BO-01 a BO-10), reunião de 04/11/2026 e quadros. Tudo o que depende de nomes reais do PHC fica "a confirmar no levantamento P1-T01". "Suposição:" marca o que não está no `00-global.md`.

## 1. Perfis, estados e periodicidades (base dos requisitos)

### 1.1 Perfis (pendência 10: nomes a confirmar com a Landeiro)

| Perfil | Role (Suposição) | Quem é | Usa |
|---|---|---|---|
| Operador | `MAN_OPERADOR` | Técnico que executa intervenções | App (Parte 2); no backoffice só consulta |
| Supervisor | `MAN_SUPERVISOR` | Supervisão que valida e fecha fichas | Validação, planeadas, dashboard, planos |
| Administrador de manutenção | `MAN_ADMIN` | Responsável que configura tudo | Tudo, incluindo configuração e equipamentos |

As roles são roles de base de dados lidas no login (padrão `UserStateService`/`Perfis` do LandeiroMaui). Criá-las é ação do utilizador (roles só com autorização explícita). O acesso a cada ecrã é decidido no menu (`HasRole`), nunca dentro da página.

### 1.2 Estados da intervenção (campo novo `u_estado`)

`Planeada` → `EmCurso` → `Concluida` → `Validada` (ficha com `Fechada? = sim`); `Cancelada` (supervisão). `Concluida` pode voltar a `EmCurso` (devolvida). Derivados, nunca gravados: **Atrasada** = `Planeada` ou `EmCurso` com `u_dataplan` anterior a hoje; **A vencer** = `Planeada` com `u_dataplan` entre hoje e hoje + antecedência.

### 1.3 Lista única de periodicidades (risco R-08)

| Código (texto da ficha) | Cálculo | Intervalo | Gera planeadas |
|---|---|---|---|
| Diária | dias | 1 | sim |
| Semanal | dias | 7 | sim |
| Quinzenal | dias | 14 | sim |
| Mensal | meses | 1 | sim |
| Trimestral | meses | 3 | sim |
| Semestral | meses | 6 | sim |
| Anual | meses | 12 | sim |
| Regular | nenhum (só meses marcados) | 0 | só com meses marcados |
| De acordo com procedimento | nenhum (só meses marcados) | 0 | só com meses marcados |
| Sempre que necessário | nenhum | 0 | nunca |

Os 8 valores atuais da Ficha (De acordo com procedimento, Diária, Mensal, Quinzenal, Regular, Semanal, Semestral, Sempre que necessário) ficam todos; acrescentam-se Trimestral e Anual (pedidos em BO-02 e usados no Word). Suposição: a lista passa a viver na tabela `u_manperiod` e a lista da Ficha no PHC alinha-se com ela (a confirmar se é lista fixa ou tabela, em P1-T01).

## 2. Matriz perfil × permissão

S = sim; N = não; C = configurável em `u_manconfig` (valor de origem indicado). Suposição: valores de origem a validar com a Landeiro.

| Permissão | Operador | Supervisor | Admin | Req. |
|---|---|---|---|---|
| Ver equipamentos e planos (consulta) | S | S | S | BO-01, BO-02 |
| Criar ou editar equipamento | N | N | S | BO-01 |
| Tornar equipamento obsoleto | N | N | S | BO-01 |
| Criar ou editar plano de manutenção | N | S | S | BO-02 |
| Gerar intervenções planeadas | N | S | S | BO-03 |
| Cancelar intervenção planeada | N | S | S | BO-03 |
| Ver lista de planeadas e alertas | S | S | S | BO-03, BO-07 |
| Criar intervenção curativa ou manual | S | S | S | PAT 1 |
| Iniciar e concluir intervenção própria | S | S | S | APP |
| Avançar intervenção fora de ordem | C (origem: N) | C (origem: S) | C (origem: S) | BO-05 |
| Editar intervenção já concluída (antes de validar) | N | S | S | BO-06 |
| Validar e fechar ficha | N | S | S | BO-06 |
| Devolver ficha ao técnico | N | S | S | BO-06 |
| Validar a ficha que a própria pessoa concluiu | n/a | C (origem: N) | C (origem: N) | BO-06 |
| Alterar configurações (antecedência, ordem, horizonte) | N | N | S | BO-04, BO-05 |
| Ver dashboard | N | S | S | BO-07 |
| Ver custos (mão de obra e peças) no dashboard | N | S | S | BO-07 |
| Registar fornecedor e documento externo | S | S | S | BO-10 |

## 3. Requisitos

### BO-01 Registo de equipamentos (infraestruturas)

- **Utilizador:** administrador (cria e edita); supervisor e operador consultam.
- **Regra:** o equipamento **é** a infraestrutura do PHC. Não se cria cópia: o backoffice lê e grava a mesma tabela que o ecrã Infraestruturas (nome real a confirmar em P1-T01). Campos usados: número, descrição, nº de série, obsoleto, tipo, marca, modelo, localização (zona), ano de aquisição, e-mail de envio de alerta, fornecedor. Viaturas (matrícula, Via Verde, Kms) ficam fora (pendência 11). Equipamento obsoleto não recebe planeadas novas.
- **Suposição:** criar e editar a infraestrutura pode continuar a fazer-se no ecrã PHC; o backoffice só cria/edita no MAUI se o levantamento mostrar que a escrita direta é segura (numeração, campos obrigatórios, triggers). Caso contrário v1 = consulta + plano + intervenções, e a criação fica no PHC.
- **Critérios de aceitação:**
  1. BO-01-C1: a lista do backoffice mostra os mesmos equipamentos (mesma contagem, excluindo viaturas) que o ecrã Infraestruturas do PHC, na base de testes.
  2. BO-01-C2: filtros por localização (zona), tipo e obsoleto; pesquisa por número ou descrição; resposta em menos de 2 s com todos os equipamentos.
  3. BO-01-C3: tornar obsoleto pede confirmação, mostra quantas planeadas futuras serão canceladas e só cancela as `Planeada` (nunca `EmCurso` nem `Concluida`).
  4. BO-01-C4: nenhuma gravação altera campos que o backoffice não mostra (teste: ler antes e depois, comparar os campos ocultos).
- **Casos limite:** equipamento sem localização; descrição duplicada; equipamento obsoleto com planeadas em curso; viatura incluída por engano (filtro por tipo).
- **Perguntas abertas:** é permitido gravar direto na tabela da infraestrutura (triggers, numeração)? Quem cria equipamentos novos hoje? Quais os valores do campo Tipo que entram (Equip. Produção sim; Veículos fora)?

### BO-02 Periodicidade e plano de manutenção por equipamento

- **Utilizador:** supervisor e administrador.
- **Regra:** um equipamento tem 0 ou mais planos (Suposição: vários, por exemplo lubrificação mensal e revisão anual externa). Cada plano: descrição, periodicidade (lista única), tipo de manutenção (Interna ou Externa), tipo de intervenção (Preventiva), meses planeados (12 marcas), dia do mês (defeito 1), data de início, última data realizada, próxima data (calculada), antecedência própria (opcional), fornecedor por defeito (externa), ativo. Carga inicial a partir do Word P.05-03 v1.4 (ver BO-09).
- **Critérios de aceitação:**
  1. BO-02-C1: criar plano com periodicidade, meses e tipo grava e a próxima data calculada aparece de imediato.
  2. BO-02-C2: periodicidade escolhida só entre os 10 valores da lista única; qualquer outro é rejeitado.
  3. BO-02-C3: plano de periodicidade "Sempre que necessário" nunca gera planeadas e o ecrã explica porquê.
  4. BO-02-C4: alterar periodicidade ou meses de um plano pergunta se as planeadas futuras `Planeada` são recalculadas; ao aceitar ficam `Cancelada` (com motivo) e geram-se as novas.
  5. BO-02-C5: grelha de meses (JAN a DEZ) editável por clique, igual à do Word.
- **Casos limite:** plano externo sem fornecedor; meses todos desmarcados (= todos os meses); periodicidade "Regular" sem meses; equipamento com dois planos da mesma periodicidade; "Interna/Ext" do Word.
- **Perguntas abertas:** "Interna/Ext" do Word = dois planos ou um plano misto? O calendário é fixo (a partir da data planeada) ou desloca-se com a data de execução (Suposição: fixo)? Que dia do mês usar quando só há meses (Suposição: dia 1)?

### BO-03 Geração das intervenções planeadas

- **Utilizador:** supervisor e administrador (manual); automático 1 vez por dia ao abrir o módulo (Suposição, sem serviço Windows).
- **Regra (decisão D-05):** cada ocorrência futura do plano materializa-se numa ficha com `u_estado = Planeada`, `u_planostamp` e `u_dataplan`. Janela: gera até hoje + horizonte (`HORIZONTE_GERACAO_DIAS`, defeito 60, nunca inferior à maior antecedência). Atrasadas: gera no máximo `GERAR_ATRASADAS_MAX` (defeito 1) ocorrências passadas, a mais recente; as anteriores ficam registadas no relatório de geração como "saltadas". Idempotente: a mesma ocorrência (plano + data) nunca gera duas fichas. Algoritmo completo em `fluxogramas.md` (secção 3) e no prompt P1-T02.
- **Critérios de aceitação** (hoje = 2026-11-05, horizonte 60 dias, até 2027-01-04):
  1. BO-03-C1: plano Mensal, dia 10, todos os meses, última data 2026-09-10: gera 2026-10-10 (atrasada), 2026-11-10 e 2026-12-10; não gera 2027-01-10.
  2. BO-03-C2: plano Quinzenal, última data 2026-10-30: gera 11-13, 11-27, 12-11 e 12-25 e nada mais.
  3. BO-03-C3: plano Semestral, meses JAN e JUL, última data 2026-07-15: não gera nada agora e a próxima data fica 2027-01-15.
  4. BO-03-C4: correr a geração duas vezes seguidas não cria fichas novas na segunda.
  5. BO-03-C5: dia 31 em fevereiro usa o último dia do mês (2027-02-28).
  6. BO-03-C6: plano "Sempre que necessário", plano inativo ou equipamento obsoleto não geram.
  7. BO-03-C7: plano novo com 12 meses de atraso gera só 1 atrasada e o relatório lista as 11 saltadas.
  8. BO-03-C8: a geração devolve relatório (criadas, já existentes, saltadas, erros por plano) e um plano com erro não impede os restantes.
- **Casos limite:** duas pessoas a gerar em simultâneo (risco de duplicados sem índice único; ver risco no README); ficha de plano já `EmCurso`; plano editado a meio; mudança de hora de verão (usar só datas, sem horas).
- **Perguntas abertas:** quantas atrasadas gerar na carga inicial? Fichas planeadas aparecem no histórico da infraestrutura do PHC (aba Fichas Interv.) e isso incomoda?

### BO-04 Antecedência do alerta

- **Utilizador:** administrador.
- **Regra:** `ALERTA_ANTECEDENCIA_DIAS` (global, defeito 7) e antecedência opcional por plano (a do plano prevalece). A app (Parte 2) lê o valor efetivo por planeada; o backoffice mostra a lista "a vencer".
- **Critérios:** (1) BO-04-C1: valor global alterado reflete-se nos alertas dos planos sem valor próprio; (2) BO-04-C2: plano com valor próprio ignora o global; (3) BO-04-C3: valor aceita 0 a 90 e rejeita negativos e texto; (4) BO-04-C4: horizonte de geração nunca fica abaixo da maior antecedência (a configuração avisa).
- **Casos limite:** antecedência 0 (alerta só no dia); antecedência maior que a periodicidade (aviso).
- **Perguntas abertas:** o alerta é só no telemóvel ou também e-mail (campo "Email Envio de Alerta" do equipamento)? Fora de âmbito até decisão.

### BO-05 Permissão de avançar fora de ordem

- **Utilizador:** administrador configura; a app aplica (Parte 2).
- **Regra:** uma chave por perfil, `AVANCAR_ORDEM_<ROLE>` (0 ou 1). "Ordem" (Suposição): fila das planeadas em aberto de cada zona, por `u_dataplan` e `u_ordem`. Sem permissão, o utilizador só pode iniciar a primeira da fila de cada zona; com permissão, qualquer uma.
- **Critérios:** (1) BO-05-C1: alterar a chave de um perfil altera `PodeAvancarForaDeOrdem(role)` na próxima leitura; (2) BO-05-C2: o ecrã mostra os 3 perfis, com origem 0/1/1; (3) BO-05-C3: só o administrador grava.
- **Casos limite:** utilizador com dois perfis (vale o mais permissivo); fila vazia; planeada atrasada de outra zona.
- **Perguntas abertas:** a ordem é por zona, por equipamento ou global? (pendência: quadro 3 só diz "avançar ordem intervenções").

### BO-06 Validação de intervenções

- **Utilizador:** supervisor e administrador.
- **Regra:** a lista mostra as fichas `Concluida`. O supervisor abre, confere (anomalia, intervenção, tempos, custos, doc. externo se externa), e **Valida** (`u_estado = Validada`, `Fechada? = sim`, `u_valinis`, `u_valdata`) ou **Devolve** ao técnico com motivo (`EmCurso`, `u_valobs`). Ficha `Validada` fica só de leitura. Suposição: quem concluiu não valida a própria ficha (`VALIDAR_PROPRIA_INTERVENCAO = 0`).
- **Critérios:** (1) BO-06-C1: só fichas `Concluida` aparecem em "Por validar"; (2) BO-06-C2: Validar grava estado, `Fechada?`, iniciais e data e remove-a da lista; (3) BO-06-C3: Devolver exige motivo e a ficha reaparece `EmCurso` na app; (4) BO-06-C4: ficha externa sem doc. externo/folha de serviço não valida (mensagem clara); (5) BO-06-C5: operador não vê o ecrã nem consegue chamar o serviço (verificação por perfil no serviço); (6) BO-06-C6: tentar validar uma ficha já `Validada` ou alterada entretanto falha com aviso, sem gravar.
- **Casos limite:** duas validações em simultâneo; ficha concluída offline que chega depois de validada (a alteração vira proposta, ver arquitetura secção 6); supervisor único que também executa.
- **Perguntas abertas:** validar a própria ficha é aceitável em equipas pequenas? Valida-se em bloco (várias fichas de uma vez)? Fecho do PHC (campo Fechada?) tem triggers ou regras?

### BO-07 Dashboard de análises e alertas

- **Utilizador:** supervisor e administrador.
- **Regra:** KPIs com período e filtros (departamento, zona, equipamento, plano, tipo): planeadas, realizadas (`Concluida` + `Validada`), atrasadas (a hoje), % cumprimento por mês (realizadas ÷ planeadas com `u_dataplan` no mês, sem canceladas), preventivas vs curativas, por equipamento e zona, custo de mão de obra e de peças, equipamentos com mais avarias (curativas), peças em rutura (só quando a Parte 3 existir). Alertas: atrasadas, a vencer na janela, concluídas por validar, planos sem próxima data, equipamentos sem plano. Estrutura como os dashboards do LandeiroMaui (separadores, `PainelBase`, modo Visualizar com recarga a 30 s); cores segundo D-09.
- **Critérios:** (1) BO-07-C1: cada KPI bate com uma consulta manual na base de testes (diferença zero) em pelo menos 3 períodos; (2) BO-07-C2: filtros combinados reduzem todos os KPIs de forma coerente; (3) BO-07-C3: abre em menos de 3 s com 12 meses de dados (medido); (4) BO-07-C4: o mosaico "peças em rutura" mostra "indisponível" se a Parte 3 não estiver instalada e nunca dá erro; (5) BO-07-C5: o modo Visualizar não mostra custos; (6) BO-07-C6: quem não tem perfil de dashboard não vê a entrada de menu.
- **Casos limite:** mês sem planeadas (cumprimento = n/a, não 0 %); ficha sem custo; equipamento obsoleto no histórico; planeada cancelada (excluída).
- **Perguntas abertas:** o custo de mão de obra vem do total da ficha ou calcula-se por horas × taxa? Quem vê custos? Tolerância em dias para "realizada no prazo"?

### BO-08 Tabelas, campos e contratos (especificação)

- **Utilizador:** equipa SigmaCode e Landeiro (quem cria no PHC).
- **Regra:** especificar, nunca criar, tabelas e campos (formato `<phc-tables>`) e publicar em `docs/contratos.md` as interfaces e DTOs que as Partes 2 e 3 consomem. Cada tabela com script de verificação só leitura. SPs `sc_<Entidade><Acao>` só se necessário.
- **Critérios:** (1) BO-08-C1: cada tabela e campo novo com nome, tipo, tamanho, nulo, defeito e regra; (2) BO-08-C2: scripts `01_verificar_*.sql` falham com mensagem clara se faltar algo; (3) BO-08-C3: `docs/contratos.md` publicado, com interface, DTOs e exemplos, e confirmado por P2 e P3 antes de P1-T02 fechar; (4) BO-08-C4: lista do que é pedido ao utilizador (criar tabelas, roles, índice único) numa só lista ordenada.
- **Casos limite:** edição do PHC não permite tabelas `u_` (alternativa D-04: base própria `MANUTENCAO`); campo com o mesmo nome já existente.
- **Perguntas abertas:** pendência 4 (criar tabelas e campos `u_`): quem, quando, em que bases?

### BO-09 Carga inicial

- **Utilizador:** administrador (com apoio SigmaCode).
- **Regra:** (a) equipamentos: os que já existem no PHC, sem migrar nada; (b) planos: a partir do Word P.05-03 v1.4, convertido para CSV (Nº, Designação, Zona, Entidade, 12 meses, periodicidade) e carregado por script gerado, executado só em `TESTES_LAND2` pelo Claude Code e em produção pelo utilizador; (c) fichas históricas: só leitura, nunca migrar papel; ficam no histórico do PHC. Linhas a vermelho do Word entram com `ativo = não` até a Landeiro confirmar (Suposição).
- **Critérios:** (1) BO-09-C1: relatório de correspondência Word ↔ equipamentos (nº igual, designação parecida) com divergências listadas; (2) BO-09-C2: carga repetível (correr 2 vezes não duplica planos); (3) BO-09-C3: depois da carga, cada linha do Word tem um plano e a soma dos meses marcados do Word = soma dos meses dos planos; (4) BO-09-C4: nenhum dado pessoal no CSV nem no script.
- **Casos limite:** nº do Word sem equipamento; equipamento sem linha no Word (relatório "sem plano"); periodicidade do Word que não bate com os meses (aviso); designações diferentes.
- **Perguntas abertas:** pendência 1 (Word do Luís); "Interna/Ext"; linhas a vermelho.

### BO-10 Manutenção externa

- **Utilizador:** qualquer perfil regista; supervisor valida.
- **Regra:** `Tipo Manutenção = Externa` obriga a fornecedor e a doc. externo / folha de serviço (campos já existentes na Ficha) para poder **concluir** (Suposição: obrigatório ao concluir e ao validar). O plano externo guarda o fornecedor por defeito. Fatura e encomendas ficam fora.
- **Critérios:** (1) BO-10-C1: plano externo cria planeadas com o fornecedor por defeito; (2) BO-10-C2: ficha externa sem fornecedor ou sem doc. externo não passa a `Concluida` nem a `Validada`; (3) BO-10-C3: ficha interna não exige esses campos; (4) BO-10-C4: o e-mail de alerta do equipamento aparece na ficha do equipamento (sem envio automático nesta fase).
- **Casos limite:** mudança de Interna para Externa a meio; várias folhas de serviço para a mesma ficha (um campo livre só guarda uma: ver P1-T01).
- **Perguntas abertas:** o doc. externo é obrigatório já na conclusão ou só quando chega a folha (dias depois)?

## 4. Requisitos não funcionais da Parte 1

SQL com `WITH (NOLOCK)` nas leituras; compatibilidade nível 100 (sem STRING_SPLIT, OPENJSON, TVP); erros com `CrashLogService.Registar` e aviso ao utilizador; entrada de menu por role fora da página; RGPD (só iniciais e nome do técnico); `verificar-segredos.ps1` antes de entregar; regras novas em helpers com teste xUnit.
