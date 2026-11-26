# Peças e armazém de manutenção (circuito v2)

Estado: código feito e testado com repositório em memória; o SQL foi verificado na base de testes TESTES_LAND2 em 2026-11-20 (ver "Verificado na base de testes"). Falta verificação em ecrã e em aparelho (ver "O que não está feito"). Decisão: `docs/adr/0003-circuito-pecas-conferencia.md` (substitui o 0002 na saída e na entrada). Verificação de pré-requisitos: `docs/sql/pecas-verificacao.sql` (só leitura). Especificação e plano: `docs/superpowers/specs/2026-11-20-circuito-pecas-v2-design.md` e `docs/superpowers/plans/2026-11-20-circuito-pecas-v2.md`.

## Circuito

1. **Entrada** (escolha do artigo e quantidade): só na LandeiroMaui. Esta app não tem entrada.
2. **Arrumação** (esta app, `/pecas/arrumar`): picar a peça e a etiqueta do lugar, por qualquer ordem; cria um dossier 66 (origem em `bi.lobs`, destino em `bi.lobs2`, na linha). A quantidade vem proposta (stock do armazém 104 menos o já localizado) e soma ao lugar se já existir.
3. **Ficha de intervenção** (esta app):
   - **A API nunca escreve em `u_pecaloc`**: o saldo por lugar é mantido pelo trigger `sc_bi_pecaloc` a partir das linhas `bi` (63 com `emconf = 0` desce, 64 soma ao buffer, 66 move da origem para o destino). A API só lê a vista `v_sc_pecaloc_disponivel` (saldo, reservado e disponível por lugar). Arrumar e mover só dão `Ok` se a vista **e** o trigger existem (`OBJECT_ID('dbo.sc_bi_pecaloc','TR')`): sem o trigger o dossier 66 não teria efeito.
   - Iniciar (Planeada para Em curso): abre o dossier 63 da ficha em conferência (`bo.emconf = 1`).
   - Juntar material com peça: cria a linha `bi` em conferência (`bi.emconf = 1`) com `bi.lobs = <stamp>|<lugar>` e `bi.lobs2 = <lugar>`. O saldo do lugar não desce: a quantidade fica **reservada** (disponível = saldo menos reservado). O stock real do PHC não se move.
   - Validar (supervisor, Concluída para Validada): o dossier e as linhas saem de conferência; o stock do PHC e o saldo do lugar descem (trigger), uma só vez.
   - Devolver (Concluída para Em curso): mantém a conferência.
   - Cancelar (supervisor): apaga o dossier; não há lugares a repor porque o saldo nunca desceu.
   - Editar ou apagar material: altera ou apaga a linha em conferência; a reserva acompanha.

O stock disponível mostrado é o stock do armazém 104 menos o que está em conferência noutras fichas. Se não chega, avisa e **não bloqueia** (regra da LandeiroMaui).

## Perfis

- Arrumação e pesquisa de peças: operador e supervisor (técnico com número); Qualidade não tem acesso.
- Juntar, editar e apagar material com peça: operador e supervisor, com a ficha editável.
- Validar e cancelar a ficha: só supervisor.

## API

Todas exigem sessão. Formato dos erros: 400 `{erro:"pedido_invalido", mensagem}`, 404, 409 `{erro}`, 503 se o PHC está indisponível (o texto SQL só chega ao cliente se for erro de negócio).

Peças e lugares (técnico ou supervisor):
- `GET /pecas?texto=`: pesquisa por até 4 palavras (máximo 60 resultados, `HaMais` se houver mais; `CampoExiste = false` se falta `stobs.u_manut`). Cada peça traz `Stock`, `EmConferencia` e `Disponivel`.
- `GET /pecas/codigo/{codigo}`: código de barras ou referência; 404 se não existe.
- `GET /pecas/{ref}/localizacoes`: lugares da peça (por quantidade disponível decrescente; `Qtt` = disponível e `Reservado` = o que está em conferência), `Stock`, `Localizado` (soma do saldo físico de cada lugar, nunca acima dele mesmo que o reservado o exceda) e `PorArrumar` (nunca negativo). **Buffer** = `u_configman.localentrada` (o da LandeiroMaui, onde as entradas esperam lugar): a linha do buffer em `u_pecaloc` não conta como lugar nem como localizado; `PorArrumar` é o que está no buffer. Sem o campo ou vazio, não há buffer e `PorArrumar` = stock menos localizado.
- Só se arruma o que está no buffer: `POST /pecas/localizacoes` devolve 400 se a quantidade excede `PorArrumar` (ou este é 0) ou se o destino é o próprio buffer. O trigger tira a quantidade da linha do buffer (apagada se ficar a 0).
- `POST /pecas/localizacoes` com `{ref, local, qtt}`: arruma. `local` é o texto da etiqueta (`LOC-A-03-2-04`) ou do lugar (`A-03-2-04`), sempre com posição. Quantidade positiva, até 3 casas, abaixo de 1.000.000. 400 se inválido, 404 se a peça não existe, 409 se a vista ou o trigger faltam ou o lugar está ocupado. Devolve os lugares atualizados.
- `POST /pecas/{ref}/mover` com `{origem, destino, qtt}`: rearrumar. Só se move o disponível. **400 se a origem está com a quantidade "não indicada" (0)**: "Indique a quantidade do lugar de origem antes de mover.", porque o trigger descarta movimentos de quantidade 0 e o dossier ficaria sem efeito.

