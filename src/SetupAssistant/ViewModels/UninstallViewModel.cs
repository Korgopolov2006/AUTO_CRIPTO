using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Откат настройки: удаление установленных компонентов и следов приложения с предварительным просмотром найденного.</summary>
public partial class UninstallViewModel : StepsViewModelBase, INavigationAware
{
    private const string ResumeNote =
        "После входа в систему приложение запустится само и завершит полную очистку (потребуется подтвердить запуск от имени администратора).";

    private readonly IUninstallService _uninstallService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigationService;
    private readonly ILoggingService _logger;

    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>Полная очистка продолжится после перезагрузки (зарегистрирован автозапуск).</summary>
    private bool _cleanupResumePending;

    protected override string IdleText => "Нажмите «Удалить»";

    [ObservableProperty]
    private bool _removePersonalCertificates;

    /// <summary>Полная очистка: кроме программ — все следы СКЗИ, с точкой восстановления и резервной копией контейнеров.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseCertificates))]
    private bool _fullCleanup;

    /// <summary>Папка отчёта последней полной очистки (резервные копии и report.txt).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReportDirectory))]
    private string? _reportDirectory;

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

    /// <summary>При полной очистке сертификаты КриптоПро удаляются всегда (их копия — в папке отчёта).</summary>
    public bool CanChooseCertificates => !FullCleanup;

    public bool HasReportDirectory => !string.IsNullOrEmpty(ReportDirectory);

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

    partial void OnFullCleanupChanged(bool value) => Rescan();

    [RelayCommand]
    private void Rescan()
    {
        if (IsRunning)
        {
            return;
        }

        Steps.Clear();
        foreach (var step in _uninstallService.BuildPlan(new UninstallOptions(RemovePersonalCertificates, FullCleanup)))
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

        var kontinentNote = withKontinent == false
            ? "Континент-АП и его настройки останутся.\n"
            : withKontinent == true ? "Будут также удалены Континент-АП и его папки настроек.\n" : string.Empty;

        if (!_dialogService.Confirm(FullCleanup ? FullCleanupMessage(kontinentNote) : UninstallMessage(kontinentNote), FullCleanup ? "Полная очистка" : "Удаление компонентов"))
        {
            return;
        }

        // Экран живёт всё время работы приложения: перед новым запуском строим план заново.
        Rescan();

        // Кнопка «КриптоПро» не должна трогать Континент-АП: его шаг убираем из плана, а не только снимаем галочку.
        if (withKontinent == false)
        {
            foreach (var step in Steps.Where(s => _uninstallService.IsKontinentStep(s)).ToList())
            {
                Steps.Remove(step);
            }
        }

        var session = FullCleanup ? _uninstallService.CreateFullCleanupSession(includeKontinent: withKontinent != false) : null;
        ReportDirectory = session?.BackupDirectory;
        await RunStepsAsync(session);
    }

    /// <summary>Продолжение полной очистки после перезагрузки (запуск с --resume): программы уже удалены, остаётся глубокая очистка.</summary>
    public async Task ResumeFullCleanupAsync(FullCleanupSession session)
    {
        if (IsRunning)
        {
            return;
        }

        ResumeRegistration.Unregister();
        _logger.Info("Продолжение после перезагрузки", "Приложение запущено после перезагрузки: завершается полная очистка");

        FullCleanup = true;
        Steps.Clear();
        foreach (var step in _uninstallService.BuildResumePlan())
        {
            Steps.Add(step);
        }

        ReportDirectory = session.BackupDirectory;
        await RunStepsAsync(session);
    }

