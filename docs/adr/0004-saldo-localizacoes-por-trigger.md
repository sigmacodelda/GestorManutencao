# ADR 0004 — Saldo por localização mantido por trigger

Data: 2026-11-26 · Estado: aceite pelo utilizador (desenho aprovado) · Detalhe: `docs/superpowers/specs/2026-11-26-saldo-localizacoes-trigger-design.md`

## Decisão
A `u_pecaloc` passa a ser mantida por um trigger novo (`sc_bi_pecaloc`) sobre `bi`, a partir dos dossiers 63 (saída), 64 (entrada) e 66 (arrumação). As apps (esta, a LandeiroMaui) e o PHC só leem; o saldo disponível vem de `v_sc_pecaloc_disponivel` (saldo menos o reservado em conferência, `emconf = 1`).

## Porquê
Três escritores (esta app, a LandeiroMaui e o PHC) e dossiers que já levam o lugar na linha. Verificado em TESTES_LAND2 com rollback: os triggers existentes do PHC só movem o stock `sa`, nunca a `u_pecaloc`.

## Autorizações registadas (norma: objetos além de SPs, views e triggers)
- Funções `fn_sc_LerLugar`, `fn_sc_Inteiro` e `fn_sc_Qtd`: autorizadas pelo utilizador ao aprovar o desenho (26/11/2026).
- Índice em `u_pecaloc(ref, armazem)`: **não autorizado**; recomendado ao cliente (decisão dele), porque a tabela só tem a chave primária e o trigger consulta por referência.
- Sem tabelas nem campos novos. Sem hints de locking (decisão do utilizador).

## Pré-requisitos para instalar em cliente (revisão 2, ainda por resolver)
- **Arranque sem dupla contagem:** a API antiga já descia a `u_pecaloc` ao juntar material. As fichas com `emconf = 1` e lugar existentes à data da instalação já estão descontadas: a view volta a subtrair como reservado e o trigger desce outra vez ao validar. É preciso um levantamento dessas linhas e uma correção única (ou decisão de as ignorar) antes de instalar, e confirmar que a LandeiroMaui deixou de escrever na `u_pecaloc`.
- **Índice em `u_pecaloc(ref, armazem)` passa a pré-requisito de cliente:** sem ele cada execução do trigger varre a tabela e pode bloquear ou causar deadlock (1205, que anula o dossier). Continua por autorizar.
- Concorrência (duas transações no mesmo lugar) não é coberta pelos testes de rollback, que correm numa só sessão.

## Consequências
- O saldo do lugar só desce quando a ficha é validada (`emconf` 1 para 0); até lá a peça conta como reservada.
- Apagar material ou ficha só é permitido com `emconf = 1`; com `emconf = 0` o trigger recusa.
- Os scripts só se instalam em bases de teste (guarda no início); instalar em cliente exige GATE do utilizador.
- Deadlock ou `SET XACT_ABORT ON` ainda podem anular a transação de um dossier; erros de dados só geram aviso.
