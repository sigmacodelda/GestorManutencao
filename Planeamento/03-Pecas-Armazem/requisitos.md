> **Base = LandeiroMaui (2026-11-06), ver `../00-Comum/base-landeiromaui.md`:**
> - JÁ COBRE: `u_matinterv` (design, qtt, epv, custo, ref) regista o material usado na ficha; é o que a app GestorManutencao liga ao ecrã "Material usado na ficha" (substitui "Pedir peça"). Compras ligadas à ficha só em leitura (`fn.u_fichaint`, `fo`). Deixa de ser necessário `u_manintpec`.
> - NOVO (a base NÃO tem; tabela/campo a criar por pessoa no PHC): pedido de peças ao armazém, stock/rutura (`u_manlocstk`), etiquetas, receção, inventário (`u_maninv`, `u_maninvlin`), saída por leitura e movimentos de armazém (`u_manmovloc`), referências de peças (`u_manpeca`, `u_manpecaequip`, `u_manlead`, `u_manplanopec`). Todos os pedidos da reunião mantêm-se.
> - `u_manconfig` lê-se como `u_configman`; as chaves de rutura são campos NOVOS em `u_configman`.

# Requisitos da Parte 3: Peças e armazém

Fonte: `00-Comum/requisitos-gerais.md` (PEC-01 a PEC-10), PAT 1 (2.ª fase), PAT 3, quadro 1 e reunião de 04/11/2026. Termos: **peça** = artigo de manutenção; **referência interna** = código da Landeiro (`codificacao-pecas.md`); **localização** = posição no armazém virtual; **planeada** = intervenção em estado Planeada (decisão D-05).

Utilizadores: **Técnico** (telemóvel, lê códigos e dá saída), **Armazém/Compras** (backoffice Windows: receção, etiquetas, inventário; quem dá entrada fica por fechar, pendência 7), **Supervisão de manutenção** (consulta e alertas), **Administrador SigmaCode/Landeiro** (carga, configuração). Perfis (roles) concretos fecham em P1-T02 (pendência 10).

Convenção dos critérios: cada critério é verificável (teste automático, consulta SQL de leitura ou passos num ecrã). "Base de testes" = `TESTES_LAND2`.

---

## PEC-01 Referências das peças (PHC ou ambiente separado)

- **Utilizador:** Administrador (cria e edita), todos (consultam).
- **Regra de negócio:** Cada peça tem referência interna (formato de `codificacao-pecas.md`), referência do fabricante, designação original e PT, fabricante, unidade e estado (ativa/obsoleta). Decisão D-04 por tomar: recomendação PHC com família própria (stock, compras e receção nativos); alternativa ambiente separado (base própria na mesma instância). A camada `IPecaService` esconde a escolha. Peças nunca se apagam: ficam obsoletas.
- **Critérios de aceitação:**
  1. Criar peça com dados válidos gera a referência seguinte do conjunto (`M001-001.001`, `.002`) e fica consultável por referência interna, do fabricante e por texto da designação (PT ou original).
  2. Criar peça com `reffabric` já existente para o mesmo fabricante é recusado com mensagem a indicar a peça existente.
  3. Referência inválida (regex) ou 999 peças no conjunto é recusada com erro claro (testes xUnit com a tabela da secção 8 de `codificacao-pecas.md`).
  4. Peça obsoleta não aparece nas listas de seleção (saída, plano) mas aparece no histórico e na pesquisa com filtro "incluir obsoletas".
  5. Com D-04 = PHC: o artigo existe na família própria e o stock vê-se nos ecrãs nativos do PHC; verificável por SELECT na base de testes.
- **Casos limite:** mesmo código em várias máquinas (D-12: um artigo, várias ligações); designação original já em português (`desigen` vazio); código do fabricante com zeros à esquerda; criar artigo no PHC exige campos obrigatórios e pode disparar triggers (levantamento P3-T01); duas pessoas a criar peças no mesmo conjunto ao mesmo tempo.
- **Perguntas abertas:** D-04, D-12; limites e caracteres permitidos na referência do PHC; quem cria os artigos no PHC (a app ou importação nativa do PHC).

## PEC-02 Carga das peças por máquina (manuais, IA e tradução)

