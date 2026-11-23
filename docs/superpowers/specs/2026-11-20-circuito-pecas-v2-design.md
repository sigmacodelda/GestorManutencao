# Circuito de peças v2: arrumação e saída em conferência

Data: 2026-11-20. Substitui o circuito do plano `2026-11-20-pecas-entrada-saida.md` (saída automática que move o stock logo). Aprovado pelo utilizador em conversa; este documento fixa o que foi decidido.

## Circuito

1. **Entrada** (escolha do artigo e quantidade): só na LandeiroMaui. Esta app não tem entrada.
2. **Arrumação** (esta app): picar a etiqueta da peça e a etiqueta de localização, em qualquer ordem; grava em `u_pecaloc`.
3. **Ficha de intervenção** (esta app): ao iniciar cria o dossier 63 em conferência (`bo.emconf = 1`); o material com peça entra nele com `bi.emconf = 1` e sai da localização escolhida; ao validar (supervisor) sai de conferência e o stock do PHC move-se.

## Decisões do utilizador

- A entrada sai desta app: retirar ecrã `/pecas/entrada`, mosaico, rotas de entrada (`POST /pecas/entradas`, `DELETE /pecas/entradas/{id}`, `GET /pecas/entradas/hoje`) e o código que só elas usam. Ficam `GET /pecas`, `GET /pecas/codigo/{codigo}`, a pesquisa, o leitor e a saída.
- **Ordem de picagem livre** na arrumação e na ficha: a pessoa pode picar primeiro a peça ou primeiro a localização (há quem prefira uma ou outra). Quem classifica é o código lido: `LOC-…` é localização (`LogicaLeitura.EEtiquetaLocal`), o resto é peça. Picar duas vezes o mesmo tipo substitui a escolha anterior. Quando há peça e localização, avança sozinho.
- A quantidade desconta-se da `u_pecaloc` **no picking** (a peça já saiu da prateleira); repõe-se se o material for apagado ou a ficha cancelada.
- Na arrumação a quantidade vem proposta: stock do armazém 104 menos o já localizado em `u_pecaloc` (alterável); soma ao lugar se já existir, como a LandeiroMaui.
- Stock disponível mostrado = stock do armazém 104 menos o que está em conferência noutras fichas; avisa se não chega e **não bloqueia** (regra da LandeiroMaui).
- Etiquetas (impressão de peça e de local) ficam para depois. Localizações configuráveis por armazém (níveis ligáveis) ficam para depois, reutilizando `u_pecaloc`.

## Arrumação (novo, `/pecas/arrumar`)

- Estado do ecrã: `peça?`, `localização?`, `quantidade`. Cada leitura (campo escondido, câmara ou pesquisa) é classificada e preenche o campo respetivo; com os dois preenchidos mostra a quantidade proposta e o botão "Arrumar". A peça mostra os lugares onde já está e o por arrumar.
- API `POST /pecas/localizacoes` (peça, corredor, estante, nível, posição, quantidade) e `GET /pecas/{ref}/localizacoes`. Perfis: operador e supervisor (mesmo padrão de `/pecas`).
- `u_pecaloc` (tabela da LandeiroMaui, já definida): `u_pecalocstamp, ref, armazem (104), corredor, estante ('A-02'), nivel, posicao, qtt, ousr*/usr*, marcada`. Grava `marcada = 0` como nos exemplos; significado por confirmar. Soma por UPDATE e, se não houver linha, INSERT, num só comando (padrão da LandeiroMaui, com applock do lugar).
- Interpretação da etiqueta de local: porta do `EtiquetaLocalHelper.Ler` da LandeiroMaui para a API e para a UI (formato `LOC-A-03-2-04`, separador tolerante). Estante normalizada como na LandeiroMaui (`A-03`).

## Ficha de intervenção

- **Iniciar** (Planeada para Em curso): cria o dossier 63 da ficha com `bo.emconf = 1` (bo, bo2, bo3) e, se o plano já tem peças (`u_tarefapeca`, a confirmar se existe e como liga à ficha), uma linha por peça com `bi.emconf = 1`, `bi.lobs` = stamp do material correspondente.
- **Juntar/editar/apagar material com peça**: cria, altera ou apaga a linha `bi` do dossier em conferência e ajusta a `u_pecaloc` do lugar escolhido. Lugar: o único com stock, ou o escolhido (lista, ou picar a etiqueta de local, antes ou depois da peça). Sem lugar com stock, avisa e deixa continuar (peça "sem localização").
- **Validar** (supervisor, Concluída para Validada): `bo.emconf = 0` e `bi.emconf = 0` mais os "toques" que disparam os triggers do PHC. Se a confirmação falhar, a ficha fica validada (valida-se primeiro a ficha e só depois se confirma a conferência, como no ADR 0003) com o aviso "Conferência por confirmar", e a confirmação repete-se (idempotente); o aviso também aparece ao reabrir a ficha.
- **Devolver** (Concluída para Em curso): mantém a conferência. **Cancelar**: apaga o dossier e repõe a `u_pecaloc`.
- PDA: campo escondido à escuta no material da ficha. Telemóvel (sem PDA): botão "Picar com a câmara", que abre a câmara (`ILeitorCamara`) e serve para a peça e para a localização, uma leitura de cada vez. O mesmo botão existe na arrumação. Mesma regra de ordem livre; escolher o lugar à mão (lista) continua possível em ambos.
- Orquestração nos endpoints (como na Task 3): `IRepositorioIntervencoes` não muda; os endpoints chamam `IRepositorioPecas` (abrir, confirmar, cancelar conferência; linha de material). Falha parcial nunca desfaz o estado da ficha: fica sinalizada ("peça por dar", conferência por confirmar) e repetível.

## O que muda no que já está construído

- Retira-se: entrada (Tasks 2 e 4).
- Mantém-se: pesquisa de peças, leitura de etiquetas (Task 5; base da arrumação e da ficha), fila offline do material com peça (Task 6, com ajustes: o offline guarda a ref e a localização; a saída em conferência dá-se ao sincronizar).
- Retrabalha-se: saída automática da Task 3 (passa a conferência com `emconf`, desconta `u_pecaloc`), `RegrasPecas`, `IRepositorioPecas`.
- Documentos da Task 7 (`docs/pecas-armazem.md`, ADR 0002, `docs/sql/pecas-verificacao.sql`) descrevem o circuito antigo: reescrever no fim.

## Por confirmar na base de testes (só leitura até haver confirmação)

1. Com `bo.emconf = 1` e `bi.emconf = 1` o PHC não conta a linha no stock; com `emconf = 0` conta (pressuposto). A base do AllKnowing não tem este conhecimento: gravar a lição depois de confirmado.
2. Que "toques" movem o stock ao passar `emconf` a 0.
3. Existência de `u_pecaloc`, `u_tarefapeca`, `stobs.u_manut`, dossiers 63/64 em `ts`, armazém 104; significado de `u_pecaloc.marcada`.
4. Como as peças do plano (`u_tarefapeca`) chegam à ficha ao iniciar.

## Fora de âmbito

Entrada de stock, impressão de etiquetas, configuração de armazéns e níveis, armazém 3D, transferência entre localizações.
