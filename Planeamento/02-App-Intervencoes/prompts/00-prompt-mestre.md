# Prompt-mestre da Parte 2: app Android de intervenções (Gestor de Manutenção, Landeiro)
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: P2-T01 a P2-T06 (projeto grande) · Tipo: levantamento, desenho, desenvolvimento, testes-documentacao
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode em cada task de desenho e desenvolvimento; Autónomo no levantamento e em testes e documentação
Papel do agente: orquestrador (decide, escreve os briefs dos subagentes, valida a evidência que eles devolvem; nunca implementa o que um subagente pode implementar)
Gate: manual (um GATE por task; nenhuma task arranca sem a confirmação da anterior)
Agentes: `sc-leitor`, `sc-dev`, `sc-doc`, `sc-revisor`

## COMO COMEÇAR

1. Este prompt orquestra as 6 tasks da Parte 2 (app de telemóvel para registar intervenções de manutenção, com modo offline). Os prompts `01-levantamento.md` a `06-testes-documentacao.md` são entregues por mim, um de cada vez; o essencial de cada um está resumido nas FASES abaixo.
2. Preenche o que está entre `[ ... ]`: nada, nesta versão. Os acessos (base de testes, telemóvel) são pedidos na FASE 0, numa só mensagem.
3. Executa só a FASE 0 e termina com `ORÇAMENTO:`. Espera a minha resposta `APROVADO` (ou ajustes).
4. Depois de `APROVADO`, executa a FASE 1 (levantamento) e pára no GATE. As seguintes arrancam só quando eu confirmar o GATE anterior.

## PAPEL, OBJETIVO E MARCA

Papel: engenheiro de software sénior (.NET 9 MAUI Blazor Hybrid em Android, SQLite, Dapper, SQL Server, PHC) a orquestrar subagentes. Escreves em português de Portugal.

Objetivo: entregar a app de telemóvel (Parte 2) com: login por PIN ou utilizador e password (base do login do LandeiroMaui), **trabalho obrigatório offline** (cache SQLite, fila de saída, sincronização sem duplicados), alerta de intervenções planeadas com a antecedência definida no backoffice, lista com botão "+", ecrã simples para criar a intervenção (equipamento, preventiva ou curativa, tempos, técnico, descrição, foto), avançar ordem conforme a configuração do backoffice e ligação às peças (Parte 3, 2.ª fase).

Fluxo ponta a ponta: backoffice gera as planeadas (Parte 1) → app sincroniza e avisa → técnico inicia ou cria curativa → regista, fotografa → fila de saída offline → sincroniza → supervisão valida no backoffice (Parte 1) → ficha Fechada.

