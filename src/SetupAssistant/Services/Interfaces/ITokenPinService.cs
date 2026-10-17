using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Смена PIN-кода носителя электронной подписи (как в «Панели управления Рутокен») через PKCS#11-драйвер производителя.</summary>
public interface ITokenPinService
{
    /// <summary>Находит установленные драйверы (Рутокен, JaCarta) и подключённые к ним носители.</summary>
    Task<PinScanResult> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Входит на носитель текущим PIN-кодом и меняет его на новый. PIN-коды нигде не сохраняются и не пишутся в журнал.
    /// Неверный PIN уменьшает счётчик попыток носителя — вызывающий код должен предупредить об этом пользователя.
    /// </summary>
    Task<PinChangeResult> ChangePinAsync(PinTokenInfo token, PinKind kind, string currentPin, string newPin, CancellationToken cancellationToken = default);
}
