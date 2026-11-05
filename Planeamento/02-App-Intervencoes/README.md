# Parte 2: app Android de intervenções (Gestor de Manutenção, Landeiro)

Estado: planeamento (rascunho 0.1, 05/11/2026). Nenhuma task arrancou. Requisitos da parte: APP-01 a APP-08 (`../00-Comum/requisitos-gerais.md`).

## 1. O que esta parte faz

App de telemóvel Android para o técnico registar intervenções de manutenção, **mesmo sem rede**: login por PIN ou por utilizador e password (base do login do LandeiroMaui), cache local SQLite, fila de saída e sincronização sem duplicados, alerta de intervenções planeadas com a antecedência definida no backoffice, lista com botão "+", ecrã simples para criar a intervenção (equipamento, preventiva ou curativa, tempos, técnico, descrição, foto), permissão de avançar ordem conforme a configuração do backoffice e ponto de ligação às peças usadas (Parte 3, 2.ª fase). A validação da ficha é do backoffice (Parte 1).

## 2. Ficheiros desta pasta

| Ficheiro | Para quê |
|---|---|
| `requisitos.md` | APP-01 a APP-08 com regras, critérios verificáveis, casos limite e perguntas; interpretação do esboço do quadro 3; **tabela de estados offline e de sincronização** |
| `fluxogramas.md` | 9 fluxogramas Mermaid (login e PIN, estados da sessão, navegação, registo, sequência de sincronização, conflitos, alertas, ordem, dependências e marcos) |
| `tarefas.md` | P2-T01 a P2-T06 em subtarefas, esforço, dependências |
| `mockups/ecras-telemovel.html` | Protótipo clicável de ficheiro único (marca SigmaCode, largura de telemóvel): login PIN e utilizador, lista com alerta, criar intervenção, pendentes, interruptor online e offline. É o marco M0 |
| `prompts/00-prompt-mestre.md` | Prompt de projeto grande (orçamentação, fases, controlo, paragem com propostas) |
| `prompts/01` a `06` | Um prompt por task: levantamento, desenho, login, offline, intervenções, testes e documentação |

## 3. Ordem de execução

1. **P2-T01 levantamento**: pode arrancar já (só leitura do LandeiroMaui, sem base de dados). Prompt 01 pronto a usar.
2. **P2-T02 desenho** e protótipo: precisa de P2-T01 e dos contratos de P1-T02. Prompts 02 a 06 são rascunho: rever cada um quando a task anterior fechar.
3. **P2-T03** login, PIN, sessão e alvo Android; **P2-T04** offline; **P2-T05** lista, alerta, criar, foto, ordem; **P2-T06** testes e documentação.
4. Dentro da parte as tasks são sequenciais, com GATE manual em cada uma. Os mesmos nomes de ficheiro de prompt estão fixados no `00-global.md`.

## 4. Dependências das outras partes (dizer sempre)

| A Parte 2 precisa de | Quando | O que acontece sem isso |
|---|---|---|
| P1-T02: contratos (`IIntervencaoService`, `IManutencaoConfigService` e os que fixar), estados, campos novos da ficha, nomes dos perfis, onde ficam as fotos | antes de P2-T02 fechar e de P2-T03 arrancar | O desenho usa contratos provisórios marcados "Suposição" e volta a rever |
| P1-T03: serviços e regras, e as tabelas e campos criados no PHC de testes por pessoas (nunca pelo Claude Code) | antes de P2-T04 | P2-T04 pára com `BLOQUEIO:` e lista o que falta |
| P1-T04: ecrã de validação | para testar ponta a ponta em P2-T06 | Testa-se só até Concluída |
| Telemóvel Android real e rede (pendências 5 e 6) | P2-T03 em diante | Testes em emulador; o roteiro de aparelho fica "não corrido" |
| Decisões D-02 e D-10 (e D-07 da Parte 1) | antes de P2-T03 | Sem elas, P2-T03 não arranca |

A Parte 3 depende desta: P3-T05 precisa do ponto de ligação `IPecasIntervencao` entregue em P2-T05.

## 5. Quem faz o quê

| Quem | Faz |
|---|---|
| Utilizador | Decide D-02 e D-10; fornece o acesso de leitura à base de testes (no `appsettings`); cria tabelas e campos no PHC; confirma os GATES; arranja telemóvel de teste; mostra o protótipo a 11/11 |
| Landeiro | Valida o protótipo; indica modelos de telemóvel, Wi-Fi e VPN, perfis, filtro das planeadas; confirma onde ficam as fotos com a informática |
| Agente principal (orquestrador) | Escreve os briefs, valida a evidência, atualiza o `00-global.md` |
| `sc-leitor` | Lê o LandeiroMaui (P2-T01) e o desenho da Parte 1 (P2-T02) |
| `sc-dev` | Implementa P2-T03 a P2-T05 e os testes |
| `sc-doc` | Documentação de P2-T06 |
| `sc-revisor` | Revisão obrigatória (segurança do desenho, SQL, mais de 5 ficheiros) |

