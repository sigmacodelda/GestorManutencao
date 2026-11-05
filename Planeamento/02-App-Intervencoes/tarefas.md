# Parte 2: tarefas da app de intervenções

As seis tasks são as do `00-Comum/00-global.md` (P2-T01 a P2-T06). Dentro da parte são sequenciais (a task NN só arranca com a anterior CONCLUÍDA, com GATE confirmado). O prompt de cada task está em `prompts/`.

**Esforço (Suposição):** horas de trabalho com Claude Code mais revisão e gates do utilizador, em dias úteis de trabalho efetivo; não inclui esperas por outras partes, nem aquisição de telemóveis, nem criação de tabelas no PHC. Intervalos são mínimo a máximo, com a maior incerteza em T03 (spike de ligação ao SQL Server em Android) e T04 (offline). Total indicativo: 110 a 200 h.

## Resumo e dependências

| ID | Task | Esforço (h) | Depende de (na parte) | Depende de (outras partes) | Prompt |
|---|---|---|---|---|---|
| P2-T01 | Levantamento: LandeiroMaui, Android, dispositivos, offline | 3 a 6 | nenhuma | nenhuma (corre já) | `prompts/01-levantamento.md` |
| P2-T02 | Desenho: UX, protótipo, SQLite, PIN, sincronização | 12 a 24 | P2-T01 | P1-T02 (contratos e campos) | `prompts/02-desenho.md` |
| P2-T03 | Alvo Android, login, PIN, sessão | 20 a 36 | P2-T02 | P1-T02 fechada; decisões D-02 e D-10 | `prompts/03-desenvolvimento-login.md` |
| P2-T04 | Cache offline, fila de saída, sincronização | 32 a 56 | P2-T03 | P1-T03 (serviços) e tabelas criadas no PHC de testes | `prompts/04-desenvolvimento-offline.md` |
| P2-T05 | Lista, alerta, criar intervenção, foto, avançar ordem | 28 a 48 | P2-T04 | P1-T03 (`IManutencaoConfigService`); P3 depende desta | `prompts/05-desenvolvimento-intervencoes.md` |
| P2-T06 | Testes e documentação | 16 a 28 | P2-T05 | telemóvel real; P1-T04 para validar ponta a ponta | `prompts/06-testes-documentacao.md` |

Marcos propostos (Suposição, risco R-01): **M0** protótipo clicável a 11/11/2026 (fecho de P2-T02 parcial); **M1** login e lista online (fim de P2-T03 mais a lista sobre a leitura direta); **M2** offline completo (fim de P2-T04); **M3** alertas e fotos (fim de P2-T05). O fecho de P2-T06 é a entrega final.

## P2-T01 Levantamento (3 a 6 h)

Pode arrancar já (só leitura do código, sem acesso a base de dados).

- [ ] Ler os ficheiros indicados do LandeiroMaui (excertos) e montar a tabela reaproveitável, adaptar, não serve com ficheiro e símbolo (0,5 a 1 h)
- [ ] Confirmar o estado Android e a ausência de código offline (Grep) e comparar D-02 com recomendação (0,5 a 1 h)
- [ ] Permissões Android, versões mínimas e dependências candidatas com documentação oficial atual (1 a 2 h)
- [ ] Análise de segurança e rede (R-03): credencial usada pelos serviços, o que ficaria no APK, suporte do `SqlClient` em Android, opções A, B, C de credencial (0,5 a 1 h)
- [ ] Lista única de perguntas e pendências, com dono, recomendação e bloqueio (0,5 a 1 h)
- [ ] Atualizar `00-global.md` e escrever o GATE (0,25 a 0,5 h)

Critérios de conclusão: os 5 critérios do prompt 01. Dependências de outras partes: nenhuma. Entrega: `docs/manutencao/p2/levantamento.md`.

## P2-T02 Desenho (12 a 24 h)

Precisa de P2-T01 e dos contratos de P1-T02. Se P1-T02 ainda não tiver fechado, desenha-se com contratos provisórios marcados como Suposição (ver `PARAGEM COM PROPOSTAS` no prompt-mestre).

- [ ] Fluxo de ecrãs e wireframes de todos os ecrãs e estados (2 a 4 h)
- [ ] Protótipo clicável: partir do `mockups/ecras-telemovel.html` e ajustar ao que a Landeiro disser a 11/11 (2 a 4 h)
- [ ] Esquema SQLite (DDL, índices, purga, migração) (2 a 3 h)
- [ ] Sincronização: janela de dados, assinatura, operações da fila, tabela de cenários de conflito (2 a 4 h)
- [ ] PIN e sessão: parâmetros do hash, bloqueio, quando pede PIN e password (1 a 2 h)
- [ ] Decisão D-10 (credencial de sincronização) com o utilizador (0,5 a 1 h)
- [ ] Notificações: planeador, reagendamento, limites do Android (1 a 2 h)
- [ ] Contrato com a Parte 1: interfaces consumidas e pedidos de extensão (1 a 2 h)
- [ ] Revisão do desenho de segurança (`sc-revisor`) e correções (1 a 2 h)

