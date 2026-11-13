# Dependências

| Pacote | Versão | Projeto | Para quê | Alternativa | Licença |
|---|---|---|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.0 | GestorManutencao.Api | Validar os tokens emitidos no login | Autenticação própria (mais código e mais risco) | MIT |
| Microsoft.Data.SqlClient | última 6.x | GestorManutencao.Api | Abrir ligação ao SQL Server do PHC com o login do utilizador | System.Data.SqlClient (descontinuado) | MIT |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.0 | GestorManutencao.Tests | Testar a API em memória | Subir a API à mão nos testes | MIT |
| xunit | do template | GestorManutencao.Tests | Testes | NUnit | Apache-2.0 |
| System.Security.Cryptography.Pkcs | 10.0.14 | GestorManutencao.Api | Fixa a versão corrigida (a 9.0.18 vinda do SqlClient tem vulnerabilidades conhecidas) | Esperar nova versão do SqlClient | MIT |
| Microsoft.AspNetCore.SignalR.Client | 10.0.14 | GestorManutencao.UI | Canal em tempo real com a API: a app é avisada no instante em que as fichas mudam (notificações e listas sem esperar) | Verificação periódica mais rápida (mais pedidos e bateria, nunca instantânea) | MIT |
| sqlite-net-pcl | 1.11.285 | GestorManutencao.Armazenamento (usado pela App) | Base SQLite local (cache e fila de envio) para o modo offline. Traz SQLitePCLRaw 3.0.3 e SourceGear.sqlite3 3.53.3 (SQLite atual); a 1.9.172 foi posta de parte porque o SQLitePCLRaw.lib.e_sqlite3 2.1.x traz um SQLite com a vulnerabilidade CVE-2025-6965 (GHSA-2m69-gcr7-jv3q) | Microsoft.Data.Sqlite (mais pesada em MAUI); ficheiros JSON (sem consultas nem transações) | MIT (SQLitePCLRaw: Apache-2.0; SQLite: domínio público) |
| FirebaseAdmin | 3.7.1 | GestorManutencao.Api | Enviar o push "acordar a app" (FCM) só com sinal, sem texto nem dados pessoais | Chamar a API HTTP v1 do FCM à mão (obriga a tratar OAuth e erros) | Apache-2.0 |
| Xamarin.Firebase.Messaging | 125.1.3 | GestorManutencao.App (só Android) | Receber o push FCM de dados que acorda a app (mesmo fechada) para verificar as fichas e mostrar os avisos | Só o canal SignalR e o serviço em primeiro plano (o Android pode matá-los com a app fechada) | Apache-2.0 |
| Xamarin.AndroidX.Fragment.Ktx | 1.9.1 | GestorManutencao.App (só Android) | Alinha com o Fragment 1.9 do MAUI; sem isto o Firebase traz a 1.8 e o dex falha (FragmentKt duplicado) | Nenhuma (é só uma versão fixa) | Apache-2.0 |

Fontes (OFL): Exo 2 e Open Sans, em `GestorManutencao.UI/wwwroot/fonts`, para funcionarem sem internet.

## Testes-Circuito (apenas desenvolvimento)

- `playwright-core` 1.65.0 (npm): liga-se ao WebView2 da app Windows por CDP para carregar nos botões. Só testes; não entra na app nem na API. Licença Apache-2.0.
