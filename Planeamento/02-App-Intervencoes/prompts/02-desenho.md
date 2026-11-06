# Desenho da app de intervenções (UX, protótipo, cache SQLite, PIN, sincronização)
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 02 (P2-T02) · Tipo: desenho
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO e espera a minha confirmação; depois autónomo até ao GATE)
Papel do agente: analista (desenha e documenta; não escreve código da app; o protótipo HTML é documento)
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 2: a app GestorManutencao já está construída com login só por PIN (`u_manutil`, campo novo `tecno`), perfis operador e supervisor; o técnico é o utilizador PHC (`us.userno`) gravado em `tecno`/`tecnome`. Login SQL por utilizador e PIN local (D-03/D-06/D-10 antigos) deixam de valer. Ecrãs Planeadas e Material usado na ficha estão em construção.
Entradas: `docs/manutencao/p2/levantamento.md` (P2-T01); `docs/manutencao/p1/desenho.md` com os contratos (P1-T02); protótipo de partida `ecras-telemovel.html`, se o utilizador o fornecer
Saídas: `docs/manutencao/p2/desenho.md`; `docs/manutencao/p2/prototipo/index.html` (protótipo clicável, ficheiro único); `docs/tasks/00-global.md` atualizado
Critérios de conclusão:
1. Fluxo de ecrãs e wireframes de todos os ecrãs (login PIN, login utilizador, lista, criar, detalhe, pendentes, definições) e dos estados (online, offline, a enviar, sincronizado, pendente, erro, conflito, devolvida, sem permissão).
2. Esquema SQLite completo (DDL, chaves, índices, purga, migração de versões) e o que fica em armazenamento seguro e nunca no SQLite.
3. Regras de sincronização, idempotência, conflitos e avançar ordem, em tabela de cenários com o resultado de cada um.
4. PIN e sessão (hash com sal, bloqueio, quando pede PIN e quando pede password), credencial de sincronização (decisão D-10) e notificações locais (agendamento, limites do Android).
5. Contrato com a Parte 1 (interfaces consumidas e pedidos de extensão a P1-T02, com os campos que a app lê e escreve) e protótipo clicável aberto num browser em largura de telemóvel e validado com o utilizador.
Gate: manual
Agentes: `sc-leitor` (ler o desenho da Parte 1), `sc-revisor` (revisão final do desenho de segurança)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid (Android), SQLite, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto e cliente: Gestor de Manutenção da Carnes Landeiro. Passa o circuito de manutenção (hoje papel, Word e PHC) para a plataforma .NET MAUI da Landeiro. Esta é a Parte 2: app de telemóvel Android para registo de intervenções. A Parte 1 (backoffice Windows, dados e regras no PHC) e a Parte 3 (peças e armazém) correm em paralelo.
- Repositórios: `C:\_SigmaRepos\Landeiro-Desmancha` (código; `LandeiroMaui` é a base) e `C:\_SigmaRepos\GestorManutencao` (planeamento); branch `dev-landeiro-manutencao-p2`. Documentos desta parte em `docs/manutencao/p2/` do repositório de código.
- Requisitos da Parte 2:
  - APP-01 Login só por PIN (tabela PHC `u_manutil`; app GestorManutencao já construída; o login SQL por utilizador do LandeiroMaui deixou de ser a base); a sessão mantém-se o máximo de tempo possível.
  - APP-02 **Obrigatório trabalhar offline**: cache local SQLite, fila de saída e sincronização.
  - APP-03 Alerta de intervenções planeadas com a antecedência definida no backoffice (funciona offline).
  - APP-04 Lista de planeadas e pendentes, com botão "+" para criar.
  - APP-05 Ecrã simples e prático para criar a intervenção: equipamento, preventiva ou curativa, tempos, técnico, descrição, foto.
  - APP-06 Avançar intervenções fora de ordem só se a configuração do backoffice o permitir.
  - APP-07 Sincronização sem duplicados e com resolução de conflitos (ganha a validação do backoffice).
  - APP-08 Peças usadas na intervenção (liga à Parte 3, 2.ª fase).
