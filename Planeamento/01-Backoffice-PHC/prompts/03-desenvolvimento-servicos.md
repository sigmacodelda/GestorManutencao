# Serviços do módulo Manutenção: equipamentos, planos, geração de planeadas, configurações e validação
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 03 (P1-T03) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md` (em `C:\_SigmaRepos\GestorManutencao\docs\tasks\`)
Modo: Plan Mode para o PLANO; depois Autónomo
Papel do agente: desenvolvedor: implementa o que está no prompt e no desenho, sem análises nem relatórios além de uma linha de contexto
Entradas: `C:\_SigmaRepos\GestorManutencao\docs\desenho.md`, `docs\contratos.md`, `docs\levantamento.md`, `docs\sql\01_verificar_manutencao.sql`
Saídas: código e testes em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (`Models/Manutencao/`, `Interfaces/Manutencao/`, `Services/Manutencao/`, `Helpers/Manutencao/`, `Helpers/Perfis`, `MauiProgram.cs`, `LandeiroMaui.Testes`); `docs/DEPENDENCIES.md` só se houver dependência nova
Critérios de conclusão:
1. Todas as interfaces de `docs/contratos.md` implementadas pela Parte 1 (equipamentos, planos, configuração, consulta de intervenções, validação, alertas) com as assinaturas e DTOs exatos do contrato, registadas em `MauiProgram.cs`, e roles novas como constantes em `Perfis`.
2. Gerador de datas e regras de periodicidade como funções puras com testes xUnit que cobrem os casos do desenho (mínimo 12, incluindo mensal dia 10, quinzenal, semestral com meses, 31 de fevereiro, 12 meses de atraso e segunda execução idempotente) e passam.
3. Geração de planeadas idempotente na base de testes: duas execuções seguidas criam fichas só na primeira; relatório (criadas, existentes, saltadas, erros) correto; erro num plano não impede os outros.
4. Validar, devolver e cancelar com verificação de perfil no serviço, regras BO-06 e BO-10, e falha sem gravar quando a ficha mudou entretanto; antecedência efetiva e "pode avançar fora de ordem" por role lidas de `u_manconfig`.
5. Compilação sem avisos, `verificar-segredos.ps1` limpo e revisão `sc-revisor` sem pendências críticas.
Gate: manual
Agentes: `sc-dev` (código e testes), `sc-leitor-bd` (conferir estrutura e dados na base de testes), `sc-revisor` (obrigatório: SQL, mais de 5 ficheiros)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto: Gestor de Manutenção da Carnes Landeiro (backoffice P1, app offline P2, peças P3). Esta task implementa a camada de serviços do backoffice; a P2-T04 (app offline) depende destes serviços.
- Repositórios: código em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (sem git: só editas ficheiros); planeamento em `C:\_SigmaRepos\GestorManutencao`.
- Situação: o desenho fixou tabelas `u_manperiod`, `u_manplano`, `u_manconfig`, campos novos na Ficha de Intervenção (estado Planeada, EmCurso, Concluida, Validada, Cancelada) e contratos. As tabelas e campos **são criados pelo utilizador no PHC**; esta task verifica-os e pára a parte dependente se faltarem.
- Regras de negócio essenciais (o desenho tem o detalhe; copiado aqui): geração até hoje + `HORIZONTE_GERACAO_DIAS`; só as últimas `GERAR_ATRASADAS_MAX` passadas; idempotente por (plano, data); `tipocalc` D soma dias e filtra meses, M soma meses ou usa os meses marcados no `diames` (limitado ao fim do mês), N só meses marcados; plano inativo, equipamento obsoleto ou `geraauto` 0 nunca geram; validar = estado Validada, Fechada sim, `u_valinis`, `u_valdata`; devolver exige motivo e volta a EmCurso; externa exige fornecedor e doc. externo/folha de serviço para concluir e validar; quem concluiu não valida (`VALIDAR_PROPRIA_INTERVENCAO` = 0).
- Anexos: entradas acima.
Nota: sem credenciais, tokens nem connection strings. Referenciar pela configuração.
</context>

<environment>
- PHC: edição Evolution (Ultimate), versão 2025.04. Cliente: Carnes Landeiro. Alojamento: servidor do cliente [CONFIRMAR].
- SQL Server: versão [CONFIRMAR]. Nível de compatibilidade da base real: assumir 100 [CONFIRMAR: ver `levantamento.md`]. `LAND2_DEMO` está em 150 e não prova compatibilidade com 100.
- Bases: `TESTES_LAND2` (testes) e `LAND2_DEMO` (demo) são as únicas onde se pode escrever, depois de `SELECT DB_NAME();` e de confirmar o nome. `LAND2` (produção) é só leitura. A connection string vive no `appsettings` do projeto, preenchida pelo utilizador; nunca a escrevas nem a leias.
- Podes criar: SPs (`sc_<Entidade><Acao>`, nunca `sp_`), views (`sc_v_<Nome>`) e triggers novos, só na base de testes e com confirmação por lote. Se o padrão do projeto for SQL inline com Dapper, segue o padrão (é o do LandeiroMaui; o desenho não prevê SPs).
- Nunca crias tabelas nem campos (nem CREATE TABLE, nem ALTER TABLE), nem alteras, desativas ou apagas triggers existentes (incluindo encriptados). Índices, funções, tipos e roles só com autorização explícita registada em <decisions>.
- Triggers existentes nas tabelas envolvidas: `[PREENCHER com a lista do levantamento]`.
- Projeto de código base: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (.NET 9, MAUI Blazor Hybrid, Dapper). Lê `CLAUDE.md` e `ARQUITETURA.md` antes de escrever código. Nesse repositório não faças commits, branches nem push: só editas ficheiros.
</environment>

<task>
Objetivo: implementar os serviços da Parte 1 conforme `contratos.md`, com a geração de planeadas e a validação testadas.
Âmbito (por fases; no fim de cada uma compila e corre os testes):
1. Verificação: corre `01_verificar_manutencao.sql` (só leitura) na base de testes. Se falhar, `BLOQUEIO:` com a lista do que falta e continua com as fases que não dependem da base (2 e testes puros).
2. Funções puras em `Helpers/Manutencao/` (`PeriodicidadeHelper`, `GeradorPlaneadas`: recebe plano, periodicidade, `hoje`, horizonte, atrasadas máx e datas já existentes; devolve datas a criar e saltadas) com testes xUnit em `LandeiroMaui.Testes` (ficheiros ligados como os testes existentes). Só datas, sem horas, cultura invariante.
3. Modelos e DTOs em `Models/Manutencao/` (os do contrato) e interfaces em `Interfaces/Manutencao/`.
4. `ManutencaoConfigService`: lê e grava `u_manconfig`; valor inválido ou em falta = valor de origem e registo no `CrashLogService`; só `MAN_ADMIN` grava.
5. `EquipamentoService`: lista com filtros (zona, tipo, obsoleto, texto), obter, gravar só os campos permitidos (nunca os que o ecrã não mostra); equipamento obsoleto cancela as `Planeada` futuras (nunca EmCurso nem Concluida). Escrita direta na infraestrutura só se o levantamento mostrou que é segura; senão grava apenas dados do módulo e a criação fica no PHC.
6. `PlanoManutencaoService`: listar, gravar, `CalcularProximas`, recalcular futuras (Planeada → Cancelada com motivo em `u_valobs`) e `GerarPlaneadasAsync`: INSERT na Ficha com `LEFT(NEWID(),25)` como chave, campos obrigatórios e numeração da ficha como o levantamento indicar (próximo número com `MAX()+1` e `NOLOCK` sem `UPDLOCK`/`HOLDLOCK`; se o PHC tiver rotina de numeração, usa a do `Repository`), `NOT EXISTS` por (plano, data) no próprio INSERT, e tratamento das violações do índice único (2601 e 2627) como "já existe". Atualiza `proxdata`. Relatório de geração.
7. `IntervencaoService` (consulta e validação): listar, obter, validar, devolver, cancelar, com verificação de perfil no serviço (`UserStateService.HasRole`), leitura de `u_estado` e do campo Fechada e deteção de alteração concorrente (compara `u_estado` e `usrdata`/`usrhora` lidos antes de gravar; falha sem gravar).
8. `AlertaManutencaoService`: a vencer (dentro da antecedência efetiva), atrasadas, por validar, planos sem próxima data, equipamentos sem plano.
9. Registo manual em `MauiProgram.cs`; constantes `MAN_OPERADOR`, `MAN_SUPERVISOR`, `MAN_ADMIN` em `Helpers/Perfis`.
10. Revisão com `sc-revisor`, `verificar-segredos.ps1`, e testes de integração na base de testes (só depois de `SELECT DB_NAME()`; dados de teste marcados com `u_origem = 'TESTE'`; script de limpeza só após confirmação por lote).
Fora de âmbito: ecrãs (P1-T04), dashboard (P1-T05), execução na app (P2), peças (P3), criar tabelas, campos, índices ou roles, enviar e-mails de alerta.
Solução escolhida e porquê: Dapper e SQL inline com `NOLOCK`, funções puras testáveis para as datas; sem SPs nem triggers (o padrão do LandeiroMaui e o desenho não os exigem).
</task>

<decisions>
- D-03: acesso a dados como o LandeiroMaui. Alternativa rejeitada: API intermédia, porque acrescenta camadas e prazo.
- D-05: planeadas = fichas com estado Planeada. Alternativa rejeitada: calcular em memória.
- Ordem (BO-05) e restantes Suposições do desenho aplicam-se tal como em `desenho.md`; não as reabras, regista o que mudar em `00-global.md`.
</decisions>

<open-points>

| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 4 | Tabelas, campos, índice único e roles criados no PHC de testes | Utilizador | sim (fases 4 a 8 e integração) |
| 10 | Nomes finais das roles | Landeiro | não (constantes ajustáveis) |
| 12 | D-02, D-04, D-07 confirmadas | Utilizador | não se já fechadas no desenho |
</open-points>

<rules>
- Segue os padrões que já existem no repositório. Prefere a solução mais simples que seja correta. Não introduzas dependências sem justificação (regista em `docs/DEPENDENCIES.md`: nome, versão, para quê, alternativa, licença) e confirma a documentação oficial atual antes de usar uma biblioteca.
- Estrutura por módulo (padrão do LandeiroMaui): `Components/Pages/Manutencao/`, `Interfaces/Manutencao/IXxxService.cs`, `Services/Manutencao/XxxService.cs`, `Models/Manutencao/`. Registo manual de cada serviço em `MauiProgram.cs`. DTOs/modelos de ecrã separados dos modelos de dados quando atravessam camadas.
- Dados: Dapper cru, uma `SqlConnection` por método a partir de `DbConfig.ConnectionString`, parâmetros nomeados, `WITH (NOLOCK)` em todo o SELECT/JOIN a tabelas base (não em CTEs nem alvos de UPDATE/DELETE). Nunca `UPDLOCK`, `HOLDLOCK`, `XLOCK`, `TABLOCK`, `ROWLOCK`, `SERIALIZABLE`, `READPAST` nem transações longas com SELECT dentro. Não inventes filtros SQL: só as condições pedidas.
- Compatibilidade SQL nível 100: sem STRING_SPLIT, OPENJSON, JSON_VALUE, TVP nem tipos de tabela; listas como XML com `.nodes()` e `.value()`. TRY_CONVERT, IIF, CONCAT, FORMAT, OFFSET…FETCH, THROW, TRY_CAST e STRING_AGG só depois de confirmados numa base de teste em nível 100. SQL dentro de `u_sqlexec` (xbase): comentários só `/* */`.
- Erros nunca em silêncio: em cada `catch` de um fluxo que grava, imprime, lê código ou sincroniza, `CrashLogService.Registar(ex, "Classe.Metodo (contexto)")` e aviso ao operador. Nada de `catch { }`, `Console.WriteLine` nem `Debug.WriteLine` para erros.
- Acesso: entrada do ecrã no menu dentro de `@if (UserStateService.HasRole(Perfis.X))`, nunca guarda "Acesso negado" dentro da página. Roles novos = constante em `Helpers/Perfis`. Os serviços verificam também o perfil (defesa em profundidade) e devolvem `ResultadoOperacao` com erro.
- `usrinis`/`ousrinis` = iniciais do utilizador autenticado (`UserStateService.Iniciais`).
- Compilação sem avisos (Nullable ligado): propriedades `string` = `""`, listas = `new()`, sem `!` nem `null!`. Compilar e correr só quando a base ativa for de testes (MSBuild do Visual Studio sobre a `.sln`, `-p:Platform=x64`, como no `CLAUDE.md` do projeto); em produção só editas ficheiros.
- Regras novas em helpers levam teste xUnit em `LandeiroMaui.Testes`.
- Comentários o mínimo possível, sempre em português: só onde o código não se explica sozinho.
- Segurança: sem credenciais em prompts, código, logs ou documentação; consultas parametrizadas; mínimo de permissões. Dados pessoais só o necessário (RGPD). Antes de entregar corre `C:\_SigmaRepos\Claude-The-ALLKNOWING\tools\verificar-segredos.ps1 -Caminho C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui`.
- Base de dados: só as ações permitidas em <environment>. Antes de pedir confirmação para instalar scripts, faz a verificação textual (sem CREATE TABLE exceto temporárias, ALTER TABLE, DROP TABLE, TRUNCATE, SELECT INTO para tabela permanente, sp_settriggerorder) e põe a guarda de base de dados no início de cada script. Uma confirmação por lote.
- Triggers: só novos, leves, seguros para várias linhas, e que nunca bloqueiam a gravação no PHC (o desenho não prevê nenhum).
- Git: branch próprio; sem commit nem push sem confirmação (no LandeiroMaui nunca).
- Revisão crítica da tua implementação antes de entregar.
- Específicas: datas só como `DateTime.Date`; nunca `DateTime.Now` dentro das funções puras (recebem `hoje`); relatório de geração sem nomes de técnicos; nunca gravar campos do PHC que o contrato não lista.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. Unitários (xUnit): os casos do desenho; plano inativo, `geraauto` 0, meses vazios, dia 31 em fevereiro, 12 meses de atraso (1 criada, 11 saltadas), datas já existentes não repetidas.
2. Integração (`TESTES_LAND2`, `SELECT DB_NAME()` antes): gerar duas vezes seguidas, a segunda cria 0; plano com erro propositado (periodicidade inexistente) não pára os restantes.
3. Validação: Concluida → Validada grava estado, Fechada, iniciais e data; devolver sem motivo falha; validar ficha externa sem doc. externo falha; validar a própria ficha falha; validar ficha já Validada falha sem gravar.
4. Perfis: operador a chamar `ValidarAsync` recebe erro; supervisor e admin passam; só admin grava configuração.
5. Concorrência: duas gerações em paralelo na base de testes não criam duplicados (com o índice único criado; sem ele, documenta o risco medido).
6. `verificar-segredos.ps1` sem ocorrências; compilação com 0 avisos novos.
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
