# Gestor de Manutenção (Landeiro) — Task global

Cliente: Carnes Landeiro · Iniciativa: Gestor de Manutenção · Criado: 2026-11-05 · Última atualização: 2026-11-05
Repositórios: `C:\_SigmaRepos\Landeiro-Desmancha` (LandeiroMaui, base) e `C:\_SigmaRepos\GestorManutencao` (planeamento) · Branch: a definir por parte (`dev-landeiro-manutencao-p1`, `-p2`, `-p3`) · Pasta das tasks: `docs/tasks/` do repositório de código
Este ficheiro cobre as 3 partes. Os prompts de cada parte estão em `../0X-*/prompts/`. Quando uma parte arrancar, copia-se o `00-global.md` para `docs/tasks/` do repositório de código.

## 1. Regras (não editar)

- Estados: PENDENTE, EM_CURSO, BLOQUEADA, CONCLUÍDA.
- Dentro de cada parte as tasks são sequenciais (a task NN só arranca com a anterior CONCLUÍDA). As 3 partes correm **em paralelo** a pedido do utilizador (decisão D-01), respeitando as dependências entre partes da secção 3.
- CONCLUÍDA = critérios cumpridos com evidência, sem pendência bloqueante e confirmação do utilizador no gate.
- Reabrir: uma task posterior que encontre um problema numa anterior reabre-a; as seguintes ficam BLOQUEADAS.
- Decisões, pendências e riscos vivem só aqui. O que não está aqui não aconteceu.
- Nunca se escrevem aqui credenciais, tokens nem connection strings.
- Os prompts das tasks a partir da 2.ª de cada parte são **rascunho**: rever quando a task anterior fechar (Parte 4, secção 1).

## 2. Ambiente

| Facto | Valor |
|---|---|
| Edição e versão do PHC | PHC Evolution (Ultimate) 2025.04 (visto no vídeo de 04/11/2026) |
| Alojamento e acesso à base | Servidor da Landeiro; acesso por login SQL por utilizador (padrão do LandeiroMaui). [CONFIRMAR alojamento] |
| SQL Server (versão) e nível de compatibilidade | [CONFIRMAR]. Norma: assumir nível 100. `LAND2_DEMO` está em 150 |
| Criar tabelas e campos no PHC: é possível? Quem cria? | [CONFIRMAR]. Existem tabelas `u_` no Landeiro. Quem cria: utilizador (Landeiro ou SigmaCode), nunca o Claude Code |
| Base de testes | `TESTES_LAND2` (testes) e `LAND2_DEMO` (demo); produção `LAND2` só leitura. Instância: [indicada pelo utilizador no appsettings] |
| Triggers existentes nas tabelas envolvidas | [a ler em P1-T01] |
| Onde corre o código | Backoffice: postos Windows (LandeiroMaui). App: telemóvel Android [CONFIRMAR modelos] |

## 3. Tasks

Dependências entre partes: P2-T03 e seguintes precisam dos contratos fechados em P1-T02; P2-T04 precisa dos serviços de P1-T03; P3-T02 precisa de P1-T02; P3-T05 precisa de P2-T05.

