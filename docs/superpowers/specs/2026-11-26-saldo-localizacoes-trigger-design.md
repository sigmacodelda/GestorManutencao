# Saldo por localização mantido por trigger (u_pecaloc)

Estado: **aprovado e implementado em TESTES_LAND2** (scripts em `docs/sql/saldo-localizacoes/`, ADR 0004). Instalação em cliente pendente dos pré-requisitos do ADR.

## 1. Objetivo

A `u_pecaloc` passa a ser o saldo físico por lugar, mantido por trigger a partir das linhas `bi`. A app, a LandeiroMaui e o PHC deixam de a escrever à mão: só a leem. O que está em conferência (`emconf = 1`) não conta no saldo; conta como reservado numa view.

## 2. Regra única: efeito de uma linha `bi` na `u_pecaloc`

Cada linha tem um efeito, calculado só pelos seus campos. O trigger aplica **efeito(inserted) − efeito(deleted)**. Com isto, inserir, editar (quantidade, lugar, `emconf`) e apagar ficam todos tratados pela mesma regra, sem casos especiais.

| Dossier | Condição | Efeito no saldo |
|---|---|---|
| 63 saída | `emconf = 0` | −`qtt` no lugar de `bi.lobs` (parte a seguir ao `\|`) |
| 63 saída | `emconf = 1` | nenhum (só reservado, ver §5) |
| 64 entrada | `emconf = 0` | +`qtt` no **buffer** (`u_configman.localentrada`); sem `lobs` |
| 66 arrumação | `emconf = 0` | −`qtt` na origem (`bi.lobs`) e +`qtt` no destino (`bi.lobs2`); origem ou destino vazio/ilegível = essa perna não conta (revisão: mais seguro do que adivinhar) |

Linha sem lugar legível (`lobs` vazio ou formato inválido): efeito 0 e o documento grava na mesma. Fica listada na view `v_sc_pecaloc_semlugar` para corrigir. O trigger nunca falha por dados do utilizador, porque um erro dele faria rollback do dossier no PHC.

Suposições a confirmar: a entrada 64 e a arrumação 66 também só movem com `emconf = 0`; o sinal (entra ou sai) vem do `ts` do dossier e não de um valor fixo.

### Resultado do Passo 0 (TESTES_LAND2, 2026-11-26) — corrige o desenho acima

Experiência em transações com ROLLBACK (nada ficou gravado): inserir, tocar, editar `emconf` e apagar linhas `bi` dos dossiers 63, 64 e 66.

- **Os triggers existentes não mexem na `u_pecaloc`** em nenhum dos três dossiers. Só movem o stock `sa` (63 com `emconf = 0` desce, 64 sobe, 66 não altera). Quem escrevia na `u_pecaloc` era o código das apps. **Não há risco de contar em dobro**: o `sc_bi_pecaloc` trata de 63, 64 e 66.
- **`bo` não tem `lobs` nem `lobs2`** nesta base. No 66, origem e destino estão na **linha**: `bi.lobs` = origem (`BUFFER` ou lugar) e `bi.lobs2` = destino (`A-01-2-01`). A tabela da §2 lê-os da `bi`.
- No 63 a linha traz `bi.lobs = '<stamp>|<lugar>'` e muitas vezes `bi.lobs2 = <lugar>`: o lugar vem de `lobs2` se estiver preenchido, senão da parte de `lobs` depois do `|`.
- O lugar tem o formato `A-01-2-01` (corredor-estante-nível-posição); na `u_pecaloc` fica corredor `A`, estante `A-01`, nível `2`, posição `1`. O buffer é corredor `BUFFER`, nível 0, posição 0 (`u_configman.localentrada = BUFFER`).
- A referência da peça tem acentos (cp1252): os scripts não podem depender de literais acentuados.
- Base com nível de compatibilidade 150 (SQL Server 2019); mantém-se a escrita compatível com nível 100.
- A API que cria o 66 grava origem e destino em `bo.lobs` e `bo.lobs2`, colunas que não existem aqui: tem de passar a gravá-los na linha `bi`.

