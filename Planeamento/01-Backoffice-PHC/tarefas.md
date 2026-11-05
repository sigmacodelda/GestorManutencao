# Parte 1 (Backoffice e PHC): tarefas

Versão 0.1 · 05/11/2026. Tasks e IDs de `00-Comum/00-global.md`. Esforço em horas de trabalho do agente com acompanhamento (mínimo a máximo), sem contar esperas por terceiros. Estimativas de partida: o prompt-mestre pede a orçamentação em tokens ao agente que executa. Dentro da Parte 1 as tasks são sequenciais; cada fecho exige o GATE do utilizador.

| ID | Task | Esforço (h) | Depende de | Alimenta | Prompt |
|---|---|---|---|---|---|
| P1-T01 | Levantamento | 3 a 6 | acesso de leitura a `TESTES_LAND2`; Word do Luís (para a parte do plano) | P1-T02, P3-T01 | `prompts/01-levantamento.md` |
| P1-T02 | Desenho | 8 a 14 | P1-T01 | P2-T02, P3-T02, P1-T03 | `prompts/02-desenho.md` |
| P1-T03 | Serviços | 14 a 24 | P1-T02; tabelas e campos criados no PHC | P2-T04, P1-T04 | `prompts/03-desenvolvimento-servicos.md` |
| P1-T04 | Ecrãs | 16 a 28 | P1-T03 | P1-T05 | `prompts/04-desenvolvimento-ecras.md` |
| P1-T05 | Dashboard | 10 a 18 | P1-T04 | P1-T06 | `prompts/05-desenvolvimento-dashboard.md` |
| P1-T06 | Testes e documentação | 6 a 12 | P1-T05 | fecho da Parte 1 | `prompts/06-testes-documentacao.md` |

Total da parte: 57 a 102 h. Caminho crítico para 11/11/2026 (protótipo): só P1-T01 (parte de estrutura) e a versão refinada do protótipo de P1-T02. Para 17/11/2026 ver risco R-01: a Parte 1 não chega a código até essa data; a solução provisória é registar no ecrã PHC atual com o plano exportado do Word.

## P1-T01 Levantamento (3 a 6 h)

- [ ] Confirmar `SELECT DB_NAME()` e versão, nível de compatibilidade e permissão para criar tabelas (só leitura). (0,5 h)
- [ ] Localizar e descrever a tabela da Infraestrutura: campos, tipos, chaves, índices, nº de linhas. (0,5 a 1 h)
- [ ] Idem para a Ficha de Intervenção e para os Movimentos Internos e Externos. (1 a 1,5 h)
- [ ] Listar triggers das tabelas envolvidas (nome, evento, estado, encriptado) e resumir o efeito. (0,5 a 1 h)
- [ ] Testar a hipótese dos Movimentos (somas contra Mão de Obra, Materiais/Peças e Total). (0,5 a 1 h)
- [ ] Recolher valores reais (periodicidade, tipos, localizações) e roles existentes. (0,25 a 0,5 h)
- [ ] Ler a estrutura do Word e propor o mapeamento, ou deixar o script de leitura pronto. (0,5 a 1 h)
- [ ] Escrever `docs/levantamento.md` com correções ao modelo candidato. (0,25 a 0,5 h)
- **Conclusão:** 5 critérios do prompt 01. Depende de outras partes: não. Bloqueio previsível: ligação à base de testes e Word.

## P1-T02 Desenho (8 a 14 h)

- [ ] Modelo de dados final em `<phc-tables>` (`u_manperiod`, `u_manplano`, `u_manconfig`, campos novos da Ficha) e índice único proposto. (2 a 3 h)
- [ ] Scripts de verificação só leitura `01_verificar_*.sql`. (1 a 1,5 h)
- [ ] Lista única de periodicidades e algoritmo de geração com tabela de casos de teste (mínimo 12). (1,5 a 2,5 h)
- [ ] Matriz de perfis e permissões, roles a criar e chaves `u_manconfig`. (0,5 a 1 h)
- [ ] Contratos: interfaces, DTOs e exemplos em `docs/contratos.md`; rever com P2 e P3. (2 a 3 h)
- [ ] Wireframes dos 6 ecrãs. (1 a 2 h)
- [ ] Protótipo clicável refinado (parte do `mockups/backoffice.html` deste planeamento) validado com o utilizador para 11/11. (1 a 2 h)
- **Conclusão:** 5 critérios do prompt 02, em especial os contratos publicados e confirmados (P2 e P3 dependem deles).
- **Depende de outras partes:** pedidos de contrato de P2 (offline, alerta) e de P3 (peças por plano, rutura).

