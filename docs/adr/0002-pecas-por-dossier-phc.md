# ADR 0002: Movimentos de peças por dossiers nativos do PHC

- Data: 2026-11-20
- Estado: substituído por ADR 0003 (circuito de conferência). Já não valem a entrada nesta app (dossier 64) nem a saída que move logo o stock.

## Contexto
A app precisa de ligar o material de uma ficha ao stock do PHC. A LandeiroMaui já usa dossiers nativos do PHC para isso. O Claude Code não cria tabelas nem campos no PHC.

## Decisão (o que continua válido)
Um dossier 63 por ficha, no armazém 104, com uma linha `bi` por material ligada por `bi.lobs` ao material (`u_matinterv`). As peças são os artigos com `stobs.u_manut = 1`. A API grava `bo`, `bo2`, `bo3`, `bi` e `bi2` sob `sp_getapplock` e em transação, sem criar tabelas nem campos e sem mexer nos triggers. Quando e como o stock se move, e o que `bi.lobs` guarda além do stamp, está no ADR 0003.

## Alternativas consideradas
- Tabelas `u_` novas com stock próprio: segunda fonte de stock a reconciliar com o PHC.
- Só gravar a peça em `u_matinterv.ref` sem movimento: o stock do PHC não refletia o consumo.
- Chamar o PHC por API ou WebService: mais dependências, e o circuito SQL já é o da LandeiroMaui.

## Consequências
O stock é o do PHC, sem segunda fonte, e a API depende das colunas e dos triggers do PHC. Ver o ADR 0003 para a verificação na base de testes e os riscos.
