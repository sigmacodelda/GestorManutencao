> **Base = LandeiroMaui (2026-11-06), ver `../00-Comum/base-landeiromaui.md`:**
> - JÁ COBRE: `u_matinterv` (design, qtt, epv, custo, ref) regista o material usado na ficha; é o que a app GestorManutencao liga ao ecrã "Material usado na ficha" (substitui "Pedir peça"). Compras ligadas à ficha só em leitura (`fn.u_fichaint`, `fo`). Deixa de ser necessário `u_manintpec`.
> - NOVO (a base NÃO tem; tabela/campo a criar por pessoa no PHC): pedido de peças ao armazém, stock/rutura (`u_manlocstk`), etiquetas, receção, inventário (`u_maninv`, `u_maninvlin`), saída por leitura e movimentos de armazém (`u_manmovloc`), referências de peças (`u_manpeca`, `u_manpecaequip`, `u_manlead`, `u_manplanopec`). Todos os pedidos da reunião mantêm-se.
> - `u_manconfig` lê-se como `u_configman`; as chaves de rutura são campos NOVOS em `u_configman`.

# Tarefas da Parte 3: Peças e armazém

Fonte: `00-Comum/00-global.md` (secção 3). Dentro da parte as tasks são sequenciais; cada uma só arranca com a anterior CONCLUÍDA e com o gate confirmado pelo utilizador. Esforço em horas de trabalho de planeamento e desenvolvimento (não tokens), com intervalo mínimo a máximo: **Suposição** de planeamento, a rever depois de P3-T02 (e depois de a Landeiro dizer se cria as tabelas, pendência 4).

| Task | Tipo | Prompt | Depende de | Esforço (h) |
|---|---|---|---|---|
| P3-T01 | levantamento | `prompts/01-levantamento.md` | acesso de leitura à base de testes (pendência 2); PDF disponível (pendência 8) e autorização D-11 para o PDF | 6 a 10 |
| P3-T02 | desenho | `prompts/02-desenho.md` | P3-T01, **P1-T02** (contratos), pendência 7, D-12 | 10 a 16 |
| P3-T03 | desenvolvimento | `prompts/03-desenvolvimento-referencias.md` | P3-T02, **D-04 decidida**, tabelas criadas no PHC (R-02), D-11 se houver IA | 24 a 40 |
| P3-T04 | desenvolvimento | `prompts/04-desenvolvimento-armazem.md` | P3-T03 | 28 a 44 |
| P3-T05 | desenvolvimento | `prompts/05-desenvolvimento-saida-alerta.md` | P3-T04, **P2-T05** (leitura no telemóvel) | 28 a 44 |
| P3-T06 | testes-documentacao | `prompts/06-testes-documentacao.md` | P3-T05 | 12 a 20 |
| Total | | | | 108 a 174 |

Dependências entre partes: P3-T02 precisa dos contratos de P1-T02 (`IEquipamentoService`, `IPlanoManutencaoService`, `IIntervencaoService`); P3-T03 precisa de D-04; P3-T05 precisa de P2-T05 para a leitura na câmara do telemóvel (a parte Windows do P3-T05 não precisa); P3-T05 devolve a P1-T05 o feed do alerta de rutura para o dashboard (BO-07) e a P2 o contrato de peças da intervenção (APP-08).

## P3-T01 Levantamento: peças, stock PHC, manual Marel, fornecedores (6 a 10 h)

Só leitura. Resultado: `docs/pecas-armazem/levantamento.md`.

