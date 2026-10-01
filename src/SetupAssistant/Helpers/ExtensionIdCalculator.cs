using System.IO;
using System.Security.Cryptography;

namespace SetupAssistant.Helpers;

/// <summary>
/// Вычисление ID расширения Chromium из ключа упаковки (.pem):
/// первые 16 байт SHA-256 от SubjectPublicKeyInfo, каждый полубайт кодируется буквами a–p.
/// </summary>
public static class ExtensionIdCalculator
{
    private const int IdByteLength = 16;

    /// <summary>Возвращает 32-символьный ID расширения либо null, если файл отсутствует или не является ключом RSA.</summary>
    public static string? TryComputeFromPemFile(string pemFilePath)
    {
        try
        {
            if (!File.Exists(pemFilePath))
            {
                return null;
            }

            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(pemFilePath));

            var hash = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());

            Span<char> id = stackalloc char[IdByteLength * 2];
            for (var i = 0; i < IdByteLength; i++)
            {
                id[i * 2] = (char)('a' + (hash[i] >> 4));
                id[i * 2 + 1] = (char)('a' + (hash[i] & 0x0F));
            }

            return new string(id);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
