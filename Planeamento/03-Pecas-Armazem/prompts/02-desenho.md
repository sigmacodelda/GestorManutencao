# Desenho da Parte 3: referências, armazém virtual, etiquetas, fluxos e alerta de rutura
Versão 0.1 (rascunho: rever quando a task anterior fechar) · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 02 (P3-T02) · Tipo: desenho
Task global: `docs/tasks/00-global.md`
Modo: Plan Mode (para no PLANO do documento e espera a minha confirmação; depois escreve)
Papel do agente: analista: desenha e propõe; não escreve código da app nem altera a base de dados
BASE LANDEIROMAUI (2026-11-06, prevalece sobre o resto deste prompt): o planeamento SEGUE a base da LandeiroMaui, onde a manutenção já está implementada no PHC. Lê `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\base-landeiromaui.md` (ou a cópia em `docs/`). Onde este prompt cita `u_manplano`, `u_configman`, `u_manperiod`, `u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico` ou `u_manintpec`, leia-se o que já existe: `u_itmanutli`/`u_itmanut`, `u_configman`, `u_periocid`, `u_fichaint.estado`, tarefa `u_itmanutli`, data da ficha, `hini`, `hfim`, `tecno`/`tecnome` e `u_matinterv`. Só o que a base não tem é NOVO: tabela/campo a criar por pessoa no PHC (nunca pelo Claude Code); confirma com leitura (SELECT) antes de assumir. Parte 3: `u_matinterv` (design, qtt, epv, custo, ref) já regista o material usado na ficha e reaproveita-se (substitui `u_manintpec` e o ecrã 'Pedir peça'). NÃO existem na base: pedido ao armazém, stock/rutura, etiquetas, receção, inventário, saída por leitura: esses mantêm-se como NOVO.
Entradas: `docs/pecas-armazem/levantamento.md` (P3-T01), contratos e modelo de P1-T02 (`docs/desenho.md` da Parte 1 ou o caminho que a Parte 1 tiver fixado)
Saídas: `docs/pecas-armazem/desenho.md`, `docs/pecas-armazem/mockups/armazem-e-etiquetas.html` (a partir do mockup do planeamento, se me fornecerem)
Critérios de conclusão:
1. Modelo de dados completo em formato `<phc-tables>` (uma secção por tabela: campos, tipo, tamanho, nulo, defeito, regra, chave, índices propostos, script de verificação só leitura), ou a variante "ambiente separado" se D-04 o decidir.
2. Contratos `IPecaService` e `IArmazemVirtualService` (métodos, DTOs, erros) fixados e coerentes com P1-T02, e o contrato com a Parte 2 (peças da intervenção, saída, inventário offline).
3. Regras documentadas com casos de teste: codificação (peças e localizações), resolução de leituras, prazo efetivo e alerta de rutura (fórmula explícita).
4. Fluxos de receção, saída, inventário e etiquetas (conteúdo do QR e da localização), e pipeline da carga por IA (extrair, propor, validar, importar) com a regra de privacidade D-11 e o formato do ficheiro de importação.
5. Mockups clicáveis e plano de testes por requisito (PEC-01 a PEC-10) com a lista de scripts de verificação.
Gate: manual: eu confirmo o fecho da task
Agentes: `sc-leitor` (ler contratos da Parte 1), `sc-regras-leitor` (dicionário PHC, só campos necessários), `sc-revisor` (revisão crítica do desenho antes do gate)

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
Nota: tabelas candidatas (nomes provisórios). O Claude Code NUNCA as cria: o utilizador cria-as no PHC. Esta task transforma-as na especificação final, depois do levantamento (P3-T01), e só nomeia campos do PHC existente que o levantamento tenha confirmado.