## 6. Como arrancar

1. Confirmar com o utilizador D-02 (projeto novo `ManutencaoMobile`, recomendado) e a resposta à pergunta de segurança da secção 8.
2. Abrir uma sessão do Claude Code na raiz do repositório de código e colar `prompts/01-levantamento.md` (ou o prompt-mestre, que pede primeiro a orçamentação e termina com `ORÇAMENTO:`).
3. Em paralelo, mostrar `mockups/ecras-telemovel.html` num browser (PIN de demonstração 1234) e preparar a reunião de 11/11.
4. Quando o levantamento fechar e P1-T02 estiver pronto, rever o prompt 02 e arrancar.

## 7. Plano por marcos (risco R-01: o prazo de 17/11/2026 é irrealista)

Uma app offline completa (Android, PIN, SQLite, fila, conflitos, alertas, fotos) estima-se em **110 a 200 horas** (`tarefas.md`), sem contar esperas por P1-T02, tabelas no PHC e telemóveis. Não cabe em 12 dias. Proposta (**Suposição**, a validar a 11/11):

| Marco | O que entrega | Quando (Suposição) | Condição |
|---|---|---|---|
| **M0** | Protótipo clicável (este mockup, atualizado com o que a Landeiro disser) e levantamento | **11/11/2026** | Nenhuma além de abrir o ficheiro |
| **M1** | App Android com login (utilizador e password, PIN) e lista online, só na base de testes | fim de novembro | P1-T02 fechada, D-02 e D-10 decididas, spike de ligação ao SQL Server a funcionar |
| **M2** | Offline completo: cache, fila, sincronização sem duplicados, conflitos | meados de dezembro | P1-T03 e tabelas criadas no PHC de testes |
| **M3** | Alertas locais, fotos e criar intervenção completa; piloto com poucos técnicos | fim de dezembro ou início de janeiro | Telemóveis e Wi-Fi confirmados |
| Fecho | Testes de aparelho, documentação e manual (P2-T06) | janeiro | P1-T04 para validar ponta a ponta |

**O que usar até lá (17/11 em diante):** registo no ecrã PHC atual (Ficha de Intervenção), com o plano exportado do Word P.05-03 a servir de lista do que fazer, e papel como recurso quando não houver posto à mão, passando para o PHC como hoje. Não se mete a app em produção antes do M2 (sem offline o técnico perde dados em zonas sem cobertura). As datas M1 a M3 dependem sobretudo de P1-T02 e do spike de P2-T03.

## 8. Perguntas que bloqueiam

1. **D-10, credencial de sincronização (bloqueia P2-T03).** O LandeiroMaui valida o login SQL do utilizador mas depois usa a cadeia de ligação da app (`RecarregarRolesAsync`). No telemóvel isso põe essa credencial dentro do APK (o LandeiroMaui já a cifra com chave fixa no código), e o PIN não permite voltar a pedir a password para ligar ao SQL. **Isto está mal para utilizadores reais; é melhor assim:** (A) API fina intermédia, recomendada antes de haver utilizadores reais; (B) cadeia da app com conta SQL de mínimo privilégio, só `TESTES_LAND2` durante M1 e M2, com o acesso atrás de `IRemotoIntervencoes` para trocar por A depois; (C) guardar a password cifrada pelo Keystore (rejeitada: contraria "a password nunca se guarda"). Recomendação: B só em testes, A antes de produção.
2. D-02 (projeto novo `ManutencaoMobile`) e D-07 (backoffice Windows no LandeiroMaui).
3. Pendências 5 (modelos de telemóvel) e 10 (nomes dos perfis), e o filtro das planeadas (todas, por zona ou por técnico).

## 9. Pressupostos novos (todos "Suposição:", não estão no `00-global.md`)

