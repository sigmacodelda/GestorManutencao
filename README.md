# GestorManutencao

App do Gestor de Manutenção (operador e supervisor) em .NET MAUI Blazor Hybrid. Uma só aplicação para Android e Windows.

- `GestorManutencao.App`: a aplicação MAUI (Android + Windows).
- `GestorManutencao.UI`: ecrãs, estilos, temas por cliente e serviços de sessão (biblioteca usada pela app).
- `GestorManutencao.Tests`: testes da sessão e dos temas.

## Tema por cliente

Cada cliente é um JSON em `GestorManutencao.UI/Temas/` (cores em claro e escuro, nome, logótipo). O cliente da compilação define-se em `TemaCliente`, em `GestorManutencao.App/MauiProgram.cs` (hoje `landeiro`). Para um cliente novo: copiar um JSON, trocar as cores e o logótipo (em `wwwroot/temas/<cliente>/`) e mudar a constante. Os testes validam as chaves e o contraste.

## Login

Só por PIN (6 dígitos), sem escolher utilizador. A app envia o PIN à `GestorManutencao.Api`, que o confere (hash com pepper) na tabela `u_manutil` do PHC e devolve um token. O supervisor cria os técnicos e o número de utilizador PHC de cada um (`tecno`); o primeiro PIN tem de ser mudado no primeiro acesso. Há limite de tentativas por PIN e por IP.

Com `ApiUrl` vazio em `GestorManutencao.App/MauiProgram.cs` a app corre em Debug com dados fictícios (`GestorManutencao.UI/Dados/utilizadores-teste.json`). Com `ApiUrl` preenchido (tem de ser https) usa a API real.

## Intervenções

Segue o circuito de manutenção da LandeiroMaui, na mesma tabela do PHC (`u_fichaint`): estados Planeada, EmCurso, Concluida, Validada e Cancelada. O técnico inicia, junta o material usado e conclui; o supervisor valida ou devolve com motivo, atribui técnicos nas Planeadas e gera as planeadas seguintes. As Planeadas mostram a última intervenção e "Fora de prazo desde" quando a data prevista já passou. Detalhe em `docs/ligacao-phc-intervencoes.md` e `docs/tabela-utilizadores.md`; o script do campo `tecno` está em `docs/criar-u_manutil.sql`.

## API

`GestorManutencao.Api` (.NET 10). Em Production precisa de `ConnectionStrings:PHCConn`, `Jwt` e `Pin:Pepper` no `appsettings` (nunca no código).

```bash
dotnet build GestorManutencao.App -f net10.0-windows10.0.19041.0
dotnet build GestorManutencao.App -f net10.0-android
dotnet test GestorManutencao.Tests
```