- [ ] Levantamento inicial numa só mensagem: acessos (base de testes), PDF Marel (pasta, tamanho), autorização D-11, decisões D-04/D-12 a confirmar, quem recebe as peças.
- [ ] `SELECT DB_NAME()` e versão/nível de compatibilidade (só leitura).
- [ ] Artigos no PHC: tabela, campos obrigatórios, chaves, comprimento e caracteres da referência, famílias (lista e uso), triggers (incluindo encriptados), como se cria um artigo (campos mínimos, tabelas relacionadas).
- [ ] Stock: tabela de stock por armazém, armazéns existentes (há um de manutenção?), como se movimenta (documentos de entrada e de saída; receção de encomenda a fornecedor existente).
- [ ] Fornecedores: tabela, fornecedor principal do artigo, campo de prazo de entrega se existir.
- [ ] Referências de peças já existentes (a Landeiro disse que não existem): contagem de artigos candidatos (famílias, referências com `M…`) e colisão do prefixo.
- [ ] Ficha de Intervenção: peças usadas já existem em `u_matinterv` (hipótese de `circuito-phc-atual.md` secção 3 CONFIRMADA); confirmar por SELECT os campos e como se liga a `st` e a `fn`/`fo`.
- [ ] Tabelas `u_` existentes (nomes, para evitar colisões) e se há sinal de que se podem criar (pendência 4).
- [ ] Amostra do PDF (se autorizado): número de páginas, camada de texto, estrutura de um conjunto, colunas "Item no.", "Marel code", quantidade, onde há códigos de barras.
- [ ] Leitura existente no LandeiroMaui: como se lê código no Windows (leitor tipo teclado) e se há leitura por câmara; como se liga o editor de etiquetas ao `ImpressaoEtiquetasService`.
- [ ] Recomendação fundamentada para D-04 e lista de perguntas para P3-T02.

Critérios de conclusão (5): estrutura PHC de artigos, stock e fornecedores documentada com evidência; referências e prefixo livre verificados e processo de criação de artigo descrito; como o stock se movimenta e onde a ficha guarda as peças; estrutura do PDF documentada (ou declarado inacessível com motivo); prazos e D-04 com recomendação e perguntas abertas.

## P3-T02 Desenho: referências, armazém virtual, etiquetas, fluxos, alerta (10 a 16 h)

Resultado: `docs/pecas-armazem/desenho.md` (e mockup `docs/pecas-armazem/mockups/`).

- [ ] Fechar a codificação (`codificacao-pecas.md`) com D-12 e os limites reais do PHC.
- [ ] Modelo de dados final em formato `<phc-tables>` por tabela (campos, tipos, nulos, defeitos, regras, verificação só leitura): `u_manmaq`, `u_manconj`, `u_manpeca`, `u_manpecaequip`, `u_manplanopec`, `u_manloc`, `u_manlocstk`, `u_manlead`, `u_manmovloc`, `u_maninv`, `u_maninvlin`. Se D-04 = ambiente separado, a variante.
- [ ] Como se cria o artigo no PHC (escolha fundamentada: importação nativa do PHC, INSERT replicando os campos obrigatórios na base de testes, ou outra) e como se movimenta o stock PHC.
- [ ] Contratos `IPecaService` e `IArmazemVirtualService` (métodos, DTOs, erros) alinhados com P1-T02; contrato com a Parte 2 (peças da intervenção, saída, inventário).
- [ ] Regras do alerta (fórmula de `requisitos.md` PEC-10), parâmetros em `u_configman` (campos NOVOS), casos de teste.
- [ ] Fluxos de receção, saída, inventário, etiquetas (conteúdo do QR e da localização).
- [ ] Pipeline da carga por IA (extrair → propor → validar → importar) e regra de privacidade D-11.
- [ ] Mockups clicáveis (partir do mockup do planeamento).
- [ ] Plano de testes por requisito e lista de scripts de verificação.

Critérios (5): modelo de dados com todas as tabelas em `<phc-tables>`; contratos e DTOs fixados e coerentes com P1-T02; regras do alerta, codificação e fluxos documentados com casos de teste; pipeline de carga com privacidade e formato de ficheiro; mockups e plano de testes.

## P3-T03 Desenvolvimento: referências, carga das peças (IA), tempos de entrega (24 a 40 h)

Pré-requisitos: tabelas criadas no PHC (BLOQUEIO se faltarem) e D-04 decidida.

- [ ] Scripts de verificação só leitura `01_verificar_<tabela>.sql` (um por tabela da parte, ou um único) e execução na base de testes.
- [ ] Helper `GeradorReferenciaPeca` e `ResolvedorLeitura` (puros) com testes xUnit (tabela de `codificacao-pecas.md` secção 8).
- [ ] `IPecaService` / `PecaService`: listar por máquina/conjunto, pesquisar, obter, criar (com geração de referência), tornar obsoleta, peças por plano e por equipamento.
- [ ] Prazos: `PrazoEntrega` (cálculo do mais longo, teste xUnit) e serviço de leitura/gravação de `u_manlead`.
- [ ] Ferramenta de extração e ficheiro de proposta (só local; IA só se D-11 permitir) e manifesto.
- [ ] Importação idempotente (CSV `Validada`), relatório de conferência.
- [ ] Ecrãs backoffice: peças por máquina (árvore e pesquisa), ficha da peça, edição das peças do plano, prazos, validação da carga.
- [ ] Menu e perfil (`Perfis`), `docs/DEPENDENCIES.md`, verificação de segredos.

