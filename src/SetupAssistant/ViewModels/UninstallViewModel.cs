using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Откат настройки: удаление установленных компонентов и следов приложения с предварительным просмотром найденного.</summary>
public partial class UninstallViewModel : StepsViewModelBase, INavigationAware
{
    private readonly IUninstallService _uninstallService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigationService;

    private CancellationTokenSource? _cancellationTokenSource;

    protected override string IdleText => "Нажмите «Удалить»";

    [ObservableProperty]
    private bool _removePersonalCertificates;

    public UninstallViewModel(IUninstallService uninstallService, IDialogService dialogService, INotificationService notifications, INavigationService navigationService)
    {
        _uninstallService = uninstallService;
        _dialogService = dialogService;
        _notifications = notifications;
        _navigationService = navigationService;

        Rescan();
    }

    /// <summary>Пока удаление не идёт и его результат не показан, при каждом входе перечитываем, что установлено сейчас.</summary>
    public void OnNavigatedTo()
    {
        if (!IsRunning && !IsCompleted)
        {
            Rescan();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    partial void OnRemovePersonalCertificatesChanged(bool value) => Rescan();

    [RelayCommand]
    private void Rescan()
    {
        if (IsRunning)
        {
            return;
        }

        Steps.Clear();
        foreach (var step in _uninstallService.BuildPlan(new UninstallOptions(RemovePersonalCertificates)))
        {
            Steps.Add(step);
        }

        IsCompleted = false;
        UpdateProgress();
    }

    [RelayCommand]
    private async Task StartUninstallAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var certificatesNote = RemovePersonalCertificates
            ? "Будут также удалены сертификаты КриптоПро из хранилища «Личное» (их можно вернуть с токена повторной настройкой)."
            : "Сертификаты останутся в хранилище.";

        var message =
            "Будут удалены КриптоПро CSP и плагин КриптоПро, драйверы Рутокен, Chromium GOST, Контур.Плагин, Плагин Госуслуг, " +
            "политика установки расширений и данные приложения. Запущенные Chromium GOST и плагины будут закрыты.\n\n" +
            "Ключи, ключевые контейнеры и содержимое токена НЕ удаляются.\n" + certificatesNote + "\n\nПродолжить?";

        if (!_dialogService.Confirm(message, "Удаление компонентов"))
        {
            return;
        }

        // Экран живёт всё время работы приложения: перед новым запуском строим план заново.
        Rescan();

        IsRunning = true;
        IsCompleted = false;
        UpdateProgress();
        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<SetupStepModel>(_ => UpdateProgress());

        try
        {
            await _uninstallService.RunAsync(Steps.ToList(), progress, _cancellationTokenSource.Token);
            IsCompleted = true;

            if (Steps.Any(s => s.Status == StatusLevel.Error))
            {
                _notifications.Show(ToastKind.Error, "Удаление завершено с ошибками", "Подробности — по каждому шагу и в журнале.");
            }
            else if (Steps.Any(s => s.Details?.Contains("перезагрузк", StringComparison.OrdinalIgnoreCase) == true))
            {
                _notifications.Show(ToastKind.Warning, "Удаление завершено", "Для полного завершения перезагрузите компьютер.");
            }
            else
            {
                _notifications.Show(ToastKind.Success, "Удаление завершено");
            }
        }
        catch (OperationCanceledException)
        {
            _notifications.Show(ToastKind.Warning, "Удаление отменено");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Ошибка удаления: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            UpdateProgress();
        }
    }

    [RelayCommand]
    private void CancelUninstall() => _cancellationTokenSource?.Cancel();

    [RelayCommand]
    private void GoBack() => _navigationService.NavigateTo<DashboardViewModel>();
}