- **Utilizador:** Administrador SigmaCode/Landeiro; validação por pessoa da Landeiro com conhecimento técnico.
- **Regra de negócio:** Pipeline em 7 etapas (`carga-pecas-ia.md`): preparar, extrair, normalizar e traduzir, propor, validar humanamente, importar, conferir. O PDF e os dados não vão para serviços externos sem autorização expressa (D-11). Nada entra sem estado `Validada`. Importação idempotente.
- **Critérios de aceitação:**
  1. Um ficheiro de proposta de uma máquina (amostra de 20 páginas) tem pelo menos 98% das linhas corretas em código do fabricante e quantidade, medido contra conferência manual das mesmas páginas.
  2. Reimportar o mesmo ficheiro duas vezes não cria peças nem ligações duplicadas (teste automático e contagem SQL antes e depois).
  3. Linhas `Proposta` ou `Rejeitada` nunca criam peças (teste).
  4. O relatório de conferência mostra, por conjunto, linhas do manual vs criadas e lista as diferenças.
  5. O manifesto não contém caminhos de rede, pessoas nem credenciais e regista se houve IA e o que foi enviado.
  6. Sem autorização D-11 registada, a ferramenta recusa chamar qualquer serviço externo (verificável: não há chamadas de rede no modo local; teste).
- **Casos limite:** PDF sem camada de texto; quantidades "AR"; linhas partidas; conjuntos sem peças; código repetido em vários conjuntos; designação mista EN/PT; PDF com 350 MB (processamento por páginas).
- **Perguntas abertas:** D-11 (autorização); PDF é pesquisável? (P3-T01); quem valida e quanto tempo tem; versão do manual (outras máquinas além de Marel?).

## PEC-03 Peças necessárias por plano e por intervenção (plano → equipamento → peças)

- **Utilizador:** Supervisão (define a lista de peças por plano), Técnico (vê as peças da intervenção).
- **Regra de negócio:** Cada plano de manutenção (Parte 1) tem lista de peças com quantidade (`u_manplanopec`); cada intervenção planeada herda a lista do plano. A lista de peças da máquina/equipamento (ligação peça ↔ equipamento) serve de catálogo para escolher. Em intervenções manuais (quadro 1, "enumera as peças") escolhe-se do catálogo da máquina. Quantidades > 0.
- **Critérios de aceitação:**
  1. Num plano posso adicionar, alterar e remover peças com quantidade; a mesma peça não repete no plano (soma ou recusa).
  2. Ao abrir uma intervenção planeada (backoffice ou telemóvel, contrato `IPecaService`) vejo as peças do plano, com stock disponível e localização principal de cada uma.
  3. Numa intervenção manual só posso escolher peças ligadas ao equipamento (com opção de "outra peça" da máquina inteira, registada como tal).
  4. Alterar a lista do plano não muda intervenções já Concluídas ou Validadas (mantêm o que foi consumido).
  5. Devolve 0 peças sem erro quando o plano não tem lista.
- **Casos limite:** plano para um equipamento sem peças ligadas; peça obsoleta no plano (avisa na edição, mantém no histórico); plano partilhado por vários equipamentos; intervenção offline sem a lista em cache (mostra "lista não disponível offline" e deixa registar peças à mão por leitura).
- **Perguntas abertas:** a lista de peças é por plano ou por equipamento+plano? (Suposição: por plano, que já pertence a um equipamento); registar "peça usada que não estava no plano" é obrigatório justificar?

## PEC-04 Tempo de entrega por artigo e fornecedor

- **Utilizador:** Compras/Administrador (configura), sistema (usa no alerta).
- **Regra de negócio:** Prazo em dias úteis ou de calendário (Suposição: dias de calendário). Pode existir por artigo, por fornecedor e por par artigo+fornecedor. **Prazo efetivo = o mais longo dos aplicáveis** ao fornecedor principal da peça. Sem nenhum definido usa o prazo por defeito (`RUTURA_PRAZO_DEFEITO_DIAS`, Suposição 30) e a peça fica marcada "prazo assumido".
- **Critérios de aceitação:**
  1. Prazo do artigo 20, do fornecedor 35, sem par → efetivo 35; com par 40 → 40; só artigo 20 → 20 (teste xUnit).
  2. Sem prazo algum → defeito e marca "prazo assumido" (teste).
  3. Alterar um prazo reflete-se no alerta na execução seguinte (teste de integração na base de testes).
  4. Prazo ≤ 0 ou não inteiro é recusado.
  5. A listagem de peças mostra prazo efetivo e de onde vem (artigo, fornecedor, par ou defeito).