| Tabela candidata | Finalidade | Campos principais (tipo) |
|---|---|---|
| `u_manmaq` | Catálogo de máquinas para a codificação das peças | `codigo` varchar(4) (`M001`), `descricao` varchar(60), `fabricante` varchar(40), `infrastamp` char(25) nulo (equipamento principal), `ativo` bit |
| `u_manconj` | Conjuntos de cada máquina (secções do manual) | `codigo` varchar(8) (`M001-001`), `maqcod` varchar(4), `seq` int, `desigen` varchar(120), `desigpt` varchar(120), `ativo` bit |
| `u_manpeca` | Extensão da peça (se D-04 = PHC; senão, o artigo todo) | `ref` varchar(18), `reffabric` varchar(40), `codbarras` varchar(40) nulo, `fabricante` varchar(40), `desigen` varchar(120), `desigpt` varchar(120), `idiomaorig` char(2), `locdefeito` varchar(20) nulo, `conjunto` varchar(8) (1.ª ocorrência), `ativo` bit |
| `u_manpecaequip` | Peça ↔ equipamento (e posição no manual) | `infrastamp` char(25), `ref` varchar(18), `conjunto` varchar(8), `itemno` varchar(10), `qtt` numeric(11,3) |
| `u_manplanopec` | Peças necessárias por plano | `planostamp` char(25), `ref` varchar(18), `qtt` numeric(11,3), `obs` varchar(100) |
| `u_manloc` | Localizações do armazém virtual | `codigo` varchar(20), `descricao` varchar(60), `armazem` numeric(5,0), `ativo` bit |
| `u_manlocstk` | Stock por peça e localização | `ref` varchar(18), `loc` varchar(20), `qtt` numeric(11,3); único (`ref`, `loc`) |
| `u_manlead` | Prazo de entrega (artigo vazio = por fornecedor; fornecedor 0 = por artigo) | `ref` varchar(18), `fornecedor` numeric(10,0), `dias` int |
| `u_manmovloc` | Movimentos de armazém (entrada E, saída S, transferência T, ajuste A, estorno) | `tipo` char(1), `ref` varchar(18), `loc` varchar(20), `locdest` varchar(20) nulo, `qtt` numeric(11,3) > 0, `fichastamp` char(25) nulo, `invstamp` char(25) nulo, `docref` varchar(30) nulo, `opid` char(36) único (anti-duplicação offline), `data` datetime, `estornode` char(25) nulo, `obs` varchar(100) |
| `u_maninv` | Sessão de inventário | `descricao` varchar(60), `armazem` numeric(5,0), `zona` varchar(20) nulo, `estado` varchar(12), `datainicio` datetime, `datafim` datetime nulo |
| `u_maninvlin` | Contagens da sessão | `invstamp` char(25), `ref` varchar(18), `loc` varchar(20), `qttcontada` numeric(11,3), `qttsistema` numeric(11,3), `aprovado` bit, `opid` char(36) único |

- Campos standard do PHC (stamp, auditoria, marcada): o PHC acrescenta-os.
- Configuração: chaves novas na `u_configman` da Parte 1 (`RUTURA_MARGEM_DIAS`, `RUTURA_PRAZO_DEFEITO_DIAS`, `RUTURA_CONTAR_ENCOMENDAS`, limiar de validação da carga). Não se cria tabela.
- Verificação: `01_verificar_<tabela>.sql` só leitura (`INFORMATION_SCHEMA` e `sys.*`), `RAISERROR` (severidade 16) com mensagem clara; se falhar, parar a parte dependente e escrever "BLOQUEIO:". Os scripts escrevem-se na P3-T03; aqui especificam-se.
- Pode ser criada neste cliente? [CONFIRMAR: pendência 2]. Criada por: o utilizador, no PHC, antes da P3-T03.
</phc-tables>

<task>
Objetivo: desenhar a Parte 3 de ponta a ponta, sem código, de forma que as tasks P3-T03 a P3-T05 arranquem sem perguntas.
Âmbito:
### Modelo de dados e criação de artigos
Fecha as tabelas candidatas (podes juntar, separar ou retirar com justificação; `u_manmovloc` substitui qualquer tabela de consumo se os Movimentos da ficha não servirem). Define como o artigo é criado no PHC (importação nativa ou INSERT replicando os campos obrigatórios na base de testes) e como o stock PHC se movimenta (documento de entrada na receção, saída ligada à ficha), conciliando a soma por localização com o stock do PHC. Se D-04 = ambiente separado, desenha a variante (base própria, stock próprio) e o impacto.
### Contratos
`IPecaService`, `IArmazemVirtualService` e DTOs; erros esperados; o que a Parte 2 consome offline (peças do plano, resolução de leitura, saída, contagem) e o que fica em cache no telemóvel (peças do equipamento e localizações, nunca as 4.000 de uma vez). Alinha nomes com P1-T02.
### Regras
Fixa a codificação e a resolução de leituras, o prazo efetivo e o alerta de rutura (abaixo) com tabelas de casos de teste. Define a regra de movimento (`opid`, sinal, estorno, negativo offline), a transferência e o ajuste de inventário, e os estados da sessão de inventário.
### Fluxos e etiquetas
Receção, saída, inventário, etiquetas (conteúdo do QR e do código de barras da peça e da localização, tamanho 60 × 40 mm a 203 dpi como Suposição, campos, contextos novos no catálogo do editor, impressão em lote).
### Carga por IA
Pipeline extrair → propor → validar → importar, formato do ficheiro de importação (CSV `;` com: máquina, conjunto, página, figura, item, código do fabricante, fabricante, designação EN e PT, idioma, quantidade, confiança, estado, observações) e manifesto; validações automáticas; ecrã de validação humana; ferramenta de extração (decide Python com biblioteca de PDF permissiva ou .NET, com critérios). **Privacidade:** o PDF e os dados da Landeiro não vão para serviços externos sem autorização expressa (D-11); recorda que o Claude Code corre localmente mas envia ao modelo o texto que lê; descreve o modo sem IA e o modo com IA parcial.
### Mockups e testes
Mockups clicáveis (lista de peças por máquina, receção, saída por leitura, inventário, desenho das etiquetas, painel de alertas), marca SigmaCode, ficheiro único; plano de testes por requisito.
Fora de âmbito: escrever código da app, criar tabelas, correr scripts de escrita, encomendas automáticas, devoluções, lotes e validades, stock mínimo por peça.
Solução escolhida e porquê: um documento de desenho com `<phc-tables>` por tabela e contratos, porque a Landeiro/SigmaCode tem de criar as tabelas à mão no PHC e os agentes seguintes têm de as verificar por leitura.

