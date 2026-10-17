using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SetupAssistant.Helpers;

/// <summary>Фон окна Mica (Windows 11, сборка 22621+). На более старых системах ничего не делает — остаётся обычный фон темы.</summary>
public static class MicaBackdrop
{
    private const int UseImmersiveDarkMode = 20;
    private const int SystemBackdropType = 38;
    private const int MicaBackdrop_Mica = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    private const int GwlStyle = -16;
    private const int WsSysMenu = 0x80000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    /// <summary>Включает Mica; возвращает false, если система его не поддерживает.</summary>
    public static bool Apply(Window window, bool dark)
    {
        if (!IsSupported)
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (handle == IntPtr.Zero || source?.CompositionTarget is null)
        {
            return false;
        }

        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref margins);

        // С Mica система рисует свои кнопки окна поверх кнопок ModernWpf — они накладываются друг на друга.
        // Без стиля WS_SYSMENU остаются только кнопки ModernWpf; сворачивание, разворачивание и Alt+F4 работают как прежде.
        SetWindowLong(handle, GwlStyle, GetWindowLong(handle, GwlStyle) & ~WsSysMenu);

        SetDark(handle, dark);
        var backdrop = MicaBackdrop_Mica;
        return DwmSetWindowAttribute(handle, SystemBackdropType, ref backdrop, sizeof(int)) == 0;
    }

    /// <summary>Тёмная/светлая отрисовка Mica и рамки; вызывается при смене темы.</summary>
    public static void SetDark(IntPtr handle, bool dark)
    {
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, sizeof(int));
    }
}
