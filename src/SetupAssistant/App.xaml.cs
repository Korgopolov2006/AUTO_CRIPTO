using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SetupAssistant.Cli;
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

        var options = CommandLineOptions.Parse(e.Args);

        var services = new ServiceCollection();
        ConfigureServices(services, options.IsHeadless);
        _serviceProvider = services.BuildServiceProvider();

        if (options.IsHeadless)
        {
            // Окна нет: приложение завершится само, когда закончится работа, с кодом возврата для скриптов.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunHeadlessAsync(options);
            return;
        }

        var config = _serviceProvider.GetRequiredService<IConfigService>();
        _serviceProvider.GetRequiredService<IUserSettingsService>().ApplyTheme();
        _serviceProvider.GetRequiredService<ILoggingService>()
            .Info("Запуск приложения", $"Версия {config.Current.AppVersion}; конфигурация: {config.ConfigSource}");

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (options.Resume)
        {
            _ = ResumeAfterRebootAsync();
        }
    }

    private async Task RunHeadlessAsync(CommandLineOptions options)
    {
        var exitCode = 2;
        try
        {
            exitCode = await new HeadlessRunner(_serviceProvider!).RunAsync(options);
        }
        finally
        {
            Shutdown(exitCode);
        }
    }

    private async Task ResumeAfterRebootAsync()
    {
        try
        {
            // Незавершённая полная очистка важнее: она могла быть запущена и после настройки.
            if (Helpers.ResumeRegistration.ReadCleanup() is { } cleanup)
            {
                _serviceProvider!.GetRequiredService<INavigationService>().NavigateTo<UninstallViewModel>();
                await _serviceProvider!.GetRequiredService<UninstallViewModel>().ResumeFullCleanupAsync(cleanup);
                return;
            }

            _serviceProvider!.GetRequiredService<INavigationService>().NavigateTo<SetupWizardViewModel>();
            await _serviceProvider!.GetRequiredService<SetupWizardViewModel>().ResumeAfterRebootAsync();
        }
        catch (Exception ex)
        {
            _serviceProvider!.GetRequiredService<ILoggingService>()
                .Error("Продолжение после перезагрузки", "Не удалось продолжить настройку", ex.Message);
        }
    }

    private static void ConfigureServices(IServiceCollection services, bool headless)
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
        services.AddSingleton<ITokenPinService, TokenPinService>();
        services.AddSingleton<ISignatureTestService, SignatureTestService>();
        services.AddSingleton<ISupportBundleService, SupportBundleService>();
        services.AddSingleton<IDiagnosticService, DiagnosticService>();
        services.AddSingleton<IWorkplaceStatusService, WorkplaceStatusService>();
        services.AddSingleton<IPreflightService, PreflightService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<ISetupOrchestrationService, SetupOrchestrationService>();
        services.AddSingleton<IUninstallService, UninstallService>();

        // Инфраструктура MVVM: в режиме без окна диалоги ничего не показывают
        if (headless)
        {
            services.AddSingleton<IDialogService, SilentDialogService>();
        }
        else
        {
            services.AddSingleton<IDialogService, DialogService>();
        }

        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels. Экраны с длительными операциями — одиночки: прогресс и результат не теряются при переходе
        // на другую страницу меню, а возврат показывает ту же выполняющуюся/завершённую операцию.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<SetupWizardViewModel>();
        services.AddSingleton<UninstallViewModel>();
        services.AddSingleton<CertificatesViewModel>();
        services.AddSingleton<TokenPinViewModel>();
        services.AddSingleton<SignatureTestViewModel>();
        services.AddSingleton<FirstRunViewModel>();
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