- **Casos limite:** vários fornecedores para a mesma peça (usa o principal; se não houver principal, o mais longo); fornecedor eliminado no PHC; prazos em semanas na fonte (converter).
- **Perguntas abertas:** o PHC já tem campo de prazo por artigo/fornecedor? (P3-T01); fornecedor principal vem do artigo no PHC?

## PEC-05 Armazém virtual (localizações)

- **Utilizador:** Armazém/Administrador (cria localizações e arruma), todos consultam.
- **Regra de negócio:** Localização com código `<Z><EE>-<P>-<PP>` (ou `RECEC`), descrição, armazém PHC e estado (ativa/inativa). Stock por (peça, localização). Uma peça pode estar em várias localizações. Mover entre localizações é uma transferência (duas linhas de movimento). Stock total por peça = soma das localizações (conciliado com o PHC; divergências assinaladas).
- **Critérios de aceitação:**
  1. Criar localização com código válido e único; inválido ou repetido é recusado.
  2. Desativar uma localização com stock > 0 é recusado.
  3. Transferir 3 unidades de A03-2-05 para B01-1-01 deixa o total da peça igual e atualiza as duas localizações (teste).
  4. Ver, por peça, o stock por localização; ver, por localização, as peças.
  5. Relatório de conciliação: peças em que a soma por localização difere do stock do armazém no PHC (se D-04 = PHC).
- **Casos limite:** stock negativo por saída offline (aceita e assinala); localização `RECEC` com stock antigo; peça sem localização; transferência para a mesma localização (recusa).
- **Perguntas abertas:** um só armazém PHC ou vários; já existe armazém de manutenção no PHC? (P3-T01).

## PEC-06 Criador de etiquetas (peça e localização)

- **Utilizador:** Armazém/Administrador, no backoffice Windows.
- **Regra de negócio:** Reaproveitar o serviço de etiquetas do LandeiroMaui (`Services/Impressao/ImpressaoEtiquetasService`) e o editor de modelos existente (`Services/Etiquetas`: `CatalogoEtiquetas`, `TemplateEtiquetaStore`, `GeradorZplService`; confirmar em P3-T01 como se liga). Dois contextos novos: Peça e Localização, com os campos de `codificacao-pecas.md`, secção 7. Impressão individual e em lote (por conjunto, por máquina, por localização, por receção).
- **Critérios de aceitação:**
  1. Pré-visualização da etiqueta da peça e da localização com dados reais e o desenho editável (campos ligar/desligar, tamanho).
  2. O QR da etiqueta lê-se com a leitura do sistema e resolve para a mesma peça/localização (teste: gerar payload → resolver).
  3. Imprimir 1 e N etiquetas; falha da impressora dá aviso e registo (CrashLogService), nunca silêncio.
  4. Texto longo não corta o código: designação em 2 linhas máx. com reticências.
  5. Mesma etiqueta reimprime-se com o mesmo conteúdo (idempotente).
- **Casos limite:** peça com código do fabricante que já traz código de barras (etiqueta própria opcional); caracteres acentuados na impressora ZPL; lote de 300 etiquetas; impressora offline.
- **Perguntas abertas:** modelo de impressora e tamanho do rolo; as etiquetas imprimem-se no Windows ou também no telemóvel (Suposição: só Windows).

## PEC-07 Receção de peças (entrada de stock)

- **Utilizador:** Armazém ou Compras (pendência 7).
- **Regra de negócio:** Receção identifica a peça (leitura ou pesquisa), quantidade e localização (por defeito `RECEC` ou a por defeito da peça); cria movimento de entrada e atualiza o stock da localização; opcionalmente liga a uma encomenda a fornecedor do PHC. Entrada no stock PHC pelo mecanismo nativo (documento de entrada), a fechar em P3-T02. Peça sem etiqueta própria: oferece imprimir.
- **Critérios de aceitação:**
  1. Receber 10 unidades em `RECEC` cria movimento tipo Entrada e stock da localização = anterior + 10.
  2. A mesma receção (mesmo `opid`) enviada duas vezes só conta uma.
  3. Quantidade ≤ 0, peça desconhecida ou localização inexistente são recusadas.
  4. Receber peça com leitura do código do fabricante identifica a peça; ambiguidade mostra lista.
  5. Se há encomenda ligada, o recebido atualiza o pendente e avisa excesso.
  6. Receção gera etiqueta quando pedido, uma por unidade ou por embalagem (escolha).
