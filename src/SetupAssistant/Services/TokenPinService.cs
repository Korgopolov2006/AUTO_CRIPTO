using System.IO;
using System.Text;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ITokenPinService"/>
public sealed class TokenPinService : ITokenPinService
{
    /// <summary>Драйверы, которые устанавливает производитель в System32: PKCS#11-библиотека каждого.</summary>
    private static readonly (string Provider, string FileName)[] Libraries =
    {
        ("Рутокен", "rtPKCS11ECP.dll"),
        ("JaCarta", "jcPKCS11-2.dll")
    };

    private readonly ILoggingService _logger;

    public TokenPinService(ILoggingService logger)
    {
        _logger = logger;
    }

    public Task<PinScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    private PinScanResult Scan(CancellationToken cancellationToken)
    {
        var tokens = new List<PinTokenInfo>();
        var providers = new List<string>();
        var problems = new List<string>();
        var factories = new Pkcs11InteropFactories();

        foreach (var (provider, path) in FindLibraries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            providers.Add(provider);

            try
            {
                using var library = factories.Pkcs11LibraryFactory.LoadPkcs11Library(factories, path, AppType.MultiThreaded);
                foreach (var slot in library.GetSlotList(SlotsType.WithTokenPresent))
                {
                    var info = slot.GetTokenInfo();
                    var flags = info.TokenFlags;
                    tokens.Add(new PinTokenInfo(
                        provider, path, slot.SlotId,
                        info.Label.Trim(), info.Model.Trim(), info.SerialNumber.Trim(),
                        flags.UserPinLocked, flags.UserPinFinalTry, flags.UserPinCountLow, flags.UserPinToBeChanged,
                        flags.SoPinLocked, flags.SoPinFinalTry, flags.SoPinCountLow));
                }
            }
            catch (Exception ex)
            {
                // Драйвер есть, но не загрузился или не отвечает: показываем причину, а не молчим.
                _logger.Warning("PIN-код носителя", $"Не удалось прочитать носители через драйвер «{provider}»", ex.Message);
                problems.Add($"{provider}: {ex.Message}");
            }
        }

        return new PinScanResult(tokens, providers, problems.Count == 0 ? null : string.Join("; ", problems));
    }

    private static IEnumerable<(string Provider, string Path)> FindLibraries()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (var (provider, fileName) in Libraries)
        {
            var path = Path.Combine(system32, fileName);
            if (File.Exists(path))
            {
                yield return (provider, path);
            }
        }
    }

    public Task<PinChangeResult> ChangePinAsync(PinTokenInfo token, PinKind kind, string currentPin, string newPin, CancellationToken cancellationToken = default) =>
        Task.Run(() => ChangePin(token, kind, currentPin, newPin), cancellationToken);

    private PinChangeResult ChangePin(PinTokenInfo token, PinKind kind, string currentPin, string newPin)
    {
        var who = kind == PinKind.User ? "пользователя" : "администратора";
        var current = Encoding.UTF8.GetBytes(currentPin);
        var next = Encoding.UTF8.GetBytes(newPin);

        try
        {
            var factories = new Pkcs11InteropFactories();
            using var library = factories.Pkcs11LibraryFactory.LoadPkcs11Library(factories, token.LibraryPath, AppType.MultiThreaded);

            var slot = library.GetSlotList(SlotsType.WithTokenPresent).FirstOrDefault(s => s.SlotId == token.SlotId);
            if (slot is null || slot.GetTokenInfo().SerialNumber.Trim() != token.SerialNumber)
            {
                return new PinChangeResult(false, "Носитель не найден: возможно, его отключили. Подключите его и повторите.");
            }

            using var session = slot.OpenSession(SessionType.ReadWrite);

            // Сначала вход текущим PIN-кодом: так ошибка «неверный PIN» отличается от ошибки нового значения.
            try
            {
                session.Login(kind == PinKind.User ? CKU.CKU_USER : CKU.CKU_SO, current);
            }
            catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_USER_ALREADY_LOGGED_IN)
            {
                // Другое приложение уже вошло на этот носитель: смена PIN всё равно возможна.
            }

            session.SetPin(current, next);

            try
            {
                session.Logout();
            }
            catch (Pkcs11Exception)
            {
                // Выход не критичен: сессия закроется вместе с using.
            }

            _logger.Info("PIN-код носителя", $"PIN-код {who} изменён (носитель № {token.SerialNumber})");
            return new PinChangeResult(true, $"PIN-код {who} изменён.");
        }
        catch (Pkcs11Exception ex)
        {
            _logger.Warning("PIN-код носителя", $"Смена PIN-кода {who} не выполнена (носитель № {token.SerialNumber})", ex.RV.ToString());
            return new PinChangeResult(false, Describe(ex.RV, kind));
        }
        catch (Exception ex)
        {
            _logger.Error("PIN-код носителя", "Непредвиденная ошибка при смене PIN-кода", ex.Message);
            return new PinChangeResult(false, $"Не удалось обратиться к носителю: {ex.Message}");
        }
        finally
        {
            Array.Clear(current);
            Array.Clear(next);
        }
    }

    private static string Describe(CKR code, PinKind kind)
    {
        var who = kind == PinKind.User ? "пользователя" : "администратора";
        return code switch
        {
            CKR.CKR_PIN_INCORRECT =>
                $"Текущий PIN-код {who} неверный. С каждой неудачной попыткой счётчик попыток носителя уменьшается: после исчерпания носитель заблокируется.",
            CKR.CKR_PIN_LOCKED =>
                $"PIN-код {who} заблокирован из-за неверных попыток. Разблокировать его можно PIN-кодом администратора в «Панели управления Рутокен».",
            CKR.CKR_PIN_LEN_RANGE =>
                "Длина PIN-кода не подходит этому носителю. Задайте другое значение (обычно от 6 до 32 символов).",
            CKR.CKR_PIN_INVALID or CKR.CKR_PIN_EXPIRED =>
                "Носитель не принял новый PIN-код: он не соответствует политике носителя. Попробуйте другое значение.",
            CKR.CKR_DEVICE_REMOVED or CKR.CKR_TOKEN_NOT_PRESENT or CKR.CKR_SLOT_ID_INVALID =>
                "Носитель отключён. Подключите его и повторите.",
            CKR.CKR_TOKEN_WRITE_PROTECTED =>
                "Носитель защищён от записи, изменить PIN-код нельзя.",
            CKR.CKR_USER_PIN_NOT_INITIALIZED =>
                "На носителе не задан PIN-код пользователя. Инициализируйте носитель в «Панели управления Рутокен».",
            _ => $"Носитель отклонил операцию (код {code})."
        };
    }
}