- Estados da intervenção: Planeada, EmCurso, Concluida, Validada (supervisão, no backoffice). A curativa nasce EmCurso no telemóvel. A supervisão pode devolver (Concluida para EmCurso) ou cancelar (Planeada para Cancelada). A ficha só fica Fechada quando validada.
- Esboço do quadro (reunião de 04/11/2026): ecrã de lista com botão "+" no canto superior direito, que abre o ecrã "Intr." com uma linha de campos de tempo e pessoa, uma caixa de texto, uma foto e um botão de ação. Nota ao lado: "configuração no BO que defina se pode avançar ordem intervenções". "Obrigatório trabalhar" (offline).
- Prazos: protótipo clicável a 11/11/2026; 17/11/2026 (Suposição) primeiras intervenções. Marcos propostos (Suposição): M0 protótipo 11/11; M1 login e lista online; M2 offline; M3 alertas e fotos.
- Situação atual e problema: ainda não há desenho. O protótipo é para a reunião de 11/11/2026 e serve para a Landeiro validar fluxo e ecrãs, não o código.
- Anexos: nenhum (o conteúdo de P2-T01 e de P1-T02 está nas Entradas).
Nota: sem credenciais, tokens nem connection strings.
</context>

<environment>
- PHC: edição Evolution (Ultimate), versão 2025.04. Cliente: Carnes Landeiro. Alojamento: servidor do cliente [CONFIRMAR].
- SQL Server: versão [CONFIRMAR]. Nível de compatibilidade da base real: assumir 100 [CONFIRMAR]. `LAND2_DEMO` está em 150 e não prova compatibilidade com 100.
- Bases: `TESTES_LAND2` (testes) e `LAND2_DEMO` (demo) são as únicas onde se pode escrever, depois de `SELECT DB_NAME();` e de confirmar o nome. `LAND2` (produção) é só leitura. A connection string vive no `appsettings` do projeto, preenchida pelo utilizador; nunca a escrevas em código, prompts, logs nem documentação.
- Podes criar: SPs (`sc_<Entidade><Acao>`, nunca `sp_`), views (`sc_v_<Nome>`) e triggers novos, só na base de testes e com confirmação por lote. Se o padrão do projeto for SQL inline com Dapper, segue o padrão (é o do LandeiroMaui).
- Nunca crias tabelas nem campos (nem CREATE TABLE, nem ALTER TABLE), nem alteras, desativas ou apagas triggers existentes (incluindo encriptados). Tabelas e campos novos: especifica-os (nome, tipo, tamanho, nulo, valor por defeito, regra) para o utilizador os criar no PHC. Índices, funções e tipos só com autorização explícita registada em <decisions>.
- Triggers existentes nas tabelas envolvidas: [a ler no levantamento da Parte 1].
- Projeto de código base: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (.NET 9, MAUI Blazor Hybrid, Dapper). Lê `CLAUDE.md` e `ARQUITETURA.md` desse projeto antes de escrever código e cumpre as suas regras. Nesse repositório não faças commits, branches nem push: só editas ficheiros.
- Telemóvel Android: modelos e versão mínima [CONFIRMAR, pendência 5]; Suposição: Android 8.0 (API 26) ou superior. Rede: Wi-Fi da fábrica ou VPN [CONFIRMAR, pendência 6]; o telemóvel só liga ao SQL Server dentro da rede interna ou por VPN.
- Máquina de desenvolvimento: Windows com Visual Studio e as cargas de trabalho .NET MAUI e Android (SDK, JDK, emulador) [CONFIRMAR]. Não instalas nada: se faltar alguma peça, listas o que falta e continuas o que não depende dela.
</environment>

<phc-tables>
Esta task não cria nem especifica tabelas novas: usa as da Parte 1 (a definir em P1-T02). Candidatos que a app vai ler e escrever, com nomes provisórios que P1-T02 fixa (não os dês como definitivos):
- Ficha de Intervenção `u_fichaint` (já existe na base): `estado` (Planeada, EmCurso, Concluida, Validada, Cancelada), `hini`, `hfim`, `tecno`, `tecnome`, `foraordem`, `valobs`; sem `u_planostamp`/`u_dataplan`/`u_ordem` por defeito (por confirmar se algum falta: seria NOVO).
- `u_itmanutli`, `u_periocid` e `u_configman` (`antecedencia`, `avancaordem`, `pinmaxtent`, entre outros) já existem; `u_manutil` (login por PIN) já existe, com campo novo `tecno`.
Pedidos opcionais a P1-T02 que o desenho deve avaliar: nota da supervisão ao devolver (`varchar(254)`), indicação "fora de ordem", e onde ficam as fotos (anexos do PHC ou pasta de rede; decide-se em P1-T01 e P1-T02). Idempotência e deteção de mudanças usam o stamp da ficha e `usrdata` + `usrhora` (campos standard), por isso não se pede campo para isso.
</phc-tables>

