using System.Globalization;
using System.Windows.Data;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>Преобразует <see cref="StatusLevel"/> в понятный пользователю текст на русском языке.</summary>
public sealed class StatusLevelToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            StatusLevel.Ok => "Готово",
            StatusLevel.Warning => "Предупреждение",
            StatusLevel.Error => "Ошибка",
            StatusLevel.InProgress => "Выполняется...",
            _ => "Не проверено"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