1. **D-10** (credencial de sincronização), com as opções A, B, C acima.
2. Documentos e saídas da parte em `docs/manutencao/p2/` do repositório de código (ver Contradições, ponto 1).
3. Interpretação do esboço: linha do ecrã "Intr." = hora de início, técnico e hora de fim; caixa de texto = anomalia e intervenção; quadrado = foto; oval = botão de ação (guardar e concluir); "+" no canto superior direito como desenhado (avaliar botão flutuante).
4. Android 8.0 (API 26) como mínimo; distribuição do APK sem Google Play (instalação manual ou gestão de dispositivos da Landeiro).
5. PIN de 4 a 6 dígitos com PBKDF2 (iterações a fixar no desenho), contador de falhas persistente, máximo de 5 (`PIN_MAX_TENTATIVAS`, defeito 5 se a cache não o tiver); pede PIN ao abrir e após 5 minutos em segundo plano; logout com pendentes bloqueado.
6. Ordem das planeadas por data planeada e depois por `u_ordem`; sem permissão só se inicia a primeira por executar; configuração em falta assume "sem permissão"; curativas nunca bloqueadas.
7. Alertas: aviso às 08:00 do dia (data planeada menos antecedência), outro no próprio dia e um por dia enquanto atrasada; reagendam após sincronizar, reiniciar e mudar a configuração.
8. Cache: planeadas dos próximos 30 dias, em curso, atrasadas e concluídas dos últimos 2 dias; sincroniza ao abrir, ao voltar a rede, de 5 em 5 minutos em primeiro plano e por botão; sincronização com a app fechada fora do M2.
9. Um técnico por ficha; até 5 fotos comprimidas; a manutenção externa (fornecedor, folha de serviço) fica no backoffice (BO-10) e não na app.
10. Estado "Devolvida" na app: a supervisão devolve (Concluida para EmCurso) e a app precisa de saber que foi devolvida; pede-se a P1-T02 uma nota ou marca.
11. Marcos M0 a M3 com as datas indicadas e o esforço de 110 a 200 horas (`tarefas.md`).
12. Interna ou externa: a app regista intervenções internas; a externa é do backoffice até decisão contrária.

## 10. Contradições encontradas (segui a norma)

1. **Saídas duplicadas:** o `00-global.md` fixa `docs/levantamento.md` e `docs/desenho.md` para as três partes, no mesmo repositório de código. Colidem. Nesta parte os documentos vão para `docs/manutencao/p2/` (e `docs/tasks/00-global.md` mantém-se partilhado). A rever no `00-global.md` pela Parte 1 e Parte 3.
2. **Login offline:** o `docs/plano-modo-offline-cache.md` do LandeiroMaui põe "login offline" fora de âmbito e a Parte 2 exige trabalhar offline com PIN. Resolve-se assim: o PIN desbloqueia a sessão já guardada (sem rede); a primeira autenticação e qualquer nova autenticação com password exigem servidor.
3. **Plano offline "reaproveitável" mas não implementado:** a arquitetura geral diz reaproveitar o plano do LandeiroMaui; esse plano é só documento (confirmado: não há `LocalDb`, `FilaSaida` nem `LigacaoEstadoService` no código a 05/11/2026). Reaproveita-se o desenho, não código, e o esforço de P2-T04 conta com isso. Também ali o "par de linhas" não existe aqui: a unidade é a intervenção com os seus movimentos e fotos.
4. **"Password nunca guardada" e D-03:** com acesso direto ao SQL Server, a sincronização em segundo plano depois de um PIN precisa de credencial; sem password guardada, só resta a cadeia da app (no APK). Ver D-10, secção 8.
5. **`SafeSecureStorage` e a regra "erros nunca em silêncio":** o LandeiroMaui engole falhas de escrita no cofre (`SafeSecureStorage.SetAsync`) e `LoginBDAsync` devolve `false` para qualquer erro (rede ou credenciais). Para o PIN e para distinguir "sem rede" de "password errada" criam-se versões estritas na app móvel (P2-T03); não se alteram as do LandeiroMaui.
6. **Estado "Devolvida":** não consta dos estados gerais (`fluxogramas-gerais.md`, secção 2: Concluida volta a EmCurso). É uma marca de ecrã da app; ver pressuposto 10.
7. **Regras do LandeiroMaui para a app móvel:** o `CLAUDE.md` do LandeiroMaui impõe teclado virtual e postos táteis; não se aplicam ao telemóvel (teclado nativo, alvos de 48 dp). O resto (Dapper com `NOLOCK`, `CrashLogService`, `Perfis`, módulo `Manutencao`, xUnit com ficheiros ligados) mantém-se.

## 11. Estado do protótipo (verificação)

O protótipo passou a verificação de sintaxe do JavaScript e abriu num browser (ecrã de login com PIN e painel de demonstração visíveis). **Não percorri os restantes ecrãs nem o modo de largura de telemóvel**; a verificação dos ecrãs de lista, criar e pendentes fica por fazer.
