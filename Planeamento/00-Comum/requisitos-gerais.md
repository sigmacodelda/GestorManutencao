# Requisitos gerais e rastreabilidade

Fontes: PAT 1 (app móvel, 2 fases), PAT 2 (circuito de manutenção na plataforma .NET MAUI da Landeiro), PAT 3 (peças e armazém), 3 fotografias do quadro e a reunião de 04/11/2026. As imagens estão em `fontes/`.

## 1. Objetivo

Passar o circuito de manutenção que hoje vive em papel, Word e PHC (ecrãs Infraestruturas e Ficha de Intervenção) para a plataforma .NET MAUI da Landeiro: equipamentos, periodicidade e planos, registo de intervenções no telemóvel (mesmo sem rede), validação no backoffice, dashboard e alertas, e depois gestão de peças e armazém.

## 2. As 3 partes

| Parte | Pasta | Âmbito | Fase dos PATs |
|---|---|---|---|
| 1 | `01-Backoffice-PHC` | Dados no PHC, equipamentos, planos e periodicidade, geração de planeadas, configurações, validação, dashboard e alertas | PAT 2 (menos o telemóvel) e backoffice do PAT 1 |
| 2 | `02-App-Intervencoes` | App móvel: login, offline, alerta, lista, criar intervenção, avançar ordem | PAT 1, 1.ª fase |
| 3 | `03-Pecas-Armazem` | Referências, peças por plano, armazém virtual, etiquetas, receção, inventário, saída por leitura, alerta de rutura | PAT 1, 2.ª fase, e PAT 3 |

## 3. Requisitos funcionais

### Parte 1: Backoffice e PHC (BO)

| ID | Requisito | Origem |
|---|---|---|
| BO-01 | Registo de equipamentos (infraestruturas): criar, editar, tornar obsoleto, localização, zona, fornecedor, e-mail de alerta. Reaproveitar o que já existe no PHC | PAT 2; reunião |
| BO-02 | Periodicidade e plano de manutenção por equipamento (diária, semanal, quinzenal, mensal, trimestral, semestral, anual, sempre que necessário), interna ou externa, meses planeados. Carga inicial a partir do Word P.05-03 | PAT 2; quadro 2; reunião |
| BO-03 | Geração das intervenções planeadas a partir do plano (próxima data, atrasos, janela de antecedência) | PAT 1 |
| BO-04 | Configuração da antecedência do alerta de intervenções planeadas (global e por plano ou equipamento) | PAT 1 |
| BO-05 | Configuração que define se o utilizador pode avançar intervenções fora de ordem (permissão por perfil) | PAT 1; quadro 3 |
| BO-06 | Validação de intervenções no backoffice: supervisão revê e fecha a ficha; só então fica Fechada | PAT 2; reunião |
| BO-07 | Dashboard de análises e alertas de manutenção: planeadas, realizadas, atrasadas, preventivas vs curativas, por equipamento e zona, custo (mão de obra e peças) | PAT 2 |
| BO-08 | Especificação das tabelas e campos a criar no PHC e dos contratos de serviço que a app e a Parte 3 consomem | Quadro 2 |
| BO-09 | Carga inicial: equipamentos existentes, plano do Word, fichas históricas (só leitura, sem migrar papel) | Reunião |
| BO-10 | Manutenção externa: fornecedor e documento externo / folha de serviço na ficha | Reunião |

### Parte 2: App móvel (APP)

| ID | Requisito | Origem |
|---|---|---|
| APP-01 | Login por PIN ou por utilizador e password, com base no login do LandeiroMaui; a sessão mantém-se o máximo de tempo possível | Quadro 3; instrução do utilizador |
| APP-02 | **Obrigatório trabalhar offline**: cache local, fila de gravações e sincronização | Quadro 3 |
| APP-03 | Alerta de intervenções planeadas, com a antecedência definida no backoffice (funciona offline) | PAT 1 |
| APP-04 | Listagem de intervenções planeadas (e pendentes), com botão "+" para criar | PAT 1; quadro 3 |
| APP-05 | Ecrã simples e prático para criar a intervenção: equipamento, preventiva ou curativa, tempos, técnico, descrição, foto | PAT 1; quadro 3 |
| APP-06 | Permissão de avançar intervenções conforme a configuração do backoffice | PAT 1; quadro 3 |
| APP-07 | Sincronização sem duplicados e com resolução de conflitos | Quadro 3 (offline) |
| APP-08 | Registo das peças usadas na intervenção (liga à Parte 3) | PAT 1, 2.ª fase |

### Parte 3: Peças e armazém (PEC)

