# Desenvolvimento do modo offline da app (cache SQLite, fila de saída, sincronização)
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 04 (P2-T04) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO e espera a minha confirmação; depois autónomo até ao GATE)
Papel do agente: desenvolvedor (implementa o que está no prompt, sem análises nem relatórios além de uma linha de contexto)
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 2: a app GestorManutencao já está construída com login só por PIN (`u_manutil`, campo novo `tecno`), perfis operador e supervisor; o técnico é o utilizador PHC (`us.userno`) gravado em `tecno`/`tecnome`. Login SQL por utilizador e PIN local (D-03/D-06/D-10 antigos) deixam de valer. Ecrãs Planeadas e Material usado na ficha estão em construção.
Entradas: `docs/manutencao/p2/desenho.md` (esquema SQLite, regras de sincronização e conflitos); contratos de P1-T02 e serviços de P1-T03 já implementados; código de P2-T03
Saídas: `LocalDb`, `LigacaoEstadoService`, `CacheLeituraService`, `FilaSaidaService`, `SincronizadorService` e a UI de estado, com testes xUnit; `docs/DEPENDENCIES.md`; `docs/tasks/00-global.md` atualizado
Critérios de conclusão:
1. `LocalDb` (SQLite) cria e migra o esquema do desenho, recupera de ficheiro corrompido sem impedir o arranque e nunca guarda segredos.
2. Os ecrãs leem sempre do SQLite; a cache atualiza por diferenças (novas, alteradas, removidas) a partir dos serviços da Parte 1, sem reler tudo; tudo o que a app precisa funciona sem rede.
3. Fila de saída: cada gravação offline fica na fila, é enviada por ordem quando há rede, é idempotente por stamp (reenviar duas vezes não cria duas fichas) e sobrevive ao fecho da app.
4. Conflitos resolvidos como no desenho (ganha a validação do backoffice; Validada ou Cancelada no servidor vira proposta e avisa; duas pessoas a iniciar a mesma planeada); erros de negócio ficam em `Erro` sem travar as independentes.
5. UI de estado: faixa "Sem ligação, N por enviar", marca por registo (sincronizado, pendente, erro, conflito), ecrã "Pendentes" com Repetir e Descartar, botão "Enviar agora"; testes xUnit a passar e o cenário manual de corte de rede escrito (declarado não corrido se não houver aparelho).
Gate: manual
Agentes: `sc-dev` (implementação), `sc-revisor` (revisão obrigatória: SQL, segurança, mais de 5 ficheiros)

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
- Situação atual e problema: o `docs/plano-modo-offline-cache.md` do LandeiroMaui é só desenho (nada implementado). Reaproveita os conceitos: estado Online/Offline, `LocalDb`, carregamento incremental por assinatura, fila de saída idempotente por stamp, ecrã de pendentes. Diferença: aqui não há "par de linhas": a unidade é a intervenção (ficha) com os seus movimentos e fotos; o anti-duplicado é o stamp gerado no telemóvel (`Guid` sem hífenes cortado a 25, como no LandeiroMaui).
- Depende de P1-T03: os serviços e as tabelas e campos da Parte 1 têm de existir na base de testes; se não existirem, `BLOQUEIO:` com a lista do que falta (não os crias).
- Anexos: nenhum.
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

