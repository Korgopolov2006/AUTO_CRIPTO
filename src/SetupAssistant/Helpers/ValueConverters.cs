using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SetupAssistant.Helpers;

/// <summary>Инвертирует bool: удобно для IsEnabled="{Binding IsBusy, Converter=...}".</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Целое число больше нуля → true.</summary>
public sealed class IsPositiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int number && number > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null → Collapsed, иначе Visible.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Уровень журнала → короткая подпись на русском.</summary>
public sealed class LogLevelToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        Microsoft.Extensions.Logging.LogLevel.Warning => "Внимание",
        Microsoft.Extensions.Logging.LogLevel.Error => "Ошибка",
        Microsoft.Extensions.Logging.LogLevel.Critical => "Критично",
        Microsoft.Extensions.Logging.LogLevel.Debug or Microsoft.Extensions.Logging.LogLevel.Trace => "Отладка",
        _ => "Инфо"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
