# Paridade com o backoffice do Landeiro: estado da implementação

Estado do worktree `agent-ad8de4f4dd9aa8de3`, no fim da fase 4. Fonte da lista: `docs/paridade-backoffice-landeiro.md` (checkout principal). Build da solução e 672 testes passam; nada disto foi visto em ecrã nem corrido contra uma base (ver o fim).

Legenda: **feito** = cumpre o que o Landeiro faz; **parcial** = falta ou difere algo, descrito na última coluna.

## 1. Os 16 itens

| # | Funcionalidade | Onde está | Teste | Estado, o que falta ou difere |
|---|---|---|---|---|
| 1 | Criar e editar equipamento | `POST /gestao/equipamentos`, `PUT /gestao/equipamentos/{id}`; `SqlRepositorioGestao.GravarEquipamentoAsync`; ecrã `GestaoEquipamento.razor` (3 passos), a partir de Equipamentos, Início e ficha do equipamento | `GestaoApiTests` (Criar_equipamento…, Ref_unica…, Viaturas…, Editar_equipamento_round_trip…), `FormEquipamentoTests`, `GestaoRegrasTests` | **Feito**, com diferenças: o fornecedor é validado em `fl` e o nome vem do número (o Landeiro grava o que recebe); `nointerno` = máximo + 1; concorrência por token. Manual PDF e e-mail são só texto (o Landeiro também): não abre o PDF nem envia alertas. A marca é texto livre com sugestões de `u_marcaequip`. |
| 2 | Histórico de estado | `u_histstats` gravado na criação e quando o estado muda; `GET /gestao/equipamentos/{id}/historico-estado`; secção na ficha do equipamento (`EquipamentoFicha.razor`, só Supervisor) | `GestaoApiTests.Historico_de_estado…`, `Criar_equipamento…` | **Feito.** |
| 3 | Tornar equipamento obsoleto | Mesmo `PUT`; `GET /gestao/equipamentos/{id}/planeadas-a-cancelar`; confirmação no ecrã com o nº | `GestaoApiTests.Obsoleto_pede_a_contagem…`, `FormEquipamentoTests` (confirmação), `GestaoDevServiceTests` | **Feito**, e mais estrito: a API recusa (409 `planeadas_mudaram`) se o nº confirmado já não é o certo. Cancela só Planeadas (`valobs` = "Equipamento obsoleto"), nunca EmCurso, em transação. |
| 4 | Instruções de manutenção | `GET`/`POST /gestao/equipamentos/{id}/instrucoes`, `PUT /gestao/instrucoes/{id}`; `GestaoInstrucoes.razor` | `GestaoApiTests.Instrucoes_listam…`, `Marcar_a_instrucao_obsoleta…` | **Parcial**: a "nova versão" nasce em branco (não copia as tarefas da anterior); não confirmei se o Landeiro as copia. Marcar obsoleta cancela as Planeadas das tarefas ("Instrução de manutenção obsoleta"). |
| 5 | Tarefas de manutenção | `GET`/`POST /gestao/instrucoes/{id}/tarefas`, `PUT`/`DELETE /gestao/tarefas/{id}`; `GestaoTarefas.razor` | `GestaoApiTests` (Tarefa_nova_ativa…, Desligar_a_tarefa…, Apagar_tarefa…, Editar_tarefa…) | **Feito.** Desligar cancela as Planeadas ("Tarefa desligada"); apagar só sem fichas fora de Planeada ou Cancelada; gravar tarefa ativa gera a planeada (reutiliza a geração existente; o Landeiro não o fazia ao gravar). A periodicidade tem de ser uma conhecida (`u_periocid` mais as já usadas). |
| 6 | Cancelar ficha | `POST /intervencoes/{id}/cancelar`; botão em `FichaCompleta.razor` | `FichaCompletaApiTests` (Cancelar_planeada…, Cancelar_ficha_em_curso…, Cancelar_recusa…), `EstadosFichaTests` | **Feito.** Motivo até 240; `valobs` = "Cancelada: motivo"; gera a seguinte, que salta a ocorrência. |
| 7 | Ficha manual | `POST /intervencoes`; `NovaFicha.razor` (Início, ficha do equipamento, Registar avaria) | `FichaCompletaApiTests` (Ficha_manual_…), `FichaServicosTests` | **Parcial**: a data da ficha é hoje, sem escolha na criação (o Supervisor muda-a depois, na edição). O `OpId` evita duplicar ao repetir. |
| 8 | Editar a ficha por completo | `PUT /intervencoes/{id}`; `FichaCompleta.razor`; `RegrasFicha.Aplicar` | `FichaCompletaApiTests` (Operador_edita…, Operador_nao_mexe…, Supervisor_edita…, Dois_editores…), `FichaRegrasTests` | **Parcial**, por escolha: o Operador só mexe na execução e as horas têm limites de dia; as horas de frio têm 2 decimais (o Landeiro arredonda ao inteiro); concorrência por versão. Editar material (`PUT …/materiais/{id}`) incluído. |
| 9 | Mão de obra interna | `GET`/`POST /intervencoes/{id}/mao-de-obra`, `PUT`/`DELETE …/{tempoId}`, `GET /tecnicos/{tecno}/custo-hora`; `FichaCompleta.razor` | `FichaCompletaApiTests` (Mao_de_obra_…, Custo_hora_sugerido…, Operador_nao_fixa_o_custo_hora…) | **Parcial**, por decisão: o Operador não escolhe o custo/hora (o servidor usa o último do técnico ou o gravado na linha) e só lança horas de quem está na ficha; o Landeiro deixa escolher. |
| 10 | Compras ligadas e externos | `GET /intervencoes/{id}/compras` (fn/fo/cm1, só com `comprasfichaint`); `MoExterna`/`MatExterno` à mão sem compras; totais `totmo`/`totref` por `SqlTotais`; `FichaCompleta.razor` | `FichaCompletaApiTests` (Totais_externos…, Sem_compras…, Operador_nao_ve_custos…), `FichaRegrasTests` | **Feito.** Compras, externos à mão e totais só para o Supervisor (o Operador recebe zeros e `CustosOcultos`). |
| 11 | Fichas antigas (estado vazio) | `PUT /intervencoes/{id}/antiga`; secção "Ações do supervisor" em `FichaCompleta.razor` | `FichaCompletaApiTests` (Ficha_antiga_fecha…, Fechar_ficha_antiga_so…, Fechar_antiga_externa…) | **Feito.** Fecha à mão (Supervisionada e Fechada); a manutenção externa exige fornecedor e documento se a configuração o pedir (e só ao passar de aberta a fechada). Ficam fora das transições. |
| 12 | Lista de fichas | `GET /intervencoes/todas` (situação, por supervisionar, tipo, datas, texto incl. nº, técnico, ordenação); `TodasFichas.razor` | `ListaFichasApiTests`, `VistaFichasTests` | **Parcial**: não há filtro por equipamento (só pelo texto); o aviso é "há mais fichas" com "Ver mais", sem o tecto de 500 do Landeiro. |
| 13 | Lista de equipamentos | `Equipamentos.razor` (KPIs clicáveis no cliente, ordenação) | `VistaEquipamentosTests` | **Feito.** |
| 14 | Planeadas | `Planeadas.razor` (KPIs, zona, texto, geração diária); `GeracaoDiaria` | `VistaPlaneadasTests`, `GeracaoDiariaTests` | **Feito.** A geração corre ao abrir o ecrã, 1 vez por dia por dispositivo e utilizador (dia guardado na cache local), só online, falha em silêncio. |
| 15 | Periodicidades | `GET /gestao/periodicidades`, `PUT /gestao/periodicidades/{id}`; `Periodicidades.razor` (Início e Mais) | `GestaoApiTests` (Periodicidades_…, Periodicidade_valida…, Mudar_a_periodicidade…), `PeriodicidadesPortadasTests` | **Feito**: unidade, intervalo e aviso; não cria nem apaga. Pressuposto por confirmar: o "aviso" grava na coluna `analise`. |
| 16 | Contadores do menu | `GET /intervencoes/contadores`; mosaicos e resumo do Início (Supervisor) | `FichaCompletaApiTests.Contadores_contam…`, `ContadoresIgualAoKpiTests`, `OfflineFichaTests` (cache e invalidação) | **Feito**, com a mesma regra do KPI (data passada ou fora de prazo). Cache de 30 s no cliente. Saiu o contador "Por atribuir" do Início. |

