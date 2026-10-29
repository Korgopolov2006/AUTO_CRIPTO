using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Группа полей редактора конфигурации с заголовком и пояснением.</summary>
public sealed record ConfigGroup(string Title, string Description, IReadOnlyList<ConfigField> Fields);

/// <summary>
/// Редактор конфигурации: самые нужные параметры Config.json меняются прямо в приложении. Сохранение вносит точечные правки
/// в файл Config\Config.json рядом с программой (комментарии сохраняются), прежний файл остаётся как Config.json.bak.
/// Параметры тихой установки (SilentArgs) здесь намеренно не редактируются.
/// </summary>
public partial class ConfigEditorViewModel : ViewModelBase, INavigationAware
{
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigationService;

    private List<ConfigField> _fields = new();

    public ObservableCollection<ConfigGroup> Groups { get; } = new();

    public string ConfigSource => _configService.ConfigSource;

    public string FilePath => _configService.EditableConfigPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardCommand))]
    private bool _hasChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasErrors;

    [ObservableProperty]
    private bool _canRestoreBackup;

    public ConfigEditorViewModel(
        IConfigService configService,
        IDialogService dialogService,
        INotificationService notifications,
        INavigationService navigationService)
    {
        _configService = configService;
        _dialogService = dialogService;
        _notifications = notifications;
        _navigationService = navigationService;
    }

    public void OnNavigatedTo() => LoadFields();

    public void OnNavigatedFrom()
    {
    }

    private void LoadFields()
    {
        foreach (var field in _fields)
        {
            field.PropertyChanged -= OnFieldChanged;
        }

        var c = _configService.Current;
        string? NotFound(string path) =>
            _configService.GetDistributionSize(path) is null ? "Файл по этому пути не найден: установка этого компонента не получится." : null;

        ConfigField Version(string title, string hint, string section, string name, string value, bool allowEmpty = false) =>
            new(title, hint, new[] { section, name }, ConfigFieldKind.Version, value ?? string.Empty, allowEmpty: allowEmpty);

        ConfigField Toggle(string title, string hint, string[] path, bool value) =>
            new(title, hint, path, ConfigFieldKind.Toggle, string.Empty, isChecked: value);

        ConfigField Number(string title, string hint, string[] path, int value, int min, int max) =>
            new(title, hint, path, ConfigFieldKind.Number, value.ToString(System.Globalization.CultureInfo.InvariantCulture), min: min, max: max);

        ConfigField Installer(string title, string name, string value) =>
            new(title, "Путь к установочному файлу относительно папки программы.", new[] { "Installers", name, "Path" },
                ConfigFieldKind.Text, value ?? string.Empty, allowEmpty: true, warn: NotFound);

        const string emptyHint = "Можно оставить пустым: тогда с рекомендуемой версией не сравнивается.";
        const string minHint = "Ниже этой версии компонент считается неисправным.";
        const string recHint = "Ниже этой версии показывается предупреждение «можно обновить».";

        var groups = new List<ConfigGroup>
        {
            new("Допустимые и рекомендуемые версии",
                "Диагностика сравнивает установленные версии с этими числами: ниже допустимой — ошибка, ниже рекомендуемой — предупреждение.",
                new[]
                {
                    Version("КриптоПро CSP: допустимо от", minHint, "MinVersions", "CryptoProCsp", c.MinVersions.CryptoProCsp),
                    Version("КриптоПро CSP: рекомендуется", recHint, "RecommendedVersions", "CryptoProCsp", c.RecommendedVersions.CryptoProCsp, true),
                    Version("КриптоПро Browser Plugin: допустимо от", minHint, "MinVersions", "CryptoProBrowserPlugin", c.MinVersions.CryptoProBrowserPlugin),
                    Version("КриптоПро Browser Plugin: рекомендуется", recHint, "RecommendedVersions", "CryptoProBrowserPlugin", c.RecommendedVersions.CryptoProBrowserPlugin, true),
                    Version("Chromium GOST: допустимо от", minHint, "MinVersions", "ChromiumGost", c.MinVersions.ChromiumGost),
                    Version("Chromium GOST: рекомендуется", emptyHint, "RecommendedVersions", "ChromiumGost", c.RecommendedVersions.ChromiumGost, true),
                    Version("Контур.Плагин: допустимо от", minHint, "MinVersions", "KonturPlugin", c.MinVersions.KonturPlugin),
                    Version("Контур.Плагин: рекомендуется", emptyHint, "RecommendedVersions", "KonturPlugin", c.RecommendedVersions.KonturPlugin, true)
                }),

            new("Обязательные компоненты",
                "Что должно стоять на рабочем месте. Отметьте нужное именно вам.",
                new[]
                {
                    Toggle("КриптоПро CSP", string.Empty, new[] { "RequiredComponents", "CryptoProCsp" }, c.RequiredComponents.CryptoProCsp),
                    Toggle("КриптоПро Browser Plugin", string.Empty, new[] { "RequiredComponents", "CryptoProBrowserPlugin" }, c.RequiredComponents.CryptoProBrowserPlugin),
                    Toggle("Chromium GOST", string.Empty, new[] { "RequiredComponents", "ChromiumGost" }, c.RequiredComponents.ChromiumGost),
                    Toggle("Контур.Плагин", string.Empty, new[] { "RequiredComponents", "KonturPlugin" }, c.RequiredComponents.KonturPlugin),
                    Toggle("Плагин Госуслуг", string.Empty, new[] { "RequiredComponents", "GosuslugiPlugin" }, c.RequiredComponents.GosuslugiPlugin),
                    Toggle("Драйверы Рутокен", string.Empty, new[] { "RequiredComponents", "RutokenDriver" }, c.RequiredComponents.RutokenDriver),
                    Toggle("Драйвер JaCarta", string.Empty, new[] { "RequiredComponents", "JaCartaDriver" }, c.RequiredComponents.JaCartaDriver)
                }),

            new("Установочные файлы",
                "Где лежат дистрибутивы. Если положили файл новой версии, укажите его путь здесь. Для Chromium GOST и Контур.Плагина это запасной файл на случай, когда нет интернета.",
                new[]
                {
                    Installer("КриптоПро CSP", "CryptoProCsp", c.Installers.CryptoProCsp.Path),
                    Installer("КриптоПро Browser Plugin", "CryptoProBrowserPlugin", c.Installers.CryptoProBrowserPlugin.Path),
                    Installer("Chromium GOST", "ChromiumGost", c.Installers.ChromiumGost.Path),
                    Installer("Контур.Плагин", "KonturPlugin", c.Installers.KonturPlugin.Path),
                    Installer("Плагин Госуслуг", "GosuslugiPlugin", c.Installers.GosuslugiPlugin.Path),
                    Installer("Драйверы Рутокен", "RutokenDriver", c.Installers.RutokenDriver.Path),
                    Installer("Драйвер JaCarta", "JaCartaDriver", c.Installers.JaCartaDriver.Path),
                    Installer("Континент-АП", "KontinentAp", c.Installers.KontinentAp.Path)
                }),

            new("Проверки и журнал",
                string.Empty,
                new[]
                {
                    Number("Ожидание носителя, секунд", "Сколько мастер ждёт, пока вы подключите токен.", new[] { "CheckSettings", "TokenWaitTimeoutSeconds" }, c.CheckSettings.TokenWaitTimeoutSeconds, 5, 600),
                    Number("Предупреждать об истечении сертификата за, дней", "За сколько дней до окончания срока сертификат помечается предупреждением.", new[] { "CheckSettings", "CertificateExpiryWarningDays" }, c.CheckSettings.CertificateExpiryWarningDays, 1, 365),
                    Number("Хранить журнал, дней", "Более старые файлы журнала удаляются.", new[] { "Logging", "RetainDays" }, c.Logging.RetainDays, 1, 365),
                    Toggle("Проверять новые версии расширений при запуске", "Без интернета проверка тихо пропускается.", new[] { "Extensions", "CheckUpdatesOnStartup" }, c.Extensions.CheckUpdatesOnStartup)
                })
        };

        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        _fields = groups.SelectMany(g => g.Fields).ToList();
        foreach (var field in _fields)
        {
            field.PropertyChanged += OnFieldChanged;
        }

        UpdateState();
        OnPropertyChanged(nameof(ConfigSource));
        OnPropertyChanged(nameof(FilePath));
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigField.IsChanged) or nameof(ConfigField.HasError))
        {
            UpdateState();
        }
    }

    private void UpdateState()
    {
        HasChanges = _fields.Any(f => f.IsChanged);
        HasErrors = _fields.Any(f => f.HasError);
        CanRestoreBackup = File.Exists(FilePath + ".bak");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var changed = _fields.Where(f => f.IsChanged).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        try
        {
            var missing = _configService.Update(changed.Select(f => new ConfigEdit(f.Path, f.ToValue())));
            var failed = changed.Where(f => missing.Contains(string.Join(".", f.Path))).ToList();

            foreach (var field in changed.Except(failed))
            {
                field.Commit();
            }

            UpdateState();
            OnPropertyChanged(nameof(ConfigSource));

            if (failed.Count > 0)
            {
                _dialogService.ShowWarning(
                    "Эти параметры не нашлись в вашем Config.json, поэтому не сохранены:\n• " +
                    string.Join("\n• ", failed.Select(f => f.Title)) +
                    "\n\nОстальное сохранено. Откройте файл через «Показать файл» и добавьте недостающие строки или возьмите новый Config.json из дистрибутива программы.",
                    "Сохранено не всё");
                return;
            }

            _notifications.Show(
                ToastKind.Success,
                "Конфигурация сохранена",
                "Откройте «Главную» и нажмите «Проверить снова», чтобы новые значения учлись.");
        }
        catch (Exception ex)
        {
            var problem = ErrorAdvisor.FromException("Не удалось сохранить конфигурацию", ex, canRetry: true);
            if (_dialogService.ShowProblem(problem) == ProblemChoice.Retry)
            {
                Save();
            }
        }
    }

    private bool CanSave() => HasChanges && !HasErrors;

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void Discard()
    {
        foreach (var field in _fields)
        {
            field.Revert();
        }

        UpdateState();
    }

    /// <summary>Возвращает файл, каким он был до последнего сохранения.</summary>
    [RelayCommand]
    private void RestoreBackup()
    {
        var backup = FilePath + ".bak";
        if (!File.Exists(backup))
        {
            return;
        }

        if (!_dialogService.ConfirmChoice(
                "Конфигурация вернётся к состоянию до последнего сохранения. Текущие значения в файле будут заменены.",
                "Вернуть прежнюю конфигурацию?", "Вернуть", "Не надо"))
        {
            return;
        }

        try
        {
            File.Copy(backup, FilePath, overwrite: true);
            _configService.Load();
            LoadFields();
            _notifications.Show(ToastKind.Success, "Прежняя конфигурация возвращена");
        }
        catch (Exception ex)
        {
            _dialogService.ShowProblem(ErrorAdvisor.FromException("Не удалось вернуть прежнюю конфигурацию", ex));
        }
    }

    /// <summary>Показывает файл в проводнике; если его ещё нет (конфигурация внутри exe) — папку, куда он будет сохранён.</summary>
    [RelayCommand]
    private void ShowFile()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true });
            }
            else
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
                }
                else
                {
                    _dialogService.ShowInfo("Файла Config.json рядом с программой пока нет: сейчас используется конфигурация внутри exe. Файл появится после первого сохранения.");
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowProblem(ErrorAdvisor.FromException("Не удалось открыть проводник", ex));
        }
    }

    [RelayCommand]
    private void Close()
    {
        if (HasChanges && !_dialogService.ConfirmChoice(
                "Есть несохранённые изменения. Если закрыть редактор, они пропадут.",
                "Закрыть без сохранения?", "Закрыть", "Остаться"))
        {
            return;
        }

        _navigationService.NavigateTo<SettingsViewModel>();
    }
}
