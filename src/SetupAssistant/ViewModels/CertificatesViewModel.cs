using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Просмотр и установка сертификатов электронной подписи; следит за подключением носителя, пока открыт.</summary>
public partial class CertificatesViewModel : ViewModelBase, INavigationAware
{
    private readonly ICertificateService _certificateService;
    private readonly ITokenService _tokenService;
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notifications;
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _tokenTimer;

    private string? _tokenSignature;
    private bool _tokenCheckRunning;

    public ObservableCollection<CertificateInfo> Certificates { get; } = new();

    [ObservableProperty]
    private CertificateInfo? _selectedCertificate;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    [ObservableProperty]
    private string _tokenStatusText = "Проверяем носитель…";

    [ObservableProperty]
    private bool _isTokenConnected;

    public CertificatesViewModel(
        ICertificateService certificateService,
        ITokenService tokenService,
        IConfigService configService,
        IDialogService dialogService,
        INotificationService notifications)
    {
        _certificateService = certificateService;
        _tokenService = tokenService;
        _configService = configService;
        _dialogService = dialogService;
        _notifications = notifications;

        _view = CollectionViewSource.GetDefaultView(Certificates);
        _view.Filter = Matches;

        _tokenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _tokenTimer.Tick += async (_, _) => await CheckTokenAsync(notify: true);

        LoadCertificates();
    }

    partial void OnSearchTextChanged(string value)
    {
        _view.Refresh();
        UpdateEmptyState();
    }

    public void OnNavigatedTo()
    {
        _ = CheckTokenAsync(notify: false);
        _tokenTimer.Start();
    }

    public void OnNavigatedFrom() => _tokenTimer.Stop();

    private bool Matches(object item)
    {
        if (string.IsNullOrWhiteSpace(SearchText) || item is not CertificateInfo c)
        {
            return true;
        }

        var query = SearchText.Trim();
        return new[] { c.Owner, c.Organization, c.Container, c.SerialNumber, c.Thumbprint }
            .Any(field => field.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void UpdateEmptyState()
    {
        IsEmpty = !_view.Cast<object>().Any();
        EmptyText = Certificates.Count == 0
            ? "Сертификаты не найдены. Подключите носитель электронной подписи и нажмите «Обновить»."
            : "Ничего не найдено по этому запросу.";
    }

    /// <summary>Опрос носителя: при подключении/отключении обновляет список и показывает уведомление.</summary>
    private async Task CheckTokenAsync(bool notify)
    {
        if (_tokenCheckRunning)
        {
            return;
        }

        _tokenCheckRunning = true;
        try
        {
            var tokens = await Task.Run(() => _tokenService.GetConnectedTokens());
            var signature = string.Join("|", tokens.Select(t => $"{t.DeviceName}:{t.SerialNumber}"));

            IsTokenConnected = tokens.Count > 0;
            TokenStatusText = tokens.Count > 0
                ? $"Носитель подключён: {tokens[0].DeviceName}"
                : "Носитель не подключён";

            if (_tokenSignature is not null && signature != _tokenSignature)
            {
                if (notify)
                {
                    _notifications.Show(
                        tokens.Count > 0 ? ToastKind.Success : ToastKind.Info,
                        tokens.Count > 0 ? "Носитель подключён" : "Носитель отключён",
                        tokens.Count > 0 ? tokens[0].DeviceName : null);
                }

                LoadCertificates();
            }

            _tokenSignature = signature;
        }
        catch (Exception)
        {
            // Опрос повторится по таймеру; ошибки WMI уже пишутся в журнал службой носителей.
        }
        finally
        {
            _tokenCheckRunning = false;
        }
    }

    [RelayCommand]
    private void LoadCertificates()
    {
        var selectedThumbprint = SelectedCertificate?.Thumbprint;
        var warningDays = _configService.Current.CheckSettings.CertificateExpiryWarningDays;

        Certificates.Clear();
        foreach (var certificate in _certificateService.GetCertificates().OrderBy(c => c.ValidTo))
        {
            (certificate.ExpiryLevel, certificate.ExpiryText) = DescribeExpiry(certificate, warningDays);
            Certificates.Add(certificate);
        }

        SelectedCertificate = Certificates.FirstOrDefault(c => c.Thumbprint == selectedThumbprint) ?? Certificates.FirstOrDefault();
        UpdateEmptyState();
    }

    private static (StatusLevel Level, string Text) DescribeExpiry(CertificateInfo certificate, int warningDays)
    {
        var days = (certificate.ValidTo.Date - DateTime.Now.Date).Days;
        if (days < 0)
        {
            return (StatusLevel.Error, $"Истёк {-days} дн. назад");
        }

        if (days == 0)
        {
            return (StatusLevel.Warning, "Истекает сегодня");
        }

        return days <= warningDays
            ? (StatusLevel.Warning, $"Истекает через {days} дн.")
            : (StatusLevel.Ok, $"Действует ещё {days} дн.");
    }

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        if (SelectedCertificate is null)
        {
            _dialogService.ShowWarning("Выберите сертификат для установки.");
            return;
        }

        IsBusy = true;
        try
        {
            var success = await _certificateService.InstallCertificateAsync(SelectedCertificate);
            if (success)
            {
                _notifications.Show(ToastKind.Success, "Сертификат установлен", SelectedCertificate.Owner);
            }
            else
            {
                _dialogService.ShowError("Не удалось установить выбранный сертификат.");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Copy(string? what)
    {
        if (SelectedCertificate is null)
        {
            return;
        }

        var (text, label) = what switch
        {
            "serial" => (SelectedCertificate.SerialNumber, "Серийный номер"),
            _ => (SelectedCertificate.Thumbprint, "Отпечаток")
        };

        try
        {
            Clipboard.SetText(text);
            _notifications.Show(ToastKind.Info, $"{label} скопирован", text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Буфер обмена занят — можно повторить.
        }
    }
}
