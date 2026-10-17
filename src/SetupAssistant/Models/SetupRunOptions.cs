namespace SetupAssistant.Models;

/// <summary>Параметры запуска мастера настройки.</summary>
/// <param name="ForceReinstall">Устанавливать и те компоненты, которые уже установлены и проходят проверку.</param>
public sealed record SetupRunOptions(bool ForceReinstall = false);