| ID | Tipo | Task | Estado | Prompt | Saídas | Critérios | Bloqueio | Atualizada |
|---|---|---|---|---|---|---|---|---|
| P1-T01 | levantamento | Estrutura PHC atual, Word do plano, perfis | PENDENTE | `../01-Backoffice-PHC/prompts/01-levantamento.md` | `docs/levantamento.md` | 0 de 5 | acesso de leitura à BD de testes | — |
| P1-T02 | desenho | Modelo de dados, contratos, ecrãs BO, protótipo | PENDENTE | `../01-Backoffice-PHC/prompts/02-desenho.md` | `docs/desenho.md` | 0 de 5 | P1-T01 | — |
| P1-T03 | desenvolvimento | Serviços, regras de periodicidade, geração de planeadas, configurações | PENDENTE | `../01-Backoffice-PHC/prompts/03-desenvolvimento-servicos.md` | código + testes | 0 de 5 | P1-T02, tabelas criadas no PHC | — |
| P1-T04 | desenvolvimento | Ecrãs do backoffice: equipamentos, planos, configuração, validação | PENDENTE | `../01-Backoffice-PHC/prompts/04-desenvolvimento-ecras.md` | código + testes | 0 de 5 | P1-T03 | — |
| P1-T05 | desenvolvimento | Dashboard e alertas | PENDENTE | `../01-Backoffice-PHC/prompts/05-desenvolvimento-dashboard.md` | código + testes | 0 de 4 | P1-T04 | — |
| P1-T06 | testes-documentacao | Testes e documentação da Parte 1 | PENDENTE | `../01-Backoffice-PHC/prompts/06-testes-documentacao.md` | `docs/` | 0 de 4 | P1-T05 | — |
| P2-T01 | levantamento | LandeiroMaui, Android, dispositivos, offline | PENDENTE | `../02-App-Intervencoes/prompts/01-levantamento.md` | `docs/levantamento.md` | 0 de 5 | — | — |
| P2-T02 | desenho | UX, wireframes, protótipo clicável, cache SQLite, PIN | PENDENTE | `../02-App-Intervencoes/prompts/02-desenho.md` | `docs/desenho.md` + protótipo | 0 de 5 | P2-T01, P1-T02 | — |
| P2-T03 | desenvolvimento | Alvo Android, login, PIN, sessão | PENDENTE | `../02-App-Intervencoes/prompts/03-desenvolvimento-login.md` | código + testes | 0 de 5 | P2-T02 | — |
| P2-T04 | desenvolvimento | Cache offline, fila de saída, sincronização | PENDENTE | `../02-App-Intervencoes/prompts/04-desenvolvimento-offline.md` | código + testes | 0 de 5 | P2-T03, P1-T03 | — |
| P2-T05 | desenvolvimento | Lista, alerta, criar intervenção, foto, avançar ordem | PENDENTE | `../02-App-Intervencoes/prompts/05-desenvolvimento-intervencoes.md` | código + testes | 0 de 6 | P2-T04 | — |
| P2-T06 | testes-documentacao | Testes e documentação da Parte 2 | PENDENTE | `../02-App-Intervencoes/prompts/06-testes-documentacao.md` | `docs/` | 0 de 4 | P2-T05 | — |
| P3-T01 | levantamento | Peças, stock PHC, manual Marel, fornecedores | PENDENTE | `../03-Pecas-Armazem/prompts/01-levantamento.md` | `docs/levantamento.md` | 0 de 5 | acesso de leitura à BD de testes | — |
| P3-T02 | desenho | Referências, armazém virtual, etiquetas, fluxos, alerta de rutura | PENDENTE | `../03-Pecas-Armazem/prompts/02-desenho.md` | `docs/desenho.md` | 0 de 5 | P3-T01, P1-T02 | — |
| P3-T03 | desenvolvimento | Referências, carga das peças (IA), tempos de entrega | PENDENTE | `../03-Pecas-Armazem/prompts/03-desenvolvimento-referencias.md` | código + testes | 0 de 5 | P3-T02, D-04 decidida | — |
| P3-T04 | desenvolvimento | Armazém virtual, etiquetas, receção, inventário | PENDENTE | `../03-Pecas-Armazem/prompts/04-desenvolvimento-armazem.md` | código + testes | 0 de 5 | P3-T03 | — |
| P3-T05 | desenvolvimento | Saída por leitura, peças do plano, alerta de rutura | PENDENTE | `../03-Pecas-Armazem/prompts/05-desenvolvimento-saida-alerta.md` | código + testes | 0 de 5 | P3-T04, P2-T05 | — |
| P3-T06 | testes-documentacao | Testes e documentação da Parte 3 | PENDENTE | `../03-Pecas-Armazem/prompts/06-testes-documentacao.md` | `docs/` | 0 de 4 | P3-T05 | — |

### Critérios de conclusão por task

Os critérios detalhados de cada task estão no topo do respetivo prompt (campo "Critérios de conclusão") e passam para aqui quando a task arranca. Marcar `[x]` só com evidência.

## 4. Decisões

| # | Decisão | Alternativas rejeitadas e porquê | Task | Data |
|---|---|---|---|---|
| D-01 | As 3 partes correm em paralelo, com dependências explícitas | Sequencial: demora mais e o utilizador quer dividir o trabalho | todas | 2026-11-05 |
| D-02 | Proposta: app de telemóvel em projeto novo `ManutencaoMobile` que partilha bibliotecas com o LandeiroMaui. **A confirmar** | Alvo Android dentro do LandeiroMaui: mais risco para os postos em produção | P2-T01, P2-T02 | 2026-11-05 |
| D-03 | Acesso a dados como no LandeiroMaui: Dapper direto ao SQL Server, login SQL por utilizador (instrução do utilizador: usar a base do LandeiroMaui) | App → API → sServices (norma geral): mais camadas e prazo maior; fica como opção se a segurança o exigir | P2-T02 | 2026-11-05 |
| D-04 | **Por decidir:** referências das peças no PHC (família própria) ou ambiente separado. Recomendação: no PHC, família própria, para stock, compras e receção nativos | Ambiente separado: duplica stock e compras | P3-T01, P3-T02 | — |
| D-05 | Intervenções planeadas materializam-se como fichas com `u_estado = Planeada` | Calcular só em memória: a app offline precisa de linhas com stamp | P1-T02 | 2026-11-05 |
| D-06 | PIN guardado só no dispositivo (hash com sal), sem campo novo no PHC | PIN central no PHC: exige campo novo | P2-T02 | 2026-11-05 |
| D-07 | "Backoffice" = ecrãs Windows do módulo Manutenção no LandeiroMaui, sobre as tabelas do PHC. **Suposição a confirmar com a Landeiro** | Painéis dentro do PHC: não é o que o PAT 2 diz ("na plataforma .NET MAUI") | P1-T02 | 2026-11-05 |
| D-08 | Documentos e mockups em Markdown + Mermaid e HTML, com a marca SigmaCode | Word/PDF: podem sair depois | todas | 2026-11-05 |
| D-09 | UI de mockups com paleta SigmaCode por defeito. A app segue as cores do LandeiroMaui só se o pedido disser "Cliente: Landeiro, usar cores do cliente" | Aplicar as cores do cliente por defeito: contraria a regra de marca | P2-T02 | 2026-11-05 |