### Codificação das peças e das localizações (proposta a fechar em P3-T02)
- Referência interna da peça: `M<MMM>-<CCC>.<PPP>` (máquina, conjunto, peça; 3 dígitos cada, 001 a 999; 12 caracteres). Regex `^M\d{3}-\d{3}\.\d{3}$`. Exemplo: `M001-001.001`. Conjunto (só chave em `u_manconj`): `^M\d{3}-\d{3}$`. Os exemplos da reunião (001, 001.1, 001.2) leem-se como conjunto e peça; o preenchimento a 3 dígitos é Suposição.
- Uma peça física = uma referência: o mesmo código do fabricante noutro conjunto ou máquina só cria ligação peça ↔ máquina/conjunto com "Item no." e quantidade (decisão D-12 por confirmar; alternativa rejeitada: um artigo por posição, porque fragmenta o stock). A referência nunca se renumera nem se reutiliza; peças obsoletas ficam.
- `<PPP>` = máximo + 1 no conjunto; na 1000.ª peça falha com erro claro. A referência do fabricante é texto (zeros à esquerda contam), normalizada em maiúsculas e sem espaços nas pontas.
- Dados da peça: `reffabric`, `codbarras` (opcional), `fabricante`, `desigen` (original em inglês, vazio se o original é português), `desigpt` (tradução ou o original), `idiomaorig` (EN, PT, XX), unidade, `locdefeito`.
- Localização: `<Z><EE>-<P>-<PP>` (zona A-Z, estante 01-99, prateleira 1-9, posição 01-99), ex. `A03-2-05`; regex `^[A-Z]\d{2}-\d-\d{2}$`; reservada `RECEC` (zona de receção).
- Payloads: peça `PEC:M001-001.001`, localização `LOC:A03-2-05` (QR e Code 128). Resolução de uma leitura, por ordem: prefixo `PEC:` → peça; prefixo `LOC:` → localização; regex da referência interna → peça; regex da localização ou `RECEC` → localização; senão procura em `reffabric` e `codbarras` (um = peça, vários = lista para escolher, nenhum = desconhecido).
- Validações de teste: `M001-001.001` e `M999-999.999` válidas; `M001-001.000`, `M000-001.001`, `M1-1.1`, `M001-001.0001` inválidas como peça; `A03-2-05` e `RECEC` válidas, `A3-2-05` e `A03-0-05` inválidas; `0070001` e `70001` são referências de fabricante diferentes; próxima peça de um conjunto com `.999` falha.

### Regra do alerta de rutura (PEC-10) e prazo de entrega (PEC-04)
- Parâmetros em `u_configman`: `RUTURA_MARGEM_DIAS` M (Suposição 7), `RUTURA_PRAZO_DEFEITO_DIAS` (Suposição 30), `RUTURA_CONTAR_ENCOMENDAS` (0 por defeito). `hoje` = data local sem hora.
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
- Task sem código: não escrevas código da app nem scripts de escrita. Os scripts de verificação só se especificam.
- Em desenho, cada decisão nova que não esteja em <decisions> marca-se "Suposição:" e lista-se no fim do documento; propõe 2 a 4 opções (a recomendada primeiro) para o que dependa de mim.
- Nomes de tabelas e campos do PHC existente só os que o levantamento confirmou; os restantes escrevem-se como "a confirmar".
- O desenho tem de resolver explicitamente: D-12 (um artigo por código do fabricante), como se cria o artigo no PHC, quem dá entrada do stock PHC, onde ficam as peças usadas (Movimentos da ficha vs `u_manmovloc`), o que acontece a uma saída offline que deixa o stock negativo, e a conciliação com o stock PHC.
- Mockup: marca SigmaCode (#5BB572, #3466AF, #604696, #1A1731, #CAC9C9; Exo 2 e Open Sans), ficheiro único, funciona em desktop e em largura de telemóvel, dados fictícios.
- Antes do gate, o `sc-revisor` faz revisão crítica do desenho (SQL de verificação, segurança, coerência com P1-T02).
</rules>

<tests>
Verificação do desenho:
1. Cada requisito PEC-01 a PEC-10 aponta para uma secção do desenho, um contrato e um teste.
2. Cada tabela tem `<phc-tables>` completo e nenhum campo tem tipo ou tamanho por definir.
3. As regras do alerta reproduzem os 8 casos de teste de <task> (verificar à mão no documento).
4. Os contratos coincidem com os de P1-T02 (nomes e DTOs); divergências listadas.
5. O mockup abre sem rede (exceto fontes) e é navegável em largura de telemóvel; `verificar-segredos.ps1` limpo.
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
