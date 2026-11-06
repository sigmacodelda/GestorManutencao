# Ligação das intervenções ao PHC

A API lê e grava nas tabelas que já existem no PHC e segue o circuito da manutenção da **LandeiroMaui** (`Services/Manutencao/IntervencaoService.cs`, `Helpers/EstadosIntervencao.cs`, `Helpers/PeriodicidadeHelper.cs`), para as duas apps tratarem as mesmas fichas da mesma forma. Não cria tabelas nem campos.

## Tabelas usadas

| Tabela | Para quê |
|---|---|
| `u_fichaint` | Ficha de intervenção: uma linha por intervenção |
| `u_equip` | Equipamentos (pesquisa, zona `local`, tipo; as viaturas ficam de fora) |
| `u_matinterv` | Material usado numa ficha (design, qtt, epv, custo) |
| `u_configman` | Configuração da manutenção (uma linha): antecedência, gerar planeadas, avançar fora de ordem, validar a própria, exigir documento nas externas, compras |
| `u_itmanutli`, `u_itmanut`, `u_periocid` | Tarefas e periodicidades, só para gerar a planeada seguinte |
| `u_manutil` | Utilizadores da app, com o número do PHC (`tecno`): ver `docs/tabela-utilizadores.md` |

## Estados (campo `estado`, texto)

`Planeada` → `EmCurso` → `Concluida` → `Validada` (fecha a ficha); `Concluida` → `EmCurso` (devolvida); `Planeada`/`EmCurso` → `Cancelada`. Vazio = ficha antiga, fora do circuito: a app não lhe mexe. "Atrasada" é derivado (planeada ou em curso com data anterior a hoje), nunca gravado.

Nota: uma versão anterior desta ligação deduzia "em curso" da hora de início e "validada" do campo `valinis`. Estava errado: o PHC e a LandeiroMaui usam o próprio campo `estado`.

## Técnico

A identidade da pessoa nas fichas é o **número de utilizador do PHC** (`u_fichaint.tecno`, o `us.userno`), com o nome em `tecnome` (45 letras). O número vem de `u_manutil.tecno`, que o supervisor indica na app (Equipa). Quem não tem número entra, mas não vê fichas (a app explica e pede ao supervisor). As colunas de auditoria (`usrinis`, `ousrinis`, `valinis`, 30 letras) levam o código do utilizador da app.

## O que cada ação grava

- **Começar:** `estado = EmCurso`, `hini` só se estiver vazio, `tecno`/`tecnome` só se a ficha não tinha dono, `foraordem`. Só de uma ficha `Planeada` do técnico ou sem dono. Tem de ser a primeira planeada da fila da zona (`u_equip.local`, por data, ordem e número), salvo se `u_configman.avancaordem` deixar avançar.
- **Concluir:** `estado = Concluida`, `resolucao`, `hini` e `hfim` (as horas indicadas, no dia em que foi iniciada), `data` = esse dia. Só de uma ficha `EmCurso` do técnico. Se `extexigedoc`, uma ficha externa exige fornecedor e documento externo. Depois gera a planeada seguinte da tarefa (se `gerarplaneadas`).
- **Validar (supervisor):** `estado = Validada`, `fechada = 1`, `supervisao = 1`, `valinis`, `valdata`. Só de uma ficha `Concluida`. Se `validarpropria` for falso, quem concluiu não valida a própria (compara o `tecno`). Mesma regra do documento externo.
- **Devolver (supervisor, com motivo):** `estado = EmCurso`, `valobs` = motivo, `hfim` vazio; apaga a planeada seguinte ainda por tocar (sem dono, tempos nem materiais), que volta a gerar-se quando for concluída outra vez.
- **Atribuir técnico (supervisor):** `tecno`/`tecnome` de uma ficha `Planeada` ou `EmCurso` (0 = sem dono).
- **Gerar planeadas (supervisor):** data da seguinte = última feita + periodicidade (`u_periocid`, dias ou meses), nunca antes de hoje; uma só planeada aberta por tarefa; número = maior número do ano + 1 (ignora os maiores que 1 000 000).
- **Registar avaria:** cria uma ficha `Curativa`, `Interna`, `EmCurso`, com `hini` = agora, `tecno`/`tecnome` do técnico, `ref`, `design` e `u_equipstamp` do equipamento e o número seguinte do ano.
- **Material usado:** insere/apaga em `u_matinterv` (`design` até 35 letras, `qtt`, `epv`, `custo = qtt × epv`) e recalcula os totais da ficha como a LandeiroMaui. Só com a ficha em curso e do técnico.
- Se a ficha mudou entretanto, a API devolve conflito (409) e a app pede para abrir a lista outra vez. Os 409 trazem o motivo (`fora_de_ordem`, `externa_sem_documento`, `propria`, `conflito`).
- As datas e horas gravadas (início, validação, auditoria) são as do **servidor SQL** (`GETDATE()`), como na LandeiroMaui; o fuso da API deixa de importar. As horas que o técnico indica ao concluir ficam no dia em que iniciou.

