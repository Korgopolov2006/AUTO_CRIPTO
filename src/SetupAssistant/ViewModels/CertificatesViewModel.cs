using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Просмотр и установка сертификатов электронной подписи из хранилищ Windows.</summary>
public partial class CertificatesViewModel : ViewModelBase
{
    private readonly ICertificateService _certificateService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<CertificateInfo> Certificates { get; } = new();

    [ObservableProperty]
    private CertificateInfo? _selectedCertificate;

    public CertificatesViewModel(ICertificateService certificateService, IDialogService dialogService, INavigationService navigationService)
    {
        _certificateService = certificateService;
        _dialogService = dialogService;
        _navigationService = navigationService;
        LoadCertificates();
    }

    [RelayCommand]
    private void LoadCertificates()
    {
        Certificates.Clear();
        foreach (var certificate in _certificateService.GetCertificates())
        {
            Certificates.Add(certificate);
        }
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
                _dialogService.ShowInfo($"Сертификат «{SelectedCertificate.Owner}» успешно установлен.");
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
    private void GoBack() => _navigationService.NavigateTo<DashboardViewModel>();
}
