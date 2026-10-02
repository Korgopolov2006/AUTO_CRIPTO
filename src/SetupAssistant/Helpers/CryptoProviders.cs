using System.Runtime.InteropServices;
using System.Text;

namespace SetupAssistant.Helpers;

/// <summary>Перечисление криптопровайдеров, зарегистрированных в Windows (CryptEnumProviders).</summary>
public static class CryptoProviders
{
    private const int ErrorNoMoreItems = 259;
    private const uint MaxProviders = 256;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CryptEnumProvidersW")]
    private static extern bool CryptEnumProviders(
        uint dwIndex, IntPtr pdwReserved, uint dwFlags, out uint pdwProvType, StringBuilder? pszProvName, ref uint pcbProvName);

    public static IReadOnlyList<(uint Type, string Name)> Enumerate()
    {
        var providers = new List<(uint, string)>();

        for (uint index = 0; index < MaxProviders; index++)
        {
            uint length = 0;
            if (!CryptEnumProviders(index, IntPtr.Zero, 0, out var type, null, ref length))
            {
                if (Marshal.GetLastWin32Error() == ErrorNoMoreItems)
                {
                    break;
                }

                continue;
            }

            var name = new StringBuilder((int)length);
            if (CryptEnumProviders(index, IntPtr.Zero, 0, out type, name, ref length))
            {
                providers.Add((type, name.ToString()));
            }
        }

        return providers;
    }
}
