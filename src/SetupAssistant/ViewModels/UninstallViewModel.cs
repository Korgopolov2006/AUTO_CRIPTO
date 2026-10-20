using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Helpers;
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
    private readonly ILoggingService _logger;

    private CancellationTokenSource? _cancellationTokenSource;

    protected override string IdleText => "Нажмите «Удалить»";

    [ObservableProperty]
    private bool _removePersonalCertificates;

    /// <summary>Удаление выполнено, но Windows завершит его (и уберёт программы из списка) только после перезагрузки.</summary>
    [ObservableProperty]
    private bool _rebootRequired;

    public UninstallViewModel(
        IUninstallService uninstallService,
        IDialogService dialogService,
        INotificationService notifications,
        INavigationService navigationService,
        ILoggingService logger)
    {
        _uninstallService = uninstallService;
        _dialogService = dialogService;
        _notifications = notifications;
        _navigationService = navigationService;
        _logger = logger;

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
        RebootRequired = false;
        UpdateProgress();
    }

    /// <summary>Кнопка «Удалить» — как раньше: все компоненты из списка, включая Континент-АП, если он найден.</summary>
    [RelayCommand]
    private Task StartUninstallAsync() => RunUninstallAsync(withKontinent: null);

    /// <summary>Кнопка «КриптоПро»: удаляет КриптоПро и остальные компоненты, Континент-АП и его настройки не трогает.</summary>
    [RelayCommand]
    private Task UninstallCryptoProAsync() => RunUninstallAsync(withKontinent: false);

    /// <summary>Кнопка «КриптоПро с Континентом»: удаляет то же самое и Континент-АП вместе с его настройками.</summary>
    [RelayCommand]
    private Task UninstallCryptoProWithKontinentAsync() => RunUninstallAsync(withKontinent: true);

    /// <param name="withKontinent">true — удалить и Континент-АП, false — не трогать его, null — как в списке.</param>
    private async Task RunUninstallAsync(bool? withKontinent)
    {
        if (IsRunning)
        {
            return;
        }

        var certificatesNote = RemovePersonalCertificates
            ? "Будут также удалены сертификаты КриптоПро из хранилища «Личное» (их можно вернуть с токена повторной настройкой)."
            : "Сертификаты останутся в хранилище.";

        var kontinentNote = withKontinent == false
            ? "Континент-АП и его настройки останутся.\n"
            : withKontinent == true ? "Будут также удалены Континент-АП и его папки настроек.\n" : string.Empty;

        var message =
            "Будут удалены КриптоПро CSP и плагин КриптоПро, драйверы Рутокен, Контур.Плагин, Плагин Госуслуг, " +
            "политика установки расширений и данные приложения. Запущенные плагины будут закрыты.\n\n" +
            "Chromium GOST остаётся установленным. Ключи, ключевые контейнеры и содержимое токена НЕ удаляются.\n" + certificatesNote + "\n" + kontinentNote + "\nПродолжить?";

        if (!_dialogService.Confirm(message, "Удаление компонентов"))
        {
            return;
        }

        // Экран живёт всё время работы приложения: перед новым запуском строим план заново.
        Rescan();

        // Кнопка «КриптоПро» не должна трогать Континент-АП: его шаг убираем из плана, а не только снимаем галочку.
        if (withKontinent == false)
        {
            foreach (var step in Steps.Where(s => s.Key == "Континент-АП").ToList())
            {
                Steps.Remove(step);
            }
        }

        IsRunning = true;
        IsCompleted = false;
        UpdateProgress();
        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<SetupStepModel>(_ => UpdateProgress());
        var askToRestart = false;

        try
        {
            await _uninstallService.RunAsync(Steps.ToList(), progress, _cancellationTokenSource.Token);
            IsCompleted = true;
            RebootRequired = Steps.Any(s => s.NeedsReboot);
            askToRestart = RebootRequired;

            if (Steps.Any(s => s.Status == StatusLevel.Error))
            {
                _notifications.Show(ToastKind.Error, "Удаление завершено с ошибками", "Подробности — по каждому шагу и в журнале.");
            }
            else if (RebootRequired)
            {
                _notifications.Show(ToastKind.Warning, "Перезагрузите компьютер", "Компоненты удалены, но программы пропадут из списка только после перезагрузки.");
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

        // Окно показываем после того, как прогресс снят: удаление закончено, осталось перезагрузить компьютер.
        if (askToRestart)
        {
            RestartNow();
        }
    }

    [RelayCommand]
    private void CancelUninstall() => _cancellationTokenSource?.Cancel();

    [RelayCommand]
    private void RestartNow()
    {
        if (!RebootPrompt.Ask(_dialogService, "удаления"))
        {
            return;
        }

        try
        {
            RebootPrompt.Schedule("Перезагрузка для завершения удаления компонентов");
            _logger.Info("Перезагрузка", "Запланирована перезагрузка через 30 секунд для завершения удаления");
            _notifications.Show(ToastKind.Info, "Перезагрузка через 30 секунд", "Отменить: команда shutdown /a в командной строке.");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Не удалось запланировать перезагрузку: {ex.Message}");
        }
    }

    [RelayCommand]
    private void GoBack() => _navigationService.NavigateTo<DashboardViewModel>();
}