## 3. Trigger sobre `bi`

- Nome: `sc_bi_pecaloc` (trigger **novo**; os existentes, encriptados, não se tocam).
- `AFTER INSERT, UPDATE, DELETE`, set-based, sem cursores.
- Calcula os efeitos de `inserted` e `deleted`, agrega por (`ref`, armazém, corredor, estante, nível, posição) e aplica a diferença com `UPDATE` (`qtt = qtt + diferença`, atómico por linha) e depois `INSERT` dos lugares que ainda não existem. Linhas que ficam a 0 apagam-se. **Sem hints de locking** (`UPDLOCK`, `HOLDLOCK` e semelhantes ficam de fora, por decisão do utilizador). A rara corrida de dois `INSERT` do mesmo lugar novo não se resolve com locks: a `v_sc_pecaloc_divergencias` apanha lugares duplicados.
- O lugar lê-se com uma função `fn_sc_LerLugar(texto)` que reproduz `RegrasLocalizacao.LerLugar` (corredor, estante normalizada `A-03`, nível, posição). Testes comparam as duas com os mesmos exemplos.
- Compatibilidade SQL nível 100: sem `TRY_CONVERT`, `CONCAT`, `IIF` nem `THROW`. A `qtt` é `varchar(13)` (`7.500`), por isso converte-se com `CASE` + `ISNUMERIC` e `CAST(... AS decimal(13,3))`.
- Só armazém 104 e só dossiers 63, 64 e 66.

## 4. Ligações (apagar tem de limpar tudo)

**Como a linha `bi` se liga à intervenção** (já existe, não é preciso campo novo):

```
u_fichaint.u_fichaintstamp  =  bo.bostamp            (o dossier 63 tem o stamp da ficha)
bo.bostamp                  =  bi.bostamp            (linhas do dossier)
u_matinterv.u_fichaintstamp =  u_fichaint.u_fichaintstamp
u_matinterv.u_matintervstamp = parte de bi.lobs antes do '|'   (a linha do material)
bi.lobs                     =  '<u_matintervstamp>|<lugar>'
```

Por isso: apagar um `u_matinterv` apaga a `bi` com `bostamp = deleted.u_fichaintstamp` e stamp do material (antes do `|`) igual ao stamp apagado; apagar uma `u_fichaint` apaga o `bo` 63 com `bostamp = deleted.u_fichaintstamp`. O stamp do material parte-se no `|`, não por posição fixa (os stamps têm menos de 25 caracteres).

| Se se apaga… | Então… | Como |
|---|---|---|
| Linha `bi` | o saldo recalcula-se | `sc_bi_pecaloc` (efeito de `deleted`) |
| Dossier 63, 64 ou 66 | o PHC apaga as linhas `bi`, e o ponto acima repõe o saldo | PHC + `sc_bi_pecaloc` |
| Material da ficha (`u_matinterv`) | apaga-se a linha `bi` ligada (`bi.lobs` começa pelo stamp do material) | trigger novo `sc_u_matinterv_del` |
| Ficha (`u_fichaint`) | apaga-se o dossier 63 dessa ficha | trigger novo `sc_u_fichaint_del` |

Regra de segurança: só se apaga em cascata se `emconf = 1` (o stock real do PHC ainda não se moveu). Se a linha ou o dossier já estiver confirmado (`emconf = 0`), o trigger **recusa apagar o material ou a ficha** com mensagem clara, como já acontece hoje no "Cancelar depois de confirmar".

Risco: o apagar em cascata dentro de um trigger dispara os triggers encriptados do PHC em `bo` e `bi`. Tem de se verificar na base de testes antes de qualquer instalação (§8).

## 5. O que a app lê

View `v_sc_pecaloc_disponivel` (por ref, armazém e lugar):

