# Circuito de peças v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Arrumação de peças em `u_pecaloc` (picagem de peça e local em qualquer ordem) e saída da ficha em dossier PHC em conferência (`emconf = 1`) que só move o stock quando o supervisor valida; sem entrada nesta app.

**Architecture:** API minimal ASP.NET Core com repositórios SQL e em memória (`IRepositorioPecas` ampliado, mais `IRepositorioLocalizacoes` novo); os endpoints da ficha (iniciar, material, validar, cancelar) orquestram as chamadas aos repositórios sem alterar `IRepositorioIntervencoes`. UI Blazor Hybrid: ecrã de arrumação novo, material da ficha com lugar, picagem por campo escondido (PDA) ou câmara, ordem livre decidida por uma função pura.

**Tech Stack:** .NET 10, ASP.NET Core, `Microsoft.Data.SqlClient` (ADO.NET), Blazor Hybrid MAUI (Android/Windows), ZXing.Net.Maui.Controls (já instalado), xUnit + WebApplicationFactory.

Especificação: `docs/superpowers/specs/2026-11-20-circuito-pecas-v2-design.md`. Plano anterior (substituído nas partes que a especificação indica): `2026-11-20-pecas-entrada-saida.md`.

## Global Constraints

- Idioma pt-PT; comentários só com o porquê; sem dependências novas.
- Dossiers: saída = `ndos 63` (um por ficha), armazém `104` (`RegrasPecas.Armazem`), `bi.lobs` = stamp do `u_matinterv` (25 caracteres). Entrada (`ndos 64`) sai da app.
- `u_pecaloc` (tabela da LandeiroMaui, já definida): `u_pecalocstamp, ref, armazem, corredor, estante ('A-02'), nivel, posicao, qtt, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora, marcada`. Nunca criar tabelas nem campos no PHC; `marcada` grava-se a 0 (significado por confirmar).
- Etiqueta de local: `LOC-A-03-2-04` (ou `LOC-A-03-2`); separador = qualquer coisa que não seja letra ou número (leitores com teclado noutra língua). Estante normalizada `A-03`.
- Conferência: pressuposto **a confirmar na base de testes**: com `bo.emconf = 1` e `bi.emconf = 1` o stock do PHC não se move; passar a `0` e dar os "toques" (`UPDATE bo SET [no]=[no]`, `UPDATE bi SET [ref]=[ref]`) move-o. Até confirmar, nada se dá como validado em SQL.
- Sem stock suficiente: avisa e deixa continuar (nunca bloqueia). Stock disponível = stock do 104 menos o que está em conferência noutras fichas.
- O desconto/reposição de `u_pecaloc` é do picking (material com peça) e da anulação/cancelamento; o stock do PHC só se move na validação.
- Erros no formato da API: 400 `{erro:"pedido_invalido", mensagem}`, 409 `{erro}`, 503 pelo filtro `RepositorioIndisponivelException`; só mostrar `ex.Message` SQL ao cliente se `Number == 50000`.
- Ordem única nos endpoints: verificar acesso/`PodeEditar` antes de qualquer efeito; depois do primeiro efeito usar `CancellationToken.None` e apanhar `Exception` com log; falha parcial nunca desfaz o estado da ficha, fica sinalizada e repetível.
- Autorização: `ComoTecnicoOuSupervisor` (padrão de `/pecas`); validar/cancelar só supervisor (como hoje).
- SQL só com SELECT contra bases reais até o nome da base de TESTES ser confirmado com `SELECT DB_NAME()`; connection string só no `appsettings` local fora do git (nunca no chat).
- Verificações: `dotnet test GestorManutencao.Tests` verde (nunca dois `dotnet test` em paralelo), `dotnet build GestorManutencao.UI`, e para tarefas que toquem no App `dotnet build GestorManutencao.App -f net10.0-android -c Debug`. `.superpowers/` e as alterações do utilizador (`.gitignore`, `GestorManutencao.App.csproj`, `Testes-Circuito/*`, `publicar-api.zip`) nunca entram nos commits.

---

### Task 1: Retirar a entrada de peças