Material da ficha:
- `GET/POST /intervencoes/{id}/materiais`, `PUT/DELETE /intervencoes/{id}/materiais/{materialId}`.
- `POST /intervencoes/{id}/materiais/{materialId}/saida`: repete a saída "por dar". Corpo opcional `{ref, local}`.
- O pedido de material leva `Ref` e `Local` (texto do lugar ou `LOC-…`). **No PUT, `Ref` omitido (ou `null`) mantém a peça, a saída e o lugar** (aparelhos antigos, ecrãs que não conhecem a peça); só `Ref` vazio explícito (`"ref": ""`) desliga a peça e anula a saída (liberta a reserva do lugar). A UI só envia `ref` quando a pessoa o conhece ou o alterou. Se a reposição da saída falhar depois de a ficha recusar a edição, o PUT responde 409 de stock desfasado, como o DELETE. A resposta traz `Ref`, `Local` (lugar de onde saiu), `SaidaDada` e `AvisoStock`.
- Escolha do lugar sem `Local` no pedido: o lugar anterior da saída (na edição), senão o único lugar da peça com disponível (`v_sc_pecaloc_disponivel`); se todo o stock está reservado, o único lugar com reservado. Com vários lugares responde **409 `{erro:"escolha_o_lugar", lugares:[…]}`** e nada se grava: o ecrã mostra a lista ou pede para picar a etiqueta do lugar. Sem nenhum lugar com stock (ou sem a vista), grava e avisa "Peça sem localização" (`avisoStock`).
- Lugar escolhido sem disponível suficiente (incluindo tudo reservado): o material grava, a peça sai com o lugar na linha (para o trigger o poder descer ao validar) e o `avisoStock` diz quanto há disponível ("O lugar … só tem N disponível"). Se não for possível consultar o lugar, grava na mesma e avisa. A saída que falha (dossier ocupado) fica por dar (repetível).
- `POST /intervencoes/{id}/iniciar`: depois de iniciar abre a conferência. Se falhar só se regista; o primeiro material com peça abre-a de novo (idempotente).
- `POST /validacao/{id}` (supervisor): valida a ficha e depois confirma a conferência. Se a confirmação falhar a ficha fica validada, a resposta é 200 com `avisoStock = "Conferência por confirmar"` e uma nova chamada repete só a confirmação. **Antes de validar**, se há materiais com peça ligada e sem saída dada ("por dar"), responde **409 `{erro:"pecas_por_dar", mensagem}`** ("Há N peça(s) por dar saída; repita a saída ou desligue a peça antes de validar.") e não faz nada: depois de validada já não se repete a saída. O supervisor pode repetir a saída ou desligar a peça numa ficha Concluída (`PodeEditar` do supervisor vale para qualquer ficha que não seja Validada ou Cancelada).
- `POST /intervencoes/{id}/cancelar` (supervisor): cancela a ficha, apaga a conferência e limpa a ligação à peça de **todos** os materiais (também os "por dar", que não têm linha no dossier); os lugares não precisam de reposição. Falhas ficam em `avisoStock` ("Conferência por cancelar") e repetir o cancelar repete só a limpeza.
- `GET /intervencoes/{id}/detalhe`: os materiais vêm com `Ref`, `SaidaDada` e `Local` (também para o supervisor, que não usa `GET /materiais`). Ao supervisor, numa ficha **Validada** com o dossier ainda em `emconf = 1` ou **Cancelada** com dossier (ou peças ainda ligadas), traz `avisoStock` ("Conferência por confirmar" / "Conferência por cancelar"); o ecrã oferece então "Repetir confirmação" / "Repetir cancelamento" mesmo depois de reabrir a ficha. A consulta é só leitura (`IRepositorioPecas.ConferenciaPendenteAsync`).
- `POST /validacao/{id}/devolver`: não mexe na conferência.

## Tabelas e campos do PHC usados