Critérios (5): referências geradas e validadas pelos testes; importação idempotente com relatório de conferência; prazo efetivo correto nos testes; ecrãs de peças, plano e validação a funcionar na base de testes; sem chamadas externas sem D-11 e sem segredos.

## P3-T04 Desenvolvimento: armazém virtual, etiquetas, receção, inventário (28 a 44 h)

- [ ] `IArmazemVirtualService` / `ArmazemVirtualService`: localizações, stock por localização, movimentos (entrada, transferência, ajuste), conciliação com o PHC.
- [ ] Ecrãs: localizações, stock por peça e por localização, receção, inventário (sessões, picagem com leitor tipo teclado, revisão e ajustes).
- [ ] Etiquetas: contextos Peça e Localização no catálogo do editor, métodos de impressão no `ImpressaoEtiquetasService`, impressão em lote, pré-visualização.
- [ ] `opid` único e idempotência de movimentos; testes xUnit das regras (transferência, ajuste, inventário).
- [ ] Testes na base de testes (SELECT antes e depois), medições de desempenho.

Critérios (5): receção e transferência atualizam o stock sem duplicar; inventário gera ajustes só nas diferenças aprovadas; etiquetas da peça e da localização imprimem e o QR resolve para o objeto certo; conciliação com o PHC mostra divergências; testes xUnit a passar e sem avisos de compilação.

## P3-T05 Desenvolvimento: saída por leitura, peças do plano, alerta de rutura (28 a 44 h)

- [ ] Saída de peças (serviço): movimento tipo Saída ligado à ficha, estorno, offline sem recusa (negativo assinalado), anti-duplicação.
- [ ] Peças da intervenção (plano → equipamento → peças): contrato para a Parte 2 (APP-08) e para o backoffice.
- [ ] Ecrã Windows de saída com leitor tipo teclado (não depende da Parte 2).
- [ ] Ecrã do telemóvel: leitura por câmara (ZXing.Net), inventário por picagem, peças da intervenção (**só depois de P2-T05**; se não estiver fechada, entrega o serviço e a parte Windows e marca a parte do telemóvel BLOQUEADA).
- [ ] `CalculadorRutura` (puro, testes xUnit com os 9 casos de PEC-10), serviço e painel de alertas, contador no menu e feed para o dashboard (P1-T05).
- [ ] Parâmetros (`u_configman`, campos NOVOS): margem, prazo por defeito, contar encomendas.
- [ ] Medição do cálculo (4.000 peças, 500 planeadas).

Critérios (5): saída por leitura baixa o stock e liga à ficha, sem duplicar; peças do plano aparecem na intervenção; alerta de rutura igual aos casos de teste; leitura no telemóvel (câmara) a funcionar ou parte declarada BLOQUEADA com motivo; desempenho dentro dos alvos ou desvio explicado.

## P3-T06 Testes e documentação (12 a 20 h)

- [ ] Matriz de rastreabilidade PEC-01..PEC-10 → teste → resultado.
- [ ] Testes de integração na base de testes (receção → saída → alerta), de idempotência (reenvio) e de offline.
- [ ] Verificação de desempenho e de segurança (`verificar-segredos.ps1`, sem dados do fabricante fora de D-11).
- [ ] Documentação de utilizador (receção, saída, inventário, etiquetas, validação da carga) e técnica (modelo de dados, contratos, regras, runbook da carga).
- [ ] Registo de decisões (ADR), `docs/DEPENDENCIES.md`, lições (`MELHORIA:`), atualização do `00-global.md`.

Critérios (4): todos os requisitos com teste e evidência; documentação de utilizador e técnica entregues; sem pendências bloqueantes; relatório de testes com falhas e riscos como são.
