# Desenvolvimento da Parte 3: referências das peças, carga por IA e tempos de entrega
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 03 (P3-T03) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO e espera a minha confirmação antes de alterar ficheiros)
Papel do agente: desenvolvedor: implementa o que está neste prompt e em `docs/pecas-armazem/desenho.md`, sem análises nem relatórios além do formato pedido
Entradas: `docs/pecas-armazem/desenho.md` (P3-T02, fonte de verdade para tabelas, contratos e regras), `docs/pecas-armazem/levantamento.md`, contratos da Parte 1 (P1-T02/P1-T03)
Saídas: código e testes no LandeiroMaui (`Interfaces/Manutencao/IPecaService.cs`, `Services/Manutencao/PecaService.cs`, helpers, ecrãs), ferramenta de extração e importação, scripts `01_verificar_*.sql`, `docs/DEPENDENCIES.md`
Critérios de conclusão:
1. Referências geradas e validadas conforme a codificação (testes xUnit com a tabela de validações a passar, incluindo `M999-999.999`, a 1000.ª peça a falhar e zeros à esquerda do código do fabricante).
2. Importação idempotente do ficheiro de carga (só linhas `Validada`; reimportar não duplica; relatório de conferência por conjunto), medida na base de testes com contagens antes e depois.
3. Prazo efetivo correto (mais longo de par, artigo e fornecedor; defeito com marca "prazo assumido") nos testes xUnit, e leitura/gravação da `u_manlead`.
4. Ecrãs do backoffice a funcionar na base de testes: peças por máquina (árvore e pesquisa), ficha da peça, peças do plano, prazos de entrega e validação da carga; entrada de menu por perfil.
5. Sem chamadas a serviços externos sem D-11 registada, sem segredos (`verificar-segredos.ps1` limpo) e compilação sem avisos.
Gate: manual: eu confirmo o fecho da task
Agentes: `sc-dev` (C#/.NET, HTML, SQL), `sc-leitor` (excertos do código), `sc-revisor` (obrigatório: SQL, segurança, mais de 5 ficheiros), `sc-leitor-bd` (verificações SELECT)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto e cliente: Gestor de Manutenção da Carnes Landeiro. Três partes em paralelo: Parte 1 Backoffice e PHC, Parte 2 App de intervenções (telemóvel), **Parte 3 Peças e armazém (esta)**. Hoje o circuito vive em papel, Word e PHC (ecrãs Infraestruturas e Ficha de Intervenção).
- Repositórios: código em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (só editas ficheiros; sem branch, commit nem push); planeamento em `C:\_SigmaRepos\GestorManutencao`. Os documentos desta parte vão para `docs/pecas-armazem/` do repositório de código (Suposição: as três partes não podem partilhar o mesmo `docs/levantamento.md`).
- Situação: a Landeiro não tem referências nem controlo de stock das peças de manutenção (estimativa dita: 3.300 a 4.000 peças; só o equipamento Midas cerca de 300). Origem das peças: manuais do fabricante (Marel, "Slaughterline Renovation", PDF de cerca de 350 MB, inglês misturado com português; cada conjunto tem lista de peças com "Item no.", "Marel code", descrição e quantidade). Fluxo alvo: compra → receção → etiqueta e localização → saída por leitura na intervenção → alerta de rutura.
- Requisitos da Parte 3: PEC-01 referências das peças no PHC (família própria, ref interna e do fabricante, designação EN e PT) ou ambiente separado; PEC-02 carga das peças por máquina a partir do PDF com IA e tradução; PEC-03 peças necessárias por plano e por intervenção (plano → equipamento → peças); PEC-04 tempo de entrega por artigo e fornecedor (se diferentes, o mais longo); PEC-05 armazém virtual (localizações); PEC-06 criador de etiquetas da peça e da localização; PEC-07 receção de peças; PEC-08 inventário por picagem; PEC-09 saída por leitura de código de barras ou QR; PEC-10 alerta de rutura (planeadas vs stock vs prazo de entrega).
- Arquitetura: módulo `Manutencao` no LandeiroMaui (Dapper, `NOLOCK`, registo manual em `MauiProgram.cs`). Contratos da parte: `IPecaService` (referências, peças por equipamento e por plano) e `IArmazemVirtualService` (localizações, stock por local, entradas, saídas, inventário), a fixar em coerência com P1-T02. Intervenções planeadas são fichas com estado Planeada (D-05). Etiquetas: `Services/Impressao/ImpressaoEtiquetasService` e o editor de modelos em `Services/Etiquetas` (`CatalogoEtiquetas`, `TemplateEtiquetaStore`, `GeradorZplService`). Leitura de códigos: ZXing.Net já existe no projeto; no Windows há leitores tipo teclado (listener JS de scanner); a leitura por câmara no Android é nova e depende da Parte 2 (P2-T05).
- Tabelas candidatas (nomes provisórios, a fechar em P3-T02; nunca se criam por código): `u_manmaq`, `u_manconj`, `u_manpeca`, `u_manpecaequip`, `u_manplanopec`, `u_manloc`, `u_manlocstk`, `u_manlead`, `u_manmovloc`, `u_maninv`, `u_maninvlin`, mais a `u_manconfig` da Parte 1 (chaves de configuração). Nomes reais das tabelas e campos do PHC existente (artigos, stock, fornecedores, ficha de intervenção) não se assumem: confirmam-se no levantamento P3-T01.
- Anexos: nenhum (o PDF Marel está numa pasta da Landeiro e só se abre com autorização D-11).
Nota: sem credenciais, tokens nem connection strings. Referenciar pela configuração.
</context>

<environment>
- PHC: edição Evolution (Ultimate), versão 2025.04. Cliente: Carnes Landeiro. Alojamento: servidor do cliente [CONFIRMAR].
- SQL Server: versão [CONFIRMAR]. Nível de compatibilidade da base real: assumir 100 [CONFIRMAR]. `LAND2_DEMO` está em 150 e não prova compatibilidade com 100.
- Bases: `TESTES_LAND2` (testes) e `LAND2_DEMO` (demo) são as únicas onde se pode escrever, depois de `SELECT DB_NAME();` e de confirmar o nome. `LAND2` (produção) é só leitura. A connection string vive no `appsettings` do projeto, preenchida pelo utilizador; nunca a escrevas em código, prompts, logs nem documentação.
- Podes criar: SPs (`sc_<Entidade><Acao>`, nunca `sp_`), views (`sc_v_<Nome>`) e triggers novos, só na base de testes e com confirmação por lote. Se o padrão do projeto for SQL inline com Dapper, segue o padrão (é o do LandeiroMaui).
- Nunca crias tabelas nem campos (nem CREATE TABLE, nem ALTER TABLE), nem alteras, desativas ou apagas triggers existentes (incluindo encriptados). Tabelas e campos novos: especifica-os (nome, tipo, tamanho, nulo, valor por defeito, regra) para o utilizador os criar no PHC. Índices, funções e tipos só com autorização explícita registada em <decisions>.
- Triggers existentes nas tabelas envolvidas: [a ler no levantamento da Parte 1].
- Projeto de código base: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (.NET 9, MAUI Blazor Hybrid, Dapper). Lê `CLAUDE.md` e `ARQUITETURA.md` desse projeto antes de escrever código e cumpre as suas regras. Nesse repositório não faças commits, branches nem push: só editas ficheiros.
</environment>

<phc-tables>
Nota: as tabelas e campos desta parte estão especificados em `docs/pecas-armazem/desenho.md` (P3-T02), uma secção `<phc-tables>` por tabela. O Claude Code NUNCA as cria. Antes de escrever código dependente:
1. Escreve os scripts `01_verificar_<tabela>.sql` (só leitura, `INFORMATION_SCHEMA` e `sys.*`, `RAISERROR` severidade 16 com mensagem clara) para as tabelas desta task (`u_manmaq`, `u_manconj`, `u_manpeca`, `u_manpecaequip`, `u_manplanopec`, `u_manlead`, mais os campos do artigo no PHC) e corre-os na base de testes depois de `SELECT DB_NAME();`.
2. Se faltar uma tabela ou campo: `BLOQUEIO:` com a lista do que falta, e continua o que não depende deles (helpers puros e testes, ferramenta de extração, ecrãs sem dados).
</phc-tables>

<task>
Objetivo: implementar as referências das peças (PEC-01), a carga por máquina com validação humana (PEC-02), as peças por plano e por equipamento (base de PEC-03) e os tempos de entrega (PEC-04).
Âmbito:
### Pré-condições
D-04 registada no `00-global.md` (sem ela, `BLOQUEIO:` e só fazes helpers puros e testes). Ramo A (PHC, família própria): artigo criado conforme o desenho (importação nativa ou INSERT na base de testes com os campos obrigatórios) e extensão em `u_manpeca`. Ramo B (ambiente separado): as mesmas regras sobre tabelas da base própria; `IPecaService` esconde a diferença.
### Fase 1: helpers puros e testes
`GeradorReferenciaPeca` (gera e valida), `ResolvedorLeitura` (classifica uma leitura em peça, localização, código do fabricante ou desconhecido, e devolve a regra aplicada) e `PrazoEntrega.Efetivo` (mais longo dos aplicáveis, prazo por defeito), tudo sem acesso a dados, com xUnit em `LandeiroMaui.Testes`.
### Fase 2: serviço de peças e dados
`IPecaService`: listar por máquina e conjunto, pesquisar (referência interna, do fabricante, texto PT e original), obter, criar com geração de referência (máximo + 1 num só comando SQL e unicidade; uma repetição em colisão), tornar obsoleta, peças por plano (`u_manplanopec`) e por equipamento (`u_manpecaequip`), prazos (`u_manlead`). Contratos exatamente como no desenho.
### Fase 3: carga das peças
Ferramenta de extração local (fora da app MAUI, em `C:\_SigmaRepos\GestorManutencao\ferramentas\carga-pecas\`; linguagem e biblioteca conforme o desenho, licença registada em `docs/DEPENDENCIES.md`) que lê o PDF por máquina, página a página, e escreve o ficheiro de proposta (CSV `;`, UTF-8 com BOM, estado `Proposta`, `confianca`) e o manifesto (hash do PDF, páginas, versão, se houve IA e o que foi enviado). Importação na app: lê só linhas `Validada`, aplica as regras de ligação (mesmo código do fabricante = mesma peça, nova ligação), idempotente, e produz o relatório de conferência (linhas do manual vs criadas, por conjunto).
**Privacidade (D-11):** só corres a extração sobre o PDF real e só chamas qualquer serviço externo (incluindo o teu próprio modelo para traduzir) se a D-11 estiver registada; senão usas um PDF de teste fictício gerado por ti, o glossário local de EN para PT e a revisão humana. A ferramenta tem de recusar chamadas externas sem a configuração de autorização ligada.
### Fase 4: ecrãs do backoffice (Windows)
Em `Components/Pages/Manutencao/`: peças por máquina (árvore máquina → conjunto → peça com pesquisa), ficha da peça (dados, ligações, prazo efetivo e origem, stock, localização), edição das peças de um plano, configuração de prazos, grelha de validação da carga (ordenar por `confianca`, corrigir, aprovar, rejeitar com motivo, aprovar acima do limiar depois de amostra, registar iniciais). Menu dentro de `@if (UserStateService.HasRole(Perfis.X))`; roles novos em `Helpers/Perfis`.
Fora de âmbito: armazém, etiquetas, receção, inventário (P3-T04); saída, peças na intervenção do telemóvel e alerta (P3-T05); criar tabelas ou campos; encomendas.
Solução escolhida e porquê: helpers puros para as regras (testáveis sem base de dados), serviço Dapper com SQL inline como o resto do LandeiroMaui, e ferramenta de extração separada da app porque a carga é pontual e o PDF tem 350 MB.

### Codificação das peças e das localizações (proposta a fechar em P3-T02)
- Referência interna da peça: `M<MMM>-<CCC>.<PPP>` (máquina, conjunto, peça; 3 dígitos cada, 001 a 999; 12 caracteres). Regex `^M\d{3}-\d{3}\.\d{3}$`. Exemplo: `M001-001.001`. Conjunto (só chave em `u_manconj`): `^M\d{3}-\d{3}$`. Os exemplos da reunião (001, 001.1, 001.2) leem-se como conjunto e peça; o preenchimento a 3 dígitos é Suposição.
- Uma peça física = uma referência: o mesmo código do fabricante noutro conjunto ou máquina só cria ligação peça ↔ máquina/conjunto com "Item no." e quantidade (decisão D-12 por confirmar; alternativa rejeitada: um artigo por posição, porque fragmenta o stock). A referência nunca se renumera nem se reutiliza; peças obsoletas ficam.
- `<PPP>` = máximo + 1 no conjunto; na 1000.ª peça falha com erro claro. A referência do fabricante é texto (zeros à esquerda contam), normalizada em maiúsculas e sem espaços nas pontas.
- Dados da peça: `reffabric`, `codbarras` (opcional), `fabricante`, `desigen` (original em inglês, vazio se o original é português), `desigpt` (tradução ou o original), `idiomaorig` (EN, PT, XX), unidade, `locdefeito`.
- Localização: `<Z><EE>-<P>-<PP>` (zona A-Z, estante 01-99, prateleira 1-9, posição 01-99), ex. `A03-2-05`; regex `^[A-Z]\d{2}-\d-\d{2}$`; reservada `RECEC` (zona de receção).
- Payloads: peça `PEC:M001-001.001`, localização `LOC:A03-2-05` (QR e Code 128). Resolução de uma leitura, por ordem: prefixo `PEC:` → peça; prefixo `LOC:` → localização; regex da referência interna → peça; regex da localização ou `RECEC` → localização; senão procura em `reffabric` e `codbarras` (um = peça, vários = lista para escolher, nenhum = desconhecido).
- Validações de teste: `M001-001.001` e `M999-999.999` válidas; `M001-001.000`, `M000-001.001`, `M1-1.1`, `M001-001.0001` inválidas como peça; `A03-2-05` e `RECEC` válidas, `A3-2-05` e `A03-0-05` inválidas; `0070001` e `70001` são referências de fabricante diferentes; próxima peça de um conjunto com `.999` falha.

### Regra do alerta de rutura (PEC-10) e prazo de entrega (PEC-04)
- Parâmetros em `u_manconfig`: `RUTURA_MARGEM_DIAS` M (Suposição 7), `RUTURA_PRAZO_DEFEITO_DIAS` (Suposição 30), `RUTURA_CONTAR_ENCOMENDAS` (0 por defeito). `hoje` = data local sem hora.
- Prazo efetivo `L(p)` = o mais longo dos aplicáveis ao fornecedor principal da peça: prazo do par (artigo, fornecedor), prazo do artigo, prazo do fornecedor; sem nenhum, o prazo por defeito (peça marcada "prazo assumido"). Dias de calendário (Suposição).
- Janela `J(p) = L(p) + M`; fim da janela = `hoje + J(p)`.
- Necessidade `N(p)` = soma das quantidades de `p` nas intervenções Planeadas (plano ativo, equipamento não obsoleto, não canceladas) com data planeada ≤ fim da janela; as atrasadas contam com data efetiva `hoje`.
- Stock `S(p)` = soma do stock das localizações ativas. Reservas `R(p)` = soma, nas intervenções EmCurso, de max(0, previsto − já saído). Disponível `D(p) = S(p) − R(p)` (pode ser negativo; se `RUTURA_CONTAR_ENCOMENDAS=1`, soma também o que está encomendado e por receber).
- Rutura se `D(p) < N(p)`; falta `F = N − D`. Data da falha = data da primeira planeada (ordenadas por data efetiva) em que o acumulado passa `D` (se `D ≤ 0`, a primeira). Data limite de compra `DLC = dataFalha − L`.
- Urgência: **Crítico** se `DLC < hoje`; **Aviso** se `hoje ≤ DLC ≤ hoje + M`.
- Casos de teste (hoje = 2026-12-01, M = 7, L = 30, janela até 2027-01-07): (1) S=2, R=0, planeadas 2027-01-05 qtt 1 e 2027-01-06 qtt 2 → N=3 > D=2, falha 2027-01-06, DLC 2026-12-07, Aviso; (2) S=0, planeada 2026-12-20 qtt 1 → DLC 2026-11-20, Crítico; (3) planeada 2027-01-10 (fora da janela) → sem alerta; (4) S=2, R=2, planeada qtt 1 → D=0, rutura, F=1; (5) atrasada 2026-11-20 qtt 1, S=0 → data efetiva hoje, DLC 2026-11-01, Crítico; (6) sem prazo definido → L=30, "prazo assumido"; (7) planeada cancelada ou equipamento obsoleto não conta; (8) prazos artigo 20, fornecedor 35, par 40 → 40; artigo 20 e fornecedor 35 sem par → 35.
</task>

<decisions>
- D-01: as 3 partes correm em paralelo, com dependências explícitas. Alternativa rejeitada: sequencial, porque demora mais.
- D-03: acesso a dados como no LandeiroMaui (Dapper direto ao SQL Server, login SQL por utilizador). Alternativa rejeitada: App → API → serviços, porque acrescenta camadas e prazo.
- D-04 **por decidir**: referências das peças no PHC com família própria (recomendado, para stock, compras e receção nativos) ou ambiente separado (base própria `MANUTENCAO` na mesma instância; duplica stock e compras). Não avançar código dependente sem a decisão registada em `docs/tasks/00-global.md`.
- D-05: intervenções planeadas materializam-se como fichas com `u_estado = Planeada`. Alternativa rejeitada: calcular só em memória, porque a app offline precisa de linhas com stamp.
- D-07 (suposição a confirmar): "backoffice" = ecrãs Windows do módulo Manutenção no LandeiroMaui.
- D-11 **por tomar** (nova): autorização para tratar o PDF Marel e os dados extraídos com serviços externos (modelos de IA em nuvem, incluindo o do Claude Code, que envia ao modelo o texto que lê). Sem autorização expressa registada: extração só com ferramentas locais, tradução por glossário e revisão humana, e o agente não abre páginas do PDF.
- D-12 **por tomar** (nova): um artigo por código do fabricante, com ligações por máquina e conjunto (recomendado). Alternativa: um artigo por posição na máquina.
</decisions>

<open-points>
| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 1 | Acesso de leitura à base de testes `TESTES_LAND2` (connection string no `appsettings`, preenchida pelo utilizador) | Utilizador | sim: levantamento |
| 2 | É possível criar tabelas e campos `u_` neste PHC? Quem cria e quando? | Utilizador / Landeiro | sim: desenvolvimento (P3-T03) |
| 3 | Quem dá entrada das peças no PHC (Compras ou manutenção) | Landeiro | sim: desenho (P3-T02) |
| 4 | PDF do manual Marel disponível à SigmaCode e autorização D-11 | Osvaldo / Landeiro | sim: levantamento do PDF e carga |
| 5 | Confirmar D-04 e D-12 | Utilizador | sim: desenho e P3-T03 |
| 6 | Modelos de telemóvel, câmara e leitor de códigos | Landeiro | sim: parte do telemóvel (P3-T05) |
| 7 | Contratos fechados de P1-T02; leitura no telemóvel pronta em P2-T05 | Partes 1 e 2 | sim: P3-T02 e P3-T05 |
</open-points>

<rules>
### Comuns
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
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo.
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
### Específicas desta task
- O `desenho.md` da P3-T02 prevalece sobre este prompt nos nomes de tabelas, campos e contratos; se houver contradição, assinala-a e segue o desenho.
- Ramo A: criar artigos no PHC só na base de testes, replicando exatamente os campos obrigatórios e tabelas relacionadas que o desenho indica; nunca alteres nem desatives triggers; confirma com uma consulta que o artigo aparece e que o stock inicial é 0.
- Referência interna: nunca se renumera nem se reutiliza; peças nunca se apagam. Código do fabricante sempre texto.
- Importação: transação curta por conjunto (sem SELECT dentro com bloqueios), erro por linha registado e continua; nada de `catch` vazio.
- Ferramenta de extração: sem credenciais, sem caminhos de rede no manifesto, sem dados pessoais; PDF lido página a página (memória baixa). Dependências novas em `docs/DEPENDENCIES.md` (nome, versão, para quê, alternativa, licença) e documentação oficial atual consultada.
- Desempenho: pesquisa de peça por código lido em menos de 500 ms com 4.000 peças e lista da árvore paginada ou virtualizada (medir e registar).
- Revisão: o `sc-revisor` é obrigatório antes do gate.
</rules>

<tests>
1. xUnit: tabela de validações da codificação (peça, conjunto, localização, `PEC:`/`LOC:`, código do fabricante único e ambíguo, zeros à esquerda, `.999` a falhar).
2. xUnit: `PrazoEntrega.Efetivo` (artigo 20, fornecedor 35, par 40 → 40; sem par → 35; só artigo → 20; nenhum → defeito com marca).
3. Importação com um ficheiro de teste (3 máquinas fictícias, códigos repetidos entre conjuntos): contagem de peças e ligações antes e depois; segunda importação sem alterações; linhas `Proposta` e `Rejeitada` não entram.
4. Verificação dos scripts `01_verificar_*.sql` na base de testes (passa com tabelas presentes; falha com mensagem se faltar uma).
5. Ecrãs: criar peça, procurar por código do fabricante, editar plano, validar carga (corrigir, aprovar, rejeitar) na base de testes.
6. Ferramenta sem D-11: demonstra que não faz chamadas de rede (execução com a configuração desligada).
7. Medições: pesquisa por código < 500 ms (4.000 peças); registar o método e os valores.
O que não puder ser corrido (por exemplo PDF real sem autorização) declara-se; não se dá como testado.
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