## P1-T03 Serviços (14 a 24 h)

- [ ] Verificar as tabelas criadas (scripts T02); parar a parte dependente se faltar algo. (0,5 h)
- [ ] Modelos e DTOs em `Models/Manutencao/`. (1 a 2 h)
- [ ] `PeriodicidadeHelper` e gerador de datas, funções puras, com testes xUnit (casos do desenho). (3 a 5 h)
- [ ] `ManutencaoConfigService` (antecedência, ordem por perfil, horizonte). (1,5 a 2,5 h)
- [ ] `EquipamentoService` (listar, obter, gravar campos permitidos). (2 a 3 h)
- [ ] `PlanoManutencaoService` (CRUD, próxima data, recalcular futuras, gerar planeadas idempotente). (3 a 5 h)
- [ ] `IntervencaoService` (listar, obter, validar, devolver, cancelar) com verificação de perfil. (2 a 4 h)
- [ ] Registo em `MauiProgram.cs`, constantes em `Perfis`, revisão `sc-revisor`, `verificar-segredos.ps1`. (1 a 2 h)
- **Conclusão:** 5 critérios do prompt 03. Depende de outras partes: tabelas criadas pelo utilizador (bloqueio); P2-T04 só arranca com estes serviços.

## P1-T04 Ecrãs (16 a 28 h)

- [ ] Menu Manutenção por perfil e rotas `/Manutencao/...`. (1 a 2 h)
- [ ] Equipamentos: lista, filtros, ficha, histórico. (3 a 5 h)
- [ ] Planos: lista por equipamento, editor com grelha de meses, aviso de recálculo. (3 a 5 h)
- [ ] Planeadas: lista, filtro Departamento, Zona ou Máquina, Plano; gerar e cancelar com relatório. (3 a 5 h)
- [ ] Validação: lista, detalhe, validar e devolver. (3 a 5 h)
- [ ] Configuração: antecedência, ordem por perfil, horizonte. (1,5 a 2,5 h)
- [ ] CSS `manutencao.css` com tokens SigmaCode (D-09), `ModalBase`, `tabela-moderna`, `sal-btn`. (1 a 2 h)
- [ ] Testes manuais em `TESTES_LAND2` e revisão. (1 a 2 h)
- **Conclusão:** 5 critérios do prompt 04. Depende de outras partes: não.

## P1-T05 Dashboard (10 a 18 h)

- [ ] Consultas dos KPIs em serviço próprio (`DashboardManutencaoService`) com testes de reconciliação. (3 a 5 h)
- [ ] Separadores Resumo, Planeamento, Equipamentos, Custos e Alertas; filtros. (4 a 7 h)
- [ ] Alertas (atrasadas, a vencer, por validar, sem plano). (1 a 2 h)
- [ ] Modo Visualizar (30 s) e mosaico de peças em rutura com ponto de extensão da Parte 3. (1 a 2 h)
- [ ] Medição de desempenho com 12 meses de dados de teste. (1 a 2 h)
- **Conclusão:** 4 critérios do prompt 05. Depende de outras partes: peças em rutura só quando existir P3-T05 (opcional).

## P1-T06 Testes e documentação (6 a 12 h)

- [ ] Matriz de rastreabilidade requisito, critério, teste, evidência. (1 a 2 h)
- [ ] Testes de integração em `TESTES_LAND2` (geração, validação, perfis). (2 a 4 h)
- [ ] Documentação: `docs/manutencao-backoffice.md`, manual do supervisor, guia de carga inicial. (2 a 4 h)
- [ ] Revisão final `sc-revisor`, `verificar-segredos.ps1`, registo de lições. (1 a 2 h)
- **Conclusão:** 4 critérios do prompt 06. Depende de outras partes: não.

## Dependências cruzadas (resumo)

| Quem precisa | De quê | Quando |
|---|---|---|
| P2-T02 e P2-T03 | `docs/contratos.md` fechado | fim de P1-T02 |
| P2-T04 | serviços de P1-T03 | fim de P1-T03 |
| P3-T02 | contratos e tabelas de P1-T02 | fim de P1-T02 |
| P1-T05 (opcional) | alerta de rutura de P3-T05 | quando existir |
| P1-T03 | tabelas e roles criados no PHC | antes de arrancar |
