# Levantamento da Parte 3: peças, stock no PHC, manual Marel e fornecedores
Versão 1.0 · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 01 (P3-T01) · Tipo: levantamento
Task global: `docs/tasks/00-global.md`
Modo: Autónomo (só leitura; pára em BLOQUEIO: ou no GATE:)
Papel do agente: leitor e analista: levanta factos com evidência e recomenda; não altera código, nem base de dados, nem ficheiros fora de `docs/pecas-armazem/`
Entradas: base de testes `TESTES_LAND2` (só SELECT), `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (só leitura), PDF Marel (só com autorização D-11)
Saídas: `docs/pecas-armazem/levantamento.md`
Critérios de conclusão:
1. Estrutura PHC de artigos, stock por armazém e fornecedores documentada (tabelas, campos, tipos, chaves, triggers incluindo encriptados), com a consulta usada como evidência.
2. Referências existentes verificadas (a Landeiro diz que não há de peças): famílias, convenção de referências, comprimento e caracteres permitidos, prefixo `M` livre ou em colisão; e descrito como se cria um artigo (campos mínimos e tabelas relacionadas).
3. Como o stock se movimenta no PHC (entrada e saída) e onde a Ficha de Intervenção guarda peças e custos (hipótese: Movimentos Internos/Externos), com a recomendação do que reaproveitar.
4. Estrutura do PDF Marel documentada (nº de páginas, camada de texto, estrutura de um conjunto, colunas "Item no.", "Marel code", quantidade, onde há códigos de barras) ou declarado inacessível, com o motivo.
5. Prazos de entrega e fornecedor principal (existem campos?), tabelas `u_` existentes, e recomendação fundamentada para D-04, mais a lista de perguntas para P3-T02.
Gate: manual: eu confirmo o fecho da task
Agentes: `sc-leitor-bd` (SELECT), `sc-regras-leitor` (dicionário PHC: `ak.ps1 phc-tabela`, `phc-campo`), `sc-leitor` (código do LandeiroMaui)

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

<task>
Objetivo: levantar, só em leitura, como o PHC da Landeiro trata artigos, stock e fornecedores, o que existe de referências de peças, como está estruturado o manual Marel e como se lê e imprime código no LandeiroMaui, para a P3-T02 desenhar sem adivinhar.
Âmbito (por esta ordem):
### Passo 0: levantamento inicial (numa só mensagem, antes de executar)
Junta o que falta e apresenta-o como opções para eu escolher: (a) se a connection string de leitura já está no `appsettings` da base de testes; (b) **autorização D-11**: se posso abrir páginas do PDF Marel (o texto que leio é enviado ao modelo): opções "sim, amostra de até 10 páginas", "só metadados (tamanho, páginas, camada de texto) e tu descreves a estrutura", "não"; (c) pasta e nome do PDF; (d) quem recebe as peças (Compras ou manutenção). Sem resposta a (b), assume "só metadados" e escreve `BLOQUEIO:` só para a parte do PDF; continua o resto.
### A. Ambiente e artigos
`SELECT DB_NAME();` (se for `LAND2`, só consultas leves e sem copiar dados pessoais), versão e nível de compatibilidade (pendência 13). Tabela de artigos: campos obrigatórios, tipos, comprimento da referência, caracteres aceites (`-` e `.`), chave, famílias (lista, nº de artigos, uso), campo de unidade, de fornecedor principal e de prazo (se existirem). Triggers existentes nas tabelas envolvidas (incluindo encriptados): só lê, nunca altera.
### B. Criar um artigo
Como se cria um artigo programaticamente sem partir o PHC: campos mínimos, tabelas relacionadas (stock por armazém, outras), triggers que disparam. Isto decide como a importação da P3-T03 cria artigos (importação nativa do PHC ou INSERT replicando os campos obrigatórios na base de testes). Não escrevas nada.
### C. Stock e armazéns
Tabela de stock por armazém, armazéns existentes (há um de manutenção?), documentos de entrada e de saída usados hoje, receção de encomenda a fornecedor (o LandeiroMaui já usa encomenda ndos 2 e receção ndos 18 para mercadoria) e se serve para peças.
### D. Fornecedores e prazos
Tabela de fornecedores, fornecedor principal do artigo, qualquer campo ou tabela de prazo de entrega por artigo ou fornecedor. Se não existe, diz que a `u_manlead` é necessária.
### E. Referências existentes e colisão
Conta artigos que possam ser peças de manutenção (famílias, designações) e verifica colisão do prefixo `M` com o formato `M<3 dígitos>-<3 dígitos>.<3 dígitos>`.
### F. Ficha de Intervenção e peças usadas
Localiza (pelo dicionário PHC) as tabelas dos Movimentos Internos/Externos e da Ficha de Intervenção; descreve como se guardam peças, quantidades e custos (Mão de Obra, Materiais/Peças) e se dá para reaproveitar em vez de criar `u_manmovloc`/`u_manintpec`.
### G. PDF Marel (só conforme D-11)
Tamanho, nº de páginas, se há camada de texto (pesquisável) ou é imagem, índice de máquinas e conjuntos, estrutura de um conjunto (figura e tabela), colunas "Item no.", "Marel code", descrição e quantidade, idiomas, onde há códigos de barras. Com autorização: só a estrutura (cabeçalhos e no máximo 3 linhas de exemplo); nunca copies o manual para o documento. Estima o nº de peças por máquina e por conjunto.
### H. LandeiroMaui
Como se lê código no Windows (leitor tipo teclado, listener de scanner), se há leitura por câmara; como o editor de etiquetas (`Services/Etiquetas`) se liga ao `ImpressaoEtiquetasService` e onde se guardam os modelos (`u_ETIQUETAS`); como se adicionaria um contexto novo.
### I. Tabelas `u_` e recomendação
Lista os nomes das tabelas `u_` existentes (evita colisões com as candidatas `u_man…`) e se há sinal de que se podem criar. Recomenda D-04 (PHC com família própria vs ambiente separado) com a evidência; confirma ou contraria D-12.
Fora de âmbito: escrever na base, criar tabelas ou campos, ler `appsettings.json`, alterar código, extrair as peças do PDF, desenhar a solução (é a P3-T02).
Solução escolhida e porquê: só consultas SELECT leves delegadas ao `sc-leitor-bd`, dicionário via `sc-regras-leitor` (nunca o dicionário inteiro) e leitura de excertos do código; é o mais barato e o mais seguro.
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
- Só leitura: nenhuma instrução que não seja `SELECT` (inclui `sys.*` e `INFORMATION_SCHEMA`). Cada consulta em `NOLOCK`, leve e sem copiar dados pessoais (clientes, trabalhadores) para o documento.
- Nunca leias `appsettings.json`, `bin/` nem `obj/`; não imprimas connection strings.
- PDF: antes de o abrir confirma a autorização D-11 no `00-global.md` ou na minha resposta ao Passo 0; sem ela, só metadados. O PDF e o seu conteúdo não vão para serviços externos além do que a D-11 autorizar. Não copies o manual (direitos de autor): só estrutura e exemplos mínimos.
- Cada afirmação do levantamento leva a evidência (consulta ou ficheiro e linha); o que não conseguires confirmar marca-o "Suposição:" ou "não confirmado". Não inventes nomes de tabelas nem de campos.
- Não escrevas nada fora de `docs/pecas-armazem/` e de `docs/tasks/00-global.md` (fecho da task).
</rules>

<tests>
Verificação do documento produzido:
1. Todos os 5 critérios têm a secção correspondente com evidência.
2. Todas as consultas listadas no anexo são SELECT (verificação textual) e nenhuma mostra dados pessoais.
3. Sem connection strings, credenciais nem IPs; corre `verificar-segredos.ps1` sobre `docs/pecas-armazem/`.
4. Cada pergunta para a P3-T02 tem dono e diz se bloqueia.
5. A recomendação para D-04 e D-12 compara PHC com ambiente separado e cita a evidência.
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
