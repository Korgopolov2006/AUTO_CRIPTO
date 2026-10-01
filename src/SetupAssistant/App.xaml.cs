using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SetupAssistant.Services;
using SetupAssistant.Services.Interfaces;
using SetupAssistant.ViewModels;

namespace SetupAssistant;

/// <summary>Точка входа приложения — настройка контейнера внедрения зависимостей.</summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Конфигурация
        services.AddSingleton<IConfigService, ConfigService>();

        // Базовые сервисы
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ISystemService, SystemService>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IVersionService, VersionService>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IInstallerService, InstallerService>();

        // Доменные сервисы
        services.AddSingleton<ICryptoProService, CryptoProService>();
        services.AddSingleton<IBrowserService, BrowserService>();
        services.AddSingleton<IExtensionService, ExtensionService>();
        services.AddSingleton<IExtensionUpdateService, ExtensionUpdateService>();
        services.AddSingleton<ICertificateService, CertificateService>();
        services.AddSingleton<ICaCertificateService, CaCertificateService>();
        services.AddSingleton<INativeMessagingService, NativeMessagingService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<ISetupOrchestrationService, SetupOrchestrationService>();

        // Инфраструктура MVVM
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<CertificatesViewModel>();
        services.AddTransient<LogViewerViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Окна
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
