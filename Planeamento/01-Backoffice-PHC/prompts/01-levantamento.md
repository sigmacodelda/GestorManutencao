# Levantamento da estrutura PHC (equipamentos e intervenções) e do Word do plano
Versão 1.0 · 05/11/2026 · Substitui: —

Iniciativa: Gestor de Manutenção · Cliente: Carnes Landeiro
Task: 01 (P1-T01) · Tipo: levantamento
Task global: `docs/tasks/00-global.md` (cópia de trabalho em `C:\_SigmaRepos\GestorManutencao\docs\tasks\`; se não existir, copia-a de `C:\_SigmaRepos\GestorManutencao\Planeamento\00-Comum\00-global.md`)
Modo: Autónomo
Papel do agente: leitor e analista: só lê (SELECT) e documenta; não altera a base, não escreve código da aplicação
Entradas: Word do plano P.05-03 v1.4 em `[PREENCHER: caminho do ficheiro .docx]` (já recebido a 05/11/2026) e, se existir, o extrato `plano-p05-03-normalizado.csv` (separador `;`, colunas no, designacao, zona, entidade, periodicidade, meses e avisos; ver abaixo "Extrato já feito"). O Word está ligado ao Excel `Lista de infraestruturas1.xlsx` (folha "plano de manutenção"): pede-o à Landeiro, é a fonte real.
Saídas: `C:\_SigmaRepos\GestorManutencao\docs\levantamento.md`
Critérios de conclusão:
1. Nome real e estrutura (campos, tipos, tamanhos, nulos, chaves, índices, nº de linhas) das tabelas por trás dos ecrãs Infraestruturas e Ficha de Intervenção e das abas Movimentos Internos e Externos, com as consultas usadas e o resultado resumido.
2. Versão do SQL Server, nível de compatibilidade, permissão do login para criar tabelas e campos `u_`, e lista de triggers das tabelas envolvidas (nome, evento, ativo ou não, encriptado ou não, efeito relevante), com evidência.
3. Hipótese "os Movimentos Internos e Externos guardam peças e custos e somam para Mão de Obra e Materiais/Peças" confirmada ou refutada com evidência, e decisão do que se reaproveita.
4. Estrutura do Word lida e mapeamento proposto para o modelo (`u_manplano`), com divergências; se o Word não chegou, o script de leitura e o mapeamento esperado prontos e `BLOQUEIO:` só dessa parte.
5. Valores reais (periodicidade, tipo de manutenção, tipo de intervenção, localizações, roles existentes) e lista de correções propostas ao modelo candidato e ao `00-global.md`.
Gate: manual
Agentes: `sc-leitor-bd` (todas as consultas à base), `sc-leitor` (excertos do LandeiroMaui)

<role>
És um engenheiro de software sénior, especialista em .NET 9 MAUI Blazor Hybrid, Dapper, SQL Server e PHC (Cegid PHC Evolution). Escreves em português de Portugal. Objetivos: segurança, confiabilidade, manutenibilidade e clareza.
</role>

<context>
- Projeto: Gestor de Manutenção da Carnes Landeiro. Passa o circuito de manutenção (papel, Word, ecrãs PHC Infraestruturas e Ficha de Intervenção) para a plataforma .NET MAUI da Landeiro: equipamentos, periodicidade e planos, intervenções no telemóvel (offline), validação no backoffice, dashboard e, depois, peças e armazém. Esta é a Parte 1 (backoffice e PHC); P1-T01 é o levantamento que fixa a realidade da base antes do desenho.
- Repositórios: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (código, só leitura nesta task) e `C:\_SigmaRepos\GestorManutencao` (planeamento e `docs/`).
- Situação: ecrã PHC Opções Internas → Manutenção → Infraestruturas (abas Dados principais, Outros Dados, Instr. Manut., Fichas Interv., Via Verde, Histórico Status). Dados principais: Nº Infraestrutura, Descrição, Nº Série, Obsoleto?, Tipo (Equip. Produção, Veículos), Marca, Modelo, Matrícula, Data, Localização (Abate de Suínos, Salsicharia), Ano de Aquisição, Email Envio de Alerta, Fornecedor (nome e nº), Valor de Aquisição, Marcada. A aba Fichas Interv. lista Data, Número, Tipo Manutenção, Tipo Intervenção, Problema, Obsoleto.
- Ficha de Intervenção: cabeçalho Supervisão? (desativado para o operador), Fechada?, Obsoleto?; abas Dados principais, Movimentos Internos, Movimentos Externos; campos Infraestrutura, Periodicidade (lista: De acordo com procedimento, Diária, Mensal, Quinzenal, Regular, Semanal, Semestral, Sempre que necessário), Ficha nº, Data, Tipo Manutenção (Interna ou Externa), Tipo Intervenção (Preventiva ou Curativa), Doc. Externo / Folha Serviço, Kms, Revisão?, Horas Frio, Anomalia/problema, Intervenção, Limpeza Local, Remoção Utensílios?, Fornecedor, Mão de Obra, Materiais/Peças, Total Ficha Intervenção, Marcada. Hoje a ficha é feita em papel e passada para o PHC; a supervisão valida e a ficha fica Fechada; para repetir uma periódica duplica-se a ficha e muda-se a data.
- Plano de manutenção atual: Word P.05-03 Plano de Manutenção de Equipamentos v1.4 (2026): Nº (igual ao nº da infraestrutura), Designação, Zona (Abate, Salsicharia…), Entidade (Interna, Externa ou Interna/Ext), colunas JAN a DEZ com "x" nos meses planeados e periodicidade resumida (Mensal, Trimestral…). Linhas a vermelho (Suposição: novos ou sem plano concluído).
- Hipótese a confirmar: as abas Movimentos Internos e Externos guardam as peças e os custos, e Mão de Obra e Materiais/Peças somam deles. Se for verdade, a ligação "peças usadas numa intervenção" já existe e reaproveita-se em vez de criar tabela nova.
- Modelo candidato a confirmar (nomes reais só depois deste levantamento): `u_manplano` (plano por equipamento), `u_manconfig` (chave/valor), campos novos na Ficha (`u_estado`, `u_planostamp`, `u_dataplan`, `u_hini`, `u_hfim`, `u_tecnico`, `u_ordem`). Decisão D-05: planeadas materializam-se como fichas com estado Planeada. Decisão D-04 (alternativa): se o PHC não permitir tabelas `u_`, base própria `MANUTENCAO` na mesma instância.
- Anexos: nenhum.
Nota: sem credenciais, tokens nem connection strings. A connection string vive no `appsettings` do projeto, preenchida pelo utilizador; nunca a leias nem a escrevas.
</context>

<environment>
- PHC: edição Evolution (Ultimate), versão 2025.04. Cliente: Carnes Landeiro. Alojamento: servidor do cliente [CONFIRMAR].
- SQL Server: versão [CONFIRMAR: é uma das saídas desta task]. Nível de compatibilidade da base real: assumir 100 [CONFIRMAR]. `LAND2_DEMO` está em 150 e não prova compatibilidade com 100.
- Bases: `TESTES_LAND2` (testes) e `LAND2_DEMO` (demo) são as únicas onde se pode escrever, depois de `SELECT DB_NAME();` e de confirmar o nome. `LAND2` (produção) é só leitura. Nesta task não escreves em nenhuma.
- Podes criar: SPs (`sc_<Entidade><Acao>`), views (`sc_v_<Nome>`) e triggers novos, só na base de testes. Nesta task não crias nada.
- Nunca crias tabelas nem campos (nem CREATE TABLE, nem ALTER TABLE), nem alteras, desativas ou apagas triggers existentes (incluindo encriptados).
- Triggers existentes nas tabelas envolvidas: a ler nesta task.
- Projeto de código base: `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (.NET 9, MAUI Blazor Hybrid, Dapper). Lê excertos de `CLAUDE.md` e `ARQUITETURA.md`. Nesse repositório não faças commits, branches nem push, e nunca leias `appsettings.json`, `bin/` nem `obj/`.
</environment>

<task>
Objetivo: descobrir, só por leitura, como o PHC guarda hoje equipamentos, intervenções e movimentos, o que se pode reaproveitar e o que é preciso criar, e preparar a carga do plano do Word.
Âmbito:
1. Confirma a base com `SELECT DB_NAME()`, versão e `compatibility_level`. Usa `sc-leitor-bd` para todas as consultas; consultas leves (`TOP`, filtros por nome), sem copiar dados pessoais (nomes de técnicos, e-mails, matrículas) para o documento.
2. Encontra as tabelas: procura por nome (`%infra%`, `%interv%`, `%manut%`, `%mov%`) e por campos; confirma a tabela certa com uma leitura filtrada por um valor conhecido do vídeo (por exemplo a infraestrutura "Depiladora Suínos" ou "Enchedora REX"), nunca com varrimentos completos. Documenta cada tabela: campos, tipos, tamanhos, nulos, chave, índices, `COUNT(*)`, campos de auditoria e a coluna que liga ficha a infraestrutura e movimento a ficha.
3. Triggers das tabelas envolvidas: nome, evento, `is_disabled`, encriptado (`definition IS NULL` em `sys.sql_modules`). Resume o efeito dos não encriptados (o que fazem ao gravar, ao fechar a ficha, aos totais). Nunca os alteres.
4. Movimentos: estrutura, relação com a ficha e teste da hipótese (para 5 fichas com valores diferentes de zero, compara a soma dos movimentos com Mão de Obra, Materiais/Peças e Total). Diz o que se reaproveita para peças e custos.
5. Valores reais: `SELECT DISTINCT` (com contagem) de periodicidade, tipo de manutenção, tipo de intervenção, localização e tipo da infraestrutura; intervalo de datas e nº de fichas por ano; fichas com Periodicidade preenchida e Preventiva (para estimar planeamento já feito no PHC).
6. Permissões e ambiente: `sys.fn_my_permissions(NULL,'DATABASE')` (existe CREATE TABLE?); tabelas `u_*` existentes e o padrão dos seus campos standard; roles da base (`sys.database_principals` com `type = 'R'`, só nomes, sem membros) e as constantes de `Helpers/Perfis` no LandeiroMaui (via `sc-leitor`). Pergunta se a edição permite tabelas `u_` só se a leitura não responder.
7. Word: lê a estrutura (tabelas, cabeçalhos, nº de linhas, linhas a vermelho, valores da coluna Entidade e da periodicidade) com uma ferramenta local de leitura de .docx, sem copiar dados pessoais. Propõe o mapeamento linha do Word → `u_manplano` (Nº → nº da infraestrutura; Designação; Zona → localização; Entidade → tipo de manutenção; JAN a DEZ → `meses` char(12); periodicidade → lista única) e lista divergências: nº do Word sem infraestrutura, infraestrutura sem linha, periodicidade incoerente com os meses, "Interna/Ext", linhas a vermelho. Se o Word não existir, entrega o plano de leitura e o mapeamento esperado e escreve `BLOQUEIO:` só para esta parte.
   Extrato já feito (05/11/2026, a validar, não a repetir às cegas): a tabela principal tem 29 colunas (Nº, Designação, Zona, Entidade, **2 células por mês** em JAN a DEZ, Manutenção) e 107 linhas de equipamento (99 com Nº inteiro, 3 com Nº decimal, 5 sem Nº; Nº de 3 a 248); zona vem de linhas "Zona: …" (herdada, Suposição); Entidade tem 8 grafias (interna, interno, externa, externo, Interna/Ext, Int+Ext, Int/Ext, Interno+Ext); periodicidade: Mensal 45, Trimestral 37, Anual 13, Semanal 3, Semestral 3, Quinquenal 1, "3VZ ANO" 1, 5 sem valor e 1 gralha ("trismestral"); Nº repetidos: 225, 231 e 233; 42 equipamentos sem marcas nos meses. O Word está ligado ao Excel `Lista de infraestruturas1.xlsx` (folha "plano de manutenção"): pede-o. Confirma com a Landeiro: (a) o que significam as 2 células por mês (Suposição: quinzenas); (b) Nºs repetidos, sem Nº e decimais; (c) Interna+Externa; (d) periodicidades fora da lista da Ficha (Trimestral, Anual, Quinquenal, "3 vezes por ano"). Não leias nomes de pessoas do cadastro do documento.
Fora de âmbito: escrever na base, criar tabelas, campos, SPs ou triggers, escrever código da aplicação, desenhar a solução (é a P1-T02), levantar as Partes 2 e 3, viaturas (matrícula, Via Verde, Kms).
Solução escolhida e porquê: levantamento só por SELECT sobre `INFORMATION_SCHEMA` e `sys.*` com leituras filtradas; é o degrau mais baixo e não toca nos dados.
Consultas de arranque (adapta os nomes; comentários com `--` só aqui no SQL Server Management; dentro de `u_sqlexec` usa só `/* */`):
```sql
SELECT DB_NAME() AS bd, d.compatibility_level, SERVERPROPERTY('ProductVersion') AS versao, SERVERPROPERTY('Edition') AS edicao FROM sys.databases d WHERE d.name = DB_NAME();
SELECT t.name, SUM(p.rows) AS linhas FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1) WHERE t.name LIKE '%infra%' OR t.name LIKE '%interv%' OR t.name LIKE '%manut%' OR t.name LIKE 'u[_]%' GROUP BY t.name ORDER BY t.name;
SELECT c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.IS_NULLABLE, c.COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = @tabela ORDER BY c.ORDINAL_POSITION;
SELECT i.name, i.is_primary_key, i.is_unique, ic.key_ordinal, col.name AS coluna FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(@tabela) ORDER BY i.name, ic.key_ordinal;
SELECT tr.name, OBJECT_NAME(tr.parent_id) AS tabela, tr.is_disabled, CASE WHEN m.definition IS NULL THEN 1 ELSE 0 END AS encriptado FROM sys.triggers tr LEFT JOIN sys.sql_modules m ON m.object_id = tr.object_id WHERE OBJECT_NAME(tr.parent_id) IN (@t1, @t2, @t3);
SELECT permission_name FROM sys.fn_my_permissions(NULL, 'DATABASE') WHERE permission_name IN ('CREATE TABLE','ALTER','CONTROL');
SELECT name FROM sys.database_principals WHERE type = 'R' ORDER BY name;
```
</task>

<decisions>
- D-03: acesso a dados como o LandeiroMaui (Dapper direto, login SQL por utilizador). Alternativa rejeitada: App → API → sServices, porque acrescenta camadas e prazo.
- D-04 (por decidir) e D-07 (backoffice = ecrãs Windows do módulo Manutenção no LandeiroMaui; Suposição a confirmar). O levantamento fornece os factos para as confirmar; não as decide.
- D-05: planeadas = fichas com estado Planeada. Alternativa rejeitada: calcular só em memória, porque a app offline precisa de linhas com stamp.
- D-08: documentos em Markdown + Mermaid, marca SigmaCode.
</decisions>

<open-points>

| # | Pendência | Dono | Bloqueia? |
|---|---|---|---|
| 1 | Word do plano P.05-03 v1.4 | Luís (Landeiro) | sim (critério 4) |
| 2 | Acesso de leitura a `TESTES_LAND2`, connection string no `appsettings` preenchida pelo utilizador | Utilizador | sim (tudo menos o Word) |
| 3 | Circuito PHC em detalhe | Rui (Landeiro) | não |
| 4 | É possível criar tabelas e campos `u_` neste PHC? | Utilizador / Landeiro | não nesta task (a resposta sai daqui) |
| 11 | Viaturas ficam fora do âmbito? | Landeiro | não |
| 13 | Nível de compatibilidade e versão do SQL Server da base real | Utilizador (SELECT nesta task) | não |
</open-points>

<rules>
- Segue os padrões que já existem no repositório. Prefere a solução mais simples que seja correta. Não introduzas dependências sem justificação (regista em `docs/DEPENDENCIES.md`: nome, versão, para quê, alternativa, licença) e confirma a documentação oficial atual antes de usar uma biblioteca.
- Dados: Dapper cru, `WITH (NOLOCK)` em todo o SELECT/JOIN a tabelas base (não em CTEs nem alvos de UPDATE/DELETE). Nunca `UPDLOCK`, `HOLDLOCK`, `XLOCK`, `TABLOCK`, `ROWLOCK`, `SERIALIZABLE`, `READPAST` nem transações longas. Não inventes filtros SQL: só as condições pedidas.
- Compatibilidade SQL nível 100: sem STRING_SPLIT, OPENJSON, JSON_VALUE, TVP; TRY_CONVERT, IIF, CONCAT, FORMAT, OFFSET…FETCH, THROW, TRY_CAST e STRING_AGG só depois de confirmados numa base de teste em nível 100. SQL dentro de `u_sqlexec`: comentários só `/* */`.
- Segurança: sem credenciais em prompts, código, logs ou documentação; consultas parametrizadas; mínimo de permissões. Dados pessoais só o necessário (RGPD). Antes de entregar corre `C:\_SigmaRepos\Claude-The-ALLKNOWING\tools\verificar-segredos.ps1 -Caminho C:\_SigmaRepos\GestorManutencao\docs`.
- Base de dados: só leitura nesta task. Não imprimas valores de campos pessoais nas respostas nem no documento.
- Não inventes nomes de tabelas ou campos: tudo o que escreveres no documento vem de uma consulta que correste; o que não conseguiste confirmar marca-o "Suposição:" ou "não confirmado".
- Git: sem commit nem push (no LandeiroMaui nunca).
- Revisão crítica do teu levantamento antes de entregar: cada afirmação tem a consulta que a prova.
- Fecho da task:
  1. Atualiza `docs/tasks/00-global.md`: estado da task, saídas, critérios (marca [x] só com evidência), decisões, pendências e riscos novos, e uma linha no Registo. Preenche a secção Ambiente (versão SQL, compatibilidade, criar `u_`, triggers).
  2. Escreve o ESTADO DA TASK no fim da resposta.
  3. Se todos os critérios estão cumpridos, escreve uma só vez "GATE: critérios cumpridos (n de n). Confirmas o fecho da task?" e pára. Não arranques a task seguinte.
  4. Usa "BLOQUEIO:" só quando uma decisão realmente trava o trabalho (credencial em falta, decisão de negócio sem alternativa razoável, escrita numa base que não é de teste, ou tabela/campo novo no PHC) e continua o que não depende dela. Fora do GATE e do BLOQUEIO, não peças aprovações.
</rules>

<tests>
1. Cada tabela documentada tem a consulta de `INFORMATION_SCHEMA` e a contagem; o nº de campos do documento bate com o da consulta.
2. A ligação ficha → infraestrutura e movimento → ficha está provada com um exemplo (contagens, sem dados pessoais).
3. A lista de triggers do documento bate com `sys.triggers` (nome, estado, encriptado).
4. A hipótese dos movimentos tem a tabela de 5 fichas (soma dos movimentos contra os totais da ficha) e uma conclusão.
5. O mapeamento do Word cobre todas as colunas do Word, ou o BLOQUEIO está registado.
6. `verificar-segredos.ps1` sem ocorrências; nenhum valor pessoal no documento.
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
