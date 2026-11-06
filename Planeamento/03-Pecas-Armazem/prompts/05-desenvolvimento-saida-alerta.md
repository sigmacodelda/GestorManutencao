# Desenvolvimento da Parte 3: saída por leitura, peças do plano e alerta de rutura
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 05 (P3-T05) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO e espera a minha confirmação antes de alterar ficheiros)
Papel do agente: desenvolvedor: implementa o que está neste prompt e em `docs/pecas-armazem/desenho.md`, sem análises nem relatórios além do formato pedido
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_configman`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 3: `u_matinterv` (design, qtt, epv, custo, ref) já regista o material usado na ficha e reaproveita-se (substitui `u_manintpec` e o ecrã 'Pedir peça'). NÃO existem na base: pedido ao armazém, stock/rutura, etiquetas, receção, inventário, saída por leitura: esses mantêm-se como NOVO.
Entradas: `docs/pecas-armazem/desenho.md`, código das P3-T03 e P3-T04 (`IPecaService`, `IArmazemVirtualService`, `ResolvedorLeitura`, `PrazoEntrega`), resultado da **P2-T05** (lista e criação de intervenções no telemóvel, cache e fila de saída), serviços de planos e intervenções da Parte 1 (P1-T03)
Saídas: código e testes (`CalculadorRutura`, serviços, ecrãs Windows e do telemóvel), scripts `01_verificar_*.sql` se houver tabela nova, atualização do contrato com a Parte 2 e feed para o dashboard (P1-T05)
Critérios de conclusão:
1. Saída por leitura (PEC-09): ler `PEC:<ref>` ou o código do fabricante com quantidade baixa o stock da localização escolhida e liga o movimento à ficha de intervenção; o reenvio do mesmo `opid` conta uma vez; estorno repõe o stock; saída offline nunca é recusada e assinala stock negativo.
2. Peças da intervenção (PEC-03/APP-08): ao abrir uma planeada vêem-se as peças do plano com stock disponível e localização; peça fora do plano é aceite com aviso "extra ao plano"; em intervenção manual escolhe-se das peças do equipamento.
3. Alerta de rutura (PEC-10): `CalculadorRutura` reproduz os casos de teste (Aviso, Crítico, fora da janela, reservas, atrasada, prazo assumido, cancelada/obsoleta) e o painel lista peça, necessidade, disponível, falta, prazo, data limite de compra, primeira planeada em falta e urgência, ordenado por data limite; contador no menu e feed para o dashboard.
4. Leitura por câmara no telemóvel (ZXing.Net) e inventário por picagem no telemóvel a funcionar **depois de P2-T05**; se a P2-T05 não estiver CONCLUÍDA, entrega o serviço e a parte Windows e marca esta parte BLOQUEADA com o motivo.
5. Desempenho: cálculo do alerta para 4.000 peças e 500 planeadas em menos de 3 s e resolução de leitura em menos de 500 ms (medidos); testes xUnit a passar, sem avisos, `verificar-segredos.ps1` limpo, `sc-revisor` sem pendências críticas.
Gate: manual: eu confirmo o fecho da task
Agentes: `sc-dev`, `sc-leitor`, `sc-revisor` (obrigatório), `sc-leitor-bd`

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto e cliente: Gestor de Manutenção da Carnes Landeiro. Três partes em paralelo: Parte 1 Backoffice e PHC, Parte 2 App de intervenções (telemóvel), **Parte 3 Peças e armazém (esta)**. Hoje o circuito vive em papel, Word e PHC (ecrãs Infraestruturas e Ficha de Intervenção).
- Repositórios: código em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (só editas ficheiros; sem branch, commit nem push); planeamento em `C:\_SigmaRepos\GestorManutencao`. Os documentos desta parte vão para `docs/pecas-armazem/` do repositório de código (Suposição: as três partes não podem partilhar o mesmo `docs/levantamento.md`).
- Situação: a Landeiro não tem referências nem controlo de stock das peças de manutenção (estimativa dita: 3.300 a 4.000 peças; só o equipamento Midas cerca de 300). Origem das peças: manuais do fabricante (Marel, "Slaughterline Renovation", PDF de cerca de 350 MB, inglês misturado com português; cada conjunto tem lista de peças com "Item no.", "Marel code", descrição e quantidade). Fluxo alvo: compra → receção → etiqueta e localização → saída por leitura na intervenção → alerta de rutura.
- Requisitos da Parte 3: PEC-01 referências das peças no PHC (família própria, ref interna e do fabricante, designação EN e PT) ou ambiente separado; PEC-02 carga das peças por máquina a partir do PDF com IA e tradução; PEC-03 peças necessárias por plano e por intervenção (plano → equipamento → peças); PEC-04 tempo de entrega por artigo e fornecedor (se diferentes, o mais longo); PEC-05 armazém virtual (localizações); PEC-06 criador de etiquetas da peça e da localização; PEC-07 receção de peças; PEC-08 inventário por picagem; PEC-09 saída por leitura de código de barras ou QR; PEC-10 alerta de rutura (planeadas vs stock vs prazo de entrega).
- Arquitetura: módulo `Manutencao` no LandeiroMaui (Dapper, `NOLOCK`, registo manual em `MauiProgram.cs`). Contratos da parte: `IPecaService` (referências, peças por equipamento e por plano) e `IArmazemVirtualService` (localizações, stock por local, entradas, saídas, inventário), a fixar em coerência com P1-T02. Intervenções planeadas são fichas com estado Planeada (D-05). Etiquetas: `Services/Impressao/ImpressaoEtiquetasService` e o editor de modelos em `Services/Etiquetas` (`CatalogoEtiquetas`, `TemplateEtiquetaStore`, `GeradorZplService`). Leitura de códigos: ZXing.Net já existe no projeto; no Windows há leitores tipo teclado (listener JS de scanner); a leitura por câmara no Android é nova e depende da Parte 2 (P2-T05).
- Tabelas candidatas (nomes provisórios, a fechar em P3-T02; nunca se criam por código): `u_manmaq`, `u_manconj`, `u_manpeca`, `u_manpecaequip`, `u_manplanopec`, `u_manloc`, `u_manlocstk`, `u_manlead`, `u_manmovloc`, `u_maninv`, `u_maninvlin`, mais campos novos na `u_configman` da base (chaves de rutura; NOVO). `u_matinterv` já cobre o material usado na ficha. Nomes reais das tabelas e campos do PHC existente (artigos, stock, fornecedores, ficha de intervenção) não se assumem: confirmam-se no levantamento P3-T01.
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
Nota: as tabelas e campos estão em `docs/pecas-armazem/desenho.md`. O Claude Code NUNCA as cria. Esta task usa `u_manmovloc`, `u_manlocstk`, `u_manplanopec`, `u_manpecaequip`, `u_manlead`, `u_maninv`, `u_maninvlin` e as chaves de `u_configman` (`RUTURA_MARGEM_DIAS`, `RUTURA_PRAZO_DEFEITO_DIAS`, `RUTURA_CONTAR_ENCOMENDAS`); corre os scripts `01_verificar_*.sql` (só leitura) na base de testes antes de escrever código dependente; se faltar algo, `BLOQUEIO:` e continua o que não depende.
</phc-tables>

<task>
Objetivo: implementar a saída de peças por leitura de código (PEC-09), a lista de peças por plano e por intervenção (PEC-03, consumida pela Parte 2 em APP-08) e o alerta de rutura de stock (PEC-10).
Âmbito:
### Fase 1: `CalculadorRutura` e prazo
Classe pura (sem acesso a dados) que recebe peças, prazos efetivos, planeadas, reservas, stock e parâmetros, e devolve a lista de alertas conforme a regra abaixo. xUnit com todos os casos. Serviço que carrega os dados com Dapper (`NOLOCK`, sem N+1: uma consulta por conjunto de dados) e chama o calculador.
### Fase 2: saída de peças
Em `IArmazemVirtualService`: `SaidaAsync` (peça, quantidade, localização, ficha, `opid`), `EstornoAsync` (até a ficha ser Validada), sugestão de localização (maior stock). Regras: quantidade > 0; online recusa quantidade acima do stock sem confirmação; offline aceita e assinala; leitura repetida na mesma peça em menos de 1 s é ignorada no ecrã (anti-duplo-disparo). Se o levantamento decidiu reaproveitar os Movimentos da ficha, também grava o consumo aí, como o desenho indica.
### Fase 3: peças do plano e da intervenção
Contrato para a Parte 2 e para o backoffice: peças do plano e da intervenção (com stock disponível, localização principal e prazo), validação "extra ao plano", e escolha entre as peças do equipamento em intervenções manuais; tudo servível da cache offline do telemóvel (só as peças do equipamento e as localizações, não as 4.000).
### Fase 4: ecrãs
Windows (não depende da Parte 2): saída com leitor tipo teclado, consulta das peças da intervenção, painel de alertas de rutura (tabela ordenada por data limite, filtros por urgência, máquina e zona), contador no menu. Telemóvel (**depende de P2-T05**): leitura por câmara com ZXing.Net (permissão de câmara, código ilegível, pesquisa à mão), saída ligada à intervenção aberta, inventário por picagem; tudo na fila de saída da Parte 2 com `opid`.
### Fase 5: integração e feed
Expõe os alertas de rutura ao dashboard da Parte 1 (BO-07) por contrato (lista e contagem); regista a limitação: o alerta só vê planeadas já geradas como fichas Planeada.
Fora de âmbito: receção e inventário no Windows (P3-T04), carga das peças (P3-T03), encomendas automáticas, e-mail de alerta (Suposição: fora da v1), stock mínimo por peça.
Solução escolhida e porquê: calculador puro testável com os casos da regra e serviço fino que só carrega dados; reaproveita a fila de saída e os pares anti-duplicação da Parte 2 em vez de criar sincronização própria.

### Regra do alerta de rutura (PEC-10) e prazo de entrega (PEC-04)
- Parâmetros em `u_configman`: `RUTURA_MARGEM_DIAS` M (Suposição 7), `RUTURA_PRAZO_DEFEITO_DIAS` (Suposição 30), `RUTURA_CONTAR_ENCOMENDAS` (0 por defeito). `hoje` = data local sem hora.
- Prazo efetivo `L(p)` = o mais longo dos aplicáveis ao fornecedor principal da peça: prazo do par (artigo, fornecedor), prazo do artigo, prazo do fornecedor; sem nenhum, o prazo por defeito (peça marcada "prazo assumido"). Dias de calendário (Suposição).
- Janela `J(p) = L(p) + M`; fim da janela = `hoje + J(p)`.
- Necessidade `N(p)` = soma das quantidades de `p` nas intervenções Planeadas (plano ativo, equipamento não obsoleto, não canceladas) com data planeada ≤ fim da janela; as atrasadas contam com data efetiva `hoje`.
- Stock `S(p)` = soma do stock das localizações ativas. Reservas `R(p)` = soma, nas intervenções EmCurso, de max(0, previsto − já saído). Disponível `D(p) = S(p) − R(p)` (pode ser negativo; se `RUTURA_CONTAR_ENCOMENDAS=1`, soma também o que está encomendado e por receber).
- Rutura se `D(p) < N(p)`; falta `F = N − D`. Data da falha = data da primeira planeada (ordenadas por data efetiva) em que o acumulado passa `D` (se `D ≤ 0`, a primeira). Data limite de compra `DLC = dataFalha − L`.
- Urgência: **Crítico** se `DLC < hoje`; **Aviso** se `hoje ≤ DLC ≤ hoje + M`.
- Casos de teste (hoje = 2026-12-01, M = 7, L = 30, janela até 2027-01-07): (1) S=2, R=0, planeadas 2027-01-05 qtt 1 e 2027-01-06 qtt 2 → N=3 > D=2, falha 2027-01-06, DLC 2026-12-07, Aviso; (2) S=0, planeada 2026-12-20 qtt 1 → DLC 2026-11-20, Crítico; (3) planeada 2027-01-10 (fora da janela) → sem alerta; (4) S=2, R=2, planeada qtt 1 → D=0, rutura, F=1; (5) atrasada 2026-11-20 qtt 1, S=0 → data efetiva hoje, DLC 2026-11-01, Crítico; (6) sem prazo definido → L=30, "prazo assumido"; (7) planeada cancelada ou equipamento obsoleto não conta; (8) prazos artigo 20, fornecedor 35, par 40 → 40; artigo 20 e fornecedor 35 sem par → 35.

### Codificação das peças e das localizações (proposta a fechar em P3-T02)
- Referência interna da peça: `M<MMM>-<CCC>.<PPP>` (máquina, conjunto, peça; 3 dígitos cada, 001 a 999; 12 caracteres). Regex `^M\d{3}-\d{3}\.\d{3}$`. Exemplo: `M001-001.001`. Conjunto (só chave em `u_manconj`): `^M\d{3}-\d{3}$`. Os exemplos da reunião (001, 001.1, 001.2) leem-se como conjunto e peça; o preenchimento a 3 dígitos é Suposição.
- Uma peça física = uma referência: o mesmo código do fabricante noutro conjunto ou máquina só cria ligação peça ↔ máquina/conjunto com "Item no." e quantidade (decisão D-12 por confirmar; alternativa rejeitada: um artigo por posição, porque fragmenta o stock). A referência nunca se renumera nem se reutiliza; peças obsoletas ficam.
- `<PPP>` = máximo + 1 no conjunto; na 1000.ª peça falha com erro claro. A referência do fabricante é texto (zeros à esquerda contam), normalizada em maiúsculas e sem espaços nas pontas.
- Dados da peça: `reffabric`, `codbarras` (opcional), `fabricante`, `desigen` (original em inglês, vazio se o original é português), `desigpt` (tradução ou o original), `idiomaorig` (EN, PT, XX), unidade, `locdefeito`.
- Localização: `<Z><EE>-<P>-<PP>` (zona A-Z, estante 01-99, prateleira 1-9, posição 01-99), ex. `A03-2-05`; regex `^[A-Z]\d{2}-\d-\d{2}$`; reservada `RECEC` (zona de receção).
- Payloads: peça `PEC:M001-001.001`, localização `LOC:A03-2-05` (QR e Code 128). Resolução de uma leitura, por ordem: prefixo `PEC:` → peça; prefixo `LOC:` → localização; regex da referência interna → peça; regex da localização ou `RECEC` → localização; senão procura em `reffabric` e `codbarras` (um = peça, vários = lista para escolher, nenhum = desconhecido).
- Validações de teste: `M001-001.001` e `M999-999.999` válidas; `M001-001.000`, `M000-001.001`, `M1-1.1`, `M001-001.0001` inválidas como peça; `A03-2-05` e `RECEC` válidas, `A3-2-05` e `A03-0-05` inválidas; `0070001` e `70001` são referências de fabricante diferentes; próxima peça de um conjunto com `.999` falha.
</task>

<decisions>
- D-01: as 3 partes correm em paralelo, com dependências explícitas. Alternativa rejeitada: sequencial, porque demora mais.
- D-03 (atualizada): acesso a dados como a LandeiroMaui (Dapper direto ao SQL Server); login da app GestorManutencao só por PIN contra a tabela PHC `u_manutil` (não login SQL por utilizador). Alternativa rejeitada: App → API → serviços, porque acrescenta camadas e prazo.
- D-04 **por decidir**: referências das peças no PHC com família própria (recomendado, para stock, compras e receção nativos) ou ambiente separado (base própria `MANUTENCAO` na mesma instância; duplica stock e compras). Não avançar código dependente sem a decisão registada em `docs/tasks/00-global.md`.
- D-05: intervenções planeadas materializam-se como fichas com `u_fichaint.estado = Planeada` (já é assim na base). Alternativa rejeitada: calcular só em memória, porque a app offline precisa de linhas com stamp.
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
- O `desenho.md` prevalece sobre este prompt; contradições assinalam-se.
- Dependência: a parte do telemóvel só arranca com a P2-T05 CONCLUÍDA no `00-global.md`; confirma o estado antes. Se não estiver, `BLOQUEIO:` só para essa parte e entregas o resto.
- Saída offline: nunca recusada nem perdida; o reenvio por `opid` é idempotente; stock negativo assinala-se (divergência para inventário), não bloqueia.
- ZXing.Net já está no projeto: reaproveita-o; câmara e permissões novas só no projeto do telemóvel (decisão D-02, a confirmar), nunca no alvo Windows dos postos. Não alteres o que existe nos ecrãs do LandeiroMaui.
- `CalculadorRutura` sem acesso a dados, sem `DateTime.Now` (recebe `hoje`), com arredondamentos e quantidades decimais corretos.
- Alertas: nunca em silêncio; se o cálculo falhar, `CrashLogService.Registar` e mensagem no painel.
- Desempenho medido com 4.000 peças e 500 planeadas (dados de teste gerados na base de testes e removidos depois, ou calculador em memória).
- Revisão: `sc-revisor` obrigatório antes do gate.
</rules>

<tests>
1. xUnit `CalculadorRutura`: os 8 casos da regra, mais quantidade decimal, várias planeadas na mesma data, `D` negativo e mudança de margem.
2. Saída: online baixa o stock e liga à ficha; reenvio com o mesmo `opid` ignora; estorno repõe; quantidade acima do stock pede confirmação online e aceita offline com assinalação; anti-duplo-disparo (duas leituras em 1 s).
3. Peças da intervenção: planeada com lista (stock e localização corretos), planeada sem lista (0 peças sem erro), extra ao plano (aviso), intervenção manual (peças do equipamento).
4. Painel: ordenação por data limite; filtros; contador no menu coincide com a lista; feed ao dashboard devolve a mesma contagem.
5. Telemóvel (só se P2-T05 estiver CONCLUÍDA): leitura por câmara de `PEC:` e de código do fabricante, câmara sem permissão, código ilegível, saída offline e sincronização sem duplicar.
6. Medições: alerta com 4.000 peças e 500 planeadas em menos de 3 s; leitura em menos de 500 ms (método e valores).
O que não puder ser corrido (dispositivo físico, por exemplo) declara-se; não se dá como testado.
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
