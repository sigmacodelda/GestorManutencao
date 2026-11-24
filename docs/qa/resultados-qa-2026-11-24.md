# Resultado do QA (textez.xlsx, 24/11/2026): triagem

Origem: folha de testes do QA (A01–L01). Aqui só entram os "Falhou" e os reparos. Estado: **não verificado em código** salvo onde indicado; o QA terá testado o APK 1.25.

## 1. Falhas reais a corrigir
| Teste | Problema | Nota |
|---|---|---|
| D01 | Validação (supervisor) não mostra o material da ficha | Lista só tem problema e "feito" |
| C06 | Assinatura externa recusada: nem técnico nem supervisor a podem refazer | Falta ação "Assinar outra vez" |
| I08 | Peça sem lugar (P3) junta-se sem aviso | Esperado: aviso "sem localização" |
| I11 | Ao editar material, picar só o lugar não o troca | Só funciona à mão |
| I13 | Consulta manual da peça mostra tudo como "por arrumar", mesmo o arrumado | Cálculo ou texto |
| I04 | Picar peça P2 e lugar L1 e juntar falhou (sem nota do QA) | Reproduzir |
| J07–J09, L01 | "Cancelar intervenção" não é encontrado por supervisor nem técnico | Existe na ficha completa (`FichaCompleta.razor`): problema de descoberta |
| J10 | Técnico não consulta fichas validadas ou canceladas | Falta histórico do técnico |
| G01 | Técnico não pode pedir a devolução de uma ficha | Funcionalidade nova |

## 2. Decisões de negócio (por desenho hoje)
| Teste | Situação atual | Pergunta |
|---|---|---|
| B06, G04, G05 | Sem rede não se entra (o PIN valida na API) | Permitir entrada offline com o último utilizador? |
| H14, I16–I18 | Arrumar e material com peça só funcionam online | Pôr na fila offline? Risco de stock desatualizado |
| I09, H12 | Quantidade acima do stock só avisa (440 de stock 10); acima do "por arrumar" bloqueia | Bloquear também o material? |
| D08 | Supervisor novo pede PIN novo à primeira entrada | Correto? O teste esperava o contrário |

## 3. Reparos de UX
- A02, C02, D09, D10: avisos fora do sítio ou não flutuantes. Já corrigido nesta versão (avisos flutuantes), a confirmar no APK 1.26.
- B03/I01: ficha sem "Começar" nem tempo em curso, só "Pausar" e "Feito". Material sem preço.
- I15: sem separação entre material já juntado e o que se vai juntar.
- K02: teste de leitura pouco explícito. K05: mensagem de permissão da câmara só no Leitor.
- B02: lupa na pesquisa de equipamento. D03: separador "Devolvidas".

## 4. Não verificável pelo QA
J02 (stock real no PHC), J06, I19, G06 (edição em simultâneo), K04 (PDA), J03/J04 (saída por dar).
