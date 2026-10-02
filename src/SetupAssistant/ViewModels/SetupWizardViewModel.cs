using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Мастер настройки рабочего места: последовательная установка и проверка всех компонентов.</summary>
public partial class SetupWizardViewModel : ViewModelBase
{
    private readonly ISetupOrchestrationService _orchestrationService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly IConfigService _configService;

    private CancellationTokenSource? _cancellationTokenSource;

    public ObservableCollection<SetupStepModel> Steps { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private int _completedStepsCount;

    public int TotalStepsCount => Steps.Count;

    public SetupWizardViewModel(
        ISetupOrchestrationService orchestrationService,
        IDialogService dialogService,
        INavigationService navigationService,
        IConfigService configService)
    {
        _orchestrationService = orchestrationService;
        _dialogService = dialogService;
        _navigationService = navigationService;
        _configService = configService;

        InitializeSteps();
    }

    private void InitializeSteps()
    {
        Steps.Clear();
        AddStep(SetupStepKind.InstallCryptoProCsp, "Установка КриптоПро CSP");
        AddStep(SetupStepKind.VerifyCryptoProCsp, "Проверка установки КриптоПро CSP");
        AddStep(SetupStepKind.InstallBrowserPlugin, "Установка КриптоПро Browser Plugin");
        AddStep(SetupStepKind.VerifyBrowserPlugin, "Проверка Browser Plugin");
        AddStep(SetupStepKind.InstallRutokenDriver, "Установка драйвера Рутокен");
        AddStep(SetupStepKind.VerifyRutokenDriver, "Проверка драйвера Рутокен");
        AddStep(SetupStepKind.InstallChromiumGost, "Установка Chromium GOST");
        AddStep(SetupStepKind.VerifyChromiumGost, "Проверка Chromium GOST");
        AddStep(SetupStepKind.InstallKonturPlugin, "Установка Контур.Плагина");
        AddStep(SetupStepKind.VerifyKonturPlugin, "Проверка Контур.Плагина");
        AddStep(SetupStepKind.InstallGosuslugiPlugin, "Установка Плагина Госуслуг");
        AddStep(SetupStepKind.VerifyGosuslugiPlugin, "Проверка Плагина Госуслуг");
        AddStep(SetupStepKind.InstallBrowserExtensions, "Установка расширений браузера");
        if (_configService.Current.CaCertificates.Count > 0)
        {
            AddStep(SetupStepKind.InstallCaCertificates, "Установка сертификатов УЦ");
        }

        AddStep(SetupStepKind.InstallTokenCertificates, "Установка сертификата с носителя в хранилище");
        AddStep(SetupStepKind.FinalDiagnostic, "Повторная диагностика рабочего места");
    }

    private void AddStep(SetupStepKind kind, string title) =>
        Steps.Add(new SetupStepModel { Kind = kind, Title = title });

    [RelayCommand]
    private async Task StartSetupAsync()
    {
        if (!_dialogService.Confirm("Будет выполнена автоматическая установка и настройка всех компонентов рабочего места. Продолжить?"))
        {
            return;
        }

        IsRunning = true;
        IsCompleted = false;
        CompletedStepsCount = 0;
        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<SetupStepModel>(_ =>
        {
            CompletedStepsCount = Steps.Count(s => s.Status is StatusLevel.Ok or StatusLevel.Warning or StatusLevel.Error);
        });

        try
        {
            await _orchestrationService.RunSetupAsync(Steps, progress, _cancellationTokenSource.Token);
            IsCompleted = true;

            var hasErrors = Steps.Any(s => s.Status == StatusLevel.Error);
            if (hasErrors)
            {
                _dialogService.ShowWarning("Настройка завершена с предупреждениями. Проверьте детали по каждому шагу.");
            }
            else
            {
                _dialogService.ShowInfo("Настройка рабочего места успешно завершена.");
            }
        }
        catch (OperationCanceledException)
        {
            _dialogService.ShowWarning("Настройка была отменена.");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Ошибка настройки рабочего места: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void CancelSetup() => _cancellationTokenSource?.Cancel();

    [RelayCommand]
    private void GoBack() => _navigationService.NavigateTo<DashboardViewModel>();
}
