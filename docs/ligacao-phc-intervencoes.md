# Ligação das intervenções ao PHC

A API lê e grava nas tabelas que já existem no PHC. Não cria tabelas nem campos.

## Tabelas usadas

| Tabela | Para quê |
|---|---|
| `u_fichaint` | Ficha de intervenção: uma linha por intervenção |
| `u_equip` | Equipamentos (pesquisa e zona) |

## Como a app lê a ficha (`u_fichaint`)

| Na app | No PHC |
|---|---|
| Identificador | `u_fichaintstamp` |
| Equipamento | `design` (ou o nome em `u_equip`) |
| Zona | `u_equip.local` |
| Tipo | `tipointerv` (`Curativa` = avaria, o resto = preventiva) |
| Data prevista | `data` |
| Problema | `problema` (máx. 200 letras) |
| O que foi feito | `resolucao` (máx. 254 letras) |
| Hora de início / fim | `hini` / `hfim` (1900-01-01 = sem hora) |
| Técnico | `tecnome` (máx. 45 letras; o nome da tabela `u_manutil`) |

Estados: `Planeada` e `Concluida` já existem no PHC. **Em curso** não existe: é uma ficha `Planeada` com `hini` preenchido. **Validada** é uma ficha com `valinis` preenchido.

## O que cada ação grava

- **Começar:** `hini` = agora e `tecnome` = nome do técnico. Só se a ficha estiver planeada, sem hora de início e sem dono (ou já dele).
- **Concluir:** `estado` = `Concluida`, `resolucao`, `hini` e `hfim`. Só se a ficha for do técnico e já tiver sido iniciada.
- **Registar avaria:** cria uma ficha `Curativa`, `Interna`, `Planeada`, com `hini` = agora, `tecnome` = o técnico, `ref`, `design` e `u_equipstamp` do equipamento, `no` = 0.
- Se a ficha mudou entretanto (outra pessoa mexeu), a API devolve conflito e a app pede para abrir a lista outra vez.

## O que a lista mostra

Fichas planeadas, sem validação, com data entre 180 dias atrás e 14 dias à frente, que são do técnico ou ainda não têm dono.

## Pressupostos por confirmar com a Landeiro

- Suposição: o técnico é identificado pelo nome (`tecnome`), porque a tabela `u_manutil` não tem o número do técnico. Se se quiser ligar ao número do PHC (`tecno`), é preciso um campo novo em `u_manutil` (`tecno numeric`, por defeito 0).
- Suposição: fichas sem dono (`tecno` = 0 e `tecnome` vazio) aparecem a todos os técnicos. Com muitas fichas planeadas isto enche a lista; convém o supervisor atribuir (campo acima).
- Suposição: uma avaria criada pela app fica com `no` = 0, porque não se sabe como o PHC numera as fichas (os números atuais não são sequenciais).
- O login SQL da API precisa de `SELECT`, `INSERT` e `UPDATE` em `u_fichaint` e `SELECT` em `u_equip`.

## Verificações já feitas

- Testado contra a base TESTES_LAND2: lista, pesquisa de equipamentos, registar avaria, começar e concluir. A ficha de teste foi apagada e a ficha real reposta.
- `u_fichaint` não tem colunas que aceitem vazio nem índices além da chave; as colunas `usrinis` e `ousrinis` têm 30 letras, por isso a auditoria grava o código do utilizador e só `tecnome` leva o nome.
- Dois operadores não podem ter o mesmo nome (as fichas identificam o técnico pelo nome). Se um supervisor mudar o nome de alguém, as fichas já iniciadas por essa pessoa deixam de lhe aparecer.

## Por decidir

- Turnos que passam da meia-noite: hoje a hora de fim não pode ser antes da de início.
- Fuso horário: as horas gravadas são as do servidor da API; se este estiver em UTC ficam desfasadas.
- Uma avaria enviada duas vezes (ligação lenta) cria duas fichas.