## 2. Cuidados ao copiar (secção 3 do documento)

| Cuidado | Cumprido? |
|---|---|
| O contador de Planeadas conta "Fora de prazo" como o KPI | **Sim**: mesma função (`RegrasManutencao.EmAtraso` no servidor, igual a `VistaPlaneadas`), com teste que compara os dois. |
| Concluir: data da ficha (Landeiro grava o dia do fim; aqui o dia em que iniciou) | **Não alinhado, por decisão**: mantém-se a regra desta app. As duas apps podem divergir na `data` de fichas concluídas. |
| Devolver: `hfim` 1900 no Landeiro, vazio aqui | **Sim**: a leitura trata os dois (`DataOuNula`). |
| Datas 1900 e "-----------" como vazios | **Parcial**: na ficha completa sim (`Visivel`, `DataOuNula`); nas listas só se tiram espaços. |
| Tamanhos máximos iguais | **Sim**: constantes copiadas (ref 30, designação 45, problema 200, resolução 254, etc.), validadas e testadas. |
| Auditoria `ousr*`/`usr*` e stamps `varchar(25)` | **Sim** em todas as escritas novas (stamps em maiúsculas); por validar numa base. |
| `matobrig` só existia nesta app | **Retirado a pedido do utilizador (10/11/2026)**: nada é obrigatório; a opção "exigir material ao concluir" foi removida e não é preciso criar o campo no PHC. |
| Ações de gestão só para Supervisor | **Sim**: política Supervisor mais verificação na base; testes de 403 ao Operador. |

## 3. Nunca correu numa base

Tudo o que é SQL novo ou alterado compila, mas nenhum correu contra uma base:

