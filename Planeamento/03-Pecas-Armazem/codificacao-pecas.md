# Codificação hierárquica de peças e localizações

Estado: proposta de planeamento (Parte 3, requisito PEC-01 e PEC-05). Os nomes reais dos campos do PHC confirmam-se em P3-T01; o que é marcado "Suposição" fecha em P3-T02 com a Landeiro.

Origem: reunião de 04/11/2026 (`00-Comum/reuniao-2026-11-04/resumo.md`, secção 3): referência do fabricante guardada ao lado de uma referência interna; código hierárquico por máquina (1 = Midas; 2 = mesa de sangria; 3 = escaldão; 4 = chamusco; seguintes = lavador…; dentro de cada máquina as peças 001, 001.1, 001.2…); designação original em inglês mais a tradução ao lado.

## 1. Princípios

1. **Uma peça física, uma referência interna.** O mesmo código do fabricante (Marel code) que aparece em vários conjuntos ou máquinas é o mesmo artigo; o stock não se fragmenta. A posição na máquina vive numa tabela de ligação, não na referência (decisão D-12, por confirmar; ver secção 6).
2. **A referência interna é um identificador estável.** Nunca se renumera. Se a peça mudar de conjunto ou deixar de ser usada, a referência mantém-se (a peça fica obsoleta, nunca se apaga nem se reutiliza).
3. **Guardam-se sempre três coisas:** referência interna (Landeiro), referência do fabricante (texto, nunca número) e designação original mais tradução.
4. A hierarquia é máquina → conjunto → peça. Subconjuntos do manual achatam-se no conjunto (a designação do conjunto leva "Conjunto > Subconjunto"). Suposição: três níveis chegam; confirmar na amostra do PDF (P3-T01).

## 2. Esquema da referência interna da peça

```
M<MMM>-<CCC>.<PPP>
```

| Parte | Significado | Formato | Intervalo |
|---|---|---|---|
| `M` | Prefixo fixo "manutenção" (Suposição: confirmar em P3-T01 que não colide com referências existentes) | 1 letra | fixo |
| `<MMM>` | Máquina (catálogo de máquinas de peças) | 3 dígitos | 001 a 999 |
| `<CCC>` | Conjunto dentro da máquina (por ordem do manual) | 3 dígitos | 001 a 999 |
| `<PPP>` | Peça dentro do conjunto (sequencial, nunca reutilizado) | 3 dígitos | 001 a 999 |

Tamanho total: 12 caracteres (cabe nos 18 do campo candidato `ref`; confirmar o limite real em P3-T01). Expressão regular de validação: `^M\d{3}-\d{3}\.\d{3}$`. Identificador de conjunto (não é artigo, só chave em `u_manconj`): `^M\d{3}-\d{3}$`. Identificador de máquina: `^M\d{3}$`.

A notação da reunião (001, 001.1, 001.2) lê-se como conjunto 001 e peças .1, .2 do conjunto. O preenchimento a três dígitos é Suposição, para ordenar bem em texto e para ter folga (manuais Marel têm centenas de linhas por máquina). Alternativa sem preenchimento: rejeitada, porque `001.10` ordena antes de `001.2` em texto.

### Exemplos (fictícios)

| Máquina | Conjunto | Peça | Referência interna | Ref. fabricante (exemplo) | Designação original (EN) | Designação PT |
|---|---|---|---|---|---|---|
| 001 Midas | 001 Cabeça de corte | 001 | `M001-001.001` | `7000123` | Cutting blade | Lâmina de corte |
| 001 Midas | 001 Cabeça de corte | 002 | `M001-001.002` | `7000124` | Blade holder, left | Suporte da lâmina, esquerdo |
| 001 Midas | 002 Transmissão | 001 | `M001-002.001` | `5000456` | O-ring 32x3 | Anel O 32x3 |
| 002 Mesa de sangria | 001 Estrutura | 001 | `M002-001.001` | `7000987` | Support roller | Rolo de suporte |

Os números de máquina da reunião (1 a 4) são exemplos ditos de viva voz: a ordem e os equipamentos definitivos fecham-se na validação do catálogo de máquinas (P3-T02). Os códigos do fabricante acima são inventados.

## 3. Regras de atribuição

1. **Máquina:** o catálogo de máquinas de peças (tabela candidata `u_manmaq`) atribui o `<MMM>` sequencial (máximo + 1). Cada máquina do catálogo pode ligar-se a um equipamento (infraestrutura) do PHC; uma máquina do manual Marel pode corresponder a vários equipamentos.
2. **Conjunto:** `<CCC>` sequencial por máquina, pela ordem em que aparece no manual na primeira importação; em reimportações, conjuntos novos recebem máximo + 1 (nunca se reordena).
3. **Peça:** `<PPP>` sequencial por conjunto, máximo + 1, atribuído à primeira ocorrência do código do fabricante. O número da linha do manual ("Item no.") guarda-se na ligação, não na referência.
4. **Ocorrência repetida** (mesmo código do fabricante já existente, na mesma máquina em outro conjunto ou em outra máquina): não cria artigo; cria só a ligação peça ↔ máquina/conjunto com a quantidade e o "Item no." do manual.
5. **Esgotamento:** se um conjunto passar de 999 peças ou uma máquina de 999 conjuntos, a geração falha com erro claro (não trunca nem salta).
6. **Concorrência:** a atribuição do próximo número é feita numa só instrução SQL (máximo + 1 dentro de uma transação curta, sem `UPDLOCK`), com validação de unicidade; em colisão, repete uma vez e depois falha com mensagem.