    private async Task RunStepsAsync(FullCleanupSession? session)
    {
        IsRunning = true;
        IsCompleted = false;
        _cleanupResumePending = false;
        UpdateProgress();
        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<SetupStepModel>(_ => UpdateProgress());
        var askToRestart = false;

        try
        {
            if (session is not null && !session.IsResume)
            {
                // Сначала точка восстановления и резервная копия: от их результата зависит вопрос про контейнеры.
                var preparation = Steps.Where(s => s.Kind is SetupStepKind.CreateRestorePoint or SetupStepKind.BackupContainers).ToList();
                await _uninstallService.RunAsync(preparation, progress, _cancellationTokenSource.Token, session);
                AskAboutContainers(session);
                await _uninstallService.RunAsync(Steps.Except(preparation).ToList(), progress, _cancellationTokenSource.Token, session);
            }
            else
            {
                await _uninstallService.RunAsync(Steps.ToList(), progress, _cancellationTokenSource.Token, session);
            }

            IsCompleted = true;
            if (session is { ContinueAfterReboot: true, IsResume: false })
            {
                // Автозапуск регистрируется сразу: очистка завершится и при перезагрузке «вручную», позже.
                ResumeRegistration.RegisterCleanup(session);
                _cleanupResumePending = true;
                _logger.Info("Полная очистка", "После перезагрузки приложение завершит очистку (зарегистрирован автозапуск)");
            }

            RebootRequired = Steps.Any(s => s.NeedsReboot) || _cleanupResumePending;
            askToRestart = RebootRequired;

            if (Steps.Any(s => s.Status == StatusLevel.Error))
            {
                _notifications.Show(ToastKind.Error, session is null ? "Удаление завершено с ошибками" : "Очистка завершена с ошибками", "Подробности — по каждому шагу и в журнале.");
            }
            else if (_cleanupResumePending)
            {
                _notifications.Show(ToastKind.Warning, "Перезагрузите компьютер", "После перезагрузки приложение само завершит полную очистку.");
            }
            else if (RebootRequired)
            {
                _notifications.Show(ToastKind.Warning, "Перезагрузите компьютер", "Компоненты удалены, но часть файлов и программ уберётся только после перезагрузки.");
            }
            else
            {
                _notifications.Show(ToastKind.Success, session is null ? "Удаление завершено" : "Полная очистка завершена");
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
        if (askToRestart && RebootPrompt.TryMarkAutoPrompted())
        {
            RestartNow();
        }
    }

    /// <summary>
    /// Отдельный вопрос об удалении ключевых контейнеров — только после успешной резервной копии; по умолчанию контейнеры остаются.
    /// </summary>
    private void AskAboutContainers(FullCleanupSession session)
    {
        var step = Steps.FirstOrDefault(s => s.Kind == SetupStepKind.BackupContainers);
        if (session.Containers.Count == 0)
        {
            return;
        }

        if (!session.ContainersBackedUp)
        {
            _dialogService.ShowWarning(
                "Резервную копию ключевых контейнеров сделать не удалось, поэтому контейнеры останутся на компьютере. " +
                "Остальная очистка продолжится.\n\nПодробности — в шаге «Резервная копия ключевых контейнеров».",
                "Ключевые контейнеры");
            return;
        }

        var list = string.Join("\n", session.Containers.Take(10).Select(c => $"• {c.Name} — {c.Owner} ({(c.InRegistry ? "реестр" : "папка")})"))
                   + (session.Containers.Count > 10 ? $"\n… и ещё {session.Containers.Count - 10}" : string.Empty);
        var message =
            $"На компьютере найдено ключевых контейнеров: {session.Containers.Count}.\n\n{list}\n\n" +
            $"Резервная копия сохранена в папке отчёта:\n{session.BackupDirectory}\n" +
            "В ней закрытые ключи — храните её так же, как сами ключи.\n\n" +
            "Удалить контейнеры с компьютера? Без резервной копии восстановить электронную подпись будет невозможно. " +
            "Содержимое токенов не затрагивается.";

        session.DeleteContainers = _dialogService.ConfirmChoice(message, "Ключевые контейнеры", "Удалить контейнеры", "Оставить на компьютере");
        _logger.Info("Полная очистка", session.DeleteContainers
            ? $"Пользователь подтвердил удаление ключевых контейнеров ({session.Containers.Count}) после резервного копирования"
            : "Ключевые контейнеры оставлены на компьютере");
        if (step is not null)
        {
            step.Details += session.DeleteContainers ? ". Контейнеры будут удалены с компьютера" : ". Контейнеры остаются на компьютере";
        }
    }

    private string UninstallMessage(string kontinentNote)
    {
        var certificatesNote = RemovePersonalCertificates
            ? "Будут также удалены сертификаты КриптоПро из хранилища «Личное» (их можно вернуть с токена повторной настройкой)."
            : "Сертификаты останутся в хранилище.";

        return "Будут удалены КриптоПро CSP и плагин КриптоПро, драйверы Рутокен, Контур.Плагин, Плагин Госуслуг, " +
               "политика установки расширений и данные приложения. Запущенные плагины будут закрыты.\n\n" +
               "Chromium GOST остаётся установленным. Ключи, ключевые контейнеры и содержимое токена НЕ удаляются.\n" + certificatesNote + "\n" +
               kontinentNote + "\nПродолжить?";
    }

    private string FullCleanupMessage(string kontinentNote) =>
        "Полная очистка: будут удалены КриптоПро CSP и плагин КриптоПро, драйверы Рутокен, Контур.Плагин, Плагин Госуслуг, " +
        "а затем все их следы в Windows — криптопровайдеры, регистрации CNG и TLS, пакеты LSA, драйверы и службы, COM-классы, " +
        "записи браузеров, папки и файлы. Удаляются и сертификаты КриптоПро из хранилища «Личное».\n" + kontinentNote + "\n" +
        "Перед удалением создаются точка восстановления Windows и резервная копия ключевых контейнеров и сертификатов в папке отчёта " +
        "(рядом с журналами приложения). Удалять ли сами контейнеры, приложение спросит отдельно после копирования.\n\n" +
        "Содержимое токенов не затрагивается. Chromium GOST остаётся установленным. Для завершения понадобится перезагрузка.\n\nПродолжить?";

    [RelayCommand]
    private void CancelUninstall() => _cancellationTokenSource.CancelInBackground();

    [RelayCommand]
    private void OpenReportDirectory()
    {
        if (ReportDirectory is null || !Directory.Exists(ReportDirectory))
        {
            _notifications.Show(ToastKind.Info, "Папки отчёта ещё нет", "Она появится при резервном копировании.");
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ReportDirectory}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void RestartNow()
    {
        if (!RebootPrompt.Ask(_dialogService, "удаления", _cleanupResumePending ? ResumeNote : null))
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
