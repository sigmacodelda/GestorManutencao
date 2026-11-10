using System.Security.Cryptography;
using System.Text;

namespace GestorManutencao.Api;

// A connection string do PHC pode vir cifrada no appsettings, no mesmo formato da LandeiroMaui (Helpers/EncriptacaoHelper): TripleDES, modo ECB,
// chave = MD5 do texto da chave, resultado em base64. A chave não está no código: vem de Seguranca:ChaveLigacao (variável de ambiente
// Seguranca__ChaveLigacao). Uma connection string em claro (com ';') passa sem mudança.
public static class LigacaoPhc
{
    public const string ChaveConfiguracao = "Seguranca:ChaveLigacao";

    public static string? Obter(IConfiguration configuracao)
    {
        var valor = configuracao.GetConnectionString("PHCConn");
        if (string.IsNullOrWhiteSpace(valor) || valor.Contains(';')) return valor;
        var chave = configuracao[ChaveConfiguracao];
        if (string.IsNullOrWhiteSpace(chave)) throw new RepositorioIndisponivelException();
        try { return Desencriptar(valor, chave); }
        catch (Exception ex) when (ex is FormatException or CryptographicException or InvalidOperationException) { throw new RepositorioIndisponivelException(ex); }
    }

    public static string Desencriptar(string textoCifrado, string chave)
    {
        var dados = Convert.FromBase64String(textoCifrado.Trim());
        using var md5 = MD5.Create();
        using var tripleDes = TripleDES.Create();
        tripleDes.Key = md5.ComputeHash(Encoding.UTF8.GetBytes(chave));
        tripleDes.Mode = CipherMode.ECB;
        tripleDes.Padding = PaddingMode.PKCS7;
        using var desencriptador = tripleDes.CreateDecryptor();
        var texto = Encoding.UTF8.GetString(desencriptador.TransformFinalBlock(dados, 0, dados.Length)).Trim();
        if (texto.StartsWith("Server=", StringComparison.OrdinalIgnoreCase)) return texto;

        // Formato antigo da LandeiroMaui: servidor, base, (3.ª linha ignorada), utilizador e palavra-passe, uma por linha.
        var partes = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Replace("\r", "").Trim()).ToArray();
        if (partes.Length < 5) throw new InvalidOperationException("Connection string desencriptada incompleta.");
        return $"Server={partes[0]};Database={partes[1]};User Id={partes[3]};Password={partes[4]};TrustServerCertificate=True;"; // sigma-ok: monta o texto com os campos desencriptados, sem valor real
    }
}