| Objeto | Uso |
|---|---|
| `st`, `stobs.u_manut` | peça = artigo com `u_manut = 1` e não inativo |
| `sa` (armazém 104) | stock real da peça; `sz` 104 = armazém de manutenção |
| `ts` 63 | tipo do dossier de saída (um por ficha). O 64 (entrada) é da LandeiroMaui |
| `bo`, `bo2`, `bo3` | dossier da ficha; `bo.bostamp` = id da ficha; `bo.emconf` |
| `bi`, `bi2` | linha por material; `bi.emconf`; `bi.lobs` liga ao material e guarda o lugar |
| `u_matinterv` | material da ficha; `ref` = peça ligada |
| `u_fichaint`, `u_equip` | número e equipamento para o nome do dossier |
| `u_pecaloc` | saldo por lugar (ref, armazém, corredor, estante, nível, posição, qtt, marcada); só o trigger a escreve |
| `v_sc_pecaloc_disponivel`, `sc_bi_pecaloc` | vista (saldo, reservado, disponível) e trigger que mantém o saldo |
| `u_tarefapeca` | peças do plano de manutenção (`itmanutlistamp`, `ref`, `design`, `qtt`); **não usada** |

Detalhes:
- `bi.lobs` é `varchar(40)`: guarda `<stamp do material>|<lugar>` (por exemplo `…|A-03-2-04`; lugar com no máximo 13 caracteres). Os stamps de `u_matinterv` têm menos de 25 caracteres, por isso o código parte no `|` e não por posição fixa.
- `u_pecaloc`: `qtt` é `varchar(13)`, `nivel` e `posicao` são `varchar(3)`, `estante` `varchar(20)`, `corredor` `varchar(10)`, `marcada` bit. A quantidade guarda-se em texto no formato `7.500`; a leitura converte para decimal. A estante guarda-se normalizada (`A-03`).
- Limites do lugar na API: corredor até 3 caracteres, estante até 999, nível e posição até 99. São mais apertados que as colunas e vêm do formato da etiqueta.
- Não se criam tabelas nem campos; não se alteram, desativam nem apagam triggers (os de `bo` e `bi` são encriptados e ativos).

## Estados e falhas parciais (repetíveis)

Regra: uma falha ao mexer no stock ou nos lugares nunca desfaz o estado da ficha; fica sinalizada e repete-se. Depois do primeiro efeito, os endpoints usam `CancellationToken.None` e apanham exceções com log.

- **Peça por dar**: material com `Ref` ligada e `SaidaDada = false`. O ecrã oferece "Repetir saída"; se já está dada, 409.
- **Validar** primeiro valida a ficha e só depois confirma a conferência. O stock do PHC nunca se move para uma ficha por validar. Se a confirmação falhar: "Conferência por confirmar". Confirmar é idempotente (verificado: confirmar duas vezes não desce duas).
- **Cancelar** apaga a conferência e limpa as ligações; não repõe lugares (o saldo nunca desceu). Falha: "Conferência por cancelar". É idempotente: uma ficha já cancelada com a limpeza por fazer (qualquer material com `Ref` ligada, com ou sem linha no dossier) repete só a limpeza, que limpa a ligação de todos esses materiais; com tudo limpo mantém a recusa 409. Cancelar depois de confirmado é recusado pelo repositório.
- Apagar material "por dar" não repõe nada se a ficha recusar (não se desfez nenhuma saída).
- Lugares: só se propõe um lugar que o trigger sabe ler (formato lido por `RegrasLocalizacao.LerLugar`); um lugar da lista com formato não reconhecido não é oferecido (a peça sai com aviso).
- Todas as escritas de dossier usam `sp_getapplock` exclusivo por tipo de dossier (10 s); se ocupado, 409 "Outro movimento em curso, tente outra vez.". Arrumar e mover usam applock por lugar (sem hints de locking nas leituras nem escritas desta app sobre os lugares).

## Fila offline

- Juntar material com peça offline guarda `Ref` e `Local` na fila e mostra "Saída por dar quando sincronizar"; a saída em conferência (com a reserva do lugar) dá-se ao sincronizar (aviso se já não há disponível). A fila antiga, sem `local`, continua a ler-se.
- Peça em vários lugares (409 `escolha_o_lugar`) não é recusa: a operação fica no estado **`PorEscolherLugar`**, pendente e visível, nunca descartada; as operações seguintes da mesma ficha esperam e as outras fichas seguem. O técnico escolhe o lugar e a operação volta a enviar-se.
- Em timeout a operação fica incerta e procura-se o material no servidor antes de reenviar, para não duplicar a saída.
- Editar, repetir saída e arrumar precisam de ligação. Sem ligação não se escolhe peça (a pesquisa é online).

## Leitura de etiquetas

