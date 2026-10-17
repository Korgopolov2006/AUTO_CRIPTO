using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Тест подписи: подписывает тестовые данные выбранным сертификатом через КриптоПро CAdESCOM и проверяет подпись.</summary>
public partial class SignatureTestViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private readonly ISignatureTestService _signatureTestService;
    private readonly ICertificateService _certificateService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notifications;

    public ObservableCollection<CertificateInfo> Certificates { get; } = new();

    public ObservableCollection<SetupStepModel> Steps { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private CertificateInfo? _selectedCertificate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _resultSuccess;

    [ObservableProperty]
    private string _resultText = string.Empty;

    public bool CanRun => SelectedCertificate is not null && !IsRunning;

    public bool HasCertificates => Certificates.Count > 0;

    public bool HasNoCertificates => Certificates.Count == 0;

    public SignatureTestViewModel(
        ISignatureTestService signatureTestService,
        ICertificateService certificateService,
        IDialogService dialogService,
        INotificationService notifications)
    {
        _signatureTestService = signatureTestService;
        _certificateService = certificateService;
        _dialogService = dialogService;
        _notifications = notifications;

        ResetSteps();
    }

    public void OnNavigatedTo() => Refresh();

    public void OnNavigatedFrom()
    {
    }

    /// <summary>F5: заново читает сертификаты с закрытым ключом.</summary>
    public void Refresh()
    {
        if (IsRunning)
        {
            return;
        }

        var selectedThumbprint = SelectedCertificate?.Thumbprint;
        var now = DateTime.Now;

        Certificates.Clear();
        foreach (var certificate in _certificateService.GetCertificates()
                     .Where(c => c.HasPrivateKey)
                     .OrderByDescending(c => c.ValidTo > now)
                     .ThenByDescending(c => c.ValidTo))
        {
            certificate.ApplyExpiry(30);
            Certificates.Add(certificate);
        }

        SelectedCertificate = Certificates.FirstOrDefault(c => c.Thumbprint == selectedThumbprint)
                              ?? Certificates.FirstOrDefault(c => c.ValidTo > now)
                              ?? Certificates.FirstOrDefault();
        OnPropertyChanged(nameof(HasCertificates));
        OnPropertyChanged(nameof(HasNoCertificates));
    }

    private void ResetSteps()
    {
        Steps.Clear();
        Steps.Add(new SetupStepModel { Title = "Компонент КриптоПро CAdESCOM", Details = "Тот же компонент, через который подписывает браузерный плагин" });
        Steps.Add(new SetupStepModel { Title = "Сертификат", Details = "Срок действия и наличие закрытого ключа" });
        Steps.Add(new SetupStepModel { Title = "Подпись тестовых данных", Details = "Создаётся отсоединённая подпись CAdES-BES. Тестовые данные никуда не сохраняются" });
        Steps.Add(new SetupStepModel { Title = "Проверка подписи", Details = "Подпись проверяется независимо, как это сделал бы получатель" });
    }

    private SetupStepModel StepFor(string stage) => stage switch
    {
        "component" => Steps[0],
        "certificate" => Steps[1],
        "sign" => Steps[2],
        _ => Steps[3]
    };

    [RelayCommand]
    private async Task RunAsync()
    {
        if (SelectedCertificate is not { } certificate || IsRunning)
        {
            return;
        }

        ResetSteps();
        HasResult = false;
        IsRunning = true;

        // Выбор владельца подписи — действие пользователя, поэтому окно запроса PIN-кода поясняем: оно открывается отдельно от приложения.
        using var prompt = _dialogService.ShowActionPrompt(
            "Ожидается ввод PIN-кода",
            "КриптоПро может запросить PIN-код токена в своём окне (оно может открыться за окном приложения или в панели задач). " +
            "Введите PIN-код, чтобы продолжить тест.\n\nЭто сообщение закроется само после теста.",
            "Закрыть это окно",
            showAfter: TimeSpan.FromSeconds(3));

        // Progress<T> создаётся здесь, в потоке интерфейса, и доставляет обновления обратно в него.
        var progress = new Progress<SignatureStageUpdate>(update =>
        {
            var step = StepFor(update.Stage);
            step.Status = update.Status;
            step.Details = update.Details;
            step.AdviceOverride = update.Advice;
        });

        try
        {
            var result = await _signatureTestService.RunAsync(certificate.Thumbprint, progress);
            ResultSuccess = result.Success;
            ResultText = result.Summary;
            HasResult = true;
            _notifications.Show(result.Success ? ToastKind.Success : ToastKind.Warning, result.Success ? "Подпись работает" : "Тест подписи не пройден", certificate.Owner);
        }
        catch (OperationCanceledException)
        {
            ResultSuccess = false;
            ResultText = "Тест отменён.";
            HasResult = true;
        }
        catch (Exception ex)
        {
            ResultSuccess = false;
            ResultText = $"Тест не удалось выполнить: {ex.Message}";
            HasResult = true;
        }
        finally
        {
            IsRunning = false;
        }
    }
}
