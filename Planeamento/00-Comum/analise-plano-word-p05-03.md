# Análise do Word P.05-03 Plano de Manutenção de Equipamentos (v1.4)

Fonte: `fontes/P.05-03-plano-manutencao-equipamentos-v1.4.docx` (recebido a 05/11/2026). Resultado da extração: `fontes/plano-p05-03-normalizado.csv` (separador `;`, UTF-8 com BOM, abre no Excel). A extração é automática e serve de **entrada da carga inicial** (BO-09, tarefa P1-T01); a Landeiro valida cada linha.

BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste ficheiro): a carga do Word passa a criar tarefas em `u_itmanutli` (instrução em `u_itmanut`, periodicidade em `u_periocid`) para equipamentos de `u_equip`; não há `u_manplano`. Ver `base-landeiromaui.md`. O que a base não tem (calendário de meses, antecedência por plano) é NOVO e está marcado como tal.

## 1. Estrutura do documento

| Parte | O que tem |
|---|---|
| Cadastro do documento | Histórico de versões: 1.0 (2017-07-13) até 1.4 (2023-11-13, "Atualização do Plano de Manutenção"). Nota: o cabeçalho diz "Data: 2026" e o histórico acaba em 2023; confirmar se há uma versão mais recente |
| Tabela principal | 119 linhas e 29 colunas: Nº, Designação, Zona, Entidade, **24 colunas de meses (2 por mês)** e Manutenção (periodicidade) |
| Origem dos dados | O Word está **ligado a um Excel**: `Lista de infraestruturas1.xlsx`, folha "plano de manutenção", intervalo L2C1:L237C29. O Excel é a fonte real e melhor ponto de partida |

## 2. Números

- 107 linhas de equipamento aproveitáveis (99 com Nº inteiro, 3 com Nº decimal: "54,1", "5,1" e "82,1", e 5 sem Nº). Nº entre 3 e 248; 79 (Depiladora Suínos, Abate) e 248 (Enchedora Rex, Salsicharia) estão lá, como no PHC.
- Zonas (herdadas da última linha "Zona: …" acima, **Suposição**): Salsicharia 39, Abate 25, Corredores 23, Expedição 12, Triparia 5, Sala de Embalar 1, Exterior 1, Outros 1. "Corredores" com 23 parece demasiado: validar.
- Periodicidade: Mensal 45, Trimestral 37, Anual 13, Semanal 3, Semestral 3, Quinquenal 1, **5 sem periodicidade legível**.
- 60 das 107 linhas têm pelo menos um aviso (coluna `avisos` do CSV).
- 42 equipamentos não têm nenhum "x" nos meses: o calendário mensal está incompleto; só a periodicidade textual existe.

## 3. Problemas de qualidade (a corrigir na carga)

| # | Problema | Exemplo | Proposta |
|---|---|---|---|
| 1 | Entidade com 8 grafias | interna, interno, externa, externo, Interna/Ext, Int+Ext, Int/Ext, Interno+Ext | Normalizar para Interna, Externa ou Interna+Externa (a ficha PHC só tem Interna e Externa: ver 4.3) |
| 2 | Periodicidade com gralhas e valores fora da lista | "trismestral", "3VZ ANO" (Estufa Elétrica nº 3), "quinquenal" | Corrigir "trismestral"; "3 vezes por ano" e "quinquenal" não existem na lista da Ficha: decidir (ver 4.2) |
| 3 | Nº repetido | 225 (2 equipamentos), 231 (2), 233 (2) | A Landeiro atribui Nºs novos ou confirma o que é igual |
| 4 | Nº decimal | 54,1 Máquina Colocar fios; 5,1 Lavar Botas; 82,1 Vias Aéreas | Sub-equipamentos: criar infraestrutura própria ou ligar ao Nº pai (decisão de P1-T02) |
| 5 | Equipamentos sem Nº | Máquina de Virar Tripas Grossas, Máquina Separar Tripa Fina, Estufa Elétrica nº1, Detetor de Metais, Polyclip | Atribuir Nº (ou confirmar se já existem no PHC) |
| 6 | Zonas como linhas de cabeçalho e células unidas | "Zona: Triparia", "Zona: Salsicharia"… | Já tratado no CSV (zona herdada); validar |
| 7 | Dois sub-campos por mês | "x x" por mês | Ver 4.1 |
| 8 | Equipamentos a vermelho | "Carrocel", "Secador", "Forno Chamusco", "Lavadora", "Tanque Escaldão" (Abate) | **Suposição:** novos ou sem plano concluído; a cor não sobrevive à extração, confirmar com a Landeiro |