**Files:**
- Modify: `GestorManutencao.Api/Pecas.cs`, `SqlRepositorioPecas.cs`, `RepositorioPecasMemoria.cs`
- Modify: `GestorManutencao.UI/Servicos/IPecasService.cs`, `PecasApiService.cs`, `PecasDevService.cs`, `GestorManutencao.UI/Modelos/Pecas.cs`, `GestorManutencao.UI/Modelos/LogicaEcraPecas.cs`, `GestorManutencao.UI/Pages/Inicio.razor`
- Delete: `GestorManutencao.UI/Pages/EntradaPecas.razor`
- Modify/Delete testes: `GestorManutencao.Tests/PecasApiTests.cs`, `PecasUiTests.cs`, `PecasRegrasTests.cs` (e fakes que implementem `IRepositorioPecas`)

**Interfaces:**
- Produces: `IRepositorioPecas` sem `DarEntradaAsync`, `EntradasDeHojeAsync`, `AnularEntradaAsync`; sem `PedidoEntrada`, `EntradaPecaDto`, `RegrasPecas.NdosEntrada`; rotas `/pecas/entradas*` removidas. Ficam `GET /pecas`, `GET /pecas/codigo/{codigo}`.
- `PecaPesquisa`, `LeitorEtiquetas`, `LogicaLeitura`, o ecrã `/leitor` e a saída (`DarSaidaAsync` etc.) ficam como estão.

- [ ] **Step 1:** Remover rotas, DTOs, métodos de repositório (interface, SQL, memória, fakes) e constantes que só a entrada usava (procurar por `Entrada` em `GestorManutencao.Api` e `GestorManutencao.UI`; `ErroLinhaInexistente` só se ficar sem uso).
- [ ] **Step 2:** Remover o ecrã `EntradaPecas.razor`, o mosaico "Entrada de peças" em `Inicio.razor`, os métodos de entrada em `IPecasService` e nas duas implementações, o modelo `EntradaPeca`, e a lógica de `LogicaEcraPecas` que só a entrada usava (resultado incerto da entrada, `FalhaDeRedePecasException`, `EntradaIncertaPecasException`). Manter o que a pesquisa e o leitor usam.
- [ ] **Step 3:** Remover/ajustar os testes da entrada; manter os testes da pesquisa, `/pecas/codigo` e das regras que continuam a existir.
- [ ] **Step 4:** `dotnet test GestorManutencao.Tests` verde; `dotnet build GestorManutencao.UI` sem erros; `Grep` por `Entrada` mostra apenas comentários históricos ou nada.
- [ ] **Step 5:** Commit (via `sc-git`): "Retirar a entrada de peças: a entrada fica na LandeiroMaui".

---

### Task 2: Localizações no armazém (API): `u_pecaloc`