## 5. Pendências

| # | Pendência | Dono | Bloqueia? (que task) | Estado |
|---|---|---|---|---|
| 1 | Word do plano de manutenção P.05-03 (v1.4, 2026) | Luís (Landeiro) | sim: P1-T01 (carga do plano) | Aberta |
| 2 | Acesso de leitura à BD de testes `TESTES_LAND2` (connection string no `appsettings`, preenchida pelo utilizador) | Utilizador | sim: P1-T01, P3-T01 | Aberta |
| 3 | Circuito PHC em detalhe (sessão com o Rui) | Rui (Landeiro) | não (levantamento corre antes) | Aberta |
| 4 | É possível criar tabelas e campos `u_` neste PHC? Quem cria e quando? | Utilizador / Landeiro | sim: P1-T03, P3-T03 | Aberta |
| 5 | Modelos de telemóvel (Android? versão mínima, câmara, leitor de códigos) | Landeiro | sim: P2-T02 | Aberta |
| 6 | Rede: Wi-Fi da fábrica cobre todas as zonas? VPN para fora? | Landeiro (informática) | não | Aberta |
| 7 | Quem dá entrada das peças no PHC (Compras ou manutenção)? | Landeiro | sim: P3-T02 | Aberta |
| 8 | PDF do manual Marel (350 MB, na pasta comum) disponível à SigmaCode | Osvaldo / Landeiro | sim: P3-T01 | Aberta |
| 9 | Prazo 11/11/2026: protótipo para validar. Prazo 17/11/2026 (Suposição): primeiras intervenções nos equipamentos novos | SigmaCode | ver risco R-01 | Aberta |
| 10 | Perfis (roles) do módulo: operador, supervisor, administrador de manutenção. Nomes e quem tem cada um | Landeiro | sim: P1-T02 | Aberta |
| 11 | Viaturas ficam fora do âmbito? (o ecrã Infraestruturas tem Via Verde e Kms) | Landeiro | não | Aberta |
| 12 | Confirmar D-02, D-04 e D-07 | Utilizador | sim: P1-T02, P2-T02, P3-T02 | Aberta |
| 13 | Nível de compatibilidade e versão do SQL Server da base real | Utilizador (SELECT em P1-T01) | não | Aberta |

## 6. Riscos

| # | Risco | Impacto | Mitigação | Task |
|---|---|---|---|---|
| R-01 | **Prazo 17/11/2026 irrealista** para app offline completa (a 1.ª fase inclui Android, offline, PIN e sincronização) | Landeiro fica sem ferramenta e continua em papel | Entregar a 11/11 só protótipo clicável. Para 17/11, solução provisória: registar no ecrã PHC atual com plano exportado do Word; a app sai por marcos | P2 |
| R-02 | Tabelas e campos novos têm de ser criados no PHC por pessoas, não pelo Claude Code | Atrasa P1-T03 e P3-T03 | Especificação fechada em P1-T02 e P3-T02 e script de verificação só leitura | P1, P3 |
| R-03 | Telemóvel liga direto ao SQL Server com credenciais SQL por utilizador | Exposição se o SQL sair da rede interna | Só Wi-Fi interno ou VPN; password nunca guardada; alternativa API (D-03) | P2 |
| R-04 | Peças: 3.300 a 4.000 referências por máquina, nomes em inglês e códigos duplos | Carga lenta e sujeita a erros | Carga por máquina, IA a propor e humano a validar, tradução ao lado | P3 |
| R-05 | Sem stock nem entradas, o consumo no telemóvel não tem sentido | Alertas de rutura inúteis | Receção (PEC-07) antes do consumo; procedimento comprar, receber, consumir | P3 |
| R-06 | Alvo Android pode afetar o projeto dos postos em produção | Regressão nos postos | Projeto novo partilhando bibliotecas (D-02); build dos postos intacto | P2 |
| R-07 | Transcrição do vídeo com erros | Requisitos mal lidos | Resumo marcado com Suposições; validar a 11/11 | todas |
| R-08 | Dois sistemas de periodicidade (lista da Ficha vs plano do Word) | Dados contraditórios | Lista única de periodicidades definida em P1-T02 | P1 |

## 7. Relatórios emitidos

| Data | Tipo | Destinatário | Ficheiro |
|---|---|---|---|
| 2026-11-05 | Resumo da reunião | Utilizador | `reuniao-2026-11-04/resumo.md` |

## 8. Registo

- 2026-11-05 · Planeamento criado: base comum, 3 partes, tasks e prompts (rascunho). Nenhuma task arrancou.

## 9. Próximo passo

O utilizador confirma as decisões D-02, D-04 e D-07 e fornece o acesso de leitura à base de testes e o Word do plano; depois arranca P1-T01, P2-T01 e P3-T01 (os três levantamentos podem correr ao mesmo tempo).
