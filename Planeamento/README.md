# Planeamento: Gestor de Manutenção (Landeiro)

Planeamento do circuito de manutenção da Landeiro na plataforma .NET MAUI, dividido em 3 partes. **Só planeamento: nada aqui é código da aplicação.** Criado em 05/11/2026 a partir da reunião de 04/11/2026, dos 3 PATs e das 3 fotografias do quadro.

> **Base = LandeiroMaui (2026-11-06):** este planeamento segue a base da LandeiroMaui, onde a manutenção já está implementada no PHC (tabelas `u_equip`, `u_itmanutli`, `u_fichaint`, `u_matinterv`, `u_configman`, entre outras). Fonte da verdade: `00-Comum/base-landeiromaui.md`. Só o que a base não tem (armazém, stock, etiquetas, receção, inventário, saída por leitura) é NOVO.

## Como está organizado

| Pasta | O que tem |
|---|---|
| `00-Comum` | Base partilhada: **`base-landeiromaui.md` (fonte da verdade)**, resumo e transcrição da reunião, circuito atual no PHC, requisitos gerais e rastreabilidade, arquitetura e modelo de dados, fluxogramas gerais, **task global (`00-global.md`)**, blocos comuns dos prompts, fontes (PATs, quadro, frames do vídeo) |
| `01-Backoffice-PHC` | Parte 1: dados no PHC, equipamentos, planos, geração de planeadas, configurações, validação, dashboard e alertas |
| `02-App-Intervencoes` | Parte 2: app de telemóvel com login (base LandeiroMaui) e PIN, offline obrigatório, alerta, lista, criar intervenção |
| `03-Pecas-Armazem` | Parte 3: referências e peças, armazém virtual, etiquetas, receção, inventário, saída por leitura, alerta de rutura |

Cada parte tem `README.md`, `requisitos.md`, `fluxogramas.md`, `tarefas.md`, `prompts/` (um prompt-mestre e um prompt por task) e `mockups/`.

## Por onde começar

1. Ler `00-Comum/reuniao-2026-11-04/resumo.md` e `00-Comum/circuito-phc-atual.md` (10 minutos).
2. Ler `00-Comum/00-global.md`: tasks, decisões a confirmar (D-04, D-07; D-02 resolvida) e `00-Comum/base-landeiromaui.md`, pendências e riscos.
3. Dar a cada responsável a pasta da sua parte. Cada um começa pelo `README.md` da parte e pelo prompt `prompts/01-levantamento.md`.
4. Os três levantamentos (P1-T01, P2-T01, P3-T01) podem correr ao mesmo tempo. As restantes tasks seguem as dependências do `00-global.md` (secção 3) e do fluxograma de sequência em `00-Comum/fluxogramas-gerais.md`.

## Prazos ditos na reunião

- 11/11/2026: protótipo para validar a viabilidade (mockups clicáveis de cada parte).
- 17/11/2026 (Suposição): Landeiro precisa de algo para registar as primeiras intervenções nos equipamentos novos. Risco R-01: a app completa não chega a essa data; ver o plano por marcos no README da Parte 2.

## Regras que valem em tudo

Marca SigmaCode nos documentos e mockups. Sem credenciais nem connection strings. Tabelas e campos novos no PHC especificam-se e são criados por pessoas, nunca pelo Claude Code. Escrita em base de dados só em `TESTES_LAND2` ou `LAND2_DEMO`. Prompts a partir da 2.ª task de cada parte são rascunhos até a task anterior fechar.
