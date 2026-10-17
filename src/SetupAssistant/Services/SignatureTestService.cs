using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ISignatureTestService"/>
public sealed class SignatureTestService : ISignatureTestService
{
    // Константы КриптоПро CAdESCOM / CAPICOM.
    private const int CapicomCurrentUserStore = 2;
    private const int CapicomLocalMachineStore = 1;
    private const int CapicomStoreOpenMaximumAllowed = 2;
    private const int CapicomCertificateFindSha1Hash = 0;
    private const int CadescomBase64ToBinary = 1;
    private const int CadescomCadesBes = 1;

    private readonly ILoggingService _logger;

    public SignatureTestService(ILoggingService logger)
    {
        _logger = logger;
    }

    public Task<SignatureTestResult> RunAsync(string thumbprint, IProgress<SignatureStageUpdate> progress, CancellationToken cancellationToken = default)
    {
        // COM-объекты КриптоПро живут в однопоточной квартире (STA); запрос PIN-кода тоже идёт из этого потока.
        var completion = new TaskCompletionSource<SignatureTestResult>();
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Run(thumbprint, progress, cancellationToken));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "SignatureTest"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private SignatureTestResult Run(string thumbprint, IProgress<SignatureStageUpdate> progress, CancellationToken cancellationToken)
    {
        _logger.Info("Тест подписи", $"Запуск теста, сертификат {thumbprint}");

        // 1. Компонент
        var signedDataType = Type.GetTypeFromProgID("CAdESCOM.CadesSignedData");
        var signerType = Type.GetTypeFromProgID("CAdESCOM.CPSigner");
        var storeType = Type.GetTypeFromProgID("CAdESCOM.Store");
        if (signedDataType is null || signerType is null || storeType is null)
        {
            const string message = "Компонент КриптоПро CAdESCOM не зарегистрирован в системе";
            progress.Report(new SignatureStageUpdate("component", StatusLevel.Error, message, HelpAdvisor.ForSignatureStage("component", message)));
            return Fail("Тест не пройден: нет компонента CAdESCOM.");
        }

        progress.Report(new SignatureStageUpdate("component", StatusLevel.Ok, "Компонент CAdESCOM найден"));
        cancellationToken.ThrowIfCancellationRequested();

        // 2. Сертификат
        // Сертификат может лежать в личном хранилище пользователя или компьютера — ищем в обоих (как и список на экране).
        X509Certificate2? certificate = null;
        var capicomLocation = CapicomCurrentUserStore;
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            certificate = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).OfType<X509Certificate2>().FirstOrDefault();
            if (certificate is not null)
            {
                capicomLocation = location == StoreLocation.CurrentUser ? CapicomCurrentUserStore : CapicomLocalMachineStore;
                break;
            }
        }

        if (certificate is null)
        {
            const string message = "Сертификат не найден в хранилище «Личное»";
            progress.Report(new SignatureStageUpdate("certificate", StatusLevel.Error, message, HelpAdvisor.ForSignatureStage("certificate", message)));
            return Fail("Тест не пройден: сертификат не найден.");
        }

        if (DateTime.Now > certificate.NotAfter || DateTime.Now < certificate.NotBefore)
        {
            var message = $"Срок действия сертификата: {certificate.NotBefore:dd.MM.yyyy} — {certificate.NotAfter:dd.MM.yyyy}, истёк или ещё не наступил";
            progress.Report(new SignatureStageUpdate("certificate", StatusLevel.Error, message, HelpAdvisor.ForSignatureStage("certificate", "истёк")));
            return Fail("Тест не пройден: сертификат недействителен по сроку.");
        }

        if (!certificate.HasPrivateKey)
        {
            const string message = "У сертификата нет закрытого ключа на этом компьютере";
            progress.Report(new SignatureStageUpdate("certificate", StatusLevel.Error, message, HelpAdvisor.ForSignatureStage("certificate", "ключевой")));
            return Fail("Тест не пройден: нет закрытого ключа.");
        }

        progress.Report(new SignatureStageUpdate("certificate", StatusLevel.Ok,
            $"Действует до {certificate.NotAfter:dd.MM.yyyy}, закрытый ключ есть"));
        cancellationToken.ThrowIfCancellationRequested();

        // 3–4. Подпись и проверка через CAdESCOM
        var data = Encoding.UTF8.GetBytes($"Проверка подписи {DateTime.Now:dd.MM.yyyy HH:mm:ss} (Помощник настройки рабочего места)");
        var base64 = Convert.ToBase64String(data);
        string signature;

        object? store2 = null;
        object? signer = null;
        object? signedData = null;
        try
        {
            progress.Report(new SignatureStageUpdate("sign", StatusLevel.InProgress, "Подписываем тестовые данные. КриптоПро может запросить PIN-код…"));

            try
            {
                dynamic comStore = store2 = Activator.CreateInstance(storeType)!;
                comStore.Open(capicomLocation, "My", CapicomStoreOpenMaximumAllowed);
                dynamic found = comStore.Certificates.Find(CapicomCertificateFindSha1Hash, thumbprint, false);
                if (found.Count == 0)
                {
                    throw new InvalidOperationException("CAdESCOM не видит этот сертификат в хранилище");
                }

                dynamic comSigner = signer = Activator.CreateInstance(signerType)!;
                comSigner.Certificate = found.Item(1);
                comSigner.CheckCertificate = true;

                dynamic comData = signedData = Activator.CreateInstance(signedDataType)!;
                comData.ContentEncoding = CadescomBase64ToBinary;
                comData.Content = base64;
                signature = comData.SignCades(comSigner, CadescomCadesBes, true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = Describe(ex);
                _logger.Warning("Тест подписи", "Не удалось подписать", message);
                progress.Report(new SignatureStageUpdate("sign", StatusLevel.Error, message, HelpAdvisor.ForSignatureStage("sign", message)));
                return Fail("Подписать не удалось.");
            }

            progress.Report(new SignatureStageUpdate("sign", StatusLevel.Ok, $"Подпись создана, размер {signature.Length} символов (CAdES-BES, отсоединённая)"));
            cancellationToken.ThrowIfCancellationRequested();

            object? verifier = null;
            try
            {
                progress.Report(new SignatureStageUpdate("verify", StatusLevel.InProgress, "Проверяем подпись…"));
                dynamic comVerify = verifier = Activator.CreateInstance(signedDataType)!;
                comVerify.ContentEncoding = CadescomBase64ToBinary;
                comVerify.Content = base64;
                comVerify.VerifyCades(signature, CadescomCadesBes, true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = Describe(ex);
                _logger.Warning("Тест подписи", "Подпись не прошла проверку", message);
                progress.Report(new SignatureStageUpdate("verify", StatusLevel.Error, message, HelpAdvisor.ForSignatureStage("verify", message)));
                return Fail("Подпись создана, но проверка не пройдена.");
            }
            finally
            {
                Release(verifier);
            }

            progress.Report(new SignatureStageUpdate("verify", StatusLevel.Ok, "Подпись корректна: данные не изменены, сертификат принят"));
            _logger.Info("Тест подписи", "Тест пройден");
            return new SignatureTestResult(true, "Электронная подпись работает: тестовые данные подписаны и подпись проверена.");
        }
        finally
        {
            Release(signedData);
            Release(signer);
            Release(store2);
        }
    }

    private SignatureTestResult Fail(string summary)
    {
        _logger.Warning("Тест подписи", summary);
        return new SignatureTestResult(false, summary);
    }

    private static string Describe(Exception ex)
    {
        var inner = ex is System.Reflection.TargetInvocationException { InnerException: { } i } ? i : ex;
        return inner is COMException com
            ? $"{com.Message.Trim()} (0x{com.HResult:X8})"
            : inner.Message.Trim();
    }

    private static void Release(object? comObject)
    {
        try
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch (Exception)
        {
            // Освобождение не критично: объект освободит сборщик мусора.
        }
    }
}