- `SqlRepositorioFichas.cs`: detalhe, edição dinâmica com token e `tecno` lidos, cancelar, ficha manual, ficha antiga, mão de obra, material, compras, fornecedores, contadores.
- `SqlRepositorioGestao.cs`: equipamentos, histórico, instruções, tarefas, periodicidades, e as transações (sem hints de bloqueio).
- `SqlRepositorioIntervencoes.cs`: lista de fichas com ordenação dinâmica, `(nolock)`, guarda de viaturas, transações no material.
- Coluna `[aquisiçao]` de `u_equip` (nome com cedilha).
- `GerarSeguinteAsync` depois de cancelar e de gravar tarefa.

Também nunca foi visto: nenhum dos ecrãs novos ou alterados, em Android ou Windows, e o `NavigationLock` no BlazorWebView.

## 4. Alterações depois da revisão da Fase 3

O que mudou (a numeração é a do revisor):

- **2**: o select de periodicidade da tarefa mostra e envia o texto cortado a 20 caracteres, sem repetidos (`RegrasGestaoUi.PeriodosParaEscolher`).
- **3**: `EhViatura` ignora acentos e maiúsculas ("Veiculos", "VEÍCULOS"), na API e na UI. Se, depois de gravar, a leitura devolve nada, a resposta é 409 em vez de 200 vazio (`OkOuConflito`).
- **4**: sem `updlock/holdlock` em `u_fichaint`. O obsoleto faz o UPDATE das Planeadas e compara as linhas afetadas com `PlaneadasConfirmadas` (nulo conta como 0); se diferir, desfaz tudo e responde 409 `planeadas_mudaram`. Apagar tarefa lê as fichas feitas com `nolock` na mesma transação.
- **5 e 6**: os ecrãs de instruções, tarefas e equipamento ignoram um segundo gravar enquanto há um a decorrer. Gravar e atualizar a lista são separados: se a gravação passou, diz-se; a falha da lista é só da lista.
- **7**: edição de uma versão da instrução na UI (versão, data, observações, com token) e "Reativar". Reativar não regenera as planeadas já canceladas.
- **8**: `(nolock)` em todas as leituras fora de transação: `Selecao`, `SqlAtual`, `u_configman`, lista de equipamentos, `SqlCandidatas`, tarefas do equipamento, anexos, números do ano, material, deteção de avaria repetida, pesquisa, detalhe da ficha e ficha manual. Ficam sem `nolock`, de propósito: os UPDATE/DELETE, os `exists` do `SqlGerar` (protegem a escrita), o recálculo de totais e a verificação da ref e do nº interno do equipamento (nunca com hints de bloqueio).
- **9**: criar ou editar equipamento usa o semáforo `Numeracao` à volta da secção crítica (ref e nº interno).
- **10**: código 409 `instrucao_obsoleta` com mensagem própria; as mensagens de gestão deixaram de dizer "intervenção" (agora "ficha").
- **12**: gravar um equipamento com obsoleto cancela as Planeadas em cada gravação, como o Landeiro (`ManutencaoService.cs:161`), não só na transição. O `SqlGerar` revalida no insert o equipamento, a tarefa e a instrução.
- **13**: a tarefa gravada com o equipamento ou a instrução obsoletos devolve `Aviso` (e não gera); o ecrã mostra-o.
- **14**: a `GestaoApiService` lança explicitamente `HttpRequestException` 401 (o que o resto da app reconhece como sessão terminada); os ecrãs mostram "A sessão terminou. Entre outra vez." (`MensagensGestao`).
- **15**: depois de gravar equipamento, instrução ou tarefa, a app limpa os contadores de 30 s e atualiza a cópia local das fichas (`AposEscritaDeGestaoAsync`).
- **16**: o repositório em memória recusa viaturas em instruções, tarefas, listagens e ficha manual; o seed ganhou uma viatura (E8) e uma tarefa dela. Testes de IDOR (id de uma coisa usado como outra), de viaturas em todos os caminhos e de `PlaneadasConfirmadas` nulo com 0 planeadas.
- **17, 18, 19 e 22**:
  - "Recarregar" com formulário preenchido pede confirmação antes de descartar.
  - O botão de voltar nas tarefas é um link real (para as instruções do equipamento) e as etiquetas aria foram corrigidas (sem `aria-pressed` nos passos; sem classe morta).
  - Um equipamento antigo com estado vazio não ganha estado nem histórico ao editar se o utilizador não mexeu.
  - No rollback, a exceção original não fica escondida.

Diferenças que ficam em relação ao Landeiro, e o que não se fez:

- **11 (não feito, decisão)**: marcar uma instrução como obsoleta cancela as planeadas sem pedir contagem, como o Landeiro. Só o equipamento pede confirmação com o nº.
- **1 (não feito)**: o SQL continua por validar numa base; fica para o utilizador.
- **20 e 21 (não feitos)**: por instrução do coordenador; os `isnull` em colunas de leitura ficam como estão.

Continua a nunca ter corrido numa base tudo o que está na lista da secção 3, agora também: o `UPDATE` das Planeadas no obsoleto contado por `ExecuteNonQuery`, o `exists` revalidado no `SqlGerar` e os `(nolock)` novos.