Marca: SigmaCode por defeito em documentos e protótipo (verde #5BB572, azul #3466AF, roxo #604696, azul-escuro #1A1731, cinzento #CAC9C9; Exo 2 e Open Sans). A app só usa as cores do LandeiroMaui se este pedido disser "Cliente: Landeiro, usar cores do cliente".

Dependências de outras partes (diz-las no orçamento): P2-T02 precisa dos contratos de P1-T02; P2-T03 e seguintes precisam dos contratos fechados; P2-T04 precisa dos serviços de P1-T03 e das tabelas e campos criados no PHC de testes; P2-T05 fornece o ponto de ligação a P3-T05.

<context>
- Repositórios: `C:\_SigmaRepos\Landeiro-Desmancha` (código; LandeiroMaui é a base, só se editam ficheiros nele quando a task o permitir) e `C:\_SigmaRepos\GestorManutencao` (planeamento); branch `dev-landeiro-manutencao-p2`; documentos em `docs/manutencao/p2/`.
- LandeiroMaui: só compila para Windows, postos táteis 1024x768, alvo Android retirado. O plano offline do LandeiroMaui é só desenho (nada implementado). O login é o login SQL do próprio utilizador e guarda perfis `DES_*` em `SecureStorage`.
- Decisões: D-01 partes em paralelo; D-02 (proposta, a confirmar) projeto novo `ManutencaoMobile` que partilha bibliotecas por ficheiros ligados; D-03 Dapper direto ao SQL Server; D-05 planeadas são fichas com estado Planeada; D-06 PIN só no dispositivo; D-09 marca SigmaCode por defeito.
- Prazos: protótipo clicável a 11/11/2026; 17/11/2026 (Suposição) primeiras intervenções nos equipamentos novos.
- Risco R-01: o prazo de 17/11/2026 é irrealista para uma app offline completa. Marcos propostos (Suposição): M0 protótipo a 11/11; M1 login e lista online; M2 offline; M3 alertas e fotos. Até lá regista-se no ecrã PHC atual com o plano exportado do Word.
- Risco R-03: o telemóvel liga ao SQL Server com a cadeia da app (que ficaria no APK) e login SQL por utilizador. Mitigação: só rede interna ou VPN, conta de mínimo privilégio, acesso remoto atrás de interface, e proposta de API fina antes de haver utilizadores reais (decisão D-10, a fechar em P2-T02).
Nota: sem credenciais, tokens nem connection strings.
</context>

<environment>
- PHC Evolution (Ultimate) 2025.04, cliente Carnes Landeiro; SQL Server [CONFIRMAR], nível de compatibilidade 100 [CONFIRMAR].
- Escrita só em `TESTES_LAND2` e `LAND2_DEMO`, depois de `SELECT DB_NAME();`. `LAND2` é só leitura. Connection string só no `appsettings`, preenchida por mim; nunca em código, prompts, logs nem documentação.
- Nunca crias tabelas nem campos nem alteras triggers existentes: especificas e eu crio no PHC. Podes criar SPs `sc_<Entidade><Acao>` (nunca `sp_`), views e triggers novos só na base de testes e com confirmação por lote.
- Telemóvel Android [CONFIRMAR modelos]; Wi-Fi da fábrica ou VPN [CONFIRMAR]. Máquina de desenvolvimento Windows com as cargas de trabalho MAUI e Android [CONFIRMAR]; não instalas nada.
</environment>

## FASE 0: ORÇAMENTAÇÃO (obrigatória, antes de qualquer trabalho)

Executa só esta fase e termina com `ORÇAMENTO:`. Não leias nem escrevas nada além do que for preciso para orçar.

1. **Levantamento único** (numa só mensagem): o que falta para executar sem perguntas a meio. Acessos (base de testes, telemóvel e emulador, ligação), decisões (D-02, D-10, filtro das planeadas, perfis), dependências das Partes 1 e 3, riscos, pendências 5, 6 e 10.
2. **Cronometragem real** do que for medível (por exemplo, quanto demora um build Android neste computador, se o SDK e o emulador existem) ou "não mensurável".
3. **Tabela de orçamentação por task** (P2-T01 a P2-T06, mais a própria Fase 0) com: tokens de entrada, tokens de saída, total, intervalo mínimo, provável e máximo, tempo estimado, e os pressupostos de cada linha. Âmbito de partida (os números são teus, com os pressupostos escritos): T01 leitura e escrita de um documento; T02 desenho com esquema, regras e um protótipo HTML; T03 projeto novo, spike de ligação, PIN, ecrãs e testes; T04 base local, cache incremental, fila, conflitos, UI de estado e testes; T05 lista, criar, câmara, notificações, regras puras e testes; T06 testes de aparelho, medições e documentação. Cada task de código inclui uma revisão do `sc-revisor`.
4. **Reserva de contingência** de pelo menos 15%; **margem de segurança** de 5% a 15% sobre o máximo; o **teto** (máximo mais margem) mostrado à parte. O teto é limite duro.
5. **Ponto de corte** e, se o total passar o limite que eu indicar, duas variantes: Completa (M0 a M3) e MVP (M1 e M2, com alertas e fotos fora).
6. Termina com `ORÇAMENTO:` e pára. Espera `APROVADO`.

## FASE 1: levantamento (P2-T01), só após APROVADO

Subagente `sc-leitor`. Resumo: o que do LandeiroMaui se reaproveita (login, sessão, armazenamento seguro, plano offline) e o que não serve (só Windows, teclado virtual, posto tátil); D-02; permissões Android e dependências candidatas; R-03 e a credencial; lista única de perguntas. Saída: `docs/manutencao/p2/levantamento.md`.
Critério de conclusão: os 5 critérios do prompt 01 com evidência. Passo seguinte: GATE; depois FASE 2.

## FASE 2: desenho e protótipo (P2-T02)

Precisa de P2-T01 e dos contratos de P1-T02. Resumo: fluxo e wireframes (login PIN e utilizador, lista, criar, pendentes), esquema SQLite, sincronização e conflitos, PIN e sessão, notificações locais, contrato com a Parte 1, protótipo clicável para 11/11. Saídas: `docs/manutencao/p2/desenho.md` e `prototipo/index.html`. Revisão do desenho de segurança pelo `sc-revisor`.
Critério: os 5 critérios do prompt 02. Passo seguinte: GATE; depois FASE 3. Se P1-T02 não tiver fechado, `PARAGEM COM PROPOSTAS` (ver abaixo).

## FASE 3: alvo Android, login, PIN, sessão (P2-T03)

Subagente `sc-dev`, revisão `sc-revisor`. Spike S0 (ligação ao SQL Server a partir do Android), projeto `ManutencaoMobile`, sessão adaptada do LandeiroMaui, PIN com hash, bloqueio, sessão longa.
Critério: os 5 critérios do prompt 03, incluindo que o build Windows do LandeiroMaui não foi afetado. Passo seguinte: GATE; depois FASE 4.

## FASE 4: offline (P2-T04)

Precisa dos serviços de P1-T03. `LocalDb`, estado de ligação, cache incremental, fila idempotente, conflitos, UI de estado. Critério: os 5 critérios do prompt 04. Passo seguinte: GATE; depois FASE 5.

## FASE 5: intervenções (P2-T05)

Lista, criar, foto, alertas locais, avançar ordem, ponto de ligação às peças. Critério: os 6 critérios do prompt 05. Passo seguinte: GATE; depois FASE 6 (e avisar a Parte 3 de que o ponto de ligação existe).

## FASE 6: testes e documentação (P2-T06)

Matriz de rastreabilidade, roteiro manual no telemóvel, medições, documentação técnica e manual. Critério: os 4 critérios do prompt 06. Passo seguinte: ENTREGÁVEIS E RECONCILIAÇÃO.

### Formato de cada entrega e regras comuns às fases

Cada entrega segue ANÁLISE, PLANO, IMPLEMENTAÇÃO, REVISÃO, TESTES, RISCOS e, no fim, ESTADO DA TASK e AUTOCRÍTICA. Regras: segue os padrões do repositório e as regras do `CLAUDE.md` do LandeiroMaui; sem dependências novas sem registo em `docs/DEPENDENCIES.md` e documentação oficial atual; Dapper cru com `WITH (NOLOCK)` e SQL nível 100; erros nunca em silêncio (`CrashLogService.Registar` e aviso); PIN só como hash com sal no armazenamento seguro; nada de segredos em lado nenhum (corre `verificar-segredos.ps1` antes de entregar); testes xUnit para a lógica e roteiro manual, declarado não corrido quando não houver aparelho; resultados verdadeiros; comentários mínimos em português; branch próprio, sem commit nem push sem confirmação (no LandeiroMaui nunca).

### Bloco de cada subagente (brief)

Cada subagente recebe: objetivo, contexto, ficheiros, contrato (interfaces e tabelas), regras, fora de âmbito, papel (só desenvolve, só analisa ou só revê) e formato do retorno (evidência técnica: ficheiros alterados, testes corridos com o resultado, riscos, `MELHORIA:` ou `nenhuma`). `sc-leitor`: só lê e devolve factos com evidência. `sc-dev`: só implementa. `sc-doc`: só escreve documentos. `sc-revisor`: só revê e aponta, não corrige.

## CONTROLO DURANTE A EXECUÇÃO

No fim de cada agente (e de cada task), regista tokens e tempo reais contra os estimados e o desvio em %. A 80% do teto aprovado, avisa. No teto, pára obrigatoriamente e responde com a PARAGEM COM PROPOSTAS. Mantém um quadro corrente (task, estimado, real, desvio).

## PARAGEM COM PROPOSTAS

Qualquer coisa que trave o trabalho (teto, credencial em falta, decisão de negócio, falha repetida, conflito, risco, contrato da Parte 1 por fechar, tabelas por criar no PHC) obriga a parar e a responder com 2 a 4 propostas, a recomendada primeiro, cada uma com impacto em tokens, tempo e risco, nunca com pergunta aberta nem a continuar em silêncio. Mantém o que não depende do bloqueio. Exemplo para a Fase 2 sem contratos: (1) desenhar com contratos provisórios marcados como Suposição e rever quando P1-T02 fechar; (2) desenhar só ecrãs e PIN; (3) esperar por P1-T02.

## ENTREGÁVEIS E RECONCILIAÇÃO

Lista fechada: `docs/manutencao/p2/` (`levantamento.md`, `desenho.md`, `prototipo/index.html`, `plano-de-testes.md`, `roteiro-manual.md`, `documentacao-tecnica.md`, `manual-do-utilizador.md`); projeto `ManutencaoMobile` com testes; `docs/DEPENDENCIES.md`; `docs/tasks/00-global.md` atualizado (estados, decisões, pendências, riscos, registo).
No fim: riscos e impactos abertos (R-01, R-03 em particular), quadro orçamento aprovado contra real (por agente e total) e lições para orçar melhor, e `MELHORIA: <...>` (ou `nenhuma`).
