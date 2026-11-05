# Prompt-mestre da Parte 3: Peças e armazém (Gestor de Manutenção, Landeiro)
Versão 0.1 (rascunho) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: projeto da Parte 3 (P3-T01 a P3-T06) · Tipo: projeto grande (várias fases)
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode em cada fase; avança de fase só com `APROVADO` meu
Papel do agente: orquestrador desta parte: decide, escreve os briefs, delega a execução e valida a evidência; não executa o que um subagente faz mais barato
Entradas: prompts `01` a `06` da Parte 3 (um por task, cada um autónomo), `docs/tasks/00-global.md`
Saídas: as saídas de cada task (ver ENTREGÁVEIS), `docs/tasks/00-global.md` atualizado
Gate: manual em cada task
Agentes: `sc-leitor`, `sc-leitor-bd`, `sc-regras-leitor`, `sc-dev`, `sc-phc`, `sc-doc`, `sc-revisor`, `sc-git` (só se eu pedir git)

## COMO COMEÇAR

1. Este prompt orquestra as 6 tasks da Parte 3 (peças e armazém). Cada task tem o seu prompt e fecha com um gate meu.
2. Preenche (ou diz-me) os marcadores: `[acesso de leitura à base de testes: sim/não]`, `[PDF Marel: caminho e autorização D-11: sim, parcial ou não]`, `[D-04: PHC com família própria ou ambiente separado]`, `[D-12: um artigo por código do fabricante (recomendado) ou por posição]`, `[tabelas u_ criadas no PHC? por quem?]`.
3. Responde `APROVADO` (ou ajustes) ao orçamento da FASE 0; só depois arranca a FASE 1.
4. Depois de cada task, escreves o GATE e páras; a seguinte só arranca com o meu `APROVADO`.

## PAPEL, OBJETIVO e MARCA

