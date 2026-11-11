# GestorManutencao

App do Gestor de Manutenção (operador e supervisor) em .NET MAUI Blazor Hybrid. Uma só aplicação para Android e Windows.

- `GestorManutencao.App`: a aplicação MAUI (Android + Windows).
- `GestorManutencao.UI`: ecrãs, estilos, temas por cliente e serviços de sessão (biblioteca usada pela app).
- `GestorManutencao.Tests`: testes da sessão e dos temas.

## Tema por cliente

Cada cliente é um JSON em `GestorManutencao.UI/Temas/` (cores em claro e escuro, nome, logótipo). O cliente da compilação define-se em `TemaCliente`, em `GestorManutencao.App/MauiProgram.cs` (hoje `landeiro`). Para um cliente novo: copiar um JSON, trocar as cores e o logótipo (em `wwwroot/temas/<cliente>/`) e mudar a constante. Os testes validam as chaves e o contraste.

## Login

Só por PIN (4 dígitos), sem escolher utilizador. A app envia o PIN à `GestorManutencao.Api`, que o confere (hash com pepper) na tabela `u_manutil` do PHC e devolve um token. O supervisor cria os técnicos e o número de utilizador PHC de cada um (`tecno`); o primeiro PIN tem de ser mudado no primeiro acesso. Há limite de tentativas por PIN e por IP.

Com `ApiUrl` vazio em `GestorManutencao.App/MauiProgram.cs` a app corre em Debug com dados fictícios (`GestorManutencao.UI/Dados/utilizadores-teste.json`). Com `ApiUrl` preenchido (tem de ser https) usa a API real.

## Intervenções

Segue o circuito de manutenção da LandeiroMaui, na mesma tabela do PHC (`u_fichaint`): estados Planeada, EmCurso, Concluida, Validada e Cancelada. O técnico inicia, junta o material usado e conclui; o supervisor valida ou devolve com motivo, atribui técnicos nas Planeadas e gera as planeadas seguintes. As Planeadas mostram a última intervenção e "Fora de prazo desde" quando a data prevista já passou. Detalhe em `docs/ligacao-phc-intervencoes.md` e `docs/tabela-utilizadores.md`; o script do campo `tecno` está em `docs/criar-u_manutil.sql`.

## Notificações

Avisos locais (Android e Windows) mais um centro de notificações na app (`/notificacoes`, ícone "Avisos"). São derivados do estado das fichas que a app já consulta, de 60 em 60 segundos e ao voltar ao primeiro plano; nada é gravado no servidor nem há Firebase. A lista (máximo 50) e as chaves já vistas ficam no aparelho, por utilizador; a primeira vez que um utilizador entra serve de linha de base (entra no centro como lida, sem aviso do sistema). A ficha devolvida mostra o motivo (`Motivo`, vindo de `valobs`, só em fichas em curso).

Limite: só avisa com a app aberta ou em segundo plano. Com a app fechada não há aviso (exigiria push). No Android 13 ou superior a permissão de notificações é pedida ao abrir; no Windows sem pacote, se o registo das notificações do sistema falhar, a app mostra uma faixa por dentro.

## API

`GestorManutencao.Api` (.NET 10). Em Production precisa de `ConnectionStrings:PHCConn`, `Jwt` e `Pin:Pepper` no `appsettings` (nunca no código).

```bash
dotnet build GestorManutencao.App -f net10.0-windows10.0.19041.0
dotnet build GestorManutencao.App -f net10.0-android
dotnet test GestorManutencao.Tests
```