## O que a lista do técnico mostra

Fichas `Planeada` ou `EmCurso`, sem viaturas, que são do técnico (`tecno` igual ao seu) ou ainda não têm dono e têm data até hoje mais `antecedencia` dias (7 na base de testes). As do próprio técnico aparecem sempre, mesmo atrasadas há muito tempo.

## Pressupostos e decisões por confirmar com a Landeiro

- Pressuposto: "Pedir peça" passa a ser **material usado na ficha** (`u_matinterv`), porque a LandeiroMaui não tem pedido ao armazém. Um pedido que o armazém aprove precisa de tabela nova no PHC.
- Pressuposto: o supervisor também pode ter número do PHC (para a regra de não validar a própria ficha); sem número, não se aplica a regra.
- Pressuposto: o técnico só conclui, devolve ou lança material em fichas suas (ou sem dono que ele inicia). A LandeiroMaui deixa qualquer pessoa concluir; aqui é mais restrito de propósito.
- Pressuposto: sem a opção de cancelar na app (a LandeiroMaui tem); fica para depois.
- Pressuposto: os totais de compras (`fn`, `fo`, `cm1`) só se somam se `comprasfichaint` estiver ativo; está desativado na base de testes, por isso esse caminho do código não foi exercitado.
- Risco: o número seguinte da ficha é o maior do ano + 1, como na LandeiroMaui; duas pessoas a criar ao mesmo tempo podem repetir o número (acontece também lá). Na base de testes o maior número do ano é 209105 (valor estranho já existente), por isso as avarias criadas pela app recebem números dessa ordem.
- Por decidir: turnos que passam da meia-noite (a hora de fim não pode ser antes da de início); uma avaria enviada duas vezes (ligação lenta) cria duas fichas; se um supervisor mudar o número de alguém, as fichas já iniciadas continuam com o número antigo.
- O login SQL da API precisa de `SELECT`, `INSERT` e `UPDATE` em `u_fichaint`, `u_matinterv` e `u_manutil`; `DELETE` em `u_fichaint` (devolver) e `u_matinterv`; e `SELECT` em `u_equip`, `u_configman`, `u_itmanutli`, `u_itmanut`, `u_periocid`, `u_tempinterv` (e `fn`, `fo`, `cm1` se as compras estiverem ativas).

## Verificações já feitas (base TESTES_LAND2)

Teste direto da camada SQL (sem passar pelo login, porque `u_manutil` ainda não tem o campo `tecno`): lista do técnico, começar (e recusar outro técnico e repetição), material (juntar, listar, apagar, totais), concluir (data e horas, gera a seguinte), devolver (apaga a seguinte) e concluir de novo, validar (`Validada`, fechada, supervisão), atribuir e remover técnico, planeadas, avaria (número do ano, em curso) e a geração geral (215 já tinham planeada aberta, 158 tarefas sem periodicidade, nada criado). Tudo reposto no fim: a ficha real voltou aos valores originais e o que o teste criou foi apagado (nada sobra).

Não testado na base real: o login e a Equipa com `tecno` (o campo ainda não existe em `u_manutil`), a regra de documento externo (`extexigedoc` está desligado), a regra de validar a própria (`validarpropria` está ligado) e o caminho de compras.