<task>
Objetivo: a app trabalha sem rede e sincroniza sem duplicados.
Âmbito:
- Fase 1: `LocalDb` (`sqlite-net-pcl` já usado no LandeiroMaui; ficheiro na pasta privada da app; WAL; escritas em lote numa transação), esquema e migrações por versão em `meta`.
- Fase 2: `LigacaoEstadoService` (Online/Offline; timeout curto; erros de negócio não contam como queda; sonda leve com intervalo que poupa bateria; reage a `Connectivity`), com a classificação de erros de ligação separada e testada.
- Fase 3: `CacheLeituraService` e `IRemotoIntervencoes` (interface que esconde o acesso remoto; implementação sobre os serviços da Parte 1; troca por API possível). Janela do desenho; deteção por assinatura (`usrdata` + `usrhora` ou a que P1-T02 fixar); novas, alteradas e removidas; configurações (`ALERTA_ANTECEDENCIA_DIAS`, `AVANCAR_ORDEM_<ROLE>`, `PIN_MAX_TENTATIVAS`) e equipamentos e planos necessários ao ecrã.
- Fase 4: `FilaSaidaService` e `SincronizadorService`: `GravarOuEnfileirar` (se online tenta direto e só enfileira por erro de ligação), ordem FIFO por utilizador com backoff, dependências (foto depende da intervenção), reenvio idempotente (`INSERT` só se o stamp não existir; `UPDATE` com condição de estado), resolução de conflitos conforme desenho, recálculo e nova leitura depois de enviar. Correm em primeiro plano; a app fechada fica fora de âmbito.
- Fase 5: UI de estado (faixa global, marca por registo, ecrã Pendentes, "Enviar agora"), logout com pendentes ligado a `IFilaPendentes` real.
Fora de âmbito: lista e ecrã de criar intervenção (task 05); notificações (task 05); sincronização com a app fechada; peças (Parte 3); alterar contratos da Parte 1 (pedir a P1).
Solução escolhida e porquê: SQLite como primeira fonte de leitura e fila idempotente por stamp, a opção mais simples que cumpre APP-02 e APP-07 e segue o plano offline do LandeiroMaui.
</task>

<decisions>
- D-03: Dapper direto ao SQL Server; todo o acesso remoto atrás de `IRemotoIntervencoes`. D-10 (credencial) vem de P2-T02.
- D-05: planeadas são fichas com estado Planeada, geradas pelo backoffice; a app nunca as gera.
- Conflitos: ganha a validação do backoffice (decidido na arquitetura geral). Alternativa rejeitada: fusão campo a campo, porque complica e a supervisão revê sempre a ficha.
- Intervalos e janelas (Suposição, do desenho): sincroniza ao abrir, ao voltar a rede, de 5 em 5 minutos em primeiro plano e por botão.
</decisions>

<open-points>
| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 4 | Tabelas e campos da Parte 1 criados na base de testes | Utilizador / Landeiro | sim: Fases 3 e 4 |
| 13 | Nível de compatibilidade e versão do SQL Server | Utilizador (SELECT) | não (assume 100) |
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
- Nunca guardar connection string, password, PIN nem tokens no SQLite nem nos logs; payloads da fila com o mínimo de dados (RGPD).
- Sincronização fora da thread da interface, com cancelamento; interface atualizada em lote (um `StateHasChanged` por rajada); nunca à espera de timeout.
- Escrita no PHC só em `TESTES_LAND2` ou `LAND2_DEMO` (`SELECT DB_NAME()` primeiro). SQL da Parte 1 já respeita `NOLOCK` e nível 100; não duplicar SQL na app.
- Descartar um pendente: confirmação e só com o role definido no desenho; nunca descartar em silêncio.
- Cada regra de conflito do desenho tem um teste com o nome do cenário.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. xUnit com SQLite em memória e `IRemotoIntervencoes` falso: só novas; só alteradas; só removidas; sem mudanças (nenhuma leitura completa); janela de datas; purga.
2. Fila: ordem FIFO; reenvio duas vezes não duplica; reinício a meio de um envio; backoff; dependência foto e intervenção; erro de negócio não trava as independentes.
3. Conflitos: Validada no servidor, Cancelada, devolvida, iniciada por outro, configuração alterada entre agendar e executar.
4. Classificação de erros: queda de ligação vs erro de negócio.
5. Manual (BD de testes, roteiro escrito; declarar não corrido se faltar aparelho): cortar a rede 5 e 20 minutos a meio de uma intervenção, gravar três registos, voltar a ligar, confirmar tudo enviado sem duplicados; matar a app durante o envio.
6. Medição: abrir a lista com cache em menos de 2 s (método: cronómetro no arranque do ecrã, 5 medições, mediana).
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