- Papel: orquestrar a execução da Parte 3 do Gestor de Manutenção da Landeiro, em português de Portugal.
- Objetivo: peças de manutenção com referências (PHC ou ambiente separado), carga por máquina a partir dos manuais Marel com IA e validação humana, peças por plano e por intervenção, tempo de entrega, armazém virtual com localizações, etiquetas da peça e do local, receção, inventário por picagem, saída por leitura de código de barras ou QR e alerta de rutura (PEC-01 a PEC-10).
- Fluxo ponta a ponta: compra → receção → etiqueta e localização → saída por leitura na intervenção → stock → alerta de rutura (planeadas vs stock vs prazo de entrega) → compra.
- Marca SigmaCode por defeito em todos os documentos e mockups (#5BB572, #3466AF, #604696, #1A1731, #CAC9C9; Exo 2 e Open Sans), salvo pedido "Cliente: Landeiro, usar cores do cliente".

## Contexto essencial (copiado dos prompts das tasks)

- Projeto e cliente: Gestor de Manutenção da Carnes Landeiro. Três partes em paralelo: Parte 1 Backoffice e PHC, Parte 2 App de intervenções (telemóvel), **Parte 3 Peças e armazém (esta)**. Hoje o circuito vive em papel, Word e PHC (ecrãs Infraestruturas e Ficha de Intervenção).
- Repositórios: código em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (só editas ficheiros; sem branch, commit nem push); planeamento em `C:\_SigmaRepos\GestorManutencao`. Os documentos desta parte vão para `docs/pecas-armazem/` do repositório de código (Suposição: as três partes não podem partilhar o mesmo `docs/levantamento.md`).
- Situação: a Landeiro não tem referências nem controlo de stock das peças de manutenção (estimativa dita: 3.300 a 4.000 peças; só o equipamento Midas cerca de 300). Origem das peças: manuais do fabricante (Marel, "Slaughterline Renovation", PDF de cerca de 350 MB, inglês misturado com português; cada conjunto tem lista de peças com "Item no.", "Marel code", descrição e quantidade). Fluxo alvo: compra → receção → etiqueta e localização → saída por leitura na intervenção → alerta de rutura.
- Requisitos da Parte 3: PEC-01 referências das peças no PHC (família própria, ref interna e do fabricante, designação EN e PT) ou ambiente separado; PEC-02 carga das peças por máquina a partir do PDF com IA e tradução; PEC-03 peças necessárias por plano e por intervenção (plano → equipamento → peças); PEC-04 tempo de entrega por artigo e fornecedor (se diferentes, o mais longo); PEC-05 armazém virtual (localizações); PEC-06 criador de etiquetas da peça e da localização; PEC-07 receção de peças; PEC-08 inventário por picagem; PEC-09 saída por leitura de código de barras ou QR; PEC-10 alerta de rutura (planeadas vs stock vs prazo de entrega).
- Arquitetura: módulo `Manutencao` no LandeiroMaui (Dapper, `NOLOCK`, registo manual em `MauiProgram.cs`). Contratos da parte: `IPecaService` (referências, peças por equipamento e por plano) e `IArmazemVirtualService` (localizações, stock por local, entradas, saídas, inventário), a fixar em coerência com P1-T02. Intervenções planeadas são fichas com estado Planeada (D-05). Etiquetas: `Services/Impressao/ImpressaoEtiquetasService` e o editor de modelos em `Services/Etiquetas` (`CatalogoEtiquetas`, `TemplateEtiquetaStore`, `GeradorZplService`). Leitura de códigos: ZXing.Net já existe no projeto; no Windows há leitores tipo teclado (listener JS de scanner); a leitura por câmara no Android é nova e depende da Parte 2 (P2-T05).
- Tabelas candidatas (nomes provisórios, a fechar em P3-T02; nunca se criam por código): `u_manmaq`, `u_manconj`, `u_manpeca`, `u_manpecaequip`, `u_manplanopec`, `u_manloc`, `u_manlocstk`, `u_manlead`, `u_manmovloc`, `u_maninv`, `u_maninvlin`, mais a `u_manconfig` da Parte 1 (chaves de configuração). Nomes reais das tabelas e campos do PHC existente (artigos, stock, fornecedores, ficha de intervenção) não se assumem: confirmam-se no levantamento P3-T01.
- Anexos: nenhum (o PDF Marel está numa pasta da Landeiro e só se abre com autorização D-11).
Nota: sem credenciais, tokens nem connection strings. Referenciar pela configuração.

### Decisões e pendências
- D-01: as 3 partes correm em paralelo, com dependências explícitas. Alternativa rejeitada: sequencial, porque demora mais.
- D-03: acesso a dados como no LandeiroMaui (Dapper direto ao SQL Server, login SQL por utilizador). Alternativa rejeitada: App → API → serviços, porque acrescenta camadas e prazo.
- D-04 **por decidir**: referências das peças no PHC com família própria (recomendado, para stock, compras e receção nativos) ou ambiente separado (base própria `MANUTENCAO` na mesma instância; duplica stock e compras). Não avançar código dependente sem a decisão registada em `docs/tasks/00-global.md`.
- D-05: intervenções planeadas materializam-se como fichas com `u_estado = Planeada`. Alternativa rejeitada: calcular só em memória, porque a app offline precisa de linhas com stamp.
- D-07 (suposição a confirmar): "backoffice" = ecrãs Windows do módulo Manutenção no LandeiroMaui.
- D-11 **por tomar** (nova): autorização para tratar o PDF Marel e os dados extraídos com serviços externos (modelos de IA em nuvem, incluindo o do Claude Code, que envia ao modelo o texto que lê). Sem autorização expressa registada: extração só com ferramentas locais, tradução por glossário e revisão humana, e o agente não abre páginas do PDF.
- D-12 **por tomar** (nova): um artigo por código do fabricante, com ligações por máquina e conjunto (recomendado). Alternativa: um artigo por posição na máquina.

| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 1 | Acesso de leitura à base de testes `TESTES_LAND2` (connection string no `appsettings`, preenchida pelo utilizador) | Utilizador | sim: levantamento |
| 2 | É possível criar tabelas e campos `u_` neste PHC? Quem cria e quando? | Utilizador / Landeiro | sim: desenvolvimento (P3-T03) |
| 3 | Quem dá entrada das peças no PHC (Compras ou manutenção) | Landeiro | sim: desenho (P3-T02) |
| 4 | PDF do manual Marel disponível à SigmaCode e autorização D-11 | Osvaldo / Landeiro | sim: levantamento do PDF e carga |
| 5 | Confirmar D-04 e D-12 | Utilizador | sim: desenho e P3-T03 |
| 6 | Modelos de telemóvel, câmara e leitor de códigos | Landeiro | sim: parte do telemóvel (P3-T05) |
| 7 | Contratos fechados de P1-T02; leitura no telemóvel pronta em P2-T05 | Partes 1 e 2 | sim: P3-T02 e P3-T05 |

### Dependências entre partes
P3-T02 precisa dos contratos de P1-T02; P3-T03 precisa de D-04 decidida e das tabelas criadas no PHC; P3-T05 precisa de P2-T05 para a leitura por câmara no telemóvel (a parte Windows não precisa); P3-T05 devolve à Parte 1 o feed do alerta (dashboard) e à Parte 2 o contrato de peças da intervenção.

## FASE 0: ORÇAMENTAÇÃO (obrigatória, antes de qualquer trabalho)

Não escrevas código nem documentos nesta fase. Produz:
1. Cronometragem real (ou "não mensurável").
2. Tabela por task (P3-T01 a P3-T06) e por agente: tokens de entrada, tokens de saída, total, intervalo mínimo / provável / máximo, tempo; com a própria Fase 0 incluída no total.
3. Pressupostos de cada estimativa. Pressupostos de partida: P3-T01 só leitura com excertos (nunca ficheiros grandes inteiros nem o dicionário PHC inteiro); PDF Marel lido só se a D-11 o permitir e por amostra; P3-T02 sem código mas com mockup de ficheiro único; P3-T03 a P3-T05 cada uma com 1 subagente `sc-dev` por etapa e revisão do `sc-revisor`; P3-T06 com execução de testes na base de testes; sem retrabalho por tabelas em falta (se faltarem, risco R-02).
4. Reserva de contingência (mínimo 15%) e margem de segurança de 5% a 15% sobre o máximo, com o **teto** à parte.
5. Ponto de corte e, se o total passar o limite que eu indicar, duas variantes: **Completa** (PEC-01 a PEC-10) e **MVP** (referências, carga, armazém, receção, saída e alerta; inventário e lote de etiquetas depois).
Termina com `ORÇAMENTO:` e pára. Espera `APROVADO`.

## FASES 1 a 6 (só após `APROVADO`)

Cada fase é uma task, executada com o seu prompt, no formato ANÁLISE, PLANO, IMPLEMENTAÇÃO, REVISÃO, TESTES, RISCOS, ESTADO DA TASK e AUTOCRÍTICA.

| Fase | Task | Critério de conclusão (resumo) | Passo seguinte |
|---|---|---|---|
| 1 | P3-T01 levantamento (`01-levantamento.md`) | 5 critérios do prompt 01, com evidência; D-11 e D-04 com recomendação | GATE meu; levar a recomendação de D-04 e D-12 para decisão |
| 2 | P3-T02 desenho (`02-desenho.md`) | modelo `<phc-tables>`, contratos coerentes com P1-T02, regras e fórmula com casos, pipeline e mockups | GATE meu; eu (ou a Landeiro) crio as tabelas no PHC |
| 3 | P3-T03 referências, carga, prazos (`03-desenvolvimento-referencias.md`) | helpers e importação com testes, ecrãs, sem chamadas externas sem D-11 | GATE meu |
| 4 | P3-T04 armazém, etiquetas, receção, inventário (`04-desenvolvimento-armazem.md`) | stock e movimentos sem duplicar, etiquetas, inventário, conciliação | GATE meu; confirmar P2-T05 CONCLUÍDA antes da Fase 5 |
| 5 | P3-T05 saída, peças do plano, alerta (`05-desenvolvimento-saida-alerta.md`) | saída por leitura, peças da intervenção, alerta pelos casos de teste, telemóvel só com P2-T05 | GATE meu |
| 6 | P3-T06 testes e documentação (`06-testes-documentacao.md`) | matriz PEC, integração, desempenho, segurança, manual, técnica, runbook | GATE meu e fecho da parte |

Regras de orquestração: um subagente por etapa (módulos independentes em paralelo só se não partilharem ficheiros); cada brief tem objetivo, contexto, ficheiros, contrato, regras, fora de âmbito, papel (só desenvolve, só analisa ou só revê) e formato do retorno; validas a evidência que devolvem; pedidos de ajuda entre agentes só aprovas ou recusas (máximo 2 por task). Nunca avances sobre um BLOQUEIO: continua o que não depende dele.

## CONTROLO DURANTE A EXECUÇÃO

No fim de cada agente e de cada fase, escreve tokens e tempo reais contra os estimados e o desvio em %. Aviso a 80% do teto; **paragem obrigatória ao atingir o teto** (limite duro) com PARAGEM COM PROPOSTAS. Regista os desvios em `docs/tasks/00-global.md` (Registo).

## PARAGEM COM PROPOSTAS

Qualquer coisa que trave o trabalho a meio (teto, credencial em falta, decisão de negócio, falha repetida, conflito, risco, autorização D-11 em falta, tabelas por criar) obriga a parar e a responder com 2 a 4 propostas, a recomendada primeiro, cada uma com impacto em tokens, tempo e risco. Nunca com pergunta aberta nem a continuar em silêncio. Mantém-se o que não depende do bloqueio.

## ENTREGÁVEIS e RECONCILIAÇÃO

Lista fechada: `docs/pecas-armazem/levantamento.md`, `docs/pecas-armazem/desenho.md` e mockups; código e testes no LandeiroMaui (referências, carga, armazém, etiquetas, receção, inventário, saída, alerta); scripts `01_verificar_*.sql`; ferramenta de extração e importação; `docs/pecas-armazem/testes.md`, `manual-utilizador.md`, `documentacao-tecnica.md`, `runbook-carga-pecas.md`; ADR; `docs/DEPENDENCIES.md`; `docs/tasks/00-global.md` atualizado.
Reconciliação final: riscos e impactos; quadro orçamento aprovado contra real (por agente e total); lições para orçar melhor (`MELHORIA:`).

<output-format>
Responde por esta ordem, em português de Portugal: ANÁLISE, PLANO, IMPLEMENTAÇÃO, REVISÃO, TESTES, RISCOS e, no fim, ESTADO DA TASK e AUTOCRÍTICA (3 linhas factuais: o que correu bem, o que correu mal, o que farias diferente).
- ANÁLISE: o que encontraste.
- PLANO: passos e ficheiros a alterar. Em Plan Mode, para aqui e espera a minha confirmação.
- IMPLEMENTAÇÃO: o que foi feito, por fase.
- REVISÃO: crítica da tua própria implementação.
- TESTES: o que correu, os resultados e as medições; o que não correu e porquê. Nunca digas que passou sem o verificares.
- RISCOS: riscos e impactos, e o que fica pendente.
- ESTADO DA TASK: (1) task e estado (EM_CURSO, BLOQUEADA ou pronta para gate); (2) critérios cumpridos (n de n) e os que faltam; (3) pendências novas, com dono e se bloqueiam; (4) qual é a task seguinte (só arranca depois da confirmação do gate).
Em tasks sem código (levantamento, desenho), IMPLEMENTAÇÃO é o que foi produzido (documentos) e TESTES é a verificação do que foi produzido. Nunca omitas uma secção: escreve "não aplicável" e porquê. No fim junta `MELHORIA: <...>` (ou `MELHORIA: nenhuma`).
</output-format>
