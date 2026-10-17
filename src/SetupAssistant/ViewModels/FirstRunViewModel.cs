using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Короткий мастер первого запуска: что установлено, что нужно этому компьютеру, подключить носитель.</summary>
public partial class FirstRunViewModel : ViewModelBase, INavigationAware
{
    private const int LastStep = 2;

    private readonly IWorkplaceStatusService _statusService;
    private readonly IUserSettingsService _userSettings;
    private readonly INavigationService _navigationService;
    private readonly ITokenService _tokenService;
    private readonly ITokenPinService _tokenPinService;
    private readonly SetupWizardViewModel _wizard;
    private readonly DispatcherTimer _tokenTimer;
    private bool _tokenCheckRunning;

    /// <summary>Шаг 1: домен и установленные компоненты.</summary>
    public ObservableCollection<ComponentInfo> Overview { get; } = new();

    /// <summary>Шаг 2: для чего нужен компьютер.</summary>
    public ObservableCollection<NeedOption> Needs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep), nameof(IsSecondStep), nameof(IsThirdStep), nameof(IsLastStep), nameof(CanGoBack), nameof(StepNumberText), nameof(StepProgress), nameof(StepTitle))]
    private int _currentStep;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isTokenConnected;

    [ObservableProperty]
    private string _tokenText = "Ищем носитель…";

    public bool IsFirstStep => CurrentStep == 0;

    public bool IsSecondStep => CurrentStep == 1;

    public bool IsThirdStep => CurrentStep == 2;

    public bool IsLastStep => CurrentStep == LastStep;

    public bool CanGoBack => CurrentStep > 0;

    public bool ShowOverviewSkeleton => IsChecking && Overview.Count == 0;

    public double StepProgress => (CurrentStep + 1) * 100.0 / (LastStep + 1);

    public string StepTitle => CurrentStep switch
    {
        0 => "Что уже установлено",
        1 => "Что нужно этому компьютеру",
        _ => "Подключите носитель"
    };

    public string StepNumberText => $"Шаг {CurrentStep + 1} из {LastStep + 1}";

    public FirstRunViewModel(
        IWorkplaceStatusService statusService,
        IUserSettingsService userSettings,
        INavigationService navigationService,
        ITokenService tokenService,
        ITokenPinService tokenPinService,
        SetupWizardViewModel wizard)
    {
        _statusService = statusService;
        _userSettings = userSettings;
        _navigationService = navigationService;
        _tokenService = tokenService;
        _tokenPinService = tokenPinService;
        _wizard = wizard;

        Needs.Add(new NeedOption("Подпись документов и вход в браузере",
            "КриптоПро CSP, КриптоПро Browser Plugin и расширения браузера",
            SetupStepKind.InstallCryptoProCsp, SetupStepKind.InstallBrowserPlugin, SetupStepKind.InstallBrowserExtensions));
        Needs.Add(new NeedOption("Сайты с ГОСТ-шифрованием", "Chromium GOST — браузер с поддержкой российской криптографии",
            SetupStepKind.InstallChromiumGost));
        Needs.Add(new NeedOption("Порталы Контур", "Контур.Плагин", SetupStepKind.InstallKonturPlugin));
        Needs.Add(new NeedOption("Госуслуги", "Плагин для входа на Госуслуги с электронной подписью", SetupStepKind.InstallGosuslugiPlugin));
        Needs.Add(new NeedOption("Токен Рутокен", "Драйвер токена и установка сертификата с него", SetupStepKind.InstallRutokenDriver, SetupStepKind.InstallTokenCertificates));

        _tokenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _tokenTimer.Tick += async (_, _) => await CheckTokenAsync();

        _statusService.Changed += (_, _) => LoadOverview();
        PropertyChanged += OnPropertyChangedInternal;
    }

    private void OnPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CurrentStep))
        {
            if (IsThirdStep)
            {
                _ = CheckTokenAsync();
                _tokenTimer.Start();
            }
            else
            {
                _tokenTimer.Stop();
            }
        }
        else if (e.PropertyName == nameof(IsChecking))
        {
            OnPropertyChanged(nameof(ShowOverviewSkeleton));
        }
    }

    public void OnNavigatedTo()
    {
        CurrentStep = 0;
        LoadOverview();
        if (_statusService.CheckedAt is null && !_statusService.IsChecking)
        {
            _ = RefreshAsync();
        }
    }

    public void OnNavigatedFrom() => _tokenTimer.Stop();

    private async Task RefreshAsync()
    {
        try
        {
            await _statusService.RefreshAsync();
        }
        catch (Exception)
        {
            // Причина записана в журнал; шаг покажет то, что удалось собрать.
        }
    }

    private void LoadOverview()
    {
        var summary = _statusService.Summary;
        IsChecking = _statusService.IsChecking;

        Overview.Clear();
        foreach (var item in summary.Results.Where(c => c.ShowOnDashboard && c.Type != ComponentType.Token).OrderByDescending(c => c.IsDeviceManagement))
        {
            Overview.Add(item);
        }

        OnPropertyChanged(nameof(ShowOverviewSkeleton));

        // По умолчанию отмечаем то, чего на компьютере не хватает; пользователь вправе снять любую отметку.
        var missing = summary.FixSteps;
        foreach (var need in Needs)
        {
            need.IsSelected = need.Kinds.Any(missing.Contains);
        }
    }

    private async Task CheckTokenAsync()
    {
        if (_tokenCheckRunning)
        {
            return;
        }

        _tokenCheckRunning = true;
        try
        {
            var device = await Task.Run(() => _tokenService.GetConnectedTokens().FirstOrDefault());
            if (device is not null)
            {
                IsTokenConnected = true;
                TokenText = $"Носитель подключён: {device.DeviceName}";
                return;
            }

            var scan = await _tokenPinService.ScanAsync();
            IsTokenConnected = scan.Tokens.Count > 0;
            TokenText = scan.Tokens.Count > 0
                ? $"Носитель подключён: {scan.Tokens[0].DisplayName}"
                : "Носитель не подключён. Подключите токен в USB-порт — окно обновится само. Это можно сделать и позже.";
        }
        catch (Exception)
        {
            TokenText = "Не удалось проверить носитель. Подключить токен можно позже.";
        }
        finally
        {
            _tokenCheckRunning = false;
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep < LastStep)
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    /// <summary>Закрыть приветствие без выбора: больше не показывать, открыть главную.</summary>
    [RelayCommand]
    private void Skip()
    {
        _tokenTimer.Stop();
        _userSettings.FirstRunCompleted = true;
        _navigationService.NavigateTo<DashboardViewModel>();
    }

    /// <summary>Готово: отмеченное переносится в мастер настройки (там его можно уточнить и запустить).</summary>
    [RelayCommand]
    private void Finish()
    {
        _tokenTimer.Stop();
        _userSettings.FirstRunCompleted = true;

        var kinds = Needs.Where(n => n.IsSelected).SelectMany(n => n.Kinds).Distinct().ToList();
        if (kinds.Count > 0)
        {
            _wizard.PrepareSelection(kinds);
            _navigationService.NavigateTo<SetupWizardViewModel>();
        }
        else
        {
            _navigationService.NavigateTo<DashboardViewModel>();
        }
    }
}

/// <summary>Вариант на втором шаге: «для чего нужен компьютер» и какие шаги мастера за этим стоят.</summary>
public partial class NeedOption : ObservableObject
{
    public NeedOption(string title, string description, params SetupStepKind[] kinds)
    {
        Title = title;
        Description = description;
        Kinds = kinds;
    }

    public string Title { get; }

    public string Description { get; }

    public IReadOnlyList<SetupStepKind> Kinds { get; }

    [ObservableProperty]
    private bool _isSelected;
}
