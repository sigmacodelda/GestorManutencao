# Base LandeiroMaui: fonte da verdade do planeamento

Criado: 2026-11-06 · Regra: o planeamento SEGUE a base da LandeiroMaui (app existente da Landeiro onde a manutenção já está implementada no PHC). Se outro ficheiro contradiz este, prevalece este. O que não está verificado fica "por confirmar".
Origem: leitura do código da LandeiroMaui (`Components/Pages/Manutencao`, `Helpers`, `Services`). Referências no formato `ficheiro:linha`, caminhos relativos à raiz da LandeiroMaui.

## 1. Páginas existentes (Components/Pages/Manutencao)

MenuManutencao, Equipamentos, EquipamentoFicha, Planeadas, FichasIntervencao, FichaIntervencaoPagina, EstadoFicha, Validacao.

## 2. Estado da intervenção

Campo texto `u_fichaint.estado` (`Helpers/EstadosIntervencao.cs`):

| De | Para |
|---|---|
| Planeada | EmCurso, Cancelada |
| EmCurso | Concluida, Cancelada |
| Concluida | Validada, EmCurso (devolver, motivo em `valobs`) |

- Vazio = ficha antiga (legado): fecha-se pelo campo Fechada; não entra nas planeadas.
- **Derivados, nunca gravados:** Atrasada (Planeada/EmCurso com data < hoje) e A vencer (Planeada com data até hoje + antecedência).
- Estado inicial de uma intervenção curativa (Registar avaria): por confirmar no código (o planeamento antigo assumia EmCurso).

## 3. Regras por ação (`Services/IntervencaoService.cs`)

| Ação | O que grava e regras | Linhas |
|---|---|---|
| Iniciar | `estado` = EmCurso; `hini` = agora só se vazio; `tecno`/`tecnome` só se `tecno` = 0; `foraordem`. Só pode iniciar a 1.ª planeada da mesma zona (`u_equip.local`) por data, ordem e nº; regra configurável "avançar fora de ordem" | 371-396 |
| Concluir | `hfim` = agora se vazio; data da ficha = dia da conclusão; gera a planeada seguinte da tarefa (config `gerarplaneadas`) a partir da última feita + periodicidade (`u_periocid`, `u_itmanutli`); regra `extexigedoc`: externa exige fornecedor e documento externo | 398-420; GerarAsync 138-252 |
| Validar | `estado` = Validada, `fechada` = 1, `supervisao` = 1, `valinis`, `valdata`; regra `validarpropria` (por defeito quem concluiu não valida a própria, comparação por `tecno` = `userno`) | 422-442 |
| Devolver | Concluida para EmCurso, motivo em `valobs` | 444-470 |
| Cancelar | Planeada/EmCurso para Cancelada, com motivo | 472-487 |

Nunca se gera planeada já atrasada (decisão 2026-11-05: fica para hoje). Geração diária ao abrir as Planeadas. Uma só planeada/em curso por tarefa (`u_itmanutli`).

## 4. Configuração: tabela `u_configman`

Campos: `antecedencia`, `gerarplaneadas`, `avancaordem`, `validarpropria`, `extexigedoc`, `pinmaxtent`, `comprasfichaint`. (Substitui a `u_manconfig` chave/valor e o `u_manplano` do planeamento antigo. Não há chaves por role: por confirmar se `avancaordem` é único ou por perfil.)

## 5. Tabelas existentes

| Tabela | Papel |
|---|---|
| `u_equip` | Equipamentos (`local` = zona) |
| `u_itmanut` | Instruções de manutenção |
| `u_itmanutli` | Tarefas (plano por equipamento/instrução) |
| `u_periocid` | Periodicidades |
| `u_fichaint` | Ficha de intervenção (estado, hini, hfim, tecno, tecnome, valobs, fechada, supervisao, valinis, valdata, foraordem, usrinis/ousrinis) |
| `u_tempinterv` | Tempos da intervenção |
| `u_matinterv` | Material usado: `design`, `qtt`, `epv`, `custo`, `ref` (`Services/ManutencaoService.cs:659-670`). **Só regista material usado** |
| `u_histstats` | Histórico/estatísticas |
| `u_configman` | Configuração (secção 4) |
| `us` | Utilizadores PHC (`userno`, `username`; `ManutencaoService.cs:732`) |

Compras ligadas à ficha: só leitura (`fn.u_fichaint`, `fo`).

## 6. Técnico

Técnico = utilizador PHC (`us.userno`/`us.username`), gravado em `u_fichaint.tecno`/`tecnome`. Iniciais em `usrinis`/`ousrinis` (máx. 30). Não há controlo de perfil/roles no código de manutenção (só `UserState`).

## 7. O que NÃO existe na base (é NOVO se o planeamento o quiser)

Pedido de peças ao armazém, stock/rutura, etiquetas, receção, inventário, saída por leitura. Tudo o que for tabela ou campo novo: **NOVO: tabela/campo a criar por pessoa no PHC** (nunca pelo Claude Code).

Confirmado: a hipótese de `circuito-phc-atual.md` (secção 3) está CONFIRMADA: "peças usadas numa intervenção" já existe (`u_matinterv`) e reaproveita-se; deixa de ser necessário `u_manintpec` para o consumo registado na ficha.

## 8. App nova GestorManutencao (já construída)

- Login só por PIN, com tabela PHC `u_manutil` (utilizador, nome, perfil, pinhash, ativo, pinprov, versao, marcada). Vai ganhar o campo `tecno` (nº PHC do técnico, numérico, por defeito 0).
- Perfis: operador e supervisor.
- Ecrãs: Início, As minhas intervenções, Registar avaria, Equipa, Validação, Planeadas (em construção), Material usado na ficha (em construção; substitui "Pedir peça").

## 9. Por confirmar

- Estado inicial da curativa criada em Registar avaria.
- `avancaordem` único ou por perfil; perfis do módulo (a base não os controla).
- Se a periodicidade em `u_periocid` cobre a lista do Word P.05-03 (meses, quinzenas).
- Campos exatos de `u_equip`/`u_itmanutli` para o mapeamento da carga do plano (ler com o `sc-leitor-bd`).
- Offline: o LandeiroMaui não tem `LocalDb`/`FilaSaida` (ver risco R-10 do `00-global.md`).
