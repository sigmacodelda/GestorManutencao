# Assinatura da empresa externa

Numa ficha de manutenção externa (`tipomanut = 'Externa'`) a empresa que fez o trabalho assina no telemóvel do técnico: desenho + nome de quem assina.
A assinatura é obrigatória para concluir a ficha, fica gravada uma só vez (imutável) e aparece na ficha completa, na secção "Externo", com o nome e a data/hora.

## Campos novos em `u_fichaint` (já criados no PHC)

```sql
ALTER TABLE u_fichaint ADD
    assinaturaext     text         NOT NULL DEFAULT '',          -- PNG em data URL: "data:image/png;base64,..."
    assinaturaextnome varchar(60)  NOT NULL DEFAULT '',          -- nome de quem assinou
    assinaturaextdata datetime     NOT NULL DEFAULT '19000101';  -- 1900-01-01 = sem assinatura
```

O memo lê-se com `cast(isnull(assinaturaext, '') as varchar(max))`. Se estes campos não existirem na base, as listas de intervenções deixam de abrir (a API lê `assinaturaextdata` em todas as listas); a ficha completa, essa, abre na mesma, sem assinatura.

## Circuito

1. O técnico escolhe "Feito" numa ficha externa em curso. Se ainda não tem assinatura, o ecrã mostra a tela de desenho e o campo "Nome de quem assina" (2 a 60 letras); sem os dois, não conclui.
2. A app envia `POST /intervencoes/{id}/assinatura-externa` com `{ nome, assinatura }` e, a seguir, `POST /intervencoes/{id}/concluir`.
3. `ConcluirAsync` recusa uma ficha externa sem assinatura: 409 `externa_sem_assinatura`. Vale sempre, mesmo com a configuração "externa exige documento" desligada.
4. A repetição de uma conclusão já feita continua a devolver 200 (idempotente).

### Rota `POST /intervencoes/{id}/assinatura-externa`

Técnico da ficha (principal ou adicional) ou supervisor; a Qualidade nunca (403).

| Resposta | Quando |
|---|---|
| 200 | Gravada, ou repetição idêntica (mesmo nome e mesmo desenho) |
| 400 | Nome com menos de 2 ou mais de 60 letras, ou desenho que não é um PNG em data URL válido (até 200 000 caracteres) |
| 404 | A ficha não existe ou não é do técnico |
| 409 `nao_externa` | A ficha não é de manutenção externa |
| 409 `ja_assinada` | Já tem outra assinatura (nunca se altera) |
| 409 `conflito` | A ficha não está em curso nem concluída por fechar |

O UPDATE só grava com `assinaturaextdata <= '19000101'`; se não gravar, a API distingue "já assinada" de "já não aceita". Usa a data/hora do servidor da base e a auditoria habitual (`usrinis`, `usrdata`, `usrhora`).

### Leitura

- `IntervencaoDto` e o `Intervencao` da app trazem `Externa` e `TemAssinaturaExterna` (para a app saber que tem de pedir a assinatura e não a pedir duas vezes).
- `FichaDetalheDto` traz `AssinaturaExt`, `AssinaturaExtNome` e `AssinaturaExtData` (nula sem assinatura). Visível a todos os que veem a ficha, incluindo o operador (a ocultação de custos não a afeta).

## Sem rede

A assinatura entra na fila de saída como `AssinarExterna` (carga `{ nome, assinatura }`, JSON em SQLite sem limite de tamanho: ~10 a 60 KB cada) e a cópia local da ficha passa a `TemAssinaturaExterna`, para o técnico poder concluir. A fila corre por ordem, por isso a assinatura segue antes da conclusão da mesma ficha; se falhar por rede, pára tudo e retoma depois, e se o servidor a recusar (`ja_assinada`, `nao_externa`) a assinatura e a conclusão dessa ficha ficam em "Por rever", com o nome de quem assinou guardado no aviso.
