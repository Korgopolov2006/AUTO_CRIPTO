using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Helpers;

/// <summary>Состав шагов мастера настройки — общий для окна приложения и режима --silent.</summary>
public static class SetupPlan
{
    /// <summary>Шаги установки с парными шагами проверки; в конце — итоговая диагностика.</summary>
    public static List<SetupStepModel> Build(IConfigService configService, bool includeTokenStep = true)
    {
        var steps = new List<SetupStepModel>();

        void Pair(SetupStepKind install, string installTitle, SetupStepKind verify, string verifyTitle)
        {
            steps.Add(new SetupStepModel { Kind = install, Title = installTitle, IsSelectable = true });
            steps.Add(new SetupStepModel { Kind = verify, Title = verifyTitle, PairedInstallKind = install });
        }

        void Single(SetupStepKind kind, string title) =>
            steps.Add(new SetupStepModel { Kind = kind, Title = title, IsSelectable = true });

        Pair(SetupStepKind.InstallCryptoProCsp, "Установка КриптоПро CSP", SetupStepKind.VerifyCryptoProCsp, "Проверка установки КриптоПро CSP");
        Pair(SetupStepKind.InstallBrowserPlugin, "Установка КриптоПро Browser Plugin", SetupStepKind.VerifyBrowserPlugin, "Проверка Browser Plugin");
        Pair(SetupStepKind.InstallRutokenDriver, "Установка драйвера Рутокен", SetupStepKind.VerifyRutokenDriver, "Проверка драйвера Рутокен");
        Pair(SetupStepKind.InstallChromiumGost, "Установка Chromium GOST", SetupStepKind.VerifyChromiumGost, "Проверка Chromium GOST");
        Pair(SetupStepKind.InstallKonturPlugin, "Установка Контур.Плагина", SetupStepKind.VerifyKonturPlugin, "Проверка Контур.Плагина");
        Pair(SetupStepKind.InstallGosuslugiPlugin, "Установка Плагина Госуслуг", SetupStepKind.VerifyGosuslugiPlugin, "Проверка Плагина Госуслуг");

        // Континент-АП нужен не на каждом рабочем месте: шаги появляются, только когда задан его дистрибутив (а импорт — когда задан файл конфигурации).
        if (configService.Current.Installers.KontinentAp.IsConfigured)
        {
            Pair(SetupStepKind.InstallKontinentAp, "Установка Континент-АП", SetupStepKind.VerifyKontinentAp, "Проверка Континент-АП");
            if (!string.IsNullOrWhiteSpace(configService.Current.Kontinent.ConfigPath))
            {
                Single(SetupStepKind.ImportKontinentConfig, "Импорт конфигурации Континент-АП");
            }
        }

        Single(SetupStepKind.InstallBrowserExtensions, "Установка расширений браузера");

        if (configService.Current.CaCertificates.Count > 0)
        {
            Single(SetupStepKind.InstallCaCertificates, "Установка сертификатов УЦ");
        }

        if (includeTokenStep)
        {
            Single(SetupStepKind.InstallTokenCertificates, "Установка сертификата с носителя в хранилище");
        }

        steps.Add(new SetupStepModel { Kind = SetupStepKind.FinalDiagnostic, Title = "Повторная диагностика рабочего места" });
        return steps;
    }

    /// <summary>Оставляет выбранными только указанные шаги установки (и их проверки); итоговая диагностика выполняется всегда.</summary>
    public static void SelectOnly(IEnumerable<SetupStepModel> steps, IReadOnlyCollection<SetupStepKind> kinds)
    {
        foreach (var step in steps)
        {
            step.IsSelected = step.Kind == SetupStepKind.FinalDiagnostic
                              || kinds.Contains(step.Kind)
                              || (step.PairedInstallKind is { } paired && kinds.Contains(paired));
        }
    }
}
