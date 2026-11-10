using System.Runtime.CompilerServices;

namespace GestorManutencao.Tests;

// Os testes usam sempre repositórios em memória: a connection string do programador (appsettings.Development.json) nunca chega à base.
// Um espaço (e não vazio) porque no Windows definir uma variável de ambiente vazia apaga-a.
internal static class AmbienteDeTestes
{
    [ModuleInitializer]
    internal static void Iniciar() => Environment.SetEnvironmentVariable("ConnectionStrings__PHCConn", " ");
}