- **Casos limite:** receção parcial; peça nova sem referência (criar na hora com perfil de administrador, ou recusar); devolução ao fornecedor (fora de âmbito v1); receção sem rede no telemóvel (Suposição: receção só no backoffice na v1).
- **Perguntas abertas:** quem recebe; ligação à encomenda do PHC é obrigatória; como entra o stock no PHC.

## PEC-08 Inventário por picagem

- **Utilizador:** Armazém (contagem), Supervisão (aprova ajustes).
- **Regra de negócio:** Sessão de inventário (por armazém, zona ou localização). Pica-se a localização e depois cada peça (cada leitura soma 1, ou introduz-se quantidade). No fim compara-se contado vs stock do sistema por (peça, localização); a Supervisão aprova os ajustes, que geram movimentos tipo Ajuste. Sessão fechada não se altera. Contagem funciona offline no telemóvel (fila).
- **Critérios de aceitação:**
  1. Abrir, contar, rever e fechar uma sessão gera movimentos de ajuste só nas diferenças aprovadas.
  2. A mesma leitura reenviada (mesmo `opid`) não duplica a contagem.
  3. Picar peça fora da localização mostra "não esperada aqui" e permite contá-la na localização atual.
  4. Relatório de diferenças por peça e localização, exportável.
  5. Não se podem dar receções/saídas numa localização enquanto a sessão a tem "em contagem" sem aviso (regra: aviso, não bloqueio, para não parar a manutenção; movimentos durante a sessão ficam marcados).
- **Casos limite:** duas pessoas a contar a mesma localização; peça nova encontrada sem referência; contagem parcial (zona); sessão abandonada (expira com aviso, não ajusta).
- **Perguntas abertas:** inventário total periódico ou rotativo; quem aprova.

## PEC-09 Saída de peças por leitura de código de barras ou QR

- **Utilizador:** Técnico, no telemóvel durante a intervenção (câmara, ZXing.Net como no LandeiroMaui); no Windows com leitor tipo teclado.
- **Regra de negócio:** Na intervenção, o técnico lê o código (peça ou do fabricante) e a quantidade, e escolhe a localização de onde tirou (sugere a de maior stock). Cria movimento tipo Saída ligado à ficha de intervenção e baixa o stock. Offline: entra na fila de saída com `opid` único; ao sincronizar nunca é recusada (a peça já saiu); se o stock ficar negativo assinala-se. O consumo aparece nos Movimentos/Materiais da ficha (a confirmar em P3-T01) e no custo de peças (BO-07). **Depende da Parte 2 (P2-T05)** no telemóvel; a leitura no Windows não depende.
- **Critérios de aceitação:**
  1. Ler `PEC:M001-001.001` com quantidade 1 baixa 1 unidade da localização escolhida e liga ao id da ficha.
  2. Ler o código do fabricante resolve a mesma peça (ou lista, se ambíguo).
  3. A mesma saída reenviada pela fila (mesmo `opid`) só conta uma vez.
  4. Peça fora da lista do plano/equipamento: aceita com aviso e marca "extra ao plano".
  5. Quantidade superior ao stock: avisa; online recusa sem confirmação; offline aceita e assinala.
  6. Câmara sem permissão ou código ilegível: mensagem clara e opção de pesquisar à mão; nunca falha em silêncio.
  7. Desfazer uma saída (até a ficha ser Validada) repõe o stock com movimento de estorno.
- **Casos limite:** vários códigos seguidos (leitura contínua); duas leituras da mesma peça em 1 s (anti-duplo-disparo); ficha já Validada; peça obsoleta; código de localização lido por engano (avisa).
- **Perguntas abertas:** a saída cria documento PHC (movimento interno) ou só movimento na tabela própria; modelos de telemóvel e leitura de câmara (pendência 5).

## PEC-10 Alerta de rutura de stock