<task>
Objetivo: fechar o desenho da app (ecrãs, dados locais, sincronização, PIN, notificações e contrato com a Parte 1) e produzir o protótipo clicável para validar.
Âmbito:
- UX: ecrã de lista com "+" no canto superior direito (como no esboço), ecrã "Intr." com linha de tempos e pessoa, caixa de texto, foto e botão de ação. Interpretação do esboço (Suposição, a validar a 11/11): linha de campos = hora de início, técnico (por defeito o utilizador) e hora de fim; caixa de texto = anomalia e intervenção; botão de ação = guardar e concluir. Alvos de toque de pelo menos 48 dp, texto de pelo menos 16 px, teclado nativo, uma mão, funciona com luvas finas (botões grandes), contraste AA.
- Esquema SQLite (proposta de partida, a rever): `meta` (versão do esquema, última sincronização, assinatura global); `config` (chave, valor, atualizado_em); `equipamento` (infrastamp, nº, descrição, zona, localização, obsoleto, assinatura); `plano` (planostamp, infrastamp, periodicidade, tipo, antecedência, ativo, assinatura); `intervencao` (stamp char(25) chave, infrastamp, planostamp, estado, tipoint, tipoman, dataplan, hini, hfim, tecnico, anomalia, descricao, limpeza, ordem, origem servidor|local, sync_estado Sincronizado|Pendente|Erro|Conflito, assinatura, atualizado_em); `intervencao_foto` (fotostamp, intervencao_stamp, caminho_local, tamanho, sync_estado, tentativas); `intervencao_peca` (2.ª fase: stamp, intervencao_stamp, ref, qtt, loc, sync_estado); `fila_saida` (id GUID, ordem autoincrementada, operacao, stamp, payload_json mínimo, utilizador, criado_em, estado, tentativas, proximo_envio, ultimo_erro, depende_de); `notificacao_agendada` (id, intervencao_stamp, quando, tipo). Nunca no SQLite: password, PIN, hash do PIN, connection string.
- Sincronização, pontos a decidir e a fechar em tabela: janela de dados em cache (planeadas até N dias à frente, em curso e atrasadas, concluídas dos últimos 2 dias); deteção de mudanças por assinatura; operações da fila (`CriarCurativa`, `IniciarPlaneada`, `GuardarIntervencao`, `ConcluirIntervencao`, `AnexarFoto`, mais `RegistarPeca` na 2.ª fase), todas idempotentes por stamp; quando sincroniza (ao abrir, ao voltar a rede, de 5 em 5 minutos em primeiro plano, botão "Enviar agora"); a sincronização em segundo plano com a app fechada fica fora do M2 (Suposição). Conflitos (ganha a validação do backoffice): ficha Validada ou Cancelada no servidor, alteração do telemóvel vira proposta guardada e avisa o técnico; ficha devolvida pela supervisão aparece como "Devolvida"; duas pessoas a iniciar a mesma planeada (a primeira ganha, a segunda é avisada); alteração de configuração entre o agendamento e a execução.
- Avançar ordem (APP-06), Suposição: as planeadas ordenam-se por `u_dataplan` e `u_ordem`. Sem permissão de avançar, só se pode iniciar a primeira por executar; as seguintes aparecem com cadeado e a razão. Curativas nunca ficam bloqueadas. Configuração lida da cache (`AVANCAR_ORDEM_<ROLE>`).
- PIN e sessão (D-06): 4 a 6 dígitos; hash com sal (PBKDF2, número de iterações a fixar com base na documentação atual) no armazenamento seguro do Android; comparação em tempo constante; contador de falhas persistente (sobrevive ao fecho da app); ao atingir `PIN_MAX_TENTATIVAS` (defeito 5, se a cache não o tiver) o PIN é anulado e passa a pedir utilizador e password, sem perder a fila de saída. A password nunca é guardada. A sessão não expira por tempo; pede PIN ao abrir e depois de X minutos em segundo plano (Suposição: 5, valor local). Termina em logout, utilizador desativado ou sem o perfil (verificado quando há rede), troca de dispositivo. Logout com pendentes: bloqueado, com aviso.
- Credencial de sincronização (decisão D-10, a propor): A) API fina intermédia (recomendada antes de haver utilizadores reais); B) cadeia da app com conta SQL de mínimo privilégio (só leitura nas tabelas necessárias e escrita só nas de manutenção), só TESTES_LAND2 durante M1 a M2; C) guardar a password do utilizador cifrada com o Keystore (rejeitada: contraria "a password nunca se guarda"). O desenho isola o acesso remoto atrás de uma interface (`IRemotoIntervencoes`) para trocar B por A sem mexer na app.
- Notificações: agendamento local a partir da cache (às 08:00 do dia que resulta de data planeada menos a antecedência, no próprio dia e uma por dia enquanto atrasada; Suposição), reagendar após cada sincronização, após reiniciar o telemóvel e quando a configuração muda; limites (otimização de bateria dos fabricantes, permissão `POST_NOTIFICATIONS` do Android 13, alarmes exatos não necessários). O que não se consegue offline: avisar de uma planeada criada no servidor depois da última sincronização.
- Contrato com a Parte 1: lista as interfaces que a app consome (`IIntervencaoService` e as que P1-T02 fixar) e os pedidos de extensão necessários à sincronização (listar assinaturas, obter por stamps, gravar idempotente por stamp, ler configuração).
Fora de âmbito: código da app; validação da ficha (Parte 1); peças além do ponto de ligação (Parte 3); iOS; sincronização com a app fechada (a propor depois do M2).
Solução escolhida e porquê: sincronização por assinatura e fila idempotente, a mais simples que cumpre APP-02 e APP-07; sem motor de merge campo a campo (o servidor decide por estado).
</task>

