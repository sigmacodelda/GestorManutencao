# Desenvolvimento da Parte 3: armazém virtual, etiquetas, receção e inventário
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 04 (P3-T04) · Tipo: desenvolvimento
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO e espera a minha confirmação antes de alterar ficheiros)
Papel do agente: desenvolvedor: implementa o que está neste prompt e em `docs/pecas-armazem/desenho.md`, sem análises nem relatórios além do formato pedido
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_configman`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 3: `u_matinterv` (design, qtt, epv, custo, ref) já regista o material usado na ficha e reaproveita-se (substitui `u_manintpec` e o ecrã 'Pedir peça'). NÃO existem na base: pedido ao armazém, stock/rutura, etiquetas, receção, inventário, saída por leitura: esses mantêm-se como NOVO.
Entradas: `docs/pecas-armazem/desenho.md` (fonte de verdade), código da P3-T03 (`IPecaService`, `GeradorReferenciaPeca`, `ResolvedorLeitura`), `Services/Impressao/ImpressaoEtiquetasService` e `Services/Etiquetas` do LandeiroMaui
Saídas: código e testes (`IArmazemVirtualService`/`ArmazemVirtualService`, ecrãs, etiquetas), scripts `01_verificar_*.sql` das tabelas desta task, `docs/DEPENDENCIES.md` se houver dependência nova
Critérios de conclusão:
1. Receção e transferência atualizam o stock por localização sem duplicar (mesmo `opid` enviado duas vezes conta uma vez) e o total da peça mantém-se numa transferência, com testes xUnit e verificação SQL na base de testes.
2. Inventário por picagem: abrir, contar, rever e fechar uma sessão gera movimentos de ajuste só nas diferenças aprovadas, com relatório de diferenças.
3. Etiquetas da peça e da localização com pré-visualização e impressão (1 e em lote); o QR (`PEC:` e `LOC:`) resolve para o objeto certo (teste: gerar payload e resolver); falha de impressão avisa e regista.
4. Conciliação com o PHC: relatório das peças em que a soma por localização difere do stock do armazém (se D-04 = PHC), e entrada no stock PHC pelo mecanismo escolhido no desenho.
5. Testes xUnit a passar, compilação sem avisos, `verificar-segredos.ps1` limpo e `sc-revisor` sem pendências críticas.
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
Nota: as tabelas e campos estão em `docs/pecas-armazem/desenho.md`. O Claude Code NUNCA as cria. Antes de escrever código dependente, escreve e corre na base de testes (depois de `SELECT DB_NAME();`) os scripts `01_verificar_<tabela>.sql` (só leitura, `RAISERROR` severidade 16) para `u_manloc`, `u_manlocstk`, `u_manmovloc`, `u_maninv` e `u_maninvlin`. Se faltar algo: `BLOQUEIO:` com a lista e continua o que não depende (helpers puros, ecrãs sem dados, modelos de etiqueta).
</phc-tables>

<task>
Objetivo: implementar o armazém virtual (PEC-05), o criador de etiquetas (PEC-06), a receção (PEC-07) e o inventário por picagem (PEC-08).
Âmbito:
### Fase 1: serviço do armazém e regras
`IArmazemVirtualService`: localizações (criar, editar, desativar sem stock), stock por peça e por localização, movimentos de entrada, transferência e ajuste (um movimento = uma linha de `u_manmovloc` com `opid`, `tipo`, `qtt` > 0 e utilizador), conciliação com o PHC. Regras em helpers puros com xUnit (transferência para a mesma localização recusada, ajuste só com sessão aprovada, desativação com stock recusada). Atualização de `u_manlocstk` e inserção do movimento na mesma transação curta.
### Fase 2: ecrãs Windows
Localizações, consulta de stock por peça e por localização, **receção** (leitor tipo teclado como no LandeiroMaui, pesquisa por texto; localização por defeito `RECEC` ou a da peça; ligação opcional a encomenda do PHC; imprime etiqueta se a peça não tiver), **inventário** (sessão por armazém, zona ou localização; picar localização e depois peças; peça fora do lugar avisa "não esperada aqui"; revisão e aprovação pela supervisão; relatório exportável). Regra: movimentos durante uma sessão de inventário avisam e ficam marcados, não bloqueiam.
### Fase 3: etiquetas
Dois contextos novos no `CatalogoEtiquetas` (Peça e Localização) com os campos do desenho, métodos de impressão no `ImpressaoEtiquetasService` seguindo o padrão dos existentes (ZPL pelo `GeradorZplService` e modelo do `TemplateEtiquetaStore`), pré-visualização com dados reais, impressão individual e em lote (por conjunto, máquina, localização ou receção), conteúdo do QR `PEC:<ref>` e `LOC:<código>`, Code 128 do texto. Tamanho por defeito 60 × 40 mm a 203 dpi (Suposição: confirmar impressora e rolo). Texto longo nunca corta o código (designação em 2 linhas com reticências).
### Fase 4: entrada no stock PHC
Conforme o desenho (documento de entrada ou o mecanismo escolhido), só na base de testes, replicando os campos obrigatórios; a receção só se dá por concluída quando o stock PHC e o da localização concordam.
Fora de âmbito: saída por leitura, peças na intervenção, leitura por câmara no telemóvel e alerta de rutura (P3-T05); encomendas automáticas, devoluções, lotes e validades.
Solução escolhida e porquê: stock atual em `u_manlocstk` mais registo de todos os movimentos em `u_manmovloc` (rasto e anti-duplicação por `opid`); etiquetas pelo editor que o LandeiroMaui já tem, em vez de um criador novo.

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
- O `desenho.md` prevalece sobre este prompt nos nomes e contratos; contradições assinalam-se.
- Todo o movimento de stock é uma linha em `u_manmovloc` (nunca só um UPDATE ao stock) e tem `opid` único; o reenvio com o mesmo `opid` é ignorado sem erro. Stock negativo nunca é recusado numa saída offline (P3-T05); aqui assinala-se.
- Impressão: respeita o padrão do `ImpressaoEtiquetasService` (`ResultadoImpressao`, `UltimoErro`, `CrashLogService.Registar`); nunca silêncio. Não mudes o comportamento das etiquetas existentes (carcaça, desmancha, receção de mercadoria): só acrescentas.
- A leitura no Windows usa o listener de scanner existente; a câmara do telemóvel não faz parte desta task (depende de P2-T05).
- Escrita no PHC só na base de testes e com a verificação textual do script (sem CREATE TABLE exceto temporárias, ALTER TABLE, DROP TABLE, TRUNCATE).
- Desempenho: resolver uma leitura em menos de 500 ms com 4.000 peças; relatório de conciliação e lista de stock paginados (medir e registar).
- Revisão: `sc-revisor` obrigatório antes do gate.
</rules>

<tests>
1. xUnit: transferência (total constante), ajuste, desativação com stock, `opid` repetido, localização inválida/duplicada.
2. Receção de 10 unidades em `RECEC`: stock da localização +10 e movimento E criado; reenvio do mesmo `opid` não altera; quantidade ≤ 0 e peça desconhecida recusadas.
3. Inventário: sessão com 3 localizações, 2 diferenças, 1 aprovada e 1 recontada; só a aprovada gera ajuste; sessão fechada não se altera; picar peça fora do sítio mostra o aviso.
4. Etiquetas: pré-visualização e impressão de 1 e de 20 etiquetas (impressora de teste ou ficheiro ZPL); o QR gerado resolve para a peça/localização (via `ResolvedorLeitura`); impressora em falta dá aviso e registo.
5. Conciliação: criar divergência propositada na base de testes e confirmar que o relatório a mostra; repor depois.
6. Medições: leitura resolvida em < 500 ms e lista de stock paginada (método e valores registados).
O que não puder ser corrido (impressora física, por exemplo) declara-se; não se dá como testado.
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