Critérios de conclusão: os 5 critérios do prompt 02. Dependências de outras partes: P1-T02 (contratos, estados, campos, perfis, onde ficam as fotos). Entrega: `docs/manutencao/p2/desenho.md` e `prototipo/index.html`.

## P2-T03 Alvo Android, login, PIN, sessão (20 a 36 h)

Precisa das decisões D-02 e D-10 e de um telemóvel ou emulador.

- [ ] Spike S0: ligação a `TESTES_LAND2` a partir de Android com `SELECT` leve; registar resultado (4 a 8 h; é o maior risco técnico desta parte)
- [ ] Projeto `ManutencaoMobile` (net9.0-android), manifesto, ficheiros ligados, CSS móvel com tokens SigmaCode, `appsettings` com campo vazio (3 a 5 h)
- [ ] `SessaoMovelService` adaptado de `UserStateService`, resultado de login tipado, `ArmazenamentoSeguro` estrito (3 a 5 h)
- [ ] PIN: `PinHasher`, `PinService`, política de bloqueio, contador persistente (3 a 5 h)
- [ ] Ecrãs: login, definir PIN, desbloquear, landing (3 a 5 h)
- [ ] Regras de sessão: segundo plano, logout com pendentes, perfis recarregados, utilizador desativado (2 a 3 h)
- [ ] Testes xUnit, roteiro manual, build do LandeiroMaui para Windows sem alterações (2 a 4 h)
- [ ] Revisão (`sc-revisor`: segurança) e correções (2 a 3 h)

Critérios de conclusão: os 5 critérios do prompt 03. Dependências de outras partes: P1-T02 (nome do role do módulo). Entrega: projeto com login e PIN a funcionar; marco M1 (lista online) fecha com a leitura direta da task 05.

## P2-T04 Cache offline, fila de saída, sincronização (32 a 56 h)

Precisa dos serviços de P1-T03 e das tabelas e campos da Parte 1 criados na base de testes (nunca os cria o Claude Code).

- [ ] `LocalDb`: esquema, migrações, recuperação de corrupção (4 a 6 h)
- [ ] `LigacaoEstadoService` e classificação de erros de ligação (3 a 5 h)
- [ ] `IRemotoIntervencoes` e `CacheLeituraService` incremental por assinatura (6 a 10 h)
- [ ] `FilaSaidaService` e `SincronizadorService`: idempotência, ordem, backoff, dependências (8 a 14 h)
- [ ] Resolução de conflitos conforme o desenho (4 a 8 h)
- [ ] UI de estado: faixa, marcas, ecrã Pendentes, "Enviar agora", logout com pendentes (3 a 5 h)
- [ ] Testes xUnit com SQLite em memória e roteiro de corte de rede (3 a 5 h)
- [ ] Revisão (`sc-revisor`: SQL, segurança, mais de 5 ficheiros) e correções (3 a 5 h)

Critérios de conclusão: os 5 critérios do prompt 04. Dependências de outras partes: P1-T03 e tabelas criadas (pendência 4). Entrega: marco M2.

## P2-T05 Lista, alerta, criar intervenção, foto, avançar ordem (28 a 48 h)

- [ ] Regras puras com testes primeiro: `RegraOrdemHelper`, `PlaneadorAlertas`, `ValidacaoIntervencao` (4 a 6 h)
- [ ] Lista, filtros, faixa de alerta, marcas, cadeado (5 a 8 h)
- [ ] Ecrã criar e editar, rascunho automático, fila (6 a 10 h)
- [ ] Foto: captura, compressão, pasta privada, envio dependente (3 a 6 h)
- [ ] Notificações locais: biblioteca justificada, permissão, reagendamento, abrir ao tocar (4 a 8 h)
- [ ] Ponto de ligação às peças (`IPecasIntervencao`) escondido (1 a 2 h)
- [ ] Testes xUnit, roteiro manual, medição da lista (2 a 4 h)
- [ ] Revisão (`sc-revisor`) e correções (3 a 4 h)

Critérios de conclusão: os 6 critérios do prompt 05. Dependências de outras partes: P1-T03 (`IManutencaoConfigService`, `IIntervencaoService`); a Parte 3 (P3-T05) depende do ponto de ligação. Entrega: marco M3.

## P2-T06 Testes e documentação (16 a 28 h)

- [ ] Matriz APP-01 a APP-08 para testes e completar os que faltam (3 a 5 h)
- [ ] Roteiro manual em telemóvel real e execução (4 a 8 h; depende de aparelho)
- [ ] Medições (lista, arranque a frio, tamanho do APK e da base) (1 a 2 h)
- [ ] Documentação técnica e manual do utilizador com a marca SigmaCode (4 a 7 h)
- [ ] Revisão (`sc-revisor`) e correção de defeitos (3 a 5 h)
- [ ] Atualizar `docs/DEPENDENCIES.md` e `00-global.md`; reconciliação (1 h)

Critérios de conclusão: os 4 critérios do prompt 06. Dependências de outras partes: telemóvel real; ponta a ponta com a validação de P1-T04. Entrega: documentação e relatório de testes.
