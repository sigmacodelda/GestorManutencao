# Circuito de testes ponta a ponta

API e app Windows testadas **em simultâneo**: duas pistas a correr ao mesmo tempo e ligadas entre si.

- **Pista API** (HTTP, supervisor e técnico): permissões, regra "o técnico só abre avarias curativas", e o que o supervisor vê (fichas, validação, contadores) enquanto o técnico trabalha na app.
- **Pista APP** (Playwright por CDP sobre o WebView2 da app Windows real): entra com o PIN pelos botões, regista a avaria, junta material, conclui, valida e devolve, percorre todas as áreas do supervisor e termina a sessão.

O que o técnico faz na app aparece ao supervisor na API, e o que o supervisor faz na app (validar, devolver) chega ao técnico. Cobre: login, perfis, avaria A (técnico na app → supervisor valida na app), avaria B (técnico na API → supervisor devolve na app → técnico conclui outra vez na app → supervisor valida na API), planeada (opcional), equipamentos e todos os mosaicos do supervisor.

## Correr

Em modo memória (sem base de dados; serve para validar o próprio circuito):

```bash
powershell -NoProfile -File Testes-Circuito/correr-circuito.ps1 -Memoria
```

Contra a base de testes, com as variáveis definidas por si na sessão (nunca em ficheiros versionados):

```bash
powershell -NoProfile -File Testes-Circuito/correr-circuito.ps1
```

Variáveis: `ConnectionStrings__PHCConn` (testes_land2), `Pin__Pepper` (o mesmo com que se criaram os PINs de `u_manutil`), `PIN_TECNICO`, `PIN_SUPERVISOR` (PINs definitivos). O script confirma `SELECT DB_NAME()` = `testes_land2` antes de qualquer escrita e pára se for outra. `-ComPlaneada` inclui começar e concluir uma planeada real do técnico (altera dados da base de testes); `-SemLimpeza` guarda as fichas criadas.

Resultados em `Testes-Circuito/resultados/<execução>/` (`relatorio.md`, `resultados.json`, capturas das falhas). Os testes de unidade e de API em memória continuam em `dotnet test GestorManutencao.Tests`.

## Limpeza

Depois do circuito (com a base real), apaga só as fichas criadas por esta execução (ids em `fichas-criadas.json`) cujo problema começa por `TESTE-E2E-`, e o material ligado. As planeadas reais tocadas com `-ComPlaneada` não se apagam (não têm o marcador).

## Notas

- A app Windows lê `GESTOR_API_URL` só em Debug e só para endereços locais (aceita o certificado de desenvolvimento apenas aí).
- O script só termina os processos que arrancou. A sessão guardada da app (se existir) é terminada no início.
- Não cobre o toque físico nem o Android: o WebView2 do Windows não substitui o teste no telemóvel.
