# GestorManutencao

App do Gestor de Manutenção (operador e supervisor) em .NET MAUI Blazor Hybrid. Uma só aplicação para Android e Windows.

- `GestorManutencao.App`: a aplicação MAUI (Android + Windows).
- `GestorManutencao.UI`: ecrãs, estilos, temas por cliente e serviços de sessão (biblioteca usada pela app).
- `GestorManutencao.Tests`: testes da sessão e dos temas.

## Tema por cliente

Cada cliente é um JSON em `GestorManutencao.UI/Temas/` (cores em claro e escuro, nome, logótipo). O cliente da compilação define-se em `TemaCliente`, em `GestorManutencao.App/MauiProgram.cs` (hoje `landeiro`). Para um cliente novo: copiar um JSON, trocar as cores e o logótipo (em `wwwroot/temas/<cliente>/`) e mudar a constante. Os testes validam as chaves e o contraste.

## Login

Só em Debug, com utilizadores fictícios (`GestorManutencao.UI/Dados/utilizadores-teste.json`). A autenticação real (`IAutenticacaoService`) depende da decisão D-10 do planeamento.

```bash
dotnet build GestorManutencao.App -f net10.0-windows10.0.19041.0
dotnet build GestorManutencao.App -f net10.0-android
dotnet test GestorManutencao.Tests
```