<decisions>
- D-02 (atualizada 2026-11-06): a app é a GestorManutencao, já construída, que segue a base da LandeiroMaui (sem projeto novo `ManutencaoMobile`). Alternativa rejeitada: alvo Android no LandeiroMaui, porque põe em risco os postos em produção. Confirmar com o utilizador antes de fechar o desenho.
- D-03 (atualizada): acesso a dados como a LandeiroMaui (Dapper direto ao SQL Server); login da app GestorManutencao só por PIN contra a tabela PHC `u_manutil` (não login SQL por utilizador). Alternativa: API intermédia (ver D-10).
- D-05: as planeadas materializam-se como fichas com `u_fichaint.estado = Planeada` (já é assim na base), geradas pela app a partir das tarefas `u_itmanutli`. Alternativa rejeitada: calcular em memória, porque a app offline precisa de linhas com stamp.
- D-06 (atualizada): o PIN vive no PHC em `u_manutil` (pinhash, pinprov, versao, ativo; campo novo `tecno` = nº PHC do técnico, por defeito 0, NOVO: campo a criar por pessoa no PHC).
- D-09: protótipo e documentos com a paleta SigmaCode (#5BB572, #3466AF, #604696, #1A1731, #CAC9C9; Exo 2 e Open Sans); só muda com "Cliente: Landeiro, usar cores do cliente".
</decisions>

<open-points>
| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 5 | Modelos de telemóvel e versão mínima | Landeiro | sim: tamanho dos alvos de toque e API mínima |
| 10 | Perfis do módulo e quem os tem | Landeiro | sim: nomes dos roles e `AVANCAR_ORDEM_<ROLE>` |
| 12 | Confirmar D-02 | Utilizador | sim: estrutura do projeto |
| N1 | D-10: credencial de sincronização | Utilizador | sim: P2-T03 |
| N2 | Filtro das planeadas: todas, por zona ou por técnico | Landeiro | sim: lista e alertas |
| N3 | Onde ficam as fotos (anexos PHC ou pasta de rede) | P1-T01 e P1-T02 | sim: operação `AnexarFoto` |
</open-points>

<rules>
### Regras comuns
- Segue os padrões que já existem no repositório. Prefere a solução mais simples que seja correta. Não introduzas dependências sem justificação (regista em `docs/DEPENDENCIES.md`: nome, versão, para quê, alternativa, licença) e confirma a documentação oficial atual antes de usar uma biblioteca.
- Estrutura por módulo (padrão do LandeiroMaui): `Components/Pages/Manutencao/`, `Interfaces/Manutencao/IXxxService.cs`, `Services/Manutencao/XxxService.cs`, `Models/Manutencao/`. Registo manual de cada serviço em `MauiProgram.cs`. DTOs/modelos de ecrã separados dos modelos de dados quando atravessam camadas.
- Dados: Dapper cru, uma `SqlConnection` por método a partir de `DbConfig.ConnectionString`, parâmetros nomeados, `WITH (NOLOCK)` em todo o SELECT/JOIN a tabelas base (não em CTEs nem alvos de UPDATE/DELETE). Nunca `UPDLOCK`, `HOLDLOCK`, `XLOCK`, `TABLOCK`, `ROWLOCK`, `SERIALIZABLE`, `READPAST` nem transações longas com SELECT dentro. Não inventes filtros SQL: só as condições pedidas.
- Compatibilidade SQL nível 100: sem STRING_SPLIT, OPENJSON, JSON_VALUE, TVP nem tipos de tabela; listas como XML com `.nodes()` e `.value()`. TRY_CONVERT, IIF, CONCAT, FORMAT, OFFSET…FETCH, THROW, TRY_CAST e STRING_AGG só depois de confirmados numa base de teste em nível 100. SQL dentro de `u_sqlexec` (xbase): comentários só `/* */`.
- Erros nunca em silêncio: em cada `catch` de um fluxo que grava, imprime, lê código ou sincroniza, `CrashLogService.Registar(ex, "Classe.Metodo (contexto)")` e aviso ao operador. Nada de `catch { }`, `Console.WriteLine` nem `Debug.WriteLine` para erros.
- Acesso: entrada do ecrã no menu dentro de `@if (UserStateService.HasRole(Perfis.X))`, nunca guarda "Acesso negado" dentro da página. Roles novos = constante em `Helpers/Perfis`.
- `usrinis`/`ousrinis` = iniciais do utilizador autenticado (`UserStateService.Iniciais`).
- Compilação sem avisos (Nullable ligado): propriedades `string` = `""`, listas = `new()`, sem `!` nem `null!`. Compilar e correr só quando a base ativa for de testes; em produção só editas ficheiros.
- Regras novas em helpers levam teste xUnit em `LandeiroMaui.Testes`.
- Comentários o mínimo possível, sempre em português: só onde o código não se explica sozinho.
- Segurança: sem credenciais em prompts, código, logs ou documentação; consultas parametrizadas; mínimo de permissões. Dados pessoais só o necessário (RGPD). Antes de entregar corre `C:\_SigmaRepos\Claude-The-ALLKNOWING\tools\verificar-segredos.ps1 -Caminho <pasta>`.
- Base de dados: só as ações permitidas em <environment>. Antes de pedir confirmação para instalar scripts, faz a verificação textual (sem CREATE TABLE exceto temporárias, ALTER TABLE, DROP TABLE, TRUNCATE, SELECT INTO para tabela permanente, sp_settriggerorder) e põe a guarda de base de dados no início de cada script. Uma confirmação por lote.
- Triggers: só novos, leves, seguros para várias linhas, e que nunca bloqueiam a gravação no PHC.
- Git: branch próprio; sem commit nem push sem confirmação (no LandeiroMaui nunca).
- Revisão crítica da tua implementação antes de entregar.
### Regras específicas desta task
- Em Plan Mode, o PLANO inclui a lista de ecrãs e as decisões a fechar; só depois de eu confirmar produzes o desenho e o protótipo.
- Protótipo: ficheiro único, sem bibliotecas além de Google Fonts, funciona em desktop e em largura de telemóvel, dados de exemplo fictícios (nunca nomes reais de pessoas), com login PIN, lista, criar intervenção, pendentes e um interruptor online/offline que mostra os estados de sincronização.
- Marca SigmaCode em documentos e protótipo. Wireframes e fluxos em Mermaid simples (ids sem espaços, texto entre aspas se tiver parênteses ou dois pontos).
- Cada decisão nova que não esteja em `<decisions>` marca-se "Suposição:" e entra no `00-global.md` como pendência.
- Revisão obrigatória do desenho de segurança (PIN, credencial, cache) pelo `sc-revisor`.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. Percorrer o protótipo: login PIN, lista, "+", criar curativa offline, ver "Pendente", voltar online e ver "Sincronizado".
2. Tabela de conflitos cobre pelo menos: ficha Validada no servidor, devolvida, iniciada por outro, duplicado por reenvio, configuração alterada, utilizador desativado.
3. Cada campo do contrato com a Parte 1 tem origem (leitura ou escrita) e tipo; nada inventado sobre nomes reais do PHC.
4. Nenhum segredo nos documentos (`verificar-segredos.ps1 -Caminho docs/manutencao/p2`).
</tests>

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