| ID | Requisito | Origem |
|---|---|---|
| PEC-01 | Referências das peças no PHC: avaliar criar no PHC (família própria, referência interna e do fabricante, designação EN e PT) ou ambiente separado | PAT 3; reunião |
| PEC-02 | Carga das peças por máquina a partir dos manuais do fabricante (PDF Marel) com apoio de IA e tradução | Reunião |
| PEC-03 | Listagem de peças necessárias por intervenção planeada (plano → equipamento → peças) | PAT 1, 2.ª fase; quadro 1 |
| PEC-04 | Tempo de entrega da peça por artigo e fornecedor (se forem diferentes assume o mais longo), configurável | PAT 3 |
| PEC-05 | Armazém virtual: localização das peças | PAT 3 |
| PEC-06 | Criador de etiquetas das peças e do local de armazenamento | PAT 3 |
| PEC-07 | Receção de peças (entrada de stock) | PAT 3; reunião |
| PEC-08 | Inventário por picagem | PAT 3 |
| PEC-09 | Leitura de código de barras ou QR para dar saída das peças do armazém | PAT 1, 2.ª fase |
| PEC-10 | Alerta de rutura de stock: intervenções planeadas vs stock vs tempo de entrega | PAT 3; reunião |

## 4. Requisitos não funcionais

1. **Offline** em tudo o que o operador faz no telemóvel (APP-02).
2. **Segurança:** sem credenciais em código, logs ou prompts; connection string só no `appsettings`; PIN guardado só como hash no armazenamento seguro do dispositivo; acesso por perfil (roles `Perfis`).
3. **Dados pessoais (RGPD):** só o necessário (iniciais e nome do técnico); fotos sem pessoas quando possível.
4. **Desempenho:** lista de intervenções abre em menos de 2 s com cache; SQL com `WITH (NOLOCK)` nas leituras (regra do LandeiroMaui).
5. **Erros nunca em silêncio:** `CrashLogService.Registar` e aviso ao operador (regra do LandeiroMaui).
6. **Compatibilidade SQL:** nível 100 [CONFIRMAR para o Landeiro]; sem STRING_SPLIT, OPENJSON, TVP.
7. **Sem tabelas nem campos criados pelo Claude Code:** especificam-se e a Landeiro ou a SigmaCode cria-os no PHC.
8. **Marca:** documentos e mockups com a paleta SigmaCode (por defeito). A app segue o estilo do LandeiroMaui só se o pedido disser `Cliente: Landeiro, usar cores do cliente` (ver decisão D-09).

## 5. Fora de âmbito (Suposição, a confirmar)

Viaturas (Via Verde, Kms, matrícula) apesar de estarem no ecrã Infraestruturas; faturação de manutenções externas; encomendas automáticas a fornecedores; migração das fichas em papel antigas; app para iOS.

## 6. Rastreabilidade PAT → requisito → parte

| Item dos PATs | Requisito | Parte |
|---|---|---|
| Alerta para intervenções planeadas | APP-03, BO-03 | 2 e 1 |
| Definição no backoffice da antecedência do alerta | BO-04 | 1 |
| Layout simples e prático para criação de intervenção | APP-05 | 2 |
| Listagem de intervenções planeadas | APP-04 | 2 |
| Configuração para permissão de avanço | BO-05, APP-06 | 1 e 2 |
| Listagem de peças necessárias para a intervenção | PEC-03 | 3 |
| Leitura por código de barras/QR para saída de peças | PEC-09 | 3 |
| Registos de equipamentos | BO-01 | 1 |
| Periodicidade de manutenções | BO-02 | 1 |
| Registos de intervenções (dispositivo móvel) | APP-05, APP-07 | 2 |
| Validação de intervenções no Backoffice | BO-06 | 1 |
| Dashboard análises e alerta de manutenções | BO-07 | 1 |
| Referências no PHC para as peças ou ambiente separado | PEC-01 | 3 |
| Tempo de entrega por artigo e fornecedor | PEC-04 | 3 |
| Controlo de armazém virtual | PEC-05 | 3 |
| Criador de etiquetas | PEC-06 | 3 |
| Receção das peças | PEC-07 | 3 |
| Inventário por picagem | PEC-08 | 3 |
| Alerta de quebra de stock | PEC-10 | 3 |
| Quadro 1: intervenção por plano ou manual, filtro Departamento → Zona/Máquina → Plano | BO-03, APP-05, PEC-03 | 1, 2, 3 |
| Quadro 2: tabelas e campos; circuito PHC (Rui); Word (Luís) | BO-08, BO-09, BO-02 | 1 |
| Quadro 3: login, offline, ecrãs, avançar ordem | APP-01 a APP-06 | 2 e 1 |