## 4. Campos associados à peça (candidatos)

| Dado | Campo candidato | Regra |
|---|---|---|
| Referência interna | `ref` | Chave, regra da secção 2 |
| Referência do fabricante | `reffabric` | Texto, normalizado (maiúsculas, sem espaços nas pontas), **nunca numérico** (preservar zeros à esquerda); único por fabricante |
| Código de barras do fabricante | `codbarras` | Opcional, só se for diferente da `reffabric` |
| Fabricante | `fabricante` | Texto (Marel, …) |
| Designação original | `desigen` | Texto do manual tal como está (em inglês); vazio se o original for português |
| Designação PT | `desigpt` | Tradução, ou o original se já estava em português |
| Idioma do original | `idiomaorig` | `EN`, `PT` ou `XX` (outro/desconhecido) |
| Unidade | a definir | Unidade do artigo no PHC (um, m, kg…) |
| Localização por defeito | `locdefeito` | Código de localização (secção 5), opcional |

## 5. Codificação das localizações do armazém virtual

```
<Z><EE>-<P>-<PP>      exemplo: A03-2-05
```

| Parte | Significado | Formato |
|---|---|---|
| `<Z>` | Zona (corredor ou sala) | 1 letra A a Z |
| `<EE>` | Estante | 2 dígitos 01 a 99 |
| `<P>` | Prateleira (de baixo para cima) | 1 dígito 1 a 9 |
| `<PP>` | Posição na prateleira (da esquerda para a direita) | 2 dígitos 01 a 99 |

Expressão regular: `^[A-Z]\d{2}-\d-\d{2}$`. Código reservado `RECEC` (zona de receção: onde a peça fica até ser arrumada). Tamanho máximo 20 (campo candidato `codigo` varchar(20)). A hierarquia está só no código (sem tabela de pais); a descrição livre explica ("Armazém de manutenção, corredor A, estante 3, prateleira 2, posição 5"). Suposição: um só armazém virtual por armazém PHC (campo `armazem`); vários só se a Landeiro pedir.

## 6. Perguntas que fecham a codificação

1. **D-12 (nova, por decidir):** artigo único por código do fabricante (recomendado: o stock soma-se; a hierarquia mostra-se na ligação) ou um artigo por posição na máquina (a referência reflete sempre a posição, mas o mesmo parafuso em 5 máquinas vira 5 artigos e o stock fragmenta-se). Recomendação: artigo único.
2. O PHC aceita `-` e `.` nas referências de artigo, e quantos caracteres? Confirmar em P3-T01.
3. O prefixo `M` está livre no PHC da Landeiro? Confirmar em P3-T01.
4. A Landeiro quer ver o nome da máquina na referência (por exemplo `MID001-…`)? Rejeitado por defeito: nomes mudam, números não.

## 7. Conteúdo dos códigos de barras e QR

| Objeto | Conteúdo (payload) | Simbologia |
|---|---|---|
| Etiqueta da peça | `PEC:M001-001.001` | QR (principal) e Code 128 do texto da referência (secundário, para leitores simples) |
| Etiqueta da localização | `LOC:A03-2-05` | QR e Code 128 |
| Código do fabricante (já na peça) | o que lá está (EAN, Code 128…), sem prefixo | o que o fabricante usar |

**Resolução de uma leitura** (ordem; a primeira regra que casar ganha; serviço único usado por receção, saída e inventário):

1. Começa por `PEC:` → peça pela referência interna.
2. Começa por `LOC:` → localização.
3. Casa com `^M\d{3}-\d{3}\.\d{3}$` → peça pela referência interna (leitor que lê só o texto).
4. Casa com `^[A-Z]\d{2}-\d-\d{2}$` ou é `RECEC` → localização.
5. Caso contrário → procura `reffabric` ou `codbarras`; um resultado = peça; vários = lista para escolher; nenhum = "código desconhecido" (oferece criar o vínculo à peça, com confirmação).

Conteúdo da etiqueta da peça (impressa): QR, referência interna (grande), referência do fabricante, designação PT (até 2 linhas), designação original (1 linha), máquina e conjunto, localização por defeito (se existir). Etiqueta da localização: QR, código (grande), descrição. Tamanho por defeito 60 × 40 mm a 203 dpi (Suposição: confirmar com a impressora da Landeiro e o rolo disponível).

## 8. Validações (para os testes xUnit do gerador e do analisador de leituras)

| Entrada | Esperado |
|---|---|
| `M001-001.001` | peça válida |
| `M999-999.999` | peça válida (limite) |
| `M001-001.000`, `M000-001.001` | inválida (zero não é número de sequência) |
| `m001-001.001` | inválida na gravação (normaliza para maiúsculas só na leitura) |
| `M1-1.1`, `M001-001`, `M001-001.0001` | inválida como peça |
| `M001-001` | conjunto válido, **não** é referência de artigo |
| `A03-2-05`, `RECEC` | localização válida |
| `A3-2-05`, `a03-2-5`, `A03-0-05` | inválida |
| `PEC:M001-001.001`, `LOC:A03-2-05` | resolve peça e localização |
| `7000123` com `reffabric` única | resolve peça; com duas peças iguais, devolve lista |
| `0070001` e `70001` | referências de fabricante diferentes (zeros à esquerda contam) |
| Próxima peça num conjunto com `.001` a `.998` | `.999`; com `.999` falha com mensagem |
