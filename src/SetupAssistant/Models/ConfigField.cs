using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SetupAssistant.Models;

/// <summary>Вид поля в редакторе конфигурации.</summary>
public enum ConfigFieldKind
{
    /// <summary>Строка (путь к файлу).</summary>
    Text,

    /// <summary>Номер версии вида 5.0.12000.</summary>
    Version,

    /// <summary>Целое число в заданных границах.</summary>
    Number,

    /// <summary>Переключатель «да / нет».</summary>
    Toggle
}

/// <summary>Одно редактируемое значение Config.json: что это, где лежит в файле и можно ли его сохранить.</summary>
public sealed partial class ConfigField : ObservableObject
{
    private static readonly Regex VersionPattern = new(@"^\d+(\.\d+){0,3}$", RegexOptions.Compiled);

    private readonly Func<string, string?>? _warn;
    private string _originalText;
    private bool _originalChecked;

    public ConfigField(
        string title, string hint, string[] path, ConfigFieldKind kind, string text, bool isChecked = false,
        int min = 0, int max = int.MaxValue, bool allowEmpty = false, Func<string, string?>? warn = null)
    {
        Title = title;
        Hint = hint;
        Path = path;
        Kind = kind;
        Min = min;
        Max = max;
        AllowEmpty = allowEmpty;
        _warn = warn;
        _originalText = text;
        _originalChecked = isChecked;
        _text = text;
        _isChecked = isChecked;
        Validate();
    }

    public string Title { get; }

    public string Hint { get; }

    public string[] Path { get; }

    public ConfigFieldKind Kind { get; }

    public int Min { get; }

    public int Max { get; }

    public bool AllowEmpty { get; }

    public bool IsToggle => Kind == ConfigFieldKind.Toggle;

    public bool IsEditable => Kind != ConfigFieldKind.Toggle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private string _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private bool _isChecked;

    /// <summary>Что не так со значением; null — значение можно сохранять.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>Замечание, которое не мешает сохранить (например, файла по этому пути пока нет).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private string? _warning;

    public bool HasError => Error is not null;

    public bool HasWarning => Warning is not null && Error is null;

    public bool IsChanged => IsToggle ? IsChecked != _originalChecked : !string.Equals(Text, _originalText, StringComparison.Ordinal);

    partial void OnTextChanged(string value) => Validate();

    /// <summary>Текущее значение для записи в файл: bool, int или string.</summary>
    public object ToValue() => Kind switch
    {
        ConfigFieldKind.Toggle => IsChecked,
        ConfigFieldKind.Number => int.Parse(Text.Trim(), System.Globalization.CultureInfo.InvariantCulture),
        _ => Text.Trim()
    };

    /// <summary>Считать текущее значение сохранённым.</summary>
    public void Commit()
    {
        _originalText = Text;
        _originalChecked = IsChecked;
        OnPropertyChanged(nameof(IsChanged));
    }

    /// <summary>Вернуть значение, каким оно было при открытии или последнем сохранении.</summary>
    public void Revert()
    {
        Text = _originalText;
        IsChecked = _originalChecked;
    }

    private void Validate()
    {
        Warning = null;
        var value = (Text ?? string.Empty).Trim();

        switch (Kind)
        {
            case ConfigFieldKind.Version:
                Error = value.Length == 0
                    ? (AllowEmpty ? null : "Укажите версию, например 5.0.12000")
                    : VersionPattern.IsMatch(value) ? null : "Версия пишется цифрами через точку, например 5.0.12000";
                break;

            case ConfigFieldKind.Number:
                Error = int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
                        && number >= Min && number <= Max
                    ? null
                    : $"Введите целое число от {Min} до {Max}";
                break;

            case ConfigFieldKind.Text:
                Error = null;
                if (value.Length > 0)
                {
                    Warning = _warn?.Invoke(value);
                }

                break;

            default:
                Error = null;
                break;
        }
    }
}
