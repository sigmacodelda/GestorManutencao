# Brief comum para os agentes que escrevem cada parte do planeamento

Papel: **só escreves documentos** (Markdown e HTML de mockup) na pasta da tua parte. Não escreves código da aplicação, não tocas na base de dados, não fazes git, não alteras nada fora da tua pasta (`C:\_SigmaRepos\GestorManutencao\Planeamento\<tua parte>\`). Escreves em português de Portugal. Sem credenciais, connection strings, IPs nem dados pessoais reais.

BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste brief): o planeamento SEGUE a base da LandeiroMaui, onde o circuito de manutenção já está implementado no PHC. A fonte da verdade é `base-landeiromaui.md` (nesta pasta); se outro ficheiro a contradiz, prevalece ela. Os agentes que escrevem partes mapeiam a base e só propõem o que lhe falta, sempre marcado NOVO.

## Ler primeiro (por esta ordem; excertos, não ficheiros enormes)

Pasta `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\`:
0. `base-landeiromaui.md` (estados em `u_fichaint.estado`, regras por ação, `u_configman`, tabelas existentes, técnico PHC, o que não existe na base; lê-o inteiro)
1. `requisitos-gerais.md` (os teus requisitos e a rastreabilidade)
2. `arquitetura-e-modelo-de-dados.md` (reaproveitamento do LandeiroMaui, modelo candidato, contratos)
3. `00-global.md` (as tasks da tua parte, decisões, pendências, riscos)
4. `circuito-phc-atual.md` e `reuniao-2026-11-04/resumo.md` (o que a Landeiro disse e mostrou)
5. `fluxogramas-gerais.md` (os fluxos gerais, para os teus detalhes serem coerentes)
6. `blocos-comuns-prompt.md` (blocos a **copiar** para dentro de cada prompt: role, environment, rules, output-format, cabeçalho e estrutura do prompt-mestre)
7. Modelo de prompt das normas: `C:\_SigmaRepos\Claude-The-ALLKNOWING\normas\06-modelo-prompt.md` (secções 1 e 1.1) e `05-modelo-task-global.md` (só se precisares).
Fotos do quadro e PATs: `00-Comum\fontes\` (podes ver as imagens com Read).
Base de código (só leitura, quando a tua parte precisar): `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui\` (`CLAUDE.md`, `ARQUITETURA.md`, `docs/plano-modo-offline-cache.md`). Nunca leias `appsettings.json` (pode ter dados sensíveis), `bin/` nem `obj/`.

## O que produzir (na tua pasta)

| Ficheiro | Conteúdo |
|---|---|
| `README.md` | O que a parte faz, ordem de execução, dependências das outras partes, quem faz o quê, como arrancar |
| `requisitos.md` | Cada requisito da parte (IDs de `requisitos-gerais.md`) com utilizador, regra de negócio, critérios de aceitação verificáveis, casos limite e perguntas abertas |
| `fluxogramas.md` | Fluxogramas Mermaid detalhados da parte (processos, estados, sequência de ecrãs ou de dados). Sintaxe simples: ids sem espaços, texto dos nós entre aspas se tiver parênteses ou dois pontos |
| `tarefas.md` | As tasks da parte (as do `00-global.md`) decompostas em subtarefas (checklist), com dependências, critérios de conclusão, esforço estimado (horas, com intervalo) e o que depende de outras partes |
| `prompts/00-prompt-mestre.md` | Prompt de projeto grande da parte (estrutura da secção 6 de `blocos-comuns-prompt.md`: COMO COMEÇAR, PAPEL/OBJETIVO/MARCA, FASE 0 ORÇAMENTAÇÃO, FASES, CONTROLO, PARAGEM COM PROPOSTAS, ENTREGÁVEIS) |
| `prompts/01-...md` a `06-...md` | Um prompt por task do `00-global.md`, com os nomes de ficheiro lá indicados, no modelo de 06-modelo-prompt.md, com os blocos comuns copiados e o conteúdo específico completo (contexto, âmbito, fora de âmbito, decisões, pendências, regras específicas, testes/verificações, critérios). O prompt 01 fica pronto a usar; os 02 a 06 levam no cabeçalho "Versão 0.1 (rascunho: rever quando a task anterior fechar)" |
| extras | os indicados no teu brief |

Tamanho: prompts compactos mas completos (um prompt ≈ 80 a 150 linhas); sem repetir texto que pertence a outro ficheiro, referencia-o. Os prompts não podem depender de ficheiros desta pasta (o Claude Code que os executa não os vê): o essencial vai dentro do prompt (resumo do contexto, os requisitos da task, as tabelas ou contratos relevantes).

## Regras de conteúdo

- Marca SigmaCode nos documentos formatados e mockups: #5BB572 verde, #3466AF azul, #604696 roxo, #1A1731 azul-escuro, #CAC9C9 cinzento; tipografia Exo 2 (principal) e Open Sans (secundária). Mockups HTML: ficheiro único, sem bibliotecas externas além de Google Fonts, funciona em desktop e em largura de telemóvel.
- Marca decisões que não estão no `00-global.md` como "Suposição:" e lista-as no fim do teu `README.md` na secção "Pressupostos novos". Não alteres o `00-global.md` nem os ficheiros de `00-Comum`; se encontrares contradição, escreve-a no teu README em "Contradições encontradas" e segue a norma.
- Tabelas e campos novos no PHC: só o que a base não tem (pedido de peças ao armazém, stock, etiquetas, receção, inventário, o campo `tecno` em `u_manutil`, etc.); nunca os crias, especificam-se para o utilizador criar (formato `<phc-tables>` do modelo de prompt) e marcam-se como NOVO. O que a base já tem (estado em `u_fichaint.estado`, `u_configman`, `u_itmanutli`, `u_periocid`, `u_matinterv`) não se redesenha. SPs `sc_<Entidade><Acao>`, nunca `sp_`.
- Regras do projeto LandeiroMaui a respeitar nos prompts de desenvolvimento: ver `blocos-comuns-prompt.md`.
- Usa os nomes de tabelas e campos que constam de `base-landeiromaui.md`; não inventes outros nomes do PHC existente: o que não está lá diz que se confirma por leitura (`sc-leitor-bd`) ou no levantamento P1-T01. Atrasada e A vencer são estados derivados, nunca gravados.
- Não escrevas estimativas de tokens inventadas sem pressupostos: na FASE 0 do prompt-mestre, a tabela de orçamentação é pedida ao agente que executa (com intervalos e pressupostos), não preenchida por ti; tu só indicas as tasks, o âmbito e os pressupostos de partida.
- No fim devolve ao principal: lista dos ficheiros criados (caminhos relativos), contradições, pressupostos novos, perguntas que bloqueiam e uma linha `MELHORIA: <...>` (ou `nenhuma`). Máximo 20 linhas, sem repetir o conteúdo dos ficheiros.
