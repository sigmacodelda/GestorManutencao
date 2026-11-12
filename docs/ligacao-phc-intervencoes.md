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

- **Início e fim (nov/2026):** `u_fichaint` já não tem `hini`/`hfim`. O início da ficha é o menor `dataini` e o fim o maior `datafim` das linhas de `u_tempinterv` (uma ficha `Planeada` ou `EmCurso` não tem fim). `dataini`, `datafim`, `pausaini` e `pausafim` pertencem à linha de tempo do técnico.
- **Começar:** `estado = EmCurso`, abre a linha do técnico em `u_tempinterv` (`dataini` = agora, `tempo` e `custo` a 0, se a ficha ainda não tem início), `tecno`/`tecnome` só se a ficha não tinha dono, `foraordem`. Só de uma ficha `Planeada` do técnico ou sem dono. Tem de ser a primeira planeada da fila da zona (`u_equip.local`, por data, ordem e número), salvo se `u_configman.avancaordem` deixar avançar.
- **Concluir:** `estado = Concluida`, `resolucao`, `data` = o dia em que foi iniciada. As horas indicadas fecham a linha aberta do técnico em `u_tempinterv` (`dataini`, `datafim`, `tempo` líquido de pausas, `custo` = tempo × último custo/hora do técnico; sem linha aberta cria-se uma já fechada) e recalculam os totais. Só de uma ficha `EmCurso` do técnico. Se `extexigedoc`, uma ficha externa exige fornecedor e documento externo. Depois gera a planeada seguinte da tarefa (se `gerarplaneadas`).
- **Validar (supervisor):** `estado = Validada`, `fechada = 1`, `supervisao = 1`, `valinis`, `valdata`. Só de uma ficha `Concluida`. Se `validarpropria` for falso, quem concluiu não valida a própria (compara o `tecno`). Mesma regra do documento externo.
- **Devolver (supervisor, com motivo):** `estado = EmCurso`, `valobs` = motivo (o fim deixa de aparecer por a ficha estar em curso; as linhas de tempo ficam); apaga a planeada seguinte ainda por tocar (sem dono, tempos nem materiais), que volta a gerar-se quando for concluída outra vez.
- **Atribuir técnico (supervisor):** `tecno`/`tecnome` de uma ficha `Planeada` ou `EmCurso` (0 = sem dono).
- **Gerar planeadas (supervisor):** data da seguinte = última feita + periodicidade (`u_periocid`, dias ou meses), nunca antes de hoje; uma só planeada aberta por tarefa; número = maior número do ano + 1 (ignora os maiores que 1 000 000).
- **Planeadas (supervisor):** cada linha mostra a última intervenção feita da tarefa (concluída ou validada; nas fichas antigas, a última do equipamento com a mesma periodicidade), a última cancelada e a data prevista (a mais recente das duas + periodicidade). Se a data prevista já passou, aparece "Fora de prazo desde" mesmo que a planeada esteja para hoje (LandeiroMaui, 5/11/2026).
- **Pausar / Retomar** (`POST /intervencoes/{id}/pausar` e `/retomar`): só de uma ficha `EmCurso` e do técnico (principal ou adicional); a pausa é **por linha de tempo de cada técnico** (`u_tempinterv.no` = técnico que pede) e atua na linha aberta do próprio utilizador; sem linha aberta (ficha devolvida, antiga, adicional sem linha) dá conflito e a API devolve `PodePausar = false` (a app só mostra Pausar/Retomar com `PodePausar`); `EmPausa` e `PodePausar` são relativos ao utilizador que consulta (a pausa de um técnico não aparece a outro nem nas listas do supervisor); o `estado` não muda (a LandeiroMaui só conhece Planeada, EmCurso, Concluida, Validada e Cancelada). Atua só em `pausaini`/`pausafim` da linha aberta do técnico em `u_tempinterv` (hora do servidor; sem linha aberta do técnico dá conflito). Há um só par de campos por linha, por isso (`pausafim` − `pausaini`) é sempre o total pausado: **Pausar** põe `pausaini` = agora e `pausafim` vazio; se já houve uma pausa terminada, desloca `pausaini` para (agora − duração da pausa anterior) para o total se manter; **Retomar** põe `pausafim` = agora. Em pausa = `pausaini` preenchido e `pausafim` vazio (a API devolve-o em `EmPausa`). Repetir o pedido (já está nesse estado) é inofensivo. **Concluir em pausa** fecha primeiro a pausa (`pausafim` = agora) e só depois conclui, para o `tempo` não contar a pausa (nunca fica negativo).
  - Limitações conhecidas da pausa: (1) offline a pausa regista a hora do **servidor** no momento da sincronização (a hora real do clique perde-se; a duração offline é aproximada). (2) Pausa que atravessa dias: a conta usa o dia de início + a hora de fim (limitação já existente). (3) `pausaini` fica deslocado (instante fictício) quando se pausa de novo depois de retomar; falta confirmar com a Landeiro como o PHC/LandeiroMaui mostram estes campos. (4) Pausa de duração zero ou relógio atrasado é indistinguível de pausa aberta (raro). (5) Pausar/Retomar recusados na sincronização são descartados sem marcar nem bloquear as outras operações da ficha.
- **Registar avaria:** cria uma ficha `Curativa`, `Interna`, `EmCurso`, com a linha de tempo aberta em `u_tempinterv` (`dataini` = agora), `tecno`/`tecnome` do técnico, `ref`, `design` e `u_equipstamp` do equipamento e o número seguinte do ano.
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
