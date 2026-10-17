using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SetupAssistant.Helpers;

/// <summary>Мигание кнопки окна на панели задач, когда приложение ждёт действия, а окно не в фокусе.</summary>
public static class WindowFlasher
{
    private const uint FlashAll = 3;
    private const uint FlashUntilForeground = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    public static void FlashUntilActivated(Window? window)
    {
        if (window is null || window.IsActive)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = handle,
            Flags = FlashAll | FlashUntilForeground,
            Count = uint.MaxValue,
            Timeout = 0
        };
        FlashWindowEx(ref info);
    }
}
