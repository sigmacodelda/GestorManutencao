# Circuito com a Qualidade (nov/2026)

Planeamento (supervisor) → Execução (técnico) → Validação (supervisor) → Conclusão (Qualidade).

## Estados e campos

Não há estado novo. `Validada` com `fechada = 0` é "validada, à espera da Qualidade"; a Qualidade conclui e a ficha fecha (`fechada = 1`). As fichas já validadas estão todas fechadas, por isso não entram na fila da Qualidade e não há migração. A LandeiroMaui continua a conhecer o estado `Validada`.

| Passo | O que grava |
|---|---|
| Validar (supervisor) | `estado = Validada`, `fechada = 0`, `supervisao = 1`, `valinis`, `valdata` |
| Devolver (supervisor, motivo obrigatório) | `estado = EmCurso`, `valobs` = último motivo, nova linha em `histdevol` (`yyyy-MM-dd HH:mm\|código\|motivo`) |
| Concluir (Qualidade) | `higienizadopor` (nome obrigatório, texto livre), `higienizadata`, `qualinis`, `qualdata`, `limrem = 1`, `fechada = 1`. Só grava se `higienizadopor` estiver vazio e a ficha for `Validada` não fechada: a higienização não se altera |

O histórico da ficha junta: técnico e fim da execução (`tecnome`, fim da linha de tempo), devoluções (`histdevol`), validação (`valinis`/`valdata`), higienização e conclusão. Os códigos de utilizador passam a nomes na API. Se os campos novos ainda não existirem na base, a ficha abre na mesma, sem histórico.

## A criar no PHC (por quem administra; primeiro na base de testes)

```sql
ALTER TABLE u_fichaint ADD
    higienizadopor varchar(60)  NOT NULL CONSTRAINT DF_u_fichaint_higienizadopor DEFAULT '',
    higienizadata  datetime     NOT NULL CONSTRAINT DF_u_fichaint_higienizadata  DEFAULT '19000101',
    qualinis       varchar(30)  NOT NULL CONSTRAINT DF_u_fichaint_qualinis       DEFAULT '',
    qualdata       datetime     NOT NULL CONSTRAINT DF_u_fichaint_qualdata       DEFAULT '19000101',
    histdevol      varchar(max) NOT NULL CONSTRAINT DF_u_fichaint_histdevol      DEFAULT '';

ALTER TABLE u_manutil DROP CONSTRAINT CK_u_manutil_perfil;
ALTER TABLE u_manutil ADD CONSTRAINT CK_u_manutil_perfil CHECK (perfil IN ('OPERADOR', 'SUPERVISOR', 'QUALIDADE'));
```

`limrem` já existe (lógico) e passa a 1 quando a Qualidade conclui. O login SQL da API precisa de `UPDATE` nos campos novos (já o tem em `u_fichaint`).

## Perfil Qualidade

- Criado na Equipa (opção "Perfil Qualidade"); o PIN provisório funciona como nos operadores. Não precisa de número do PHC.
- Só vê `GET /qualidade` (fichas por concluir) e conclui com `POST /qualidade/{id}/concluir`. Não usa as rotas de técnico nem de supervisor.
- A app abre logo no ecrã Qualidade.

## Pressupostos

- Supervisores podem criar utilizadores Qualidade; só o PIN de configuração cria supervisores.
- O técnico pode marcar `limrem` durante a execução (já era assim); a Qualidade volta a pô-lo a 1 ao concluir.
- Se a LandeiroMaui validar uma ficha, fecha-a directamente e salta a Qualidade.
- O ecrã da Qualidade lista as fichas sem o histórico completo (o endpoint `GET /qualidade/{id}/detalhe` já o devolve).
