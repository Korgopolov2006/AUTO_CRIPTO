using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>Преобразует <see cref="StatusLevel"/> в цвет индикатора состояния для UI.</summary>
public sealed class StatusLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            StatusLevel.Ok => new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
            StatusLevel.Warning => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            StatusLevel.Error => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
            StatusLevel.InProgress => new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            _ => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