## 4. Decisões que isto abre (entram no `00-global.md`)

### 4.1 O que são as duas colunas por mês?
Cada mês tem 2 células. **Suposição:** correspondem a duas quinzenas (ou à 1.ª e 2.ª metade do mês). O valor por equipamento varia entre 0, 2, 6, 8, 18, 20 e 22 marcas "x", o que combina com mensal (20 a 24 marcas), trimestral (6 a 8) e semestral/anual (2). **A confirmar com a Landeiro:** se a marca na 1.ª ou 2.ª célula quer dizer quinzena, semana ou apenas redundância. O CSV guarda por mês o número de marcas (0, 1 ou 2), `meses_x_por_mes_jan_a_dez`.

### 4.2 Lista única de periodicidades (risco R-08)
A Ficha PHC tem: De acordo com procedimento, Diária, Mensal, Quinzenal, Regular, Semanal, Semestral, Sempre que necessário. O plano Word usa também Trimestral, Anual, Quinquenal e "3 vezes por ano". Na base, a periodicidade das tarefas vem de `u_periocid`: **por confirmar** (leitura com o `sc-leitor-bd`) se cobre Trimestral, Anual, Quinquenal e quinzenas. Só os valores que faltarem são NOVOS (linhas a acrescentar a `u_periocid` por pessoa no PHC); a lista única de 12 valores proposta antes fica como referência. Se "3VZ ANO" for só um erro de escrita de Trimestral, passa a Trimestral.

### 4.3 Entidade: Interna, Externa ou as duas
A ficha do PHC só aceita Interna ou Externa. 14 equipamentos do Word estão como Interna+Externa. Proposta: uma tarefa `u_itmanutli` por tipo (uma Interna e uma Externa) para o mesmo equipamento; a base permite várias tarefas por equipamento, pelo que isto já resolve o caso. Uma intervenção externa exige fornecedor e documento externo (regra `extexigedoc`). Confirma P1-T02.

### 4.4 Equipamentos 42 sem calendário
Para os 42 equipamentos sem "x" usa-se só a periodicidade: a base calcula a planeada seguinte como a última intervenção feita + periodicidade; sem histórico, a data de arranque da tarefa vem da Landeiro. O calendário de meses (JAN a DEZ) que os outros têm não existe na base: é NOVO e só entra se o desenho o mantiver.

## 5. Impacto no planeamento

- Pendência 1 (Word do plano): **recebida**. Nova pendência: o Excel de origem `Lista de infraestruturas1.xlsx` (melhor fonte, com possível cor e comentários), e confirmar a versão mais recente do plano.
- P1-T01 passa a receber o CSV como entrada e a validar os avisos com a Landeiro; o mapeamento para `u_itmanutli` fica quase feito (Nº do equipamento em `u_equip`, designação, zona = `u_equip.local`, entidade, periodicidade de `u_periocid`; os meses são NOVOS). Campos exatos de `u_equip` e `u_itmanutli`: por confirmar com leitura.
- Carga: script idempotente, só em `TESTES_LAND2` pelo agente; produção pelo utilizador (pressuposto 12 da Parte 1).
- A carga também tem de cruzar o Nº com as infraestruturas já existentes no PHC (o PHC tem pelo menos os nºs 79 e 248 vistos no vídeo).
