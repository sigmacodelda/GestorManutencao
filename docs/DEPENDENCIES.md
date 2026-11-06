# Dependências

| Pacote | Versão | Projeto | Para quê | Alternativa | Licença |
|---|---|---|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.0 | GestorManutencao.Api | Validar os tokens emitidos no login | Autenticação própria (mais código e mais risco) | MIT |
| Microsoft.Data.SqlClient | última 6.x | GestorManutencao.Api | Abrir ligação ao SQL Server do PHC com o login do utilizador | System.Data.SqlClient (descontinuado) | MIT |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.0 | GestorManutencao.Tests | Testar a API em memória | Subir a API à mão nos testes | MIT |
| xunit | do template | GestorManutencao.Tests | Testes | NUnit | Apache-2.0 |
| System.Security.Cryptography.Pkcs | 10.0.14 | GestorManutencao.Api | Fixa a versão corrigida (a 9.0.18 vinda do SqlClient tem vulnerabilidades conhecidas) | Esperar nova versão do SqlClient | MIT |

Fontes (OFL): Exo 2 e Open Sans, em `GestorManutencao.UI/wwwroot/fonts`, para funcionarem sem internet.