**Files:**
- Create: `GestorManutencao.Api/Localizacoes.cs` (DTOs, regras, interface, endpoints), `SqlRepositorioLocalizacoes.cs`, `RepositorioLocalizacoesMemoria.cs`
- Modify: `GestorManutencao.Api/Program.cs` (registos; memória em Development, SQL no outro ramo; `app.MapLocalizacoes()`)
- Test: `GestorManutencao.Tests/LocalizacoesRegrasTests.cs`, `LocalizacoesApiTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record LugarDto(string Corredor, string Estante, int Nivel, int Posicao, decimal Qtt)
{ public string Texto => $"{Estante}-{Nivel}-{Posicao:00}"; }                       // A-02-1-01
public sealed record LocalizacoesPecaDto(string Ref, decimal Stock, decimal Localizado, decimal PorArrumar, IReadOnlyList<LugarDto> Lugares);
public sealed record PedidoArrumar(string? Ref, string? Local, decimal? Qtt);        // Local = texto da etiqueta (LOC-…) ou A-02-1-01
public static class RegrasLocalizacao {
  public static (string Corredor, int Estante, int Nivel, int Posicao)? LerEtiqueta(string? texto); // LOC-A-03-2-04, separador tolerante, posição obrigatória aqui
  public static (string Corredor, int Estante, int Nivel, int? Posicao)? LerLocal(string? texto);   // aceita LOC-… (4 ou 5 partes) e A-03-2(-04)
  public static string NormalizarEstante(string corredor, int estante);                             // "A-03"
}
public interface IRepositorioLocalizacoes {
  Task<LocalizacoesPecaDto> ListarAsync(string refPeca, CancellationToken ct);                      // lugares ordenados por qtt desc; PorArrumar = max(0, Stock104 − Localizado)
  Task<ResultadoMovimento> ArrumarAsync(string refPeca, string corredor, int estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);  // soma ao lugar ou cria (UPDATE; IF @@ROWCOUNT=0 INSERT), applock "pecas-lugar-<ref>-<estante>-<nivel>-<posicao>"
  Task<ResultadoMovimento> DescontarAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);  // Ok mesmo que o lugar fique a 0; Falha se não houver qtt suficiente neste lugar (a saída continua, ver Task 5)
  Task<ResultadoMovimento> ReporAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);      // = soma de volta (igual a Arrumar sem validar stock)
  Task<string> VerificarTabelaAsync(CancellationToken ct);                                          // "" ou o que falta (como a LandeiroMaui)
}
```
- Rotas (`ComoTecnicoOuSupervisor`, filtro 503): `GET /pecas/{ref}/localizacoes` → `LocalizacoesPecaDto`; `POST /pecas/localizacoes` com `PedidoArrumar` → 200 `LocalizacoesPecaDto` atualizado, 400 pedido inválido (ref ≤ 18, local legível, qtt > 0 com ≤ 3 casas e < 1.000.000), 404 se a peça não existe (`IRepositorioPecas.ObterPorCodigoAsync` ou `ListarAsync` por ref exata), 409 ocupado ou tabela em falta (`{erro}` com o texto de `VerificarTabelaAsync`).
- Consumes: `IRepositorioPecas.ObterPorCodigoAsync` (peça existe), `RegrasPecas.Armazem`, `ResultadoMovimento`, padrão de `Parametro()`/`sp_getapplock`/`RollbackSeguroAsync` de `SqlRepositorioPecas`.