`disponível = u_pecaloc.qtt − Σ bi.qtt das linhas do dossier 63 com emconf = 1 nesse lugar`

Assim dois técnicos não pedem a mesma peça, e a peça só sai de verdade do lugar quando a ficha é validada. É o mesmo princípio do "disponível = stock menos o que está em conferência" que já existe para o stock total.

Outras views (só leitura): `v_sc_pecaloc_semlugar` (linhas sem lugar legível) e `v_sc_pecaloc_divergencias` (artigos em que a soma da `u_pecaloc` com o buffer não bate com o stock `sa` do armazém 104, ou com saldo negativo).

## 6. O que muda na app

**Hoje** a API desconta e repõe a `u_pecaloc` ao juntar, editar, apagar e cancelar material ("a peça já saiu da prateleira"). **Passa a**: o saldo só desce quando a ficha é validada.

- Remover da API o desconto e a reposição de lugares (`DescontarLugar`, "Lugares por repor", "Conferência por cancelar" só fica para a conferência).
- Juntar, editar e apagar material passam a mexer só na linha `bi`; o trigger trata do resto.
- Escolher o lugar na ficha usa `v_sc_pecaloc_disponivel`.
- Arrumar e mover continuam a criar o dossier 66; deixa de haver qualquer escrita direta na `u_pecaloc` (já só existia na estrutura do armazém e nos testes).
- O inventário continua como está (fora do trigger).

## 7. O que se pede ao PHC (cliente)

- Nenhuma tabela nem campo novo.
- Scripts a instalar na base de **testes** primeiro: `fn_sc_LerLugar`, `sc_bi_pecaloc`, `sc_u_matinterv_del`, `sc_u_fichaint_del`, e as três views.
- Recomendado (decisão do cliente): índice único por lugar em `u_pecaloc` (ref, armazém, corredor, estante, nível, posição), porque sem ele duas escritas em simultâneo podem duplicar o lugar.

## 8. Passo 0 e verificação (antes de instalar nada)

**Passo 0, obrigatório:** o trigger existente da LandeiroMaui já move a `u_pecaloc` nas arrumações (66) e talvez nas entradas (64). Instalar o novo sobre o mesmo efeito contaria tudo duas vezes. Na base de teste (`SELECT DB_NAME()` primeiro) fazem-se um 64, um 66 e um 63 com `emconf = 0`, e comparam-se a `u_pecaloc` antes e depois. O que o trigger existente já fizer fica de fora do novo. Se o existente tratar de tudo menos a saída 63, o `sc_bi_pecaloc` fica só com o 63 e com a cascata.

Casos de teste (todos em TESTES_LAND2):
1. Saída 63 com `emconf = 1`: saldo igual, disponível desce.
2. Validar (`emconf = 0`): saldo desce uma vez; repetir a validação não desce duas.
3. Editar quantidade e lugar antes e depois de validar.
4. Apagar material e ficha com `emconf = 1`: linha e dossier apagam-se, disponível volta.
5. Apagar com `emconf = 0`: recusa.
6. Entrada 64: soma no buffer; apagar o dossier repõe.
7. Arrumação 66: sai do buffer, entra no destino; apagar repõe.
8. `lobs` vazio ou inválido: documento grava, linha aparece em `v_sc_pecaloc_semlugar`.
9. Inserção de várias linhas numa só instrução (set-based).
10. Paridade entre `fn_sc_LerLugar` e `RegrasLocalizacao.LerLugar`.

## 9. A decidir

1. Aprovar a regra de efeito da §2 (incluindo 64 e 66 só com `emconf = 0`).
2. Aprovar a mudança de comportamento da §6 (saldo só desce ao validar, em vez de ao juntar o material).
3. Aprovar a cascata de apagar da §4 e a recusa quando já confirmado.
4. Aprovar o Passo 0 na base de testes (preciso do nome da base; a connection string fica no `appsettings`).
