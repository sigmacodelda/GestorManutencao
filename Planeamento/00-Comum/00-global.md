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
| D-02 | **Confirmada pelo utilizador (05/11/2026):** app de telemóvel em projeto novo `ManutencaoMobile` que partilha bibliotecas com o LandeiroMaui | Alvo Android dentro do LandeiroMaui: mais risco para os postos em produção | P2-T01, P2-T02 | 2026-11-05 |
| D-03 | Acesso a dados como no LandeiroMaui: Dapper direto ao SQL Server, login SQL por utilizador (instrução do utilizador: usar a base do LandeiroMaui) | App → API → sServices (norma geral): mais camadas e prazo maior; fica como opção se a segurança o exigir | P2-T02 | 2026-11-05 |
| D-04 | **Confirmada pelo utilizador (05/11/2026):** referências das peças no PHC, em família própria, para stock, compras e receção nativos. **A família e os artigos ainda não estão criados: a solução tem de funcionar sem eles (ver D-14)** | Ambiente separado: duplica stock e compras | P3-T01, P3-T02 | — |
| D-05 | Intervenções planeadas materializam-se como fichas com `u_estado = Planeada` | Calcular só em memória: a app offline precisa de linhas com stamp | P1-T02 | 2026-11-05 |
| D-06 | **Confirmada pelo utilizador (05/11/2026), depois de uma correção:** o PIN existe só na aplicação (hash com sal no telemóvel), sem campo novo no PHC. Com login já registado, ao reabrir a app basta o PIN. Sem login registado, faz login normal e depois pode criar o PIN | PIN central no PHC: o utilizador retirou-o (primeira indicação do mesmo dia, corrigida) | P2-T02 | 2026-11-05 |
| D-07 | **Confirmada pelo utilizador (05/11/2026):** "Backoffice" = ecrãs Windows do módulo Manutenção no LandeiroMaui, sobre as tabelas do PHC; esse trabalho já está a ser tratado à parte, mas o utilizador quer o planeamento completo também nesta pasta (mantém-se a Parte 1 inteira) | Painéis dentro do PHC: não é o que o PAT 2 diz ("na plataforma .NET MAUI") | P1-T02 | 2026-11-05 |
| D-08 | Documentos e mockups em Markdown + Mermaid e HTML, com a marca SigmaCode | Word/PDF: podem sair depois | todas | 2026-11-05 |
| D-09 | UI de mockups com paleta SigmaCode por defeito. A app segue as cores do LandeiroMaui só se o pedido disser "Cliente: Landeiro, usar cores do cliente" | Aplicar as cores do cliente por defeito: contraria a regra de marca | P2-T02 | 2026-11-05 |
| D-10 | **Decidida pelo utilizador (05/11/2026): sem API; login exatamente igual ao do LandeiroMaui (login SQL de cada utilizador).** Pressuposto para o PIN funcionar offline e a sincronização não pedir a password de cada vez: no login online a password é guardada cifrada no cofre do Android (SecureStorage) e o PIN, criado só na app, desbloqueia-a. É a opção C abaixo, que tinha recomendado não usar; fica por decisão do utilizador. Mitigações previstas: contas SQL por técnico só com as tabelas de manutenção, SQL só acessível na rede da fábrica (Wi-Fi ou VPN), bloqueio após tentativas de PIN falhadas, e perda do telemóvel resolvida mudando a password SQL do técnico. Registo original: **Por decidir (bloqueia P2-T03): credencial de sincronização da app móvel.** Depois do login, os serviços do LandeiroMaui usam a cadeia de ligação da app (ficaria dentro do APK) e o PIN não permite voltar a pedir a password. Opções: (A) API fina intermédia; (B) cadeia da app com conta SQL de mínimo privilégio, só em `TESTES_LAND2` durante M1 e M2, com o acesso atrás de `IRemotoIntervencoes`; (C) guardar a password cifrada. Recomendação: B só em testes e A antes de produção. **Isto está mal para utilizadores reais herdar de D-03 sem mais** | C: rejeitada, guarda credenciais no telemóvel | P2-T02, P2-T03 | — |
| D-11 | **Por decidir (bloqueia P3-T01 na parte do PDF). O PDF ainda não foi recebido: pedir à Landeiro (pergunta D7). Até lá, só amostra:** autorização da Landeiro para tratar o PDF Marel com serviços externos (IA em nuvem, incluindo o Claude Code, que envia o texto lido ao modelo). Até haver autorização: extração local sem IA e amostras mínimas | Assumir "processa localmente": inexato | P3-T01, P3-T03 | — |
| D-12 | **Confirmada pelo utilizador (05/11/2026):** um artigo por código do fabricante, ligações às máquinas por posição (a mesma peça em vários conjuntos não fragmenta o stock) | Um artigo por ocorrência na máquina: fragmenta stock | P3-T02 | — |
| D-14 | **Requisito do utilizador (05/11/2026):** a Parte 3 funciona sem a família de artigos e sem os artigos criados no PHC. As peças vivem primeiro numa tabela própria (`u_manpeca`) com a ligação ao artigo PHC vazia até existir; stock, compras e receção ligam-se quando o artigo for criado (carga em lote idempotente) | Esperar pelos artigos: bloqueia a carga e o protótipo | P3-T01, P3-T02, P3-T05 | 2026-11-05 |
| D-13 | Saídas de documentação por parte, para não colidirem: P1 em `C:\_SigmaRepos\GestorManutencao\docs\` (trabalho) e `LandeiroMaui\docs\manutencao\` (final); P2 em `docs/manutencao/p2/`; P3 em `docs/pecas-armazem/`. Os caminhos "Saídas" da secção 3 são os dos prompts | Uma só pasta `docs/` para as 3 partes: colide | todas | 2026-11-05 |

## 5. Pendências

| # | Pendência | Dono | Bloqueia? (que task) | Estado |
|---|---|---|---|---|
| 1 | Word do plano de manutenção P.05-03 (v1.4) | Luís (Landeiro) | sim: P1-T01 (carga do plano) | **Recebido a 05/11/2026**; análise em nalise-plano-word-p05-03.md |
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
| 14 | Decidir D-10 (credencial de sincronização da app) | Utilizador | sim: P2-T03 | Aberta |
| 15 | Decidir D-11 (PDF Marel e IA em nuvem: até onde) | Utilizador / Landeiro | sim: P3-T01 (parte do PDF), P3-T03 | Aberta |
| 16 | Decidir D-12 (um artigo por código do fabricante) | Utilizador | sim: P3-T02 | Aberta |
| 17 | Impressora e rolo de etiquetas (formato assumido 60x40 mm) | Landeiro | sim: P3-T04 | Aberta |
| 18 | Filtro das planeadas que o operador vê (por zona, por equipamento ou todas) | Landeiro | não | Aberta |
| 20 | Excel de origem Lista de infraestruturas1.xlsx (folha "plano de manutenção") e confirmar se existe versão do plano mais recente que 2023-11-13 | Landeiro | não (melhora a carga) | Aberta |
| 21 | Landeiro valida o plano do Word: 2 células por mês (Suposição: quinzenas), Nºs repetidos (225, 231, 233), 5 sem Nº, 3 decimais, Interna+Externa (14), periodicidades fora da lista | Landeiro | sim: carga em P1-T03 | Aberta |
| 19 | Percorrer o protótipo clicável do telemóvel de ponta a ponta antes de 11/11 (o agente só testou login e sintaxe) | SigmaCode | não | Aberta |

## 6. Riscos

| # | Risco | Impacto | Mitigação | Task |
|---|---|---|---|---|
| R-01 | **Prazo 17/11/2026 irrealista** para app offline completa (a 1.ª fase inclui Android, offline, PIN e sincronização) | Landeiro fica sem ferramenta e continua em papel | Entregar a 11/11 só protótipo clicável. Para 17/11, solução provisória: registar no ecrã PHC atual com plano exportado do Word; a app sai por marcos | P2 |
| R-02 | Tabelas e campos novos têm de ser criados no PHC por pessoas, não pelo Claude Code | Atrasa P1-T03 e P3-T03 | Especificação fechada em P1-T02 e P3-T02 e script de verificação só leitura | P1, P3 |
| R-03 | Telemóvel liga direto ao SQL Server com credenciais SQL por utilizador | Exposição se o SQL sair da rede interna | Só Wi-Fi interno ou VPN; password nunca guardada; alternativa API (D-03) | P2 |
| R-09 | Cadeia de ligação ao SQL dentro do APK (D-10) | Credencial extraível do telemóvel | Conta de mínimo privilégio só em testes; API antes de produção | P2 |
| R-10 | O plano offline do LandeiroMaui é só desenho (não há `LocalDb` nem `FilaSaida` no código) | Esforço de P2-T04 maior | Esforço de P2-T04 já conta com isso (Parte 2 estima 110 a 200 h) | P2 |
| R-04 | Peças: 3.300 a 4.000 referências **no total** (Midas cerca de 300), nomes em inglês e códigos duplos | Carga lenta e sujeita a erros | Carga por máquina, IA a propor e humano a validar, tradução ao lado | P3 |
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
- 2026-11-05 · Word P.05-03 recebido e analisado: CSV normalizado de 107 linhas em fontes/, problemas de qualidade e pendências 20 e 21. Prompt P1-T01 atualizado.
- 2026-11-05 · Partes 1, 2 e 3 escritas por agentes e consolidadas aqui: D-10 a D-13, pendências 14 a 19, riscos R-09 e R-10. Esforço estimado (documentado em cada `tarefas.md`): P1 57 a 102 h, P2 110 a 200 h, P3 108 a 174 h.
- Nota de contradição das partes: os agentes seguiram o bloco comum (nenhum git no LandeiroMaui) em vez de "branch por parte"; o branch, se existir, cria-o o utilizador fora do agente.

## 9. Próximo passo

O utilizador confirma as decisões D-02, D-04 e D-07 e fornece o acesso de leitura à base de testes e o Word do plano; depois arranca P1-T01, P2-T01 e P3-T01 (os três levantamentos podem correr ao mesmo tempo).