- Modos: Automático (PDA reconhecido = Leitor, senão Câmara), Câmara e Leitor, escolhidos em `/leitor`. A preferência fica no aparelho.
- **PDA**: campo escondido sempre à escuta, que recebe o código do leitor tipo teclado (Enter ou Tab no fim); sem teclado virtual.
- **Telemóvel sem PDA**: botão "Picar com a câmara" (ZXing.Net.Maui, só Android: QR, EAN, Code 128/39, DataMatrix), uma leitura de cada vez. Existe na arrumação e na ficha.
- **Ordem livre**: quem classifica é o código lido. `LOC-…` é lugar, o resto é peça. Picar duas vezes o mesmo tipo substitui a escolha anterior; com peça e lugar avança sozinho. Separador da etiqueta tolerante (qualquer carácter que não seja letra ou número, para leitores com o teclado noutra língua).
- Escolher o lugar à mão (lista dos lugares da peça) continua possível na arrumação e na ficha.
- Código lido sem peça: "Código não encontrado: X".

## Verificado na base de testes

Base TESTES_LAND2, 2026-11-20:
- `bo.emconf` e `bi.emconf` existem (bit).
- Com `emconf = 1` na linha e no dossier, o stock real do armazém 104 **não se move** e o stock disponível (stock menos em conferência) desce.
- Ao confirmar (`emconf = 0` e os toques `UPDATE bo SET [no]=[no]` e `UPDATE bi SET [ref]=[ref]`) o stock real desce a quantidade.
- Cancelar antes de confirmar apaga `bo`, `bo2`, `bo3`, `bi`, `bi2` e deixa o stock igual. Cancelar depois de confirmar é recusado. Confirmar duas vezes não desce duas.
- Os triggers de `bo` e `bi` são encriptados e estão ativos.
- `bi.lobs` é `varchar(40)`. `bi.lobs2` também existe (`varchar(40)`), mas usa-se `bi.lobs` porque o dicionário base do PHC não tem `lobs2`.
- Os stamps de `u_matinterv` têm menos de 25 caracteres.
- `u_pecaloc` só tem a chave primária (sem índice único por lugar); colunas como na secção das tabelas.
- `u_tarefapeca` existe e liga à ficha por `itmanutlistamp`.
- A verificação apanhou dois bugs, já corrigidos: `COL_LENGTH` devolve `smallint` (não `int`) e a listagem de lugares lia `qtt` (varchar) como decimal.

Pré-requisitos que ficam por confirmar em cada cliente: `docs/sql/pecas-verificacao.sql`.

## Riscos e limitações

- **Concorrência na arrumação**: a LandeiroMaui arruma em `u_pecaloc` sem o mesmo applock e a tabela não tem índice único por lugar. Duas arrumações simultâneas (uma aqui e outra na LandeiroMaui, ou dois lugares novos iguais ao mesmo tempo) podem duplicar o lugar. Mitigação possível: índice único por lugar no PHC (decisão do cliente; esta app não cria índices).
- **Limites do lugar** mais apertados que as colunas (corredor até 3 caracteres, estante até 999, nível e posição até 99); vêm da etiqueta. Lugares fora disto, criados noutra app, podem não se conseguir escolher aqui.
- **`bi.lobs`** pode ser usado por outras rotinas do PHC do cliente: confirmar antes de instalar. Esta app guarda aí o stamp do material e o lugar.
- **Saldo por lugar e stock do PHC descem juntos**, ao validar (trigger `sc_bi_pecaloc`); entre juntar o material e validar, o disponível do lugar mostra o que já está reservado em conferência.
- **Revisão**: a revisão final do ramo (críticos e importantes) foi corrigida (relatório `.superpowers/sdd/v2-final-fix-report.md`); as correções dessa ronda não tiveram segunda revisão.
- A API depende dos triggers e colunas do PHC (`u_manut`, `emconf`, `bi.lobs`, dossiers 63, armazém 104). Uma atualização do PHC que mude os triggers pode afetar o circuito.
- Os dossiers criam-se com o cliente 1 ("Carnes Landeiro, SA.") e origem "Gestor Manutencao", como na LandeiroMaui.
- O armazém é fixo (104).
- Dois leitores de etiquetas não devem estar ativos ao mesmo tempo no mesmo ecrã.

## O que não está feito

- **Etiquetas**: impressão de etiquetas de peça e de lugar.
- **Configuração de armazéns** e de níveis de localização ligáveis (reutilizando `u_pecaloc`).
- **Peças do plano (`u_tarefapeca`)** ao iniciar a ficha: por decisão de negócio fica de fora; a ficha só tem as peças que o técnico junta. A tabela existe e liga à ficha por `itmanutlistamp`, por isso é possível mais tarde.
- **Verificação em ecrã e em aparelho**: câmara, PDA (Zebra, Honeywell, Urovo, Chainway), foco do campo escondido, Enter nos formulários (juntar e editar não deve gravar ao pesquisar a peça), ids distintos no DOM, hífen trocado nas etiquetas, persistência do modo, APK Android a abrir sem erro, leitor no Windows.
- Entrada de stock, transferência entre lugares, armazém 3D: fora de âmbito.
