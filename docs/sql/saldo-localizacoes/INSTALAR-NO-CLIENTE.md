# Instalar o saldo por localização no servidor do cliente

O saldo de `u_pecaloc` passa a ser mantido só pelo trigger `sc_bi_pecaloc`. Nem a API do Gestor nem o Landeiro escrevem nessa tabela.

## 1. Antes de começar

- Instalar com **0 fichas de manutenção em conferência** (sem isto, o material já descontado desce duas vezes ao validar). Confirmar com SELECT:

```sql
SELECT DB_NAME() AS base;
SELECT COUNT(*) AS fichas_em_conferencia FROM bo WITH (NOLOCK) WHERE ndos = 63 AND emconf = 1;   -- tem de dar 0
```

- Existem `u_pecaloc` (com `marcada`) e `u_configman.localentrada`. O buffer (`localentrada`) está preenchido.
- Quem instala conhece o nome exato da base do cliente.
- Nenhuma decisão sobre índices: **não se cria nenhum índice** (decidido).

## 2. Scripts a correr no SQL Server (por esta ordem)

Pasta `docs/sql/saldo-localizacoes/`. Em cada um, no início, a guarda `IF @bd NOT IN (N'TESTES_LAND2')` tem de passar a incluir o nome da base do cliente, por exemplo `N'TESTES_LAND2', N'<base do cliente>'`. Sem isso o script recusa-se a correr.

| # | Script | Cria |
|---|--------|------|
| 1 | `01-fn_sc_LerLugar.sql` | funções `fn_sc_Inteiro`, `fn_sc_QtdOk`, `fn_sc_Qtd`, `fn_sc_LerLugar` |
| 2 | `02-trigger-sc_bi_pecaloc.sql` | trigger `sc_bi_pecaloc` em `bi` (mantém o saldo a partir dos dossiers 63, 64 e 66) |
| 3 | `03-trigger-sc_u_matinterv_del.sql` | trigger `sc_u_matinterv_del` em `u_matinterv` |
| 4 | `04-trigger-sc_u_fichaint_del.sql` | trigger `sc_u_fichaint_del` em `u_fichaint` |
| 5 | `05-views.sql` | vistas `v_sc_pecaloc_disponivel`, `v_sc_pecaloc_semlugar`, `v_sc_pecaloc_divergencias` |

Não correr `06-testes-rollback.sql` no cliente (é só para a base de testes). `00-desinstalar.sql` é a marcha-atrás.

Depois de cada script, confirmar que o `SELECT DB_NAME()` mostra a base certa e que o objeto existe:

```sql
SELECT name, type_desc FROM sys.objects WHERE name LIKE 'sc[_]%' OR name LIKE 'fn[_]sc[_]%' OR name LIKE 'v[_]sc[_]%';
```

## 3. Aplicações (publicar logo a seguir aos scripts, no mesmo dia)

1. **API do Gestor de Manutenção**: versão da `main` (commit `3a5c227` ou posterior). Exige as vistas e o trigger (sem eles arrumar e mover recusam com 409).
2. **Landeiro (LandeiroMaui)**: build com a alteração que deixa de escrever em `u_pecaloc` (`PecasLocalizacaoService` e `PecasMovimentoService`).
3. **Todos os postos Landeiro têm de ficar atualizados** (LandeiroUpdater). Um posto com a versão antiga continua a gravar o saldo à mão e, com o trigger ativo, conta duas vezes.

## 4. Verificação depois de instalar

```sql
SELECT * FROM v_sc_pecaloc_divergencias;   -- esperado: vazio (ou só o que já existia)
SELECT * FROM v_sc_pecaloc_semlugar;       -- linhas sem lugar legível: rever
```

Teste rápido (uma peça de teste): dar entrada, arrumar do buffer para um lugar, mover, juntar material a uma ficha, validar. O saldo do lugar tem de mexer uma só vez em cada passo.

## 5. Limites conhecidos

- Sem índice único por lugar, a rara corrida de duas arrumações no mesmo lugar novo pode duplicar a linha. Aparece em `v_sc_pecaloc_divergencias` (tipo `DUPLICADO`).
- Anular uma entrada tira a quantidade só do buffer. Se a peça já tinha sido arrumada, o buffer fica negativo (tipo `NEGATIVO` nas divergências) e corrige-se com uma arrumação ou inventário.
- Mover a partir de um lugar com quantidade "não indicada" (0) é recusado: indicar primeiro a quantidade.
