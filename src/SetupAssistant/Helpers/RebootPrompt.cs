using System.Diagnostics;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Helpers;

/// <summary>Общий вопрос «перезагрузить компьютер» для мастера и удаления: один текст, предупреждение про несохранённые файлы.</summary>
public static class RebootPrompt
{
    public const int DelaySeconds = 30;

    /// <summary>Модальное окно с предложением перезагрузки; true — пользователь выбрал «Перезагрузить сейчас».</summary>
    /// <param name="completedWork">Что завершится после перезагрузки, в родительном падеже: «настройки», «удаления».</param>
    /// <param name="note">Дополнение к тексту (например, что настройка продолжится после входа в систему).</param>
    public static bool Ask(IDialogService dialogs, string completedWork, string? note = null)
    {
        var message =
            $"Для завершения {completedWork} необходимо перезагрузить компьютер.\n\n" +
            "Сохраните все открытые файлы и закройте программы: несохранённые данные будут потеряны. " +
            $"Компьютер перезагрузится через {DelaySeconds} секунд после нажатия «Перезагрузить сейчас».";

        if (!string.IsNullOrWhiteSpace(note))
        {
            message += "\n\n" + note;
        }

        return dialogs.ConfirmChoice(message, "Требуется перезагрузка", "Перезагрузить сейчас", "Позже");
    }

    /// <summary>Запускает перезагрузку через <see cref="DelaySeconds"/> секунд; отменить можно командой <c>shutdown /a</c>.</summary>
    public static void Schedule(string reason) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", $"/r /t {DelaySeconds} /c \"{reason}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
}
