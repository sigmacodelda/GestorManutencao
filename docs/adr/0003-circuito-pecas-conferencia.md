# ADR 0003: Saída de peças em conferência e localizações em `u_pecaloc`

- Data: 2026-11-20
- Estado: aceite (substitui o ADR 0002 na saída e na entrada)

## Contexto
O circuito inicial dava entrada nesta app e uma saída que movia logo o stock do PHC ao juntar o material. Isso não deixava ao supervisor conferir antes de o stock descer, e a entrada já existe na LandeiroMaui. Faltava ainda saber de que lugar da prateleira sai cada peça. O Claude Code não cria tabelas nem campos no PHC.

## Decisão
- A entrada fica só na LandeiroMaui; esta app retira-a.
- A saída é um dossier 63 por ficha, em conferência (`bo.emconf = 1`, linhas `bi.emconf = 1`): não move o stock real do armazém 104, só reduz o disponível. Ao validar a ficha, passa a `emconf = 0` e dão-se os toques (`UPDATE bo SET [no]=[no]`, `UPDATE bi SET [ref]=[ref]`) que fazem os triggers do PHC moverem o stock.
- `u_pecaloc` (tabela da LandeiroMaui) é a fonte das localizações. A arrumação é feita aqui, o picking desconta do lugar escolhido e a anulação ou o cancelamento repõem.
- O lugar de cada saída guarda-se em `bi.lobs` como `<stamp do material>|<lugar>` (`varchar(40)`), partindo no `|` porque os stamps de `u_matinterv` têm menos de 25 caracteres. Não se usa `bi.lobs2` porque o dicionário base do PHC não o tem.
- Validar valida primeiro a ficha e confirma depois a conferência, para o stock nunca se mover numa ficha por validar. Se a confirmação falhar, a ficha fica validada com o aviso "Conferência por confirmar" e repete-se (idempotente).
- Falhas parciais nunca desfazem o estado da ficha: ficam sinalizadas ("peça por dar", conferência por confirmar ou cancelar) e repetíveis. Sem stock suficiente avisa e não bloqueia.

## Alternativas consideradas
- Saída que move logo o stock (ADR 0002): sem conferência do supervisor, e anular obrigava a movimentos inversos no PHC.
- Confirmar a conferência antes de validar a ficha e recusar a validação se falhar: mais seguro para o stock, mas a ficha ficava presa por uma falha do PHC. Ficou a opção com aviso e repetição.
- Tabelas `u_` novas para lugares: obrigava a criar tabelas e a reconciliar com a `u_pecaloc` da LandeiroMaui.
- Guardar o lugar em `bi.lobs2`: o dicionário base não o tem.
- Gravar o stamp de `bi.lobs` em posição fixa de 25 caracteres: os stamps reais são mais curtos.

## Consequências
O stock do PHC continua a ser a única fonte e só se move na validação. Verificado na base TESTES_LAND2 em 2026-11-20: com `emconf = 1` o stock real não se move, ao confirmar desce, cancelar antes de confirmar apaga o dossier e deixa o stock igual, cancelar depois é recusado e confirmar duas vezes não desce duas. Riscos que ficam: a LandeiroMaui arruma em `u_pecaloc` sem o mesmo applock e a tabela não tem índice único por lugar (duplicação possível com arrumações simultâneas); `bi.lobs` pode ser usado por outras rotinas do PHC do cliente (confirmar); os triggers de `bo` e `bi` são encriptados, por isso o circuito depende deles sem os poder ler; uma atualização do PHC que mude o `emconf` ou os triggers pode afetá-lo. Câmara, PDA e foco ficam por testar em aparelho.
