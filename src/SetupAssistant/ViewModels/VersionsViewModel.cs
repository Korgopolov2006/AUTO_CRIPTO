using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>
/// Экран «Версии»: установленные версии рядом с допустимыми, рекомендуемыми и актуальными на серверах производителей.
/// Показывает только сведения; установкой по-прежнему занимается мастер настройки.
/// </summary>
public partial class VersionsViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private static readonly Regex VersionInName = new(@"(\d+(?:\.\d+){2,})", RegexOptions.Compiled);
    private const string Dash = "—";

    private readonly IWorkplaceStatusService _statusService;
    private readonly IConfigService _configService;
    private readonly IVersionService _versionService;
    private readonly IRemoteVersionService _remoteService;
    private readonly IExtensionUpdateService _extensionUpdateService;
    private readonly INavigationService _navigationService;
    private readonly SetupWizardViewModel _wizard;

    private bool _loadedOnce;

    public ObservableCollection<VersionRow> Components { get; } = new();

    public ObservableCollection<VersionRow> Extensions { get; } = new();

    [ObservableProperty]
    private string _summaryTitle = "Версии ещё не проверены";

    [ObservableProperty]
    private string _summaryText = "Нажмите «Проверить актуальные версии»: программа спросит у серверов производителей, что у них сейчас выложено. Файлы при этом не скачиваются.";

    [ObservableProperty]
    private StatusLevel _summaryLevel = StatusLevel.NotChecked;

    [ObservableProperty]
    private string _checkedAtText = string.Empty;

    /// <summary>Есть что обновить: можно перейти в мастер с уже отмеченным.</summary>
    [ObservableProperty]
    private bool _canOpenSetup;

    public VersionsViewModel(
        IWorkplaceStatusService statusService,
        IConfigService configService,
        IVersionService versionService,
        IRemoteVersionService remoteService,
        IExtensionUpdateService extensionUpdateService,
        INavigationService navigationService,
        SetupWizardViewModel wizard)
    {
        _statusService = statusService;
        _configService = configService;
        _versionService = versionService;
        _remoteService = remoteService;
        _extensionUpdateService = extensionUpdateService;
        _navigationService = navigationService;
        _wizard = wizard;
    }

    public void OnNavigatedTo()
    {
        if (_loadedOnce || IsBusy)
        {
            return;
        }

        _loadedOnce = true;
        _ = CheckAsync();
    }

    public void OnNavigatedFrom()
    {
    }

    public void Refresh() => _ = CheckAsync();

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = "Смотрим установленные версии…";
        try
        {
            if (_statusService.CheckedAt is null || DateTime.Now - _statusService.CheckedAt > TimeSpan.FromMinutes(2))
            {
                await _statusService.RefreshAsync();
            }

            BusyMessage = "Спрашиваем у серверов, что сейчас актуально…";
            var remoteTask = _remoteService.CheckAsync();
            var extensionsTask = _extensionUpdateService.CheckAsync();

            IReadOnlyList<RemoteDistribution> remote;
            IReadOnlyList<ExtensionUpdateInfo> extensions;
            try
            {
                remote = await remoteTask;
                extensions = await extensionsTask;
            }
            catch (Exception)
            {
                remote = Array.Empty<RemoteDistribution>();
                extensions = Array.Empty<ExtensionUpdateInfo>();
            }

            Build(remote, extensions);
            CheckedAtText = $"Проверено в {DateTime.Now:HH:mm}";
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    private void Build(IReadOnlyList<RemoteDistribution> remote, IReadOnlyList<ExtensionUpdateInfo> extensions)
    {
        var config = _configService.Current;
        var results = _statusService.Summary.Results;

        var definitions = new (ComponentType Type, string Key, string Name, InstallerDefinition Installer, string? Min, string? Recommended)[]
        {
            (ComponentType.CryptoProCsp, "CryptoProCsp", "КриптоПро CSP", config.Installers.CryptoProCsp, config.MinVersions.CryptoProCsp, config.RecommendedVersions.CryptoProCsp),
            (ComponentType.CryptoProBrowserPlugin, "CryptoProBrowserPlugin", "КриптоПро ЭЦП Browser plug-in", config.Installers.CryptoProBrowserPlugin, config.MinVersions.CryptoProBrowserPlugin, config.RecommendedVersions.CryptoProBrowserPlugin),
            (ComponentType.ChromiumGost, "ChromiumGost", "Chromium GOST", config.Installers.ChromiumGost, config.MinVersions.ChromiumGost, config.RecommendedVersions.ChromiumGost),
            (ComponentType.KonturPlugin, "KonturPlugin", "Контур.Плагин", config.Installers.KonturPlugin, config.MinVersions.KonturPlugin, config.RecommendedVersions.KonturPlugin),
            (ComponentType.GosuslugiPlugin, "GosuslugiPlugin", "Плагин Госуслуг", config.Installers.GosuslugiPlugin, null, null),
            (ComponentType.RutokenDriver, "RutokenDriver", "Драйверы Рутокен", config.Installers.RutokenDriver, null, null)
        };

        Components.Clear();
        foreach (var d in definitions)
        {
            var info = results.FirstOrDefault(c => c.Type == d.Type);
            var server = remote.FirstOrDefault(r => r.Key == d.Key);
            Components.Add(BuildComponentRow(d.Name, info, d.Installer, d.Min, d.Recommended, server));
        }

        Extensions.Clear();
        foreach (var e in extensions)
        {
            Extensions.Add(BuildExtensionRow(e));
        }

        // Итог по всей странице.
        var rows = Components.Concat(Extensions).ToList();
        var errors = rows.Count(r => r.Level == StatusLevel.Error);
        var warnings = rows.Count(r => r.Level == StatusLevel.Warning);
        var offline = remote.Count > 0 && remote.Where(r => !r.IsFixed).All(r => r.Error is not null);

        if (errors > 0)
        {
            SummaryLevel = StatusLevel.Error;
            SummaryTitle = "Есть версии ниже допустимых";
            SummaryText = $"Ниже допустимой: {errors}. Обновите их в разделе «Настройка» (отметьте «Переустановить установленные»).";
        }
        else if (warnings > 0)
        {
            SummaryLevel = StatusLevel.Warning;
            SummaryTitle = "Есть что обновить";
            SummaryText = $"Устарело или есть новее: {warnings}. Подробности в таблице ниже.";
        }
        else if (offline)
        {
            SummaryLevel = StatusLevel.Info;
            SummaryTitle = "Сравнить с серверами не удалось";
            SummaryText = "Нет связи с серверами производителей. Установленные версии сверены только с допустимыми и рекомендуемыми из конфигурации.";
        }
        else
        {
            SummaryLevel = StatusLevel.Ok;
            SummaryTitle = "Версии в порядке";
            SummaryText = "Установленное не ниже допустимого и рекомендуемого, новее на серверах ничего не найдено.";
        }

        CanOpenSetup = errors + warnings > 0;
    }

    private VersionRow BuildComponentRow(
        string name, ComponentInfo? info, InstallerDefinition installer, string? min, string? recommended, RemoteDistribution? server)
    {
        var installed = info is { IsInstalled: true } ? info.InstalledVersion : null;
        var latest = server?.Version;
        var note = DistributionNote(installer, server);

        var (level, verdict) = Evaluate(installed, info, min, recommended, latest, server);

        return new VersionRow(
            name,
            string.IsNullOrWhiteSpace(installed) ? (info is { IsInstalled: true } ? "версия не определена" : "не установлен") : installed,
            Show(min),
            Show(recommended),
            latest ?? (server is { IsFixed: true } ? "зафиксирована" : server?.Error is not null ? "не удалось" : server?.FileName is not null ? "номер не сообщается" : Dash),
            level,
            verdict,
            note);
    }

    private (StatusLevel Level, string Verdict) Evaluate(
        string? installed, ComponentInfo? info, string? min, string? recommended, string? latest, RemoteDistribution? server)
    {
        if (info is null || !info.IsInstalled)
        {
            return (StatusLevel.Info, "Не установлен");
        }

        if (string.IsNullOrWhiteSpace(installed))
        {
            return (StatusLevel.Info, "Установлен, версию определить не удалось");
        }

        if (!string.IsNullOrWhiteSpace(min) && _versionService.Compare(installed, min) < 0)
        {
            return (StatusLevel.Error, $"Ниже допустимой (нужно от {min})");
        }

        if (!string.IsNullOrWhiteSpace(latest) && _versionService.Compare(installed, latest) < 0)
        {
            return (StatusLevel.Warning, $"Есть новее: {latest}");
        }

        if (!string.IsNullOrWhiteSpace(recommended) && _versionService.Compare(installed, recommended) < 0)
        {
            return (StatusLevel.Warning, $"Ниже рекомендуемой ({recommended})");
        }

        if (!string.IsNullOrWhiteSpace(latest))
        {
            return (StatusLevel.Ok, "Актуальная версия");
        }

        if (server is { Error: not null })
        {
            return (StatusLevel.Ok, "В порядке по конфигурации (с сервером не сверено)");
        }

        return (StatusLevel.Ok, "В порядке");
    }

    private string DistributionNote(InstallerDefinition installer, RemoteDistribution? server)
    {
        if (server is null)
        {
            return string.Empty;
        }

        if (server.IsFixed)
        {
            return "Версия зафиксирована в конфигурации, адрес для проверки не задан.";
        }

        if (server.Error is not null)
        {
            return $"Сервер: {server.Error}.";
        }

        var onServer = $"{server.FileName}" +
                       (server.Size is { } size ? $", {size / 1024.0 / 1024.0:0.#} МБ" : string.Empty) +
                       (server.Modified is { } modified ? $", изменён {modified.LocalDateTime:dd.MM.yyyy}" : string.Empty);

        var localName = Path.GetFileName(installer.Path);
        if (string.IsNullOrWhiteSpace(localName))
        {
            return $"На сервере: {onServer}. Дистрибутив в конфигурации не задан.";
        }

        var localSize = _configService.GetDistributionSize(installer.Path);
        if (localSize is null)
        {
            return $"На сервере: {onServer}. Ваш дистрибутив «{localName}» не найден.";
        }

        var localVersion = VersionInName.Match(localName);
        if (server.Version is not null && localVersion.Success)
        {
            return _versionService.Compare(server.Version, localVersion.Value) > 0
                ? $"Ваш дистрибутив {localVersion.Value} старее серверного {server.Version}."
                : $"Ваш дистрибутив ({localVersion.Value}) не старее серверного.";
        }

        var same = string.Equals(localName, server.FileName, StringComparison.OrdinalIgnoreCase) && (server.Size is null || server.Size == localSize);
        return same
            ? "Ваш дистрибутив совпадает с файлом на сервере."
            : $"Файл на сервере отличается от вашего дистрибутива «{localName}»: {onServer}. Номер версии сервер не сообщает, возможно вышла новая.";
    }

    private static VersionRow BuildExtensionRow(ExtensionUpdateInfo e)
    {
        var (level, verdict) = e.Error is not null
            ? (StatusLevel.Info, $"Не удалось проверить: {e.Error}")
            : e.IsUpdateAvailable
                ? (StatusLevel.Warning, $"Есть новая версия {e.LatestVersion}")
                : (StatusLevel.Ok, "Актуальна");

        return new VersionRow(e.Name, e.CurrentVersion, Dash, Dash, e.LatestVersion ?? Dash, level, verdict, string.Empty);
    }

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? Dash : value;

    [RelayCommand]
    private void OpenConfigEditor() => _navigationService.NavigateTo<ConfigEditorViewModel>();

    /// <summary>Открывает мастер с отмеченными проблемными компонентами. Ничего не запускает.</summary>
    [RelayCommand]
    private void OpenSetup()
    {
        var kinds = _statusService.Summary.FixSteps;
        if (kinds.Count > 0)
        {
            _wizard.PrepareSelection(kinds);
        }

        _navigationService.NavigateTo<SetupWizardViewModel>();
    }
}