- **Utilizador:** Supervisão/Compras (recebem), sistema (calcula).
- **Regra de negócio** (fórmula explícita; `hoje` = data local do servidor, sem hora):
  - Parâmetros: margem `M` (`RUTURA_MARGEM_DIAS`, Suposição 7) e prazo por defeito (`RUTURA_PRAZO_DEFEITO_DIAS`, Suposição 30).
  - Prazo efetivo `L(p)` conforme PEC-04. **Janela** `J(p) = L(p) + M` dias; fim da janela = `hoje + J(p)`.
  - **Necessidade** `N(p)` = soma das quantidades da peça `p` nas intervenções **Planeadas** (plano ativo, equipamento não obsoleto, não canceladas) com data planeada ≤ fim da janela, **incluindo as atrasadas** (para estas a data efetiva é `hoje`).
  - **Stock** `S(p)` = soma do stock por localização ativa. **Reservas** `R(p)` = soma, nas intervenções **EmCurso**, de max(0, previsto − já saído) por peça. **Disponível** `D(p) = S(p) − R(p)`.
  - **Rutura** se `D(p) < N(p)`. **Falta** `F(p) = N(p) − D(p)`.
  - **Data da falha:** ordenar as planeadas da janela por data efetiva e acumular as quantidades; é a data da primeira em que o acumulado passa `D(p)` (se `D(p) ≤ 0`, a primeira).
  - **Data limite de compra** `DLC(p) = dataFalha − L(p)`.
  - **Urgência:** **Crítico** se `DLC < hoje` (já não chega a tempo); **Aviso** se `hoje ≤ DLC ≤ hoje + M`.
  - Opcional (`RUTURA_CONTAR_ENCOMENDAS`, por defeito 0): `D(p)` soma as quantidades em encomendas a fornecedor ainda por receber (só se o levantamento P3-T01 mostrar onde estão no PHC).
- **Critérios de aceitação (casos de `hoje` = 2026-12-01, `M` = 7, `L` = 30, janela até 2027-01-07):**
  1. `S`=2, `R`=0, planeadas 2027-01-05 (qtt 1) e 2027-01-06 (qtt 2): `N`=3 > `D`=2; dataFalha = 2027-01-06; `DLC` = 2026-12-07 → **Aviso**.
  2. `S`=0, planeada 2026-12-20 (qtt 1): dataFalha 2026-12-20; `DLC` = 2026-11-20 < hoje → **Crítico**.
  3. Planeada em 2027-01-10 (fora da janela): `N`=0 → sem alerta.
  4. `S`=2, `R`=2 (uma EmCurso) e planeada qtt 1: `D`=0 < 1 → rutura, `F`=1.
  5. Planeada atrasada (2026-11-20, qtt 1) com `S`=0: data efetiva `hoje`; `DLC` = 2026-11-01 → **Crítico**.
  6. Peça sem prazo definido: `L` = 30, alerta marcado "prazo assumido".
  7. Planeada cancelada ou equipamento obsoleto: não conta.
  8. O painel lista peça, necessidade, disponível, falta, prazo, `DLC`, primeira planeada em falta (equipamento e data) e urgência; ordena por `DLC`.
  9. Cálculo para 4.000 peças e 500 planeadas em menos de 3 s na base de testes (Suposição de alvo; medir).
- **Casos limite:** `D(p)` negativo (stock negativo ou reservas acima do stock); quantidades decimais; peça com várias planeadas na mesma data (soma); mudança de `M` com alertas ativos; planeadas ainda não geradas (Parte 1 gera à frente por janela de antecedência: o alerta só vê o que existe como ficha Planeada; limitação a documentar).
- **Perguntas abertas:** canal do alerta (painel e contador no menu; e-mail diário fica fora da v1, Suposição); encomendas em trânsito entram no cálculo?; o alerta tem estado "já encomendado" (silenciar até à data prevista)?

---

## Requisitos não funcionais da Parte 3

1. **Offline** (APP-02): consulta de peças do plano, leitura de códigos, saídas e contagens funcionam sem rede com fila de saída e `opid` único (anti-duplicação).
2. **Desempenho:** resolver uma leitura em menos de 500 ms com 4.000 peças (Suposição, medir); listas paginadas ou virtualizadas (nunca carregar 4.000 linhas de uma vez no telemóvel).
3. **Segurança e RGPD:** sem dados pessoais nos movimentos além das iniciais do utilizador; PDF e dados do fabricante não saem sem autorização (D-11).
4. **Rastreabilidade:** todo o movimento de stock tem tipo, utilizador (iniciais), data, `opid` e origem (receção, ficha, inventário).
5. **SQL:** `WITH (NOLOCK)` nas leituras, parâmetros nomeados, compatível com nível 100 [CONFIRMAR].
6. **Erros:** `CrashLogService.Registar` e aviso ao operador em todo o fluxo que grava ou lê código.

## Fora de âmbito (Suposição)

Encomendas automáticas a fornecedores; devoluções; faturação; lotes e validades de peças; stock mínimo e ponto de encomenda por peça (só rutura vs planeadas); impressão de etiquetas a partir do telemóvel; foto da peça no catálogo (pode vir depois).
