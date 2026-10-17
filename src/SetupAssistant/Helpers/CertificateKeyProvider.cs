using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace SetupAssistant.Helpers;

/// <summary>Провайдер закрытого ключа, к которому привязан сертификат (свойство CERT_KEY_PROV_INFO).</summary>
public static class CertificateKeyProvider
{
    private const uint CertKeyProvInfoPropId = 2;

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CertGetCertificateContextProperty(IntPtr certContext, uint propertyId, IntPtr data, ref uint dataSize);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CryptKeyProvInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string ContainerName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string ProviderName;

        public uint ProviderType;
        public uint Flags;
        public uint ParamCount;
        public IntPtr Params;
        public uint KeySpec;
    }

    /// <summary>Имя провайдера из свойства сертификата или null, если сертификат не привязан к ключу.</summary>
    public static string? GetProviderName(X509Certificate2 certificate)
    {
        try
        {
            uint size = 0;
            if (!CertGetCertificateContextProperty(certificate.Handle, CertKeyProvInfoPropId, IntPtr.Zero, ref size) || size == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!CertGetCertificateContextProperty(certificate.Handle, CertKeyProvInfoPropId, buffer, ref size))
                {
                    return null;
                }

                return Marshal.PtrToStructure<CryptKeyProvInfo>(buffer).ProviderName;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Сертификат привязан к ключу криптопровайдера КриптоПро.</summary>
    public static bool IsCryptoPro(X509Certificate2 certificate) =>
        GetProviderName(certificate)?.Contains("Crypto-Pro", StringComparison.OrdinalIgnoreCase) == true;
}
