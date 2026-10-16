using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Проверка компонентов КриптоПро: CSP, Browser Plugin, провайдеры, контейнеры, считыватели.</summary>
public interface ICryptoProService
{
    ComponentInfo CheckCsp();

    ComponentInfo CheckBrowserPlugin();

    /// <summary>Зарегистрированные в Windows провайдеры КриптоПро: ГОСТ 2012 (типы 80/81) и ГОСТ 2001 (тип 75).</summary>
    ComponentInfo CheckProviders();

    /// <summary>Ключевые контейнеры, доступные КриптоПро CSP (токены, смарт-карты, реестр). PIN-код не запрашивается.</summary>
    ComponentInfo CheckContainers();

    /// <summary>Считыватели смарт-карт, видимые КриптоПро CSP. Это отдельный уровень от USB-драйвера токена.</summary>
    ComponentInfo CheckReaders();

    /// <summary>Наличие «Континент-АП». Только определение: настройки продукта приложение не изменяет.</summary>
    ComponentInfo CheckKontinentAp();
}
