# Ecrãs do backoffice de manutenção: equipamentos, planos, planeadas, validação e configuração
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 04 (P1-T04) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md` (em `C:\_SigmaRepos\GestorManutencao\docs\tasks\`)
Modo: Plan Mode para o PLANO; depois Autónomo
Papel do agente: desenvolvedor: implementa o que está no prompt e no desenho, sem análises nem relatórios além de uma linha de contexto
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_manconfig`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 1: estados, iniciar/concluir/validar/devolver/cancelar, geração das planeadas e config já estão na LandeiroMaui; o desenho não os reinventa, mapeia-os e propõe só o que falta (por confirmar).
Entradas: `C:\_SigmaRepos\GestorManutencao\docs\desenho.md` (wireframes e protótipo `docs\prototipo\backoffice.html`), `docs\contratos.md`; serviços da P1-T03 já no LandeiroMaui
Saídas: ecrãs e CSS em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (`Components/Pages/Manutencao/`, `wwwroot/css/manutencao.css`, `Components/Layout/NavMenu.razor`, `wwwroot/index.html`); testes em `LandeiroMaui.Testes` para a lógica nova em helpers
Critérios de conclusão:
1. Cinco rotas `/Manutencao/...` (Equipamentos, Planos, Planeadas, Validação, Configuração; o Dashboard é a P1-T05) funcionais na base de testes, com as ações do desenho (listar e filtrar, editar plano e meses, gerar e cancelar planeadas com relatório, validar e devolver, gravar configuração).
2. Entrada de menu por role dentro de `@if (UserStateService.HasRole(Perfis.X))`, sem guarda "Acesso negado" dentro das páginas; cada perfil vê só o que a matriz permite.
3. Fluxo completo comprovado na base de testes: plano → gerar planeadas → ficha Concluida (preparada à mão) → validar → ficha Validada e Fechada; devolver com motivo volta a EmCurso; ficha externa sem doc. externo não valida.
4. Estilo conforme D-09 (paleta SigmaCode salvo a frase de cliente), `ModalBase`, `tabela-moderna`, `sal-btn`, `OrdenacaoTabela`, `Numpad`, `Virtualize` em listas longas, sem `forceLoad`, `touch-action: pan-y`; sem erros de consola.
5. Compilação sem avisos, `verificar-segredos.ps1` limpo e revisão `sc-revisor` sem pendências críticas.
Gate: manual
Agentes: `sc-dev` (ecrãs), `sc-leitor` (padrões do LandeiroMaui), `sc-revisor` (obrigatório: mais de 5 ficheiros)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto: Gestor de Manutenção da Carnes Landeiro. O backoffice são os ecrãs Windows (postos e escritório) do módulo `Manutencao` no LandeiroMaui; a app de telemóvel é a Parte 2. Os serviços (P1-T03) já existem; esta task constrói a interface.
- Ecrãs (requisitos): Equipamentos (BO-01: lista, filtros por zona, tipo e obsoleto, ficha com dados, planos, histórico e e-mail de alerta; viaturas fora); Planos (BO-02: lista por equipamento, editor com periodicidade da lista única, interna ou externa, grelha JAN a DEZ clicável, dia do mês, início, antecedência própria, fornecedor por defeito, ativo; ao mudar periodicidade ou meses avisa que as planeadas futuras serão recalculadas); Planeadas (BO-03: lista das fichas Planeada e atrasadas, filtro Departamento → Zona ou Máquina → Plano, botão Gerar com relatório, cancelar planeada com motivo); Validação (BO-06: lista Concluida, detalhe com anomalia, intervenção, tempos, custos, doc. externo, botões Validar e Devolver com motivo; ficha Validada só leitura); Configuração (BO-04 e BO-05: antecedência global, horizonte, atrasadas máx, avançar fora de ordem por perfil, validar a própria; só administrador grava).
- Perfis: operador (só consulta, sem entrada de menu de gestão), supervisor, administrador de manutenção; matriz no desenho.
- Marca (D-09): ecrãs com a paleta SigmaCode (#5BB572, #3466AF, #604696, #1A1731, #CAC9C9; Exo 2 e Open Sans) em `manutencao.css` com variáveis `--man-*`, **a menos que** esta linha esteja preenchida: `[PREENCHER: vazio = paleta SigmaCode; ou "Cliente: Landeiro, usar cores do cliente" para usar o tokens.css do LandeiroMaui]`.
- Anexos: entradas acima.
Nota: sem credenciais, tokens nem connection strings.
</context>

<environment>
- PHC: edição Evolution (Ultimate), versão 2025.04. Cliente: Carnes Landeiro. Alojamento: servidor do cliente [CONFIRMAR].
- SQL Server: versão [CONFIRMAR]. Nível de compatibilidade da base real: assumir 100 [CONFIRMAR]. `LAND2_DEMO` está em 150 e não prova compatibilidade com 100.
- Bases: `TESTES_LAND2` (testes) e `LAND2_DEMO` (demo) são as únicas onde se pode escrever, depois de `SELECT DB_NAME();` e de confirmar o nome. `LAND2` (produção) é só leitura. A connection string vive no `appsettings` do projeto, preenchida pelo utilizador; nunca a escrevas nem a leias.
- Podes criar: SPs, views e triggers novos só na base de testes e com confirmação por lote; esta task não precisa de nenhum.
- Nunca crias tabelas nem campos, nem alteras triggers existentes. Índices, funções, tipos e roles só com autorização explícita registada em <decisions>.
- Triggers existentes nas tabelas envolvidas: `[PREENCHER com a lista do levantamento]`.
- Projeto de código base: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui`. Lê `CLAUDE.md` e `ARQUITETURA.md` antes de escrever código. Nesse repositório não faças commits, branches nem push: só editas ficheiros.
</environment>

<task>
Objetivo: entregar os ecrãs do backoffice de manutenção sobre os serviços da P1-T03.
Âmbito (por fases; compila no fim de cada uma, só com a base ativa de testes):
1. Menu e rotas: grupo "Manutenção" no `NavMenu.razor`, cada entrada dentro do `@if (HasRole(...))` do perfil; página inicial por perfil em `GetLandingRoute` só se o desenho o pedir.
2. `manutencao.css` (tokens `--man-*`, cartões, tabelas) ligado no `index.html` depois dos CSS partilhados; sem classes de botão por página (`sal-btn`), tabelas `tabela-moderna`, modais com `ModalBase`.
3. Equipamentos: lista com `OrdenacaoTabela`, filtros, ficha em separadores (Dados, Planos, Intervenções, Alertas).
4. Planos: editor com grelha de meses clicável, validação da periodicidade contra a lista única, recálculo com `ConfirmAlert` mostrando quantas planeadas serão canceladas.
5. Planeadas: filtros em cascata, estado e atraso por cores com texto (nunca só cor), botão Gerar com relatório (criadas, existentes, saltadas, erros) num modal, cancelar com motivo.
6. Validação: lista, detalhe, Validar e Devolver; mensagens claras para os erros do serviço (externa sem doc., ficha mudou, a própria ficha).
7. Configuração: campos numéricos com `Numpad`, avisos coerentes (horizonte nunca abaixo da maior antecedência), só administrador edita.
8. Teste do fluxo completo na base de testes e revisão com `sc-revisor`.
Fora de âmbito: dashboard (P1-T05), app de telemóvel, peças, criar tabelas, campos ou roles, envio de e-mails, edição de equipamentos no MAUI se o desenho a deixou no PHC.
Solução escolhida e porquê: componentes Blazor com serviços injetados, como o resto do LandeiroMaui, reutilizando `ModalBase`, `OrdenacaoTabela`, `Numpad` e os CSS partilhados; CSS próprio só para os tokens da marca.
</task>

<decisions>
- D-07 (Suposição a confirmar): backoffice = ecrãs do LandeiroMaui. Alternativa rejeitada: painéis no PHC.
- D-09: paleta SigmaCode por defeito. Alternativa rejeitada: cores do cliente por defeito, porque contraria a regra de marca. Nota: os ecrãs ficam dentro de uma app com `tokens.css` vermelho Landeiro; a diferença visual é assumida e registada como pendência.
- D-05: planeadas = fichas Planeada. Contratos e matriz do desenho não se reabrem.
</decisions>

<open-points>

| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 10 | Nomes finais das roles | Landeiro | não |
| — | Departamento: existe campo próprio ou agrupa-se por localização? (o levantamento responde) | Utilizador | não (usa localização) |
| — | Validação em bloco (várias fichas de uma vez) | Landeiro | não (fora da v1) |
</open-points>

<rules>
- Segue os padrões que já existem no repositório. Prefere a solução mais simples que seja correta. Não introduzas dependências sem justificação (regista em `docs/DEPENDENCIES.md`).
- Estrutura por módulo: `Components/Pages/Manutencao/`, `Interfaces/Manutencao/`, `Services/Manutencao/`, `Models/Manutencao/`. Registo manual em `MauiProgram.cs`. DTOs/modelos de ecrã separados dos de dados.
- Dados: só através dos serviços; se faltar uma consulta, acrescenta-a ao serviço (Dapper, `WITH (NOLOCK)`, parâmetros nomeados; nunca `UPDLOCK`, `HOLDLOCK`, `XLOCK`, `TABLOCK`, `ROWLOCK`, `SERIALIZABLE`, `READPAST`). Nível 100: sem STRING_SPLIT, OPENJSON, JSON_VALUE, TVP.
- Erros nunca em silêncio: em cada `catch` que grava, `CrashLogService.Registar(ex, "Pagina.Metodo (contexto)")` e `ShowAlert`/`ShowNotification`. Nada de `catch { }`, `Console.WriteLine` nem `Debug.WriteLine`. Handlers `async void` que gravam têm `try/catch` próprio.
- Acesso: entrada de menu dentro de `@if (UserStateService.HasRole(Perfis.X))`, nunca guarda "Acesso negado" dentro da página. `usrinis`/`ousrinis` = `UserStateService.Iniciais`.
- Compilação sem avisos (Nullable ligado): `string` = `""`, listas = `new()`, sem `!` nem `null!`. Em atributos Razor não uses `""` dentro de `@onclick` (usa `string.Empty`). Compilar e correr só com a base ativa de testes; em produção só editas ficheiros.
- UI do projeto: navegação sem `forceLoad: true`; scroll tátil `touch-action: pan-y`; teclado virtual global (não o ligues campo a campo), numéricos com `Numpad`; botões `sal-btn sal-btn-primary|secondary`; tabelas `tabela-moderna` (compactas dentro de cartões); modais `ModalBase`; listas longas com `Virtualize` e `@key`; CSS novo com `var(--…)`, nunca px ou hex à mão para estes valores; sem CSS morto.
- Regras novas em helpers levam teste xUnit em `LandeiroMaui.Testes`. Comentários o mínimo, em português.
- Segurança: sem credenciais em código, logs ou documentação; dados pessoais só o necessário (iniciais e nome do técnico). Antes de entregar corre `C:\_SigmaRepos\Claude-The-ALLKNOWING\tools\verificar-segredos.ps1 -Caminho C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui`.
- Base de dados: só as ações permitidas em <environment>; dados de teste marcados com `u_origem = 'TESTE'`; uma confirmação por lote para qualquer script.
- Git: sem commit nem push (no LandeiroMaui nunca). Revisão crítica da tua implementação antes de entregar.
- Acessibilidade: estados sempre com texto e ícone além da cor; contraste mínimo 4,5:1 (na paleta SigmaCode, texto `#1A1731` sobre fundo claro); alvos tácteis de pelo menos 44 px.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. Cada perfil (operador, supervisor, administrador) abre a aplicação: vê só as entradas permitidas; um URL direto sem role não mostra dados (o serviço recusa).
2. Fluxo: criar plano mensal → Gerar → relatório correto → segunda geração cria 0 → preparar uma ficha Concluida na base de testes → Validar → Validada e Fechada; Devolver com motivo → EmCurso.
3. Ficha externa sem doc. externo: Validar mostra o aviso e não grava.
4. Editar meses de um plano com planeadas futuras: o aviso indica o número certo e, ao aceitar, ficam Cancelada com motivo e geram-se as novas.
5. Configuração: valor negativo e texto rejeitados; só o administrador grava; horizonte abaixo da antecedência avisa.
6. Listas com 300 equipamentos e 2000 planeadas rolam sem atrasos visíveis (`Virtualize`); abrir cada ecrã em menos de 2 s com cache quente (mede e regista).
7. Largura 1024×768 (posto tátil) e 1920×1080 sem sobreposições; consola sem erros. O que não puderes verificar (por exemplo clique real no posto) declara-se.
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
