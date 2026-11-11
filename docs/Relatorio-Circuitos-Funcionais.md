# Relatório dos circuitos funcionais (app Windows + API, base testes_land2)

Data: 11/11/2026. Execução final: 45 de 45 passos ok, 136 s, 404 pedidos à API. Relatório técnico da execução, com os pedidos à API de cada passo: `Testes-Circuito/resultados/2026-11-11T12-08-06-023Z_funcionais_y3odrj/relatorio.md`.

## Como se testou
- App Windows real (WebView2) controlada por Playwright por CDP: clica nos botões, escreve nos campos, lê o que aparece.
- API local ligada à base `testes_land2` (confirmada com `SELECT DB_NAME()` antes de qualquer escrita).
- Utilizadores de teste próprios (`e2e.sup4`, `e2e.tec4`), com PIN de 4 números aleatório, criados no início e apagados no fim.
- A cada passo regista-se: o que se clicou, os pedidos que a API recebeu (verbo, rota, estado, ms, lidos do log da API) e os erros da consola da app.
- Dados criados com marcador `TESTE-E2E-`, apagados no fim (3 fichas, 1 equipamento com instrução e tarefa, 1 operador). A periodicidade "Mensal" é reposta no valor original.

## Circuitos (todos ok)
| Circuito | Passos | O que cobre |
|---|---|---|
| 01 Login | 3 | PIN errado recusado; entrada automática ao 4.º número; técnico sem "Nova ficha" nem "Equipa" |
| 02 Equipamento | 6 | Validações dos campos obrigatórios, criar em 3 passos, ver a ficha, editar, referência repetida recusada |
| 02b Periodicidade | 3 | "Mensal" passa a 1 mês; intervalo vazio recusado; valor original reposto |
| 03 Instruções e tarefas | 4 | Criar versão, tarefa (descrição obrigatória), editar, a tarefa gera a planeada |
| 04 Planeadas | 4 | Pesquisa, contadores, atribuir ao técnico, "Gerar planeadas" |
| 05 Fichas, equipamentos, navegação | 3 | Pesquisa e filtros, KPIs, barra inferior do supervisor |
| 06 Periodicidades e configuração | 2 | Editar e repor; antecedência inválida (999) recusada |
| 07 Ficha manual e completa | 5 | Nova ficha, editar, horas de mão de obra, cancelar com motivo obrigatório |
| 08 Técnico | 5 | Planeada: Começar e Feito; avaria; material (juntar, editar, apagar); barra do técnico |
| 09 Validação | 4 | Validar, devolver (só com motivo), técnico vê o motivo e reconclui, "Validar e fechar" |
| 10 Obsoleto, equipa e PIN | 5 | Obsoleto com confirmação; criar operador com PIN provisório de 4 números; PIN fraco e diferente recusados; novo PIN de 4 números e entrada |

## Alterações feitas durante os testes
- PIN a 4 números: ecrã de login (entra ao 4.º), "Mudar PIN" (campos de 4), API (PIN novo de 4; o login ainda aceita PINs antigos de 4 a 6).
- Regras de geração das planeadas vindas do Landeiro (início do plano, limite de atraso, equipamento fora de serviço não gera), com testes de unidade.
- `Testes-Circuito/correr-circuito.ps1`: opções `-CriarUtilizadores` e `-Circuito`, log de pedidos da API, limpeza dos dados criados.

## Achados
1. **Polling elevado.** Em 136 s a app fez 76 pedidos a `/intervencoes/{id}/materiais`, 40 a `/intervencoes`, 30 a `/equipamentos/{id}`, 27 a `/planeadas` e 25 a `/validacao`. Os ecrãs repetem leituras com muita frequência; com rede fraca no chão de fábrica convém reduzir (ou só recarregar após uma ação).
2. **Base de testes sem intervalo na periodicidade "Mensal"** (unidade `nenhum`, 0). Com isso nenhuma tarefa mensal gera planeada. A mesma situação tem de ser verificada na base real; o circuito 02b configura-a e repõe-na.
3. **404 em `GET /intervencoes/{id}`** quando o supervisor abre, pela lista, uma ficha que já não é do técnico: aparece 2 vezes no passo "Validar e fechar". O passo passa, mas convém confirmar se é só uma leitura redundante ou um pedido a evitar.
4. **`POST /planeadas/gerar`** demora cerca de 2,5 s; nenhum outro pedido passou de 1,5 s.
5. Sem erros na consola da app durante toda a execução. Avisos do log da API: um, no PIN errado (esperado). Os 499 são pedidos cancelados ao navegar.

## Não coberto
- Toque físico e Android (o WebView2 do Windows não os substitui).
- Modo offline e fila de envios.
- Fotos (envio e apagar), anexos e compras ligadas.
- A comparação do esquema com a base real: o script `Testes-Circuito/comparar-esquema.ps1` está pronto, falta uma ligação à `land2`. Na `testes_land2` não falta nenhum campo usado pelo código.
