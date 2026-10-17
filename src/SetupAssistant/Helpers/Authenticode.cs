using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace SetupAssistant.Helpers;

/// <summary>Проверка цифровой подписи Authenticode у скачанных установщиков (WinVerifyTrust + издатель).</summary>
public static class Authenticode
{
    public readonly record struct Result(bool IsValid, string? Signer, string Message);

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdRevocationCheckNone = 0x10;

    /// <param name="expectedSigner">Подстрока из имени владельца подписи (например «CRYPTO-PRO»); пусто — издатель не проверяется.</param>
    public static Result Verify(string path, string? expectedSigner)
    {
        string signer;
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            signer = certificate.Subject;
        }
        catch (Exception)
        {
            return new Result(false, null, "файл не подписан");
        }

        var status = WinVerify(path);
        if (status != 0)
        {
            return new Result(false, signer, $"подпись недействительна (код 0x{status:X8})");
        }

        if (!string.IsNullOrWhiteSpace(expectedSigner)
            && !signer.Contains(expectedSigner, StringComparison.OrdinalIgnoreCase))
        {
            return new Result(false, signer, $"файл подписан другим издателем: {signer}");
        }

        return new Result(true, signer, "подпись действительна");
    }

    private static int WinVerify(string path)
    {
        var fileInfoSize = Marshal.SizeOf<WinTrustFileInfo>();
        var dataSize = Marshal.SizeOf<WinTrustData>();
        var pFile = Marshal.AllocHGlobal(fileInfoSize);
        var pData = Marshal.AllocHGlobal(dataSize);

        try
        {
            Marshal.StructureToPtr(
                new WinTrustFileInfo { cbStruct = (uint)fileInfoSize, pcwszFilePath = path }, pFile, false);

            Marshal.StructureToPtr(
                new WinTrustData
                {
                    cbStruct = (uint)dataSize,
                    dwUIChoice = WtdUiNone,
                    fdwRevocationChecks = WtdRevokeNone,
                    dwUnionChoice = WtdChoiceFile,
                    pFile = pFile,
                    dwProvFlags = WtdRevocationCheckNone
                },
                pData,
                false);

            return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, pData);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
            Marshal.FreeHGlobal(pFile);
            Marshal.FreeHGlobal(pData);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;

        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
