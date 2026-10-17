using System.Diagnostics;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Helpers;

/// <summary>Пользовательский сценарий «Пакет для поддержки»: выбор файла, вопрос о сертификатах, создание, уведомление. Общий для экранов «Настройки» и «Журнал».</summary>
public static class SupportBundleWorkflow
{
    public static async Task RunAsync(
        IDialogService dialogs,
        INotificationService notifications,
        ISupportBundleService bundles,
        ISystemService system)
    {
        var path = dialogs.ShowSaveFileDialog(
            $"Пакет_для_поддержки_{system.GetSystemInfo().ComputerName}_{DateTime.Now:yyyyMMdd_HHmm}.zip",
            "Архив ZIP (*.zip)|*.zip");
        if (path is null)
        {
            return;
        }

        var includeCertificates = dialogs.Confirm(
            "Добавить в пакет список сертификатов (владелец, организация, серийный номер, срок действия)?\n\n" +
            "Это персональные данные: добавляйте их, только если специалисту поддержки нужно разобраться именно с сертификатами. " +
            "PIN-коды, закрытые ключи и содержимое токена в пакет не попадают никогда.",
            "Пакет для поддержки");

        try
        {
            var created = await bundles.CreateAsync(path, includeCertificates);
            notifications.Show(ToastKind.Success, "Пакет для поддержки создан", created);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{created}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"Не удалось создать пакет для поддержки: {ex.Message}");
        }
    }
}