- [ ] **Step 1: Testes das regras puras (falham primeiro)** em `LocalizacoesRegrasTests.cs`: `LerEtiqueta("LOC-A-03-2-04")` → ("A",3,2,4); `LOC'A'03'2'04` (apóstrofos) igual; `LOC-A-03-2` com `LerLocal` → posição null; `A-02-1-01` com `LerLocal` → ("A",2,1,1); `LOC-A-03` (3 partes), `LOC-A-0-2-1` (estante 0), `ABC-A-03-2-04` (prefixo errado) → null; `NormalizarEstante("a", 3)` = "A-03"; `PorArrumar` nunca negativo (função pura `Calcular(stock, localizado)`).
- [ ] **Step 2:** Implementar `RegrasLocalizacao` (porta do `EtiquetaLocalHelper.Ler` da LandeiroMaui em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui\Helpers\EtiquetaLocalHelper.cs`; usar `Regex.Split(texto.ToUpperInvariant(), @"[^\p{L}\p{N}]+")`).
- [ ] **Step 3: Testes de API (WebApplicationFactory, memória)** em `LocalizacoesApiTests.cs`: 401 sem token; 403 perfil Qualidade; GET devolve lugares (ordenados) e `PorArrumar`; POST soma a lugar existente e cria novo; peça inexistente 404; ref/local/qtt inválidos 400; tabela em falta 409 com o texto; 503 do repositório indisponível. Amostra em memória: `P001` com stock 10 e um lugar `A-02-1-01` qtt 3.
- [ ] **Step 4:** Implementar `RepositorioLocalizacoesMemoria` (lock; usa o stock da peça da amostra de `RepositorioPecasMemoria`) e `SqlRepositorioLocalizacoes` (ADO.NET, SQL da `PecasLocalizacaoService` da LandeiroMaui reescrito sem Dapper, `usrinis/usrdata/usrhora` preenchidos, `marcada = 0` no INSERT, `estante` normalizada, `LTRIM(RTRIM())` nas comparações como a LandeiroMaui). O SQL só compila; é verificado na Task 7.
- [ ] **Step 5:** Endpoints, registos em `Program.cs`, testes verdes, suite completa verde.
- [ ] **Step 6:** `sc-revisor` (SQL novo); corrigir Critical e Important; commit via `sc-git`.

---

### Task 3: Ecrã de arrumação (UI)

**Files:**
- Create: `GestorManutencao.UI/Pages/ArrumarPecas.razor` (`@page "/pecas/arrumar"`), `GestorManutencao.UI/Modelos/LogicaPicagem.cs`
- Modify: `GestorManutencao.UI/Modelos/Pecas.cs` (modelos `Lugar`, `LocalizacoesPeca`), `Servicos/IPecasService.cs`, `PecasApiService.cs`, `PecasDevService.cs`, `Pages/Inicio.razor` (mosaico "Arrumação"), `Componentes/PecaPesquisa.razor` (só o que for preciso para receber leituras por fora)
- Test: `GestorManutencao.Tests/PicagemTests.cs`, `PecasUiTests.cs`

**Interfaces:**
- Consumes: Task 2 (`GET /pecas/{ref}/localizacoes`, `POST /pecas/localizacoes`), `LogicaLeitura.EEtiquetaLocal`, `LeitorEtiquetas`, `PecaPesquisa.LerCodigoAsync`.
- Produces (puro, em `GestorManutencao.UI/Modelos/LogicaPicagem.cs`, usado também na Task 6):
```csharp
public enum TipoLeitura { Peca, Local }
public sealed record EstadoPicagem(string? Peca, string? Local)
{
  public bool Completo => Peca is not null && Local is not null;
  public EstadoPicagem Aplicar(string leitura) =>      // classifica: EEtiquetaLocal → Local (substitui o anterior), senão → Peca (substitui o anterior)
}
public static class LogicaPicagem { public static TipoLeitura Classificar(string leitura); }
// IPecasService: Task<LocalizacoesPeca?> LugaresAsync(string refPeca, CancellationToken ct); Task<LocalizacoesPeca> ArrumarAsync(string refPeca, string local, decimal qtt, CancellationToken ct);
```

- [ ] **Step 1: Testes (falham primeiro)** em `PicagemTests.cs`: peça primeiro e depois local → Completo; local primeiro e depois peça → Completo; duas peças seguidas → fica a última; dois locais → fica o último; leitura com apóstrofos `LOC'A'02'1'01` é Local; código com espaço interno é Peca; leitura vazia é ignorada (Aplicar devolve o mesmo estado). Testes do serviço (HttpMessageHandler falso): corpo JSON de `ArrumarAsync`, 400/404/409 → mensagens pt-PT.
- [ ] **Step 2:** `LogicaPicagem`, modelos, métodos do serviço (API e Dev com dados de amostra).
- [ ] **Step 3:** Ecrã `ArrumarPecas.razor`: duas zonas (Peça, Local) que se preenchem pela leitura (campo escondido em PDA via `LeitorEtiquetas`, botão "Picar com a câmara" noutro caso, pesquisa por escrito para a peça, lista dos lugares da peça para escolher o local à mão); com `Completo` mostra a quantidade proposta (`PorArrumar`) alterável e o botão "Arrumar" (Enter confirma); aviso "Código não encontrado" e "etiqueta de local" já tratados pelo `PecaPesquisa`; depois de arrumar mostra os lugares atualizados, limpa a peça e o local, e volta o foco ao campo escondido (sem teclado virtual em modo Leitor). `@page` fora de `<form>` aninhado (lição da Task 6 do plano anterior): nenhum `<form>` dentro de outro.
- [ ] **Step 4:** Mosaico "Arrumação" em `Inicio.razor` para operador e supervisor (não Qualidade); registo do serviço já existe.
- [ ] **Step 5:** `dotnet test` (filtro `PicagemTests|PecasUiTests` e suite uma vez), `dotnet build GestorManutencao.UI`; verificação manual em Dev **por registar** (não afirmar que foi visto).
- [ ] **Step 6:** `sc-revisor`; corrigir Critical e Important; commit via `sc-git`.

---

### Task 4: Conferência no PHC (repositório de peças)

**Files:**
- Modify: `GestorManutencao.Api/Pecas.cs` (interface e regras), `SqlRepositorioPecas.cs`, `RepositorioPecasMemoria.cs`, `GestorManutencao.Api/Intervencoes.cs` (só `MaterialDto`/`PedidoMaterial` se preciso), fakes em `GestorManutencao.Tests`
- Test: `GestorManutencao.Tests/PecasConferenciaTests.cs` (memória) e ajuste dos testes de saída existentes

**Interfaces:**
- Produces: em `IRepositorioPecas` substituir `DarSaidaAsync`/`AnularSaidaAsync` por:
```csharp
// Dossier 63 da ficha em conferência: bo.emconf=1 (bo, bo2, bo3). Idempotente: se já existe devolve o id.
Task<ResultadoMovimento> AbrirConferenciaAsync(string fichaId, string iniciais, CancellationToken ct);
// Linha bi.emconf=1 do material (bi.lobs = materialId, armazém 104). Cria o dossier se faltar. Não toca nos triggers de stock.
Task<ResultadoMovimento> DarSaidaAsync(string fichaId, string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct);
Task<ResultadoMovimento> AnularSaidaAsync(string materialId, string iniciais, CancellationToken ct);   // apaga a linha (e o dossier se ficar vazio? NÃO: em conferência o dossier mantém-se até validar/cancelar); limpa u_matinterv.ref
// Validar: bo.emconf=0 e bi.emconf=0 + toques (UPDATE bo SET [no]=[no]; UPDATE bi SET [ref]=[ref]). Idempotente.
Task<ResultadoMovimento> ConfirmarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct);
// Cancelar: apaga linhas, bi2 e o dossier da ficha (só se ainda em conferência).
Task<ResultadoMovimento> CancelarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct);
Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(...);   // mantém; SaidaDada = existe linha bi no dossier da ficha com lobs = material
Task<IReadOnlyDictionary<string, decimal>> EmConferenciaPorPecaAsync(IReadOnlyCollection<string> refs, string? excetoFichaId, CancellationToken ct);  // soma bi.qtt de linhas ndos 63 com emconf=1 por ref (para o stock disponível)
```
- `PecaDto` ganha `decimal EmConferencia` e `decimal Disponivel` (= Stock − EmConferencia); `AbaixoDoMinimo` continua a usar o stock real; `ListarAsync`/`ObterPorCodigoAsync` preenchem-nos; `RegrasPecas.AvisoStock(pedida, disponivel)` passa a receber o disponível.
- Consumes: `ResultadoMovimento`, padrão de `bo/bo2/bo3/bi/bi2` já portado do `PecasMovimentoService` (LandeiroMaui) em `SqlRepositorioPecas`, `sp_getapplock` `pecas-dossier-<ndos>`.

- [ ] **Step 1: Testes na memória (falham primeiro)** em `PecasConferenciaTests.cs`: abrir conferência é idempotente; dar saída cria a linha em conferência e o `Stock` real da peça NÃO desce, mas `Disponivel` desce; anular a saída devolve o disponível; confirmar a conferência desce o `Stock` real e zera `EmConferencia`; confirmar duas vezes não desce duas; cancelar a conferência apaga linhas e devolve o disponível; `EmConferenciaPorPecaAsync` exclui a própria ficha; aviso de stock usa o disponível.
- [ ] **Step 2:** Implementar na memória e atualizar os fakes dos testes.
- [ ] **Step 3: SQL.** Reescrever em `SqlRepositorioPecas` o que cria o dossier 63 para acrescentar `emconf = 1` em `bo` e nas linhas `bi`, e NÃO dar os "toques" na criação; `ConfirmarConferenciaAsync` faz `UPDATE bo SET emconf = 0` e `UPDATE bi SET emconf = 0` das linhas da ficha e só depois os toques, numa transação com applock; `CancelarConferenciaAsync` apaga bi2, bi, bo3, bo2, bo do dossier da ficha se `emconf = 1` e devolve 409 `{erro}` se já estiver confirmado. O SQL só compila; o pressuposto de que `emconf = 1` não move stock e `0` + toques move é verificado na Task 7 (se falhar: `BLOQUEIO:` com o que o PHC faz de facto).
- [ ] **Step 4:** `dotnet test` (suite completa) verde.
- [ ] **Step 5:** `sc-revisor` (SQL e >300 linhas); corrigir Critical e Important; commit via `sc-git`.

---

### Task 5: Ficha: conferência ao iniciar, material com lugar, validar e cancelar

**Files:**
- Modify: `GestorManutencao.Api/Intervencoes.cs` (endpoints iniciar, materiais POST/PUT/DELETE/saida, validacao, cancelar; `MaterialDto`/`PedidoMaterial`), `Pecas.cs` se preciso
- Test: `GestorManutencao.Tests/MaterialPecasApiTests.cs` (adaptar), `FichaConferenciaApiTests.cs` (novo)

**Interfaces:**
- Consumes: Tasks 2 e 4 (`IRepositorioLocalizacoes`, `IRepositorioPecas` com conferência), `ComoTecnico`, `ComoSupervisor`, `FichaDetalhe.PodeEditar`.
- Produces: `PedidoMaterial(Design, Qtt, Epv, Ref = null, Local = null)` com `Local` = texto do lugar (`A-02-1-01` ou `LOC-…`); `MaterialDto` ganha `Local` (o lugar de onde saiu, quando conhecido, lido de `u_matinterv`? **não existe campo**: guardar o lugar na própria linha `bi` do dossier via `bi.lobs2` (texto `A-02-1-01`) se o campo existir; senão decidir na Task 7 e registar `BLOQUEIO`). Respostas: `SaidaDada` = linha em conferência existe.
- Comportamento dos endpoints (ordem única da Global Constraints):
  - `POST /intervencoes/{id}/iniciar`: depois de `repo.IniciarAsync` com sucesso chama `pecas.AbrirConferenciaAsync(id, t.Codigo)` (falha só loga e fica retentável: o `POST` do material abre-o de novo, é idempotente). Peças já previstas no plano entram na Task 5b (ver Step 6).
  - `POST/PUT /materiais`: valida peça e lugar (404/409/400), grava o material (`IRepositorioIntervencoes`), `DarSaidaAsync` em conferência e `DescontarAsync` do lugar escolhido; se o lugar não tem quantidade suficiente: grava na mesma, `AvisoStock`/aviso de lugar na resposta, sem bloquear; PUT desfaz (`AnularSaidaAsync` + `ReporAsync` do lugar antigo) e refaz como já fazia a Task 3 do plano anterior.
  - `DELETE /materiais/{materialId}`: valida acesso, `AnularSaidaAsync` + `ReporAsync`, apaga o material, repõe a saída se a ficha recusar (padrão atual).
  - `POST /validacao/{id}` (supervisor): depois de `repo.ValidarAsync` com sucesso chama `ConfirmarConferenciaAsync`; se falhar, a ficha fica validada e o endpoint responde 200 com `{ avisoStock: "Conferência por confirmar" }` e loga; um segundo `POST /validacao/{id}` não repete a validação mas tenta de novo a confirmação (idempotente). Alternativa mais segura a decidir pelo implementador e registar: confirmar a conferência **antes** de validar a ficha e, se falhar, recusar a validação com 409.
  - `POST /intervencoes/{id}/cancelar`: depois de `repo.CancelarAsync` chama `CancelarConferenciaAsync` e repõe todos os lugares (lê as linhas antes de apagar).
  - `POST .../materiais/{materialId}/saida` (repetir) continua só para "por dar".

- [ ] **Step 1: Testes de API (memória) (falham primeiro)**: iniciar abre a conferência; material com peça e lugar tira do lugar, o stock real não desce e o disponível sim; PUT muda lugar/quantidade e repõe/desconta certo; DELETE repõe; lugar sem quantidade suficiente grava e avisa; validar desce o stock real; cancelar apaga a conferência e repõe os lugares; devolver (`/validacao/{id}/devolver`) mantém a conferência; técnico sem acesso 404/409 como hoje; falhas parciais deixam "por dar"/"por confirmar" repetível; peça sem lugar (sem `u_pecaloc`) é permitida.
- [ ] **Step 2:** Implementar nos endpoints e nos repositórios em memória; adaptar os testes existentes que assumiam saída que move o stock.
- [ ] **Step 3:** Garantir que `IRepositorioIntervencoes` não muda de assinatura.
- [ ] **Step 4:** Peças previstas no plano: investigar, em `C:\_SigmaRepos\Landeiro-Desmancha\LandeiroMaui` (`ModalPecaTarefa`, `PecasDaTarefa`, `Instrucoes`, `IntervencaoService`) como `u_tarefapeca` liga às tarefas da instrução e à ficha. Se a ligação à ficha se obtém com SELECT (existe `u_tarefapeca`, ficha → tarefas), `AbrirConferenciaAsync` cria também as linhas `bi.emconf = 1` dessas peças (qtt do plano, sem lugar, material correspondente criado em `u_matinterv` se o circuito da LandeiroMaui o faz); senão `BLOQUEIO:` com o que falta (tabela por criar no PHC, por pessoa) e deixar esta parte fora, registada nas notas.
- [ ] **Step 5:** `dotnet test` suite completa verde.
- [ ] **Step 6:** `sc-revisor`; corrigir Critical e Important; commit via `sc-git`.

---

### Task 6: Ficha na UI: peça com lugar (online e offline)

**Files:**
- Modify: `GestorManutencao.UI/Pages/DetalheIntervencao.razor`, `Modelos/Intervencoes.cs`, `Servicos/IIntervencoesService.cs`, `IntervencoesApiService.cs`, `Offline/OfflineIntervencoesService.cs`, `Offline/SincronizadorFila.cs`, `Componentes/PecaPesquisa.razor`, `wwwroot/app.css`
- Test: `GestorManutencao.Tests/MaterialPecasOfflineTests.cs`, `PicagemTests.cs`

**Interfaces:**
- Consumes: Task 5 (`PedidoMaterial.Local`, `MaterialDto.Local`), Task 3 (`LogicaPicagem`, `EstadoPicagem`, `IPecasService.LugaresAsync`), `LeitorEtiquetas`.
- Produces: `Material.Local` / `CargaMaterial.Local`; parâmetro opcional `localPeca` nas interfaces de material (retrocompatível, a fila antiga continua a funcionar).

- [ ] **Step 1: Testes (falham primeiro)**: JSON do POST/PUT leva `local`; fila offline guarda ref e local e reenvia ao sincronizar; fila antiga sem `local` desserializa; ordem livre de picagem na ficha (peça→local e local→peça) usando `EstadoPicagem`; peça com um só lugar com stock escolhe-o sozinha; vários lugares → lista para escolher à mão ou picar o local.
- [ ] **Step 2:** Ficha: no formulário de juntar material, a pesquisa de peça fica fora de qualquer `<form>` (lição anterior), com `IdCampo` próprio por formulário; PDA: campo escondido à escuta; sem PDA: botão "Picar com a câmara" que lê uma etiqueta de cada vez (peça ou local, `LogicaPicagem.Classificar`); com peça e local escolhidos avança para a quantidade; peça com vários lugares mostra os lugares com a quantidade de cada, e o aviso "stock disponível" (disponível, não o stock real) sem bloquear.
- [ ] **Step 3:** Offline: o material na fila leva `ref` e `local`; mostra "Saída por dar quando sincronizar" até enviar; o lugar só se confirma ao sincronizar (aviso se já não há quantidade).
- [ ] **Step 4:** `dotnet test` filtro e suite completa uma vez; `dotnet build GestorManutencao.UI`; `dotnet build GestorManutencao.App -f net10.0-android -c Debug` se o App for tocado.
- [ ] **Step 5:** `sc-revisor`; corrigir Critical e Important; commit via `sc-git`. Verificação em ecrã/aparelho fica **por registar** (Enter nos formulários, PDA, câmara).

---

### Task 7: Base de testes real, documentação e entrega

**Files:**
- Create/Modify: `docs/sql/pecas-verificacao.sql` (só SELECT; acrescentar `u_pecaloc`, `emconf`), `docs/pecas-armazem.md` (reescrever para o circuito v2), `docs/adr/0003-pecas-conferencia-e-localizacoes.md` (substitui o 0002 na parte da saída e da entrada)
- Modify: `docs/adr/0002-pecas-por-dossier-phc.md` (estado: parcialmente substituído pelo 0003), `docs/DEPENDENCIES.md` só se mudar algo

- [ ] **Step 1:** `BLOQUEIO:` se a connection string de TESTES não estiver no `appsettings` local: pedir ao utilizador (nunca no chat). Sem ela, parar aqui e entregar o resto como "por verificar".
- [ ] **Step 2:** `sc-leitor-bd`: `SELECT DB_NAME()` e confirmar com o utilizador que é de TESTES; correr `docs/sql/pecas-verificacao.sql` (só leitura): `ts` 63, `sz`/`sa` 104, `stobs.u_manut`, `u_pecaloc` (campos e `marcada`), `bi.lobs`/`bi.lobs2` tamanhos, `bo.emconf`/`bi.emconf`, triggers de `bi`/`bo`/`stobs` só a listar, `u_tarefapeca`.
- [ ] **Step 3:** Teste ponta a ponta na base de testes (escrita só depois do nome confirmado): arrumar uma peça; iniciar uma ficha de teste (dossier 63 com `emconf = 1`, `sa.stock` inalterado); juntar material com peça e lugar (`u_pecaloc.qtt` desce, `sa.stock` inalterado, `bi.emconf = 1`); validar (`emconf = 0`, `sa.stock` desce); cancelar outra ficha (dossier apagado, `u_pecaloc` reposto). Registar cada resultado com números reais; se o pressuposto da conferência falhar, `BLOQUEIO:` com o comportamento real.
- [ ] **Step 4:** Reescrever `docs/pecas-armazem.md` e o ADR 0003 com o circuito v2 e as verificações reais; gravar, com o `ak.ps1` e o fluxo de melhoria, a lição sobre `emconf` e o stock do PHC (a base do AllKnowing estava vazia neste tema).
- [ ] **Step 5:** `dotnet test GestorManutencao.Tests` verde; `C:/_SigmaRepos/Claude-The-ALLKNOWING/tools/verificar-segredos.ps1 -Caminho C:\_SigmaRepos\GestorManutencao` (os 5 achados antigos em ficheiros existentes são anteriores a este trabalho: confirmar com o utilizador se são placeholders); revisão final do ramo com `sc-revisor` sobre `git merge-base main HEAD..HEAD` (incluir a lista de Minor do ledger).
- [ ] **Step 6:** Commit e entrega via `sc-git`; push só com `GATE:` aprovado pelo utilizador.

---

## Autorrevisão

- **Cobertura da especificação:** entrada retirada (T1); arrumação com ordem livre e quantidade proposta (T2, T3); stock disponível (T4); iniciar abre conferência, material com lugar, desconto no picking, validar, devolver, cancelar (T4, T5); PDA e câmara na ficha, ordem livre (T6); offline (T6); verificações no PHC e documentação (T7). Peças do plano (`u_tarefapeca`): T5 Step 4, com `BLOQUEIO` se a ligação exigir tabela nova.
- **Tipos:** `LugarDto`/`Lugar`, `LocalizacoesPecaDto`/`LocalizacoesPeca`, `PedidoArrumar`, `EstadoPicagem`, `PedidoMaterial.Local`, `MaterialDto.Local` coincidem entre tarefas; `IRepositorioLocalizacoes.DescontarAsync/ReporAsync` usados na T5; `IRepositorioPecas.EmConferenciaPorPecaAsync` usado em `PecaDto.EmConferencia`.
- **Pontos de atenção:** (1) pressuposto de `emconf` por confirmar (T7 Step 2 e 3); (2) onde guardar o lugar de cada saída (campo da `bi`) por confirmar na base (T5/T7); (3) `Concluida -> Validada` e a confirmação da conferência não são atómicas (T5, política decidida no Step 2); (4) SQL só compila até à T7; (5) APK e câmara/PDA por testar em aparelho.
