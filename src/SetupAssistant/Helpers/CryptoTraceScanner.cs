using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>
/// Поиск следов СКЗИ для полной очистки; сам ничего не меняет. Правило везде одно: собственные разделы и папки производителя
/// удаляются целиком, а в общих разделах Windows — только записи, которые указывают на файлы производителя или носят его имя
/// (имя криптопровайдера, хоста браузера, хранилища). Решение по общим файлам Windows — по подписи файла, а не по маске имени:
/// рядом с cpcng.dll в System32 лежит CPFilters.dll от Microsoft. Карта следов — docs/crypto-cleanup-research.md.
/// </summary>
public static partial class CryptoTraceScanner
{
    private const string CategoryOwn = "Собственные разделы реестра";
    private const string CategoryCapi = "Криптопровайдеры CryptoAPI и OID";
    private const string CategoryCng = "CNG и шифронаборы TLS";
    private const string CategoryLsa = "Пакеты безопасности LSA";
    private const string CategorySystem = "Службы, драйверы, перехват системных DLL";
    private const string CategoryEventLog = "Журналы событий";
    private const string CategoryShell = "Интеграция в Windows";
    private const string CategoryCom = "COM-классы";
    private const string CategoryBrowsers = "Браузеры";
    private const string CategoryStores = "Хранилища сертификатов";
    private const string CategoryFiles = "Папки и файлы";

    private const string CryptographyKey = @"SYSTEM\CurrentControlSet\Control\Cryptography";

    /// <summary>Типы криптопровайдеров ГОСТ (75 — 2001, 80 — 2012/256, 81 — 2012/512): общие у всех российских CSP.</summary>
    private static readonly int[] GostProviderTypes = { 75, 80, 81 };

    /// <summary>Файлы одного ключевого контейнера HDIMAGE (папка «имя.000»).</summary>
    private static readonly string[] ContainerFileNames = { "header.key", "primary.key", "name.key", "masks.key" };

    private static readonly RegistryKey Hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    private static readonly RegistryKey Hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);

    private static readonly VendorTraits[] Vendors =
    {
        new(CryptoVendor.CryptoPro,
            PathMarkers: new[] { @"\Crypto Pro\" },
            NamePrefixes: new[] { "Crypto-Pro", "CryptoPro" },
            CompanyMarkers: new[] { "крипто-про", "crypto-pro", "криптопро", "cryptopro" },
            ProductMarkers: Array.Empty<string>(),
            SystemFilePrefixes: new[] { "cp", "cades" },
            KnownFiles: new[] { "cpcsp.dll", "cpsuprt.dll", "cpcspi.dll", "cpext.dll", "cpcng.dll", "cpssl.dll", "cpsspap.dll", "pkivalidator.dll", "cadescom.dll", "cades.dll", "cpcsp.ru.adm", "cadesplugin.admx", "cadesplugin.adml" },
            KnownFilePrefixes: new[] { "cprodspr", "cproctrl" },
            NativeHosts: new[] { "ru.cryptopro.nmcades" },
            // Все три ID пускает хост ru.cryptopro.nmcades: iifchhfn… и pfhgbfnn… — в Chrome Web Store, epebfceh… — в каталоге
            // Opera/Яндекса (его cadesplugin прописывает Яндекс.Браузеру).
            ExtensionIds: new[] { "iifchhfnnmpdbibifmljnfjhpififfog", "pfhgbfnnjiafkhfdkmpiflachepdcjod", "epebfcehmdedogndhlcacafjaacknbcm" },
            CertificateStores: new[] { "CryptoProTrustedStore" },
            EventSources: new[] { "CProCtrl", "cpcsp", "cpext" },
            Services: new[] { "CProCtrl" },
            OwnRegistryBranches: new[] { @"SOFTWARE\Crypto Pro", @"SOFTWARE\WOW6432Node\Crypto Pro", @"SOFTWARE\Policies\Crypto-Pro", @"SOFTWARE\WOW6432Node\Policies\Crypto-Pro" },
            OwnUserBranches: new[] { @"SOFTWARE\Crypto Pro" },
            OwnFolders: new[] { @"%ProgramFiles%\Crypto Pro", @"%ProgramFiles(x86)%\Crypto Pro", @"%CommonProgramFiles%\Crypto Pro", @"%CommonProgramFiles(x86)%\Crypto Pro", @"%ProgramData%\Crypto Pro" },
            ProfileFolders: new[] { @"AppData\Local\Crypto Pro", @"AppData\Roaming\Crypto Pro" },
            StartMenuFolders: new[] { "КРИПТО-ПРО", "Crypto-Pro" },
            PublisherMarkers: new[] { "криптопро", "крипто-про", "crypto-pro", "cryptopro" }),
        new(CryptoVendor.Rutoken,
            PathMarkers: new[] { @"\Aktiv Co\" },
            NamePrefixes: new[] { "Aktiv " },
            // Мини-драйвер и CSP Рутокена выпущены партнёром «Electronic Platforms», поэтому смотрим и на название продукта.
            CompanyMarkers: new[] { "\"актив\"", "aktiv co" },
            ProductMarkers: new[] { "rutoken" },
            SystemFilePrefixes: new[] { "rt", "rutoken" },
            KnownFiles: new[] { "rtcsp.dll", "rtminidrv.dll", "rtpkcs11.dll", "rtpkcs11ecp.dll", "rtapi.dll", "rtapiex.dll", "rtapilt.dll" },
            KnownFilePrefixes: Array.Empty<string>(),
            NativeHosts: Array.Empty<string>(),
            ExtensionIds: Array.Empty<string>(),
            CertificateStores: Array.Empty<string>(),
            EventSources: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            OwnRegistryBranches: new[] { @"SOFTWARE\Aktiv Co", @"SOFTWARE\WOW6432Node\Aktiv Co" },
            OwnUserBranches: new[] { @"SOFTWARE\Aktiv Co" },
            OwnFolders: new[] { @"%ProgramFiles%\Aktiv Co", @"%ProgramFiles(x86)%\Aktiv Co", @"%ProgramData%\Aktiv Co" },
            ProfileFolders: new[] { @"AppData\Local\Aktiv Co", @"AppData\Roaming\Aktiv Co" },
            StartMenuFolders: new[] { "Рутокен" },
            PublisherMarkers: new[] { "\"актив\"", "aktiv" }),
        new(CryptoVendor.Kontur,
            PathMarkers: new[] { @"\SkbKontur\DiagPlugin\" },
            NamePrefixes: Array.Empty<string>(),
            CompanyMarkers: Array.Empty<string>(),
            ProductMarkers: Array.Empty<string>(),
            SystemFilePrefixes: Array.Empty<string>(),
            KnownFiles: Array.Empty<string>(),
            KnownFilePrefixes: Array.Empty<string>(),
            NativeHosts: new[] { "kd.nc" },
            ExtensionIds: Array.Empty<string>(),
            CertificateStores: Array.Empty<string>(),
            EventSources: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            OwnRegistryBranches: Array.Empty<string>(),
            OwnUserBranches: Array.Empty<string>(),
            OwnFolders: Array.Empty<string>(),
            ProfileFolders: new[] { @"AppData\Local\SkbKontur\DiagPlugin" },
            StartMenuFolders: Array.Empty<string>(),
            PublisherMarkers: Array.Empty<string>()),
        new(CryptoVendor.Gosuslugi,
            PathMarkers: new[] { @"\IFCPlugin\" },
            NamePrefixes: Array.Empty<string>(),
            CompanyMarkers: Array.Empty<string>(),
            ProductMarkers: Array.Empty<string>(),
            SystemFilePrefixes: Array.Empty<string>(),
            KnownFiles: Array.Empty<string>(),
            KnownFilePrefixes: Array.Empty<string>(),
            NativeHosts: new[] { "ru.rtlabs.ifcplugin" },
            ExtensionIds: new[] { "pbefkdcndngodfeigfdgiodgnmbgcfha" },
            CertificateStores: Array.Empty<string>(),
            EventSources: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            OwnRegistryBranches: Array.Empty<string>(),
            OwnUserBranches: Array.Empty<string>(),
            OwnFolders: new[] { @"%ProgramFiles%\Rostelecom\IFCPlugin", @"%ProgramFiles(x86)%\Rostelecom\IFCPlugin" },
            ProfileFolders: new[] { @"AppData\Roaming\Rostelecom\IFCPlugin" },
            StartMenuFolders: Array.Empty<string>(),
            PublisherMarkers: Array.Empty<string>()),
        // У «Кода Безопасности» много продуктов в одной папке и с одной подписью (Secret Net Studio, Jinn-Client), поэтому
        // признаки — только Континент-АП, КБ CSP и eXtended Container: по подпапкам и по названию продукта в файле.
        new(CryptoVendor.SecurityCode,
            PathMarkers: new[] { @"\Security Code\Continent", @"\Security Code\CSP\", @"\Security Code\XC\" },
            NamePrefixes: new[] { "Security Code" },
            CompanyMarkers: Array.Empty<string>(),
            ProductMarkers: new[] { "код безопасности csp", "континент", "extendedcontainer" },
            SystemFilePrefixes: new[] { "sc", "c4", "vpnclient", "xc" },
            KnownFiles: new[] { "sccsp4.dll", "sckcsp4.sys", "c4vpn.sys", "xc.dll" },
            KnownFilePrefixes: Array.Empty<string>(),
            NativeHosts: Array.Empty<string>(),
            ExtensionIds: Array.Empty<string>(),
            CertificateStores: new[] { "ContinentVpnClient Server Store" },
            EventSources: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            // Папки и ветки Континента и КБ CSP удаляет сам шаг удаления продукта (DataPaths/RegistryPaths в Config.json):
            // общая папка «Security Code» может принадлежать и другим продуктам (Secret Net Studio).
            OwnRegistryBranches: Array.Empty<string>(),
            OwnUserBranches: Array.Empty<string>(),
            OwnFolders: Array.Empty<string>(),
            ProfileFolders: Array.Empty<string>(),
            StartMenuFolders: Array.Empty<string>(),
            PublisherMarkers: Array.Empty<string>())
    };

    /// <summary>Ищет следы выбранных производителей.</summary>
    /// <param name="vendors">Чьи следы искать.</param>
    /// <param name="includeContainers">
    /// false — ключевые контейнеры (ветка «Settings» КриптоПро, папки HDIMAGE) в список не попадают; true — попадают с пометкой
    /// <see cref="CleanupItem.IsContainerData"/>.
    /// </param>
    public static IReadOnlyList<CleanupItem> Scan(CryptoVendor vendors, bool includeContainers)
    {
        // С привилегией резервного копирования просматриваются и закрытые папки (у КриптоПро — ProgramData\Crypto Pro\Crypto).
        SystemPrivileges.EnableBackupRestore();

        var context = new ScanContext(Vendors.Where(v => vendors.HasFlag(v.Vendor)).ToArray(), includeContainers);

        // Разделы независимы и почти всё время ждут реестр и диск, поэтому идут параллельно: общее время — самый долгий раздел
        // (COM-классы), а не сумма. Каждый раздел пишет в свою копию контекста; результат собирается в прежнем порядке разделов.
        var sections = new Action<ScanContext>[]
        {
            ScanOwnBranches,
            ScanCryptoApi,
            ScanCng,
            ScanLsa,
            ScanSystem,
            ScanShell,
            c => ScanCom(c, @"SOFTWARE\Classes"),
            c => ScanCom(c, @"SOFTWARE\WOW6432Node\Classes"),
            ScanBrowsers,
            ScanCertificateStores,
            ScanFiles
        };
        var forks = sections.Select(_ => context.Fork()).ToArray();
        Parallel.For(0, sections.Length, i => sections[i](forks[i]));

        foreach (var item in forks.SelectMany(fork => fork.Items))
        {
            context.Add(item);
        }

        return context.Items;
    }

    // ---------- Собственные разделы ----------

    private static void ScanOwnBranches(ScanContext context)
    {
        foreach (var vendor in context.Vendors)
        {
            foreach (var branch in vendor.OwnRegistryBranches)
            {
                AddOwnBranch(context, vendor, "HKLM", Hklm, branch);
            }

            foreach (var (sid, hive) in context.UserHives)
            {
                foreach (var branch in vendor.OwnUserBranches)
                {
                    AddOwnBranch(context, vendor, $@"HKU\{sid}", hive, branch);
                }
            }
        }
    }

    /// <summary>
    /// Ветка производителя целиком. У КриптоПро в подразделе «Settings» лежат ключевые контейнеры: если их не удаляют, ветка
    /// удаляется по частям, а «Settings» остаётся.
    /// </summary>
    private static void AddOwnBranch(ScanContext context, VendorTraits vendor, string hiveName, RegistryKey hive, string branch)
    {
        using var key = SafeOpen(hive, branch);
        if (key is null)
        {
            return;
        }

        var hasContainers = BranchHasContainers(key);
        if (!hasContainers || context.IncludeContainers)
        {
            context.AddKey(vendor, CategoryOwn, $@"{hiveName}\{branch}", isContainerData: hasContainers);
            return;
        }

        foreach (var name in key.GetSubKeyNames().Where(n => !n.Equals("Settings", StringComparison.OrdinalIgnoreCase)))
        {
            context.AddKey(vendor, CategoryOwn, $@"{hiveName}\{branch}\{name}");
        }

        foreach (var value in key.GetValueNames())
        {
            context.AddValue(vendor, CategoryOwn, $@"{hiveName}\{branch}", value);
        }
    }

    /// <summary>В ветке есть контейнеры: непустой Settings\Keys или Settings\Users\&lt;SID&gt;\Keys (закрытые списком доступа — тоже).</summary>
    private static bool BranchHasContainers(RegistryKey branch)
    {
        using var settings = SafeOpenForBackup(branch, "Settings");
        if (settings is null)
        {
            return false;
        }

        using (var machineKeys = SafeOpenForBackup(settings, "Keys"))
        {
            if (machineKeys is { SubKeyCount: > 0 })
            {
                return true;
            }
        }

        using var users = SafeOpenForBackup(settings, "Users");
        foreach (var sid in users?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var keys = SafeOpenForBackup(users!, $@"{sid}\Keys");
            if (keys is null ? SafeOpen(users!, sid) is null : keys.SubKeyCount > 0)
            {
                // Раздел пользователя не открылся даже в обход прав — считаем, что контейнеры там есть.
                return true;
            }
        }

        return false;
    }

    // ---------- CryptoAPI ----------

    private static void ScanCryptoApi(ScanContext context)
    {
        var oidInfo = new List<(string Path, bool CryptoProMarked)>();

        foreach (var root in new[] { @"SOFTWARE\Microsoft\Cryptography", @"SOFTWARE\WOW6432Node\Microsoft\Cryptography" })
        {
            ScanCapiProviders(context, root);
            ScanSmartCards(context, root);
            ScanOids(context, root, oidInfo);
        }

        // Описания алгоритмов решаются после просмотра обоих видов реестра: другой ГОСТ-СКЗИ может быть виден только в одном.
        var cryptoPro = context.Vendors.FirstOrDefault(v => v.Vendor == CryptoVendor.CryptoPro);
        foreach (var (path, marked) in cryptoPro is null ? new() : oidInfo)
        {
            var oid = path[(path.LastIndexOf('\\') + 1)..];
            if (marked || (!context.OtherGostPresent && oid.StartsWith("1.2.643", StringComparison.Ordinal)))
            {
                context.AddKey(cryptoPro!, CategoryCapi, path);
            }
        }

        foreach (var (sid, hive) in context.UserHives)
        {
            // Пользовательские умолчания типов провайдеров: «Provider Type 080» → Name.
            using var userCrypto = SafeOpen(hive, @"SOFTWARE\Microsoft\Cryptography\Provider");
            foreach (var type in userCrypto?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var typeKey = SafeOpen(userCrypto!, type);
                if (context.OwnerOfName(typeKey?.GetValue("Name") as string) is { } vendor)
                {
                    context.AddKey(vendor, CategoryCapi, $@"HKU\{sid}\SOFTWARE\Microsoft\Cryptography\Provider\{type}");
                }
            }
        }
    }

    private static void ScanCapiProviders(ScanContext context, string root)
    {
        var remaining = new List<(string Name, int Type)>();

        using (var providers = SafeOpen(Hklm, $@"{root}\Defaults\Provider"))
        {
            foreach (var name in providers?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var provider = SafeOpen(providers!, name);
                var type = provider?.GetValue("Type") is int t ? t : -1;
                var owner = context.OwnerOfName(name) ?? context.OwnerOfPath(provider?.GetValue("Image Path") as string);
                if (owner is { } vendor)
                {
                    context.AddKey(vendor, CategoryCapi, $@"HKLM\{root}\Defaults\Provider\{name}");
                }
                else
                {
                    remaining.Add((name, type));
                    if (GostProviderTypes.Contains(type))
                    {
                        context.OtherGostPresent = true;
                    }
                }
            }
        }

        using var types = SafeOpen(Hklm, $@"{root}\Defaults\Provider Types");
        foreach (var typeName in types?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var typeKey = SafeOpen(types!, typeName);
            if (context.OwnerOfName(typeKey?.GetValue("Name") as string) is not { } vendor)
            {
                continue;
            }

            // Тип по умолчанию указывает на удаляемый провайдер. Если того же типа есть другой (например, ViPNet), умолчание
            // переводится на него, иначе раздел типа удаляется.
            var number = int.TryParse(typeName.Replace("Type", string.Empty).Trim(), out var n) ? n : -1;
            var replacement = remaining.FirstOrDefault(p => p.Type == number).Name;
            if (replacement is not null)
            {
                context.Add(new CleanupItem
                {
                    Kind = CleanupItemKind.RegistryValue,
                    Vendor = vendor.Vendor,
                    Category = CategoryCapi,
                    Path = $@"HKLM\{root}\Defaults\Provider Types\{typeName}",
                    ValueName = "Name",
                    Item = replacement
                });
            }
            else
            {
                context.AddKey(vendor, CategoryCapi, $@"HKLM\{root}\Defaults\Provider Types\{typeName}");
            }
        }
    }

    /// <summary>Описания носителей (Calais\SmartCards): провайдер или мини-драйвер производителя.</summary>
    private static void ScanSmartCards(ScanContext context, string root)
    {
        using var cards = SafeOpen(Hklm, $@"{root}\Calais\SmartCards");
        foreach (var name in cards?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var card = SafeOpen(cards!, name);
            if (card is null)
            {
                continue;
            }

            var owner = context.OwnerOfName(card.GetValue("Crypto Provider") as string)
                        ?? context.OwnerOfName(card.GetValue("Smart Card Key Storage Provider") as string)
                        ?? context.OwnerOfPath(card.GetValue("80000001") as string);
            if (owner is { } vendor)
            {
                context.AddKey(vendor, CategoryCapi, $@"HKLM\{root}\Calais\SmartCards\{name}");
            }
        }
    }

    /// <summary>
    /// Регистрации функций OID с библиотекой производителя. На тех же OID ГОСТ (1.2.643.*) регистрируются и другие СКЗИ, поэтому
    /// решает значение «Dll», а описания алгоритмов ГОСТ (CryptDllFindOIDInfo) удаляются, только если другого ГОСТ-СКЗИ не остаётся.
    /// </summary>
    private static void ScanOids(ScanContext context, string root, List<(string Path, bool CryptoProMarked)> oidInfo)
    {
        foreach (var encoding in new[] { "EncodingType 0", "EncodingType 1" })
        {
            using var functions = SafeOpen(Hklm, $@"{root}\OID\{encoding}");
            foreach (var function in functions?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var functionKey = SafeOpen(functions!, function);
                foreach (var oid in functionKey?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var oidKey = SafeOpen(functionKey!, oid);
                    if (oidKey is null)
                    {
                        continue;
                    }

                    var path = $@"HKLM\{root}\OID\{encoding}\{function}\{oid}";
                    if (function.Equals("CryptDllFindOIDInfo", StringComparison.OrdinalIgnoreCase))
                    {
                        oidInfo.Add((path, oidKey.GetValue("CPExtraInfo") is not null));
                        continue;
                    }

                    if (function.Equals("CryptDllFindLocalizedName", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var value in oidKey.GetValueNames())
                        {
                            if (context.OwnerOfPath(oidKey.GetValue(value) as string) is { } owner)
                            {
                                context.AddValue(owner, CategoryCapi, path, value);
                            }
                        }

                        continue;
                    }

                    var dll = oidKey.GetValue("Dll") as string;
                    if (context.OwnerOfPath(dll) is { } vendor)
                    {
                        context.AddKey(vendor, CategoryCapi, path);
                    }
                    else if (oid.StartsWith("1.2.643", StringComparison.Ordinal) && !string.IsNullOrEmpty(dll))
                    {
                        // Функцию ГОСТ обслуживает чужая библиотека: значит, установлен другой ГОСТ-СКЗИ.
                        context.OtherGostPresent = true;
                    }
                }
            }
        }
    }

    // ---------- CNG ----------

    /// <summary>
    /// Поставщики CNG производителя и их функции в конфигурации (контексты Default и SSL, в т. ч. шифронаборы TLS). Где у функции
    /// есть и другие поставщики (SHA1, SHA256, KEY_STORAGE у Microsoft), убирается только поставщик, а не функция.
    /// </summary>
    private static void ScanCng(ScanContext context)
    {
        var owned = new Dictionary<string, VendorTraits>(StringComparer.OrdinalIgnoreCase);

        using (var providers = SafeOpen(Hklm, $@"{CryptographyKey}\Providers"))
        {
            foreach (var name in providers?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var um = SafeOpen(providers!, $@"{name}\UM");
                if ((context.OwnerOfName(name) ?? context.OwnerOfPath(um?.GetValue("Image") as string)) is { } vendor)
                {
                    owned[name] = vendor;
                }
            }
        }

        using var configuration = SafeOpen(Hklm, $@"{CryptographyKey}\Configuration\Local");
        foreach (var contextName in configuration?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var contextKey = SafeOpen(configuration!, contextName);
            foreach (var interfaceName in contextKey?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (!uint.TryParse(interfaceName, System.Globalization.NumberStyles.HexNumber, null, out var interfaceId))
                {
                    continue;
                }

                using var interfaceKey = SafeOpen(contextKey!, interfaceName);
                foreach (var function in interfaceKey?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var functionKey = SafeOpen(interfaceKey!, function);
                    var list = functionKey?.GetValue("Providers") as string[] ?? Array.Empty<string>();

                    // Поставщик мог быть уже удалён деинсталлятором, а его имя в списках осталось: узнаём его по имени.
                    var ours = list.Where(p => owned.ContainsKey(p) || context.OwnerOfName(p) is not null).ToList();
                    if (ours.Count == 0)
                    {
                        continue;
                    }

                    var vendor = owned.TryGetValue(ours[0], out var v) ? v : context.OwnerOfName(ours[0])!;
                    if (ours.Count == list.Length)
                    {
                        context.Add(new CleanupItem { Kind = CleanupItemKind.CngFunction, Vendor = vendor.Vendor, Category = CategoryCng, Path = contextName, CngInterface = interfaceId, Item = function });
                        continue;
                    }

                    foreach (var provider in ours)
                    {
                        context.Add(new CleanupItem { Kind = CleanupItemKind.CngFunctionProvider, Vendor = vendor.Vendor, Category = CategoryCng, Path = contextName, CngInterface = interfaceId, Item = function, CngProvider = provider });
                    }
                }
            }
        }

        foreach (var (name, vendor) in owned)
        {
            context.Add(new CleanupItem { Kind = CleanupItemKind.CngProvider, Vendor = vendor.Vendor, Category = CategoryCng, Path = name });
        }
    }

    // ---------- LSA ----------

    /// <summary>Пакеты безопасности производителя в списках LSA (cpsspap у КриптоПро): убирается элемент, остальные остаются.</summary>
    private static void ScanLsa(ScanContext context)
    {
        foreach (var keyPath in new[] { @"SYSTEM\CurrentControlSet\Control\Lsa", @"SYSTEM\CurrentControlSet\Control\Lsa\OSConfig" })
        {
            using var key = SafeOpen(Hklm, keyPath);
            foreach (var valueName in new[] { "Security Packages", "Authentication Packages", "Notification Packages" })
            {
                foreach (var package in key?.GetValue(valueName) as string[] ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(package) && context.OwnerOfPath(package.Trim('"') + ".dll") is { } vendor)
                    {
                        context.Add(new CleanupItem { Kind = CleanupItemKind.ListItem, Vendor = vendor.Vendor, Category = CategoryLsa, Path = $@"HKLM\{keyPath}", ValueName = valueName, Item = package });
                    }
                }
            }
        }

        using var securityProviders = SafeOpen(Hklm, @"SYSTEM\CurrentControlSet\Control\SecurityProviders");
        foreach (var provider in (securityProviders?.GetValue("SecurityProviders") as string ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (context.OwnerOfPath(provider) is { } vendor)
            {
                context.Add(new CleanupItem { Kind = CleanupItemKind.ListItem, Vendor = vendor.Vendor, Category = CategoryLsa, Path = @"HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders", ValueName = "SecurityProviders", Item = provider });
            }
        }
    }

    // ---------- Службы, драйверы, журналы ----------

    private static void ScanSystem(ScanContext context)
    {
        var cryptoPro = context.Vendors.FirstOrDefault(v => v.Vendor == CryptoVendor.CryptoPro);
        if (cryptoPro is not null)
        {
            // Драйвер CProCtrl подменяет системные DLL по этим спискам и проверяет хэши файлов КриптоПро.
            foreach (var name in new[] { "CProPatches", "CProIntegrity" })
            {
                context.AddKeyIfExists(cryptoPro, CategorySystem, Hklm, $@"SYSTEM\CurrentControlSet\Control\Session Manager\{name}");
            }
        }

        using var services = SafeOpen(Hklm, @"SYSTEM\CurrentControlSet\Services");
        foreach (var name in services?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            if (name.Equals("EventLog", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var service = SafeOpen(services!, name);
            using var parameters = service is null ? null : SafeOpen(service, "Parameters");
            var owner = context.Vendors.FirstOrDefault(v => v.Services.Contains(name, StringComparer.OrdinalIgnoreCase))
                        ?? context.OwnerOfPath(service?.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string)
                        ?? context.OwnerOfPath(parameters?.GetValue("ServiceDll", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
            if (owner is not null)
            {
                context.Add(new CleanupItem { Kind = CleanupItemKind.Service, Vendor = owner.Vendor, Category = CategorySystem, Path = name });
            }
        }

        using var logs = SafeOpen(Hklm, @"SYSTEM\CurrentControlSet\Services\EventLog");
        foreach (var log in logs?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var logKey = SafeOpen(logs!, log);
            foreach (var source in logKey?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var sourceKey = SafeOpen(logKey!, source);
                if (sourceKey is null)
                {
                    continue;
                }

                // Имена источников общие («cng», «csp», «pcsc»): решает файл сообщений. Источник без файла — только по точному имени.
                var files = new[] { "EventMessageFile", "CategoryMessageFile", "ParameterMessageFile" }
                    .Select(v => sourceKey.GetValue(v, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string)
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .SelectMany(f => f!.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    .ToList();

                var owner = files.Count == 0
                    ? context.Vendors.FirstOrDefault(v => v.EventSources.Contains(source, StringComparer.OrdinalIgnoreCase))
                    : files.Select(context.OwnerOfPath).FirstOrDefault(o => o is not null);
                if (owner is not null)
                {
                    context.AddKey(owner, CategoryEventLog, $@"HKLM\SYSTEM\CurrentControlSet\Services\EventLog\{log}\{source}");
                }
            }
        }
    }

    // ---------- Интеграция в Windows ----------

    private static void ScanShell(ScanContext context)
    {
        foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion" })
        {
            using (var appPaths = SafeOpen(Hklm, $@"{root}\App Paths"))
            {
                foreach (var name in appPaths?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var app = SafeOpen(appPaths!, name);
                    if ((context.OwnerOfPath(app?.GetValue(null) as string) ?? context.OwnerOfPath(app?.GetValue("Path") as string)) is { } vendor)
                    {
                        context.AddKey(vendor, CategoryShell, $@"HKLM\{root}\App Paths\{name}");
                    }
                }
            }

            AddValuesByData(context, Hklm, "HKLM", $@"{root}\Control Panel\Cpls");
            AddValuesByData(context, Hklm, "HKLM", $@"{root}\Run");
            AddValuesByData(context, Hklm, "HKLM", $@"{root}\RunOnce");
            AddValuesByName(context, Hklm, "HKLM", $@"{root}\SharedDLLs");

            using var extended = SafeOpen(Hklm, $@"{root}\Control Panel\Extended Properties");
            foreach (var name in extended?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                AddValuesByName(context, Hklm, "HKLM", $@"{root}\Control Panel\Extended Properties\{name}");
            }
        }

        foreach (var (sid, hive) in context.UserHives)
        {
            AddValuesByData(context, hive, $@"HKU\{sid}", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            AddValuesByData(context, hive, $@"HKU\{sid}", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce");
        }
    }

    /// <summary>Значения, данные которых указывают на файлы производителя (Cpls, Run).</summary>
    private static void AddValuesByData(ScanContext context, RegistryKey hive, string hiveName, string keyPath)
    {
        using var key = SafeOpen(hive, keyPath);
        foreach (var name in key?.GetValueNames() ?? Array.Empty<string>())
        {
            if (context.OwnerOfPath(key!.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string) is { } vendor)
            {
                context.AddValue(vendor, CategoryShell, $@"{hiveName}\{keyPath}", name);
            }
        }
    }

    /// <summary>Значения, имя которых — путь к файлу производителя (SharedDLLs, Extended Properties).</summary>
    private static void AddValuesByName(ScanContext context, RegistryKey hive, string hiveName, string keyPath)
    {
        using var key = SafeOpen(hive, keyPath);
        foreach (var name in key?.GetValueNames() ?? Array.Empty<string>())
        {
            if (context.OwnerOfPath(name) is { } vendor)
            {
                context.AddValue(vendor, CategoryShell, $@"{hiveName}\{keyPath}", name);
            }
        }
    }

    // ---------- COM ----------

    /// <summary>
    /// COM-классы с сервером в папке производителя (CAdESCOM и библиотеки КриптоПро), их библиотеки типов, интерфейсы, ProgID и AppID.
    /// </summary>
    private static void ScanCom(ScanContext context, params string[] roots)
    {
        foreach (var root in roots)
        {
            using var classes = SafeOpen(Hklm, root);
            if (classes is null)
            {
                continue;
            }

            var clsids = new Dictionary<string, VendorTraits>(StringComparer.OrdinalIgnoreCase);
            var typeLibs = new Dictionary<string, VendorTraits>(StringComparer.OrdinalIgnoreCase);
            var appIds = new Dictionary<string, VendorTraits>(StringComparer.OrdinalIgnoreCase);
            var progIds = new Dictionary<string, VendorTraits>(StringComparer.OrdinalIgnoreCase);

            using (var clsidRoot = SafeOpen(classes, "CLSID"))
            {
                foreach (var clsid in clsidRoot?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var classKey = SafeOpen(clsidRoot!, clsid);
                    if (classKey is null)
                    {
                        continue;
                    }

                    VendorTraits? owner = null;
                    foreach (var server in new[] { "InprocServer32", "LocalServer32", "InprocHandler32" })
                    {
                        using var serverKey = SafeOpen(classKey, server);
                        owner ??= context.OwnerOfPath(serverKey?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
                    }

                    if (owner is null)
                    {
                        continue;
                    }

                    clsids[clsid] = owner;
                    context.AddKey(owner, CategoryCom, $@"HKLM\{root}\CLSID\{clsid}");

                    if (classKey.GetValue("AppID") is string appId)
                    {
                        appIds[appId] = owner;
                    }

                    foreach (var progIdKey in new[] { "ProgID", "VersionIndependentProgID" })
                    {
                        using var key = SafeOpen(classKey, progIdKey);
                        if (key?.GetValue(null) is string progId && !string.IsNullOrWhiteSpace(progId))
                        {
                            progIds[progId] = owner;
                        }
                    }

                    using var typeLib = SafeOpen(classKey, "TypeLib");
                    if (typeLib?.GetValue(null) is string libId)
                    {
                        typeLibs[libId] = owner;
                    }
                }
            }

            using (var typeLibRoot = SafeOpen(classes, "TypeLib"))
            {
                foreach (var libId in typeLibRoot?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var lib = SafeOpen(typeLibRoot!, libId);
                    var owner = typeLibs.GetValueOrDefault(libId) ?? FindTypeLibOwner(context, lib);
                    if (owner is not null)
                    {
                        typeLibs[libId] = owner;
                        context.AddKey(owner, CategoryCom, $@"HKLM\{root}\TypeLib\{libId}");
                    }
                }
            }

            if (typeLibs.Count > 0)
            {
                using var interfaces = SafeOpen(classes, "Interface");
                foreach (var iid in interfaces?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var typeLib = SafeOpen(interfaces!, $@"{iid}\TypeLib");
                    if (typeLib?.GetValue(null) is string libId && typeLibs.TryGetValue(libId, out var owner))
                    {
                        context.AddKey(owner, CategoryCom, $@"HKLM\{root}\Interface\{iid}");
                    }
                }
            }

            // ProgID («CAdESCOM.CadesSignedData.1» и т. п.): по ссылке из класса и по своему CLSID.
            foreach (var name in classes.GetSubKeyNames())
            {
                if (name.StartsWith('.') || name.StartsWith('{') || !name.Contains('.'))
                {
                    continue;
                }

                var owner = progIds.GetValueOrDefault(name);
                if (owner is null && clsids.Count > 0)
                {
                    using var clsid = SafeOpen(classes, $@"{name}\CLSID");
                    if (clsid?.GetValue(null) is string id)
                    {
                        owner = clsids.GetValueOrDefault(id);
                    }
                }

                if (owner is not null)
                {
                    context.AddKey(owner, CategoryCom, $@"HKLM\{root}\{name}");
                }
            }

            using var appIdRoot = SafeOpen(classes, "AppID");
            foreach (var appId in appIdRoot?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (appIds.TryGetValue(appId, out var owner))
                {
                    context.AddKey(owner, CategoryCom, $@"HKLM\{root}\AppID\{appId}");
                    continue;
                }

                // Записи AppID по имени exe ссылаются на GUID AppID значением «AppID».
                using var entry = SafeOpen(appIdRoot!, appId);
                if (entry?.GetValue("AppID") is string target && appIds.TryGetValue(target, out var targetOwner))
                {
                    context.AddKey(targetOwner, CategoryCom, $@"HKLM\{root}\AppID\{appId}");
                }
            }
        }
    }

    /// <summary>Библиотека типов принадлежит производителю, если хоть один её файл (версия\язык\win32|win64) лежит у него.</summary>
    private static VendorTraits? FindTypeLibOwner(ScanContext context, RegistryKey? lib)
    {
        foreach (var version in lib?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var versionKey = SafeOpen(lib!, version);
            foreach (var lcid in versionKey?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                foreach (var platform in new[] { "win32", "win64" })
                {
                    using var file = SafeOpen(versionKey!, $@"{lcid}\{platform}");
                    if (context.OwnerOfPath(file?.GetValue(null) as string) is { } owner)
                    {
                        return owner;
                    }
                }
            }
        }

        return null;
    }

    // ---------- Браузеры ----------

    private static readonly string[] BrowserRoots = { @"Google\Chrome", "Chromium", @"Microsoft\Edge", "Mozilla", @"Yandex\YandexBrowser" };

    /// <summary>Хосты native messaging плагинов и внешние расширения, которые прописывают установщики плагинов.</summary>
    private static void ScanBrowsers(ScanContext context)
    {
        var roots = new List<(RegistryKey Hive, string HiveName, string Software)>
        {
            (Hklm, "HKLM", "SOFTWARE"),
            (Hklm, "HKLM", @"SOFTWARE\WOW6432Node")
        };
        roots.AddRange(context.UserHives.Select(u => (u.Hive, $@"HKU\{u.Sid}", "SOFTWARE")));

        foreach (var (hive, hiveName, software) in roots)
        {
            foreach (var browser in BrowserRoots)
            {
                var hostsPath = $@"{software}\{browser}\NativeMessagingHosts";
                using (var hosts = SafeOpen(hive, hostsPath))
                {
                    foreach (var name in hosts?.GetSubKeyNames() ?? Array.Empty<string>())
                    {
                        using var host = SafeOpen(hosts!, name);
                        var owner = context.Vendors.FirstOrDefault(v => v.NativeHosts.Contains(name, StringComparer.OrdinalIgnoreCase))
                                    ?? context.OwnerOfPath(host?.GetValue(null) as string);
                        if (owner is not null)
                        {
                            context.AddKey(owner, CategoryBrowsers, $@"{hiveName}\{hostsPath}\{name}");
                        }
                    }
                }

                var extensionsPath = $@"{software}\{browser}\Extensions";
                using var extensions = SafeOpen(hive, extensionsPath);
                foreach (var id in extensions?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    var owner = context.Vendors.FirstOrDefault(v => v.ExtensionIds.Contains(id, StringComparer.OrdinalIgnoreCase));
                    if (owner is not null)
                    {
                        context.AddKey(owner, CategoryBrowsers, $@"{hiveName}\{extensionsPath}\{id}");
                    }
                }
            }
        }
    }

    private static readonly (string Name, string UserData)[] BrowserProfileRoots =
    {
        ("Chromium / Chromium GOST", @"AppData\Local\Chromium\User Data"),
        ("Google Chrome", @"AppData\Local\Google\Chrome\User Data"),
        ("Microsoft Edge", @"AppData\Local\Microsoft\Edge\User Data"),
        ("Яндекс.Браузер", @"AppData\Local\Yandex\YandexBrowser\User Data")
    };

    /// <summary>ID расширений, которые прописывают установщики плагинов (iifchhfn…, epebfceh…, pfhgbfnn…).</summary>
    public static IEnumerable<string> VendorExtensionIds => Vendors.SelectMany(v => v.ExtensionIds);

    /// <summary>
    /// Расширения из <paramref name="known"/> (ID → название), которые лежат в профилях браузеров у всех пользователей. Профиль
    /// браузера очистка не правит: файлы настроек защищены подписью, и браузер сбросил бы их. Поэтому только список для
    /// удаления вручную.
    /// </summary>
    public static IReadOnlyList<BrowserExtensionInfo> FindProfileExtensions(IReadOnlyDictionary<string, string> known)
    {
        var found = new List<BrowserExtensionInfo>();
        foreach (var userProfile in UserProfiles())
        {
            foreach (var (browser, userData) in BrowserProfileRoots)
            {
                foreach (var profile in SafeEnumerateDirectories(Path.Combine(userProfile, userData)))
                {
                    if (!File.Exists(Path.Combine(profile, "Preferences")))
                    {
                        continue;
                    }

                    foreach (var (id, name) in known)
                    {
                        var version = SafeEnumerateDirectories(Path.Combine(profile, "Extensions", id)).FirstOrDefault();
                        if (version is not null)
                        {
                            found.Add(new BrowserExtensionInfo(browser, Path.GetFileName(profile), Path.GetFileName(userProfile), id, ReadExtensionName(version) ?? name));
                        }
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Название из manifest.json установленного расширения; null — нет или оно локализовано (__MSG_…__).</summary>
    private static string? ReadExtensionName(string versionFolder)
    {
        try
        {
            using var manifest = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(versionFolder, "manifest.json")),
                new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var name = manifest.RootElement.TryGetProperty("name", out var value) ? value.GetString() : null;
            return string.IsNullOrWhiteSpace(name) || name.StartsWith("__MSG_", StringComparison.Ordinal) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------- Хранилища сертификатов ----------

    private static void ScanCertificateStores(ScanContext context)
    {
        foreach (var vendor in context.Vendors)
        {
            foreach (var store in vendor.CertificateStores)
            {
                context.AddKeyIfExists(vendor, CategoryStores, Hklm, $@"SOFTWARE\Microsoft\SystemCertificates\{store}");
                foreach (var (sid, hive) in context.UserHives)
                {
                    using var key = SafeOpen(hive, $@"SOFTWARE\Microsoft\SystemCertificates\{store}");
                    if (key is not null)
                    {
                        context.AddKey(vendor, CategoryStores, $@"HKU\{sid}\SOFTWARE\Microsoft\SystemCertificates\{store}");
                    }
                }
            }
        }
    }

    // ---------- Файлы ----------

    private static void ScanFiles(ScanContext context)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

        foreach (var vendor in context.Vendors)
        {
            var folders = vendor.OwnFolders.Select(Environment.ExpandEnvironmentVariables).ToList();
            foreach (var profile in context.Profiles)
            {
                folders.AddRange(vendor.ProfileFolders.Select(f => Path.Combine(profile, f)));
                folders.AddRange(vendor.StartMenuFolders.Select(f => Path.Combine(profile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs", f)));
            }

            folders.AddRange(vendor.StartMenuFolders.Select(f => Path.Combine(programs, f)));

            foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AddFolder(context, vendor, folder);
            }
        }

        // Файлы производителя в системных папках Windows — только с его подписью (CompanyName/ProductName).
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var directory in new[] { Path.Combine(windows, "System32"), Path.Combine(windows, "SysWOW64"), Path.Combine(windows, @"System32\drivers") })
        {
            foreach (var file in SafeEnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                var vendor = context.Vendors.FirstOrDefault(v => IsKnownFile(v, name)
                                                                 || (v.SystemFilePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && IsSignedBy(file, v)));
                if (vendor is not null)
                {
                    context.AddFile(vendor, file);
                }
            }
        }

        // Шаблоны групповых политик: у них нет сведений о версии, поэтому по точному имени.
        var policyFiles = new List<string> { Path.Combine(windows, @"System32\GroupPolicy\Adm\cpcsp.ru.adm"), Path.Combine(windows, @"PolicyDefinitions\CadesPlugin.admx") };
        foreach (var language in SafeEnumerateDirectories(Path.Combine(windows, "PolicyDefinitions")))
        {
            policyFiles.Add(Path.Combine(language, "CadesPlugin.adml"));
        }

        var cryptoPro = context.Vendors.FirstOrDefault(v => v.Vendor == CryptoVendor.CryptoPro);
        if (cryptoPro is not null)
        {
            foreach (var file in policyFiles.Where(File.Exists))
            {
                context.AddFile(cryptoPro, file);
            }
        }
    }

    /// <summary>
    /// Папка производителя целиком. Папки с ключевыми контейнерами внутри без согласия на их удаление не трогаются: тогда папка
    /// разбирается на части, и в список попадает всё, кроме контейнеров.
    /// </summary>
    private static void AddFolder(ScanContext context, VendorTraits vendor, string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var containers = FindContainerFolders(folder);
        if (containers.Count == 0 || context.IncludeContainers)
        {
            context.Add(new CleanupItem { Kind = CleanupItemKind.Directory, Vendor = vendor.Vendor, Category = CategoryFiles, Path = folder, IsContainerData = containers.Count > 0 });
            return;
        }

        foreach (var child in SafeEnumerateDirectories(folder))
        {
            if (!containers.Any(c => IsSameOrInside(c, child)))
            {
                context.Add(new CleanupItem { Kind = CleanupItemKind.Directory, Vendor = vendor.Vendor, Category = CategoryFiles, Path = child });
            }
            else if (!containers.Any(c => string.Equals(c, child, StringComparison.OrdinalIgnoreCase)))
            {
                AddFolder(context, vendor, child);
            }
        }

        foreach (var file in SafeEnumerateFiles(folder))
        {
            context.AddFile(vendor, file);
        }
    }

    // ---------- Ключевые контейнеры ----------

    /// <summary>
    /// Ключевые контейнеры КриптоПро на компьютере: в реестре (Settings\Users\&lt;SID&gt;\Keys и Settings\Keys) и в папках
    /// считывателя HDIMAGE во всех профилях. Носители (токены, флешки) не просматриваются.
    /// </summary>
    public static IReadOnlyList<KeyContainerInfo> FindContainers()
    {
        SystemPrivileges.EnableBackupRestore();

        var result = new List<KeyContainerInfo>();

        foreach (var root in new[] { @"SOFTWARE\WOW6432Node\Crypto Pro\Settings", @"SOFTWARE\Crypto Pro\Settings" })
        {
            AddRegistryContainers(result, $@"{root}\Keys", "компьютер");

            using var users = SafeOpenForBackup(Hklm, $@"{root}\Users");
            foreach (var sid in users?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                AddRegistryContainers(result, $@"{root}\Users\{sid}\Keys", AccountName(sid));
            }
        }

        foreach (var root in ContainerSearchRoots())
        {
            foreach (var folder in FindContainerFolders(root.Path))
            {
                result.Add(new KeyContainerInfo(ReadContainerName(folder) ?? Path.GetFileName(folder), folder, false, root.Owner));
            }
        }

        return result;
    }

    private static void AddRegistryContainers(List<KeyContainerInfo> result, string keysPath, string owner)
    {
        using var keys = SafeOpenForBackup(Hklm, keysPath);
        foreach (var name in keys?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            result.Add(new KeyContainerInfo(name, $@"HKLM\{keysPath}\{name}", true, owner));
        }
    }

    /// <summary>Папки, где считыватель HDIMAGE хранит контейнеры: «Crypto Pro» в профилях и в ProgramData.</summary>
    private static IEnumerable<(string Path, string Owner)> ContainerSearchRoots()
    {
        yield return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Crypto Pro"), "компьютер");
        foreach (var profile in UserProfiles())
        {
            var owner = Path.GetFileName(profile);
            yield return (Path.Combine(profile, @"AppData\Local\Crypto Pro"), owner);
            yield return (Path.Combine(profile, @"AppData\Roaming\Crypto Pro"), owner);
        }
    }

    /// <summary>Папки контейнеров HDIMAGE внутри <paramref name="root"/> (в них лежат header.key, primary.key и т. д.).</summary>
    public static List<string> FindContainerFolders(string root)
    {
        var found = new List<string>();
        try
        {
            if (!Directory.Exists(root))
            {
                return found;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.key", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false }))
            {
                if (ContainerFileNames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                    && Path.GetDirectoryName(file) is { } folder
                    && !found.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(folder);
                }
            }
        }
        catch (Exception)
        {
            // Часть папки не читается: считаем контейнером её целиком, чтобы без согласия она не удалилась (см. AddFolder).
            found.Add(root);
        }

        return found;
    }

    /// <summary>Имя контейнера из name.key (DER: SEQUENCE { IA5String имя }); null — прочитать не удалось.</summary>
    private static string? ReadContainerName(string folder)
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(folder, "name.key"));
            var index = Array.IndexOf(bytes, (byte)0x16);
            if (index < 0 || index + 1 >= bytes.Length)
            {
                return null;
            }

            int length = bytes[index + 1];
            var start = index + 2;
            if ((length & 0x80) != 0)
            {
                var size = length & 0x7F;
                length = 0;
                for (var i = 0; i < size && start < bytes.Length; i++)
                {
                    length = (length << 8) | bytes[start++];
                }
            }

            return start + length <= bytes.Length ? Encoding.UTF8.GetString(bytes, start, length).Trim('\0', ' ') : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string AccountName(string sid)
    {
        try
        {
            return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        }
        catch (Exception)
        {
            return sid;
        }
    }

    // ---------- Общее ----------

    /// <summary>Профили пользователей (обычные учётные записи и Azure AD), из ProfileList.</summary>
    internal static List<string> UserProfiles()
    {
        var result = new List<string>();
        using var list = SafeOpen(Hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        foreach (var sid in list?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !sid.StartsWith("S-1-12-1-", StringComparison.Ordinal))
            {
                continue;
            }

            using var profile = SafeOpen(list!, sid);
            if (profile?.GetValue("ProfileImagePath") is string path && Directory.Exists(Environment.ExpandEnvironmentVariables(path)))
            {
                result.Add(Environment.ExpandEnvironmentVariables(path));
            }
        }

        return result;
    }

    /// <summary>Загруженные кусты пользователей (вошедшие в систему; текущий пользователь — тоже здесь).</summary>
    private static List<(string Sid, RegistryKey Hive)> LoadedUserHives()
    {
        var result = new List<(string, RegistryKey)>();
        foreach (var sid in Hku.GetSubKeyNames())
        {
            if ((sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || sid.StartsWith("S-1-12-1-", StringComparison.Ordinal))
                && !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase)
                && SafeOpen(Hku, sid) is { } hive)
            {
                result.Add((sid, hive));
            }
        }

        return result;
    }

    internal static RegistryKey? SafeOpen(RegistryKey parent, string subKey)
    {
        try
        {
            return parent.OpenSubKey(subKey);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static RegistryKey? SafeOpenForBackup(RegistryKey parent, string subKey)
    {
        try
        {
            return SystemPrivileges.OpenReadable(parent, subKey);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory).ToList() : Enumerable.Empty<string>();
        }
        catch (Exception)
        {
            return Enumerable.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateDirectories(directory).ToList() : Enumerable.Empty<string>();
        }
        catch (Exception)
        {
            return Enumerable.Empty<string>();
        }
    }

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Имя файла производителя, известное точно (cpsspap.dll, CProCtrl.5.0.0.10008.sys).</summary>
    private static bool IsKnownFile(VendorTraits vendor, string name) =>
        vendor.KnownFiles.Contains(name, StringComparer.OrdinalIgnoreCase)
        || vendor.KnownFilePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Установленные программы производителей из <paramref name="vendors"/>, которых нет в списке удаляемых (например, КриптоПро PDF
    /// или КриптоАРМ-модули КриптоПро). Глубокая очистка с ними сломала бы их, поэтому сначала их нужно удалить.
    /// </summary>
    /// <param name="isManaged">Программа из списка удаляемых (по отображаемому имени).</param>
    public static IReadOnlyList<string> FindForeignProducts(CryptoVendor vendors, Func<string, bool> isManaged)
    {
        var markers = Vendors.Where(v => vendors.HasFlag(v.Vendor)).SelectMany(v => v.PublisherMarkers).ToArray();
        var found = new List<string>();
        if (markers.Length == 0)
        {
            return found;
        }

        var roots = new List<(RegistryKey Hive, string Path)>
        {
            (Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Hklm, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
        };
        roots.AddRange(LoadedUserHives().Select(u => (u.Hive, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")));

        foreach (var (hive, path) in roots)
        {
            using var uninstall = SafeOpen(hive, path);
            foreach (var name in uninstall?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var entry = SafeOpen(uninstall!, name);
                var displayName = entry?.GetValue("DisplayName") as string;
                var publisher = entry?.GetValue("Publisher") as string ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(displayName)
                    && markers.Any(m => publisher.Contains(m, StringComparison.OrdinalIgnoreCase))
                    && !isManaged(displayName)
                    && !found.Contains(displayName, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(displayName);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Файл производителя по сведениям о версии: метка компании — только в CompanyName, метка продукта — в ProductName или
    /// FileDescription. Описание «Криптопровайдер SecurityCode» содержит «криптопро», поэтому компанию по описанию не ищем.
    /// </summary>
    private static bool IsSignedBy(string file, VendorTraits vendor)
    {
        if (vendor.CompanyMarkers.Length == 0 && vendor.ProductMarkers.Length == 0)
        {
            return false;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            var product = $"{info.ProductName}|{info.FileDescription}";
            return vendor.CompanyMarkers.Any(m => info.CompanyName?.Contains(m, StringComparison.OrdinalIgnoreCase) == true)
                   || vendor.ProductMarkers.Any(m => product.Contains(m, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint bufferLength);

    /// <summary>Короткий путь «C:\PROGRA~2\COMMON~1\CRYPTO~1\…» в длинный (если файл ещё существует).</summary>
    private static string ToLongPath(string path)
    {
        if (!path.Contains('~'))
        {
            return path;
        }

        var buffer = new StringBuilder(1024);
        return GetLongPathNameW(path, buffer, (uint)buffer.Capacity) is > 0 and < 1024 ? buffer.ToString() : path;
    }

    private sealed record VendorTraits(
        CryptoVendor Vendor,
        string[] PathMarkers,
        string[] NamePrefixes,
        string[] CompanyMarkers,
        string[] ProductMarkers,
        string[] SystemFilePrefixes,
        string[] KnownFiles,
        string[] KnownFilePrefixes,
        string[] NativeHosts,
        string[] ExtensionIds,
        string[] CertificateStores,
        string[] EventSources,
        string[] Services,
        string[] OwnRegistryBranches,
        string[] OwnUserBranches,
        string[] OwnFolders,
        string[] ProfileFolders,
        string[] StartMenuFolders,
        string[] PublisherMarkers);

    /// <summary>Состояние одного поиска: производители, кэш подписей файлов, найденное.</summary>
    private sealed class ScanContext
    {
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private readonly string _system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

        // Общие для всех разделов (и потоков): путь из реестра → производитель; файл + производитель → подписан ли им.
        private readonly ConcurrentDictionary<string, VendorTraits?> _pathOwners;
        private readonly ConcurrentDictionary<string, bool> _signed;

        public ScanContext(VendorTraits[] vendors, bool includeContainers)
        {
            Vendors = vendors;
            IncludeContainers = includeContainers;
            UserHives = LoadedUserHives();
            Profiles = UserProfiles();
            _pathOwners = new(StringComparer.OrdinalIgnoreCase);
            _signed = new(StringComparer.OrdinalIgnoreCase);
        }

        private ScanContext(ScanContext parent)
        {
            Vendors = parent.Vendors;
            IncludeContainers = parent.IncludeContainers;
            UserHives = parent.UserHives;
            Profiles = parent.Profiles;
            _pathOwners = parent._pathOwners;
            _signed = parent._signed;
        }

        /// <summary>Копия для параллельного раздела: общие кэши и сведения о пользователях, но свой список найденного.</summary>
        public ScanContext Fork() => new(this);

        public VendorTraits[] Vendors { get; }

        public bool IncludeContainers { get; }

        public List<(string Sid, RegistryKey Hive)> UserHives { get; }

        public List<string> Profiles { get; }

        public List<CleanupItem> Items { get; } = new();

        /// <summary>Есть ГОСТ-СКЗИ, который не удаляется (ViPNet, не выбранный Код Безопасности и т. п.).</summary>
        public bool OtherGostPresent { get; set; }

        public void Add(CleanupItem item)
        {
            var identity = $"{item.Kind}|{item.Path}|{item.ValueName}|{item.Item}|{item.CngInterface}|{item.CngProvider}";
            if (_seen.Add(identity))
            {
                Items.Add(item);
            }
        }

        public void AddKey(VendorTraits vendor, string category, string path, bool isContainerData = false) =>
            Add(new CleanupItem { Kind = CleanupItemKind.RegistryKey, Vendor = vendor.Vendor, Category = category, Path = path, IsContainerData = isContainerData });

        public void AddKeyIfExists(VendorTraits vendor, string category, RegistryKey hive, string subKey)
        {
            using var key = SafeOpen(hive, subKey);
            if (key is not null)
            {
                AddKey(vendor, category, $@"HKLM\{subKey}");
            }
        }

        public void AddValue(VendorTraits vendor, string category, string path, string valueName) =>
            Add(new CleanupItem { Kind = CleanupItemKind.RegistryValue, Vendor = vendor.Vendor, Category = category, Path = path, ValueName = valueName });

        public void AddFile(VendorTraits vendor, string path) =>
            Add(new CleanupItem { Kind = CleanupItemKind.File, Vendor = vendor.Vendor, Category = CategoryFiles, Path = path });

        /// <summary>Производитель по имени криптопровайдера/поставщика («Crypto-Pro GOST R 34.10-2012 …»).</summary>
        public VendorTraits? OwnerOfName(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? null
                : Vendors.FirstOrDefault(v => v.NamePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// Производитель по пути к файлу из реестра. Понимает кавычки, аргументы, «@файл,-123», переменные среды, «\SystemRoot\»,
        /// «\??\», короткие имена и имена файлов без папки (тогда файл ищется в System32). Файлы в папках Windows — по подписи.
        /// </summary>
        public VendorTraits? OwnerOfPath(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (_pathOwners.TryGetValue(raw, out var cached))
            {
                return cached;
            }

            var owner = ResolveOwner(raw);
            _pathOwners[raw] = owner;
            return owner;
        }

        private VendorTraits? ResolveOwner(string raw)
        {
            var path = ExtractPath(raw);
            if (path.Length == 0)
            {
                return null;
            }

            var marked = Vendors.FirstOrDefault(v => v.PathMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase)));
            if (marked is not null)
            {
                return marked;
            }

            var longPath = ToLongPath(path);
            marked = Vendors.FirstOrDefault(v => v.PathMarkers.Any(m => longPath.Contains(m, StringComparison.OrdinalIgnoreCase)));
            if (marked is not null)
            {
                return marked;
            }

            // Только файлы из папок Windows или без папки: чужие программы в Program Files по имени файла не трогаем.
            var directory = Path.GetDirectoryName(longPath);
            var inWindows = string.IsNullOrEmpty(directory) || longPath.StartsWith(_windows, StringComparison.OrdinalIgnoreCase);
            if (!inWindows)
            {
                return null;
            }

            var name = Path.GetFileName(longPath);
            var full = string.IsNullOrEmpty(directory) ? Path.Combine(_system32, name) : longPath;

            // Точные имена известны без обращения к диску (cpsspap.dll, CProCtrl.5.0.0.10008.sys).
            if (Vendors.FirstOrDefault(v => IsKnownFile(v, name)) is { } known)
            {
                return known;
            }

            // Остальное — по сведениям о версии внутри файла, но только для имён, похожих на файлы производителя (как и при поиске
            // самих файлов в ScanFiles): в реестре тысячи ссылок на системные DLL, а читать версию каждой с диска — секунды на холодном кэше.
            foreach (var vendor in Vendors.Where(v => v.SystemFilePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
            {
                if (_signed.GetOrAdd($"{full}|{vendor.Vendor}", _ => File.Exists(full) && IsSignedBy(full, vendor)))
                {
                    return vendor;
                }
            }

            return null;
        }

        private string ExtractPath(string raw)
        {
            var text = raw.Trim();
            if (text.StartsWith('@'))
            {
                text = text[1..];
                var comma = text.LastIndexOf(',');
                if (comma > 0)
                {
                    text = text[..comma];
                }
            }

            if (text.StartsWith('"'))
            {
                var end = text.IndexOf('"', 1);
                text = end > 0 ? text[1..end] : text.Trim('"');
            }
            else
            {
                // Без кавычек путь кончается на расширении исполняемого файла, дальше — аргументы.
                var match = System.Text.RegularExpressions.Regex.Match(text, @"^.*?\.(dll|exe|sys|cpl|ocx|tlb|adm|admx|adml)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    text = match.Value;
                }
            }

            text = Environment.ExpandEnvironmentVariables(text);
            if (text.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                text = text[4..];
            }

            if (text.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                text = Path.Combine(_windows, text[@"\SystemRoot\".Length..]);
            }
            else if (text.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase))
            {
                text = Path.Combine(_windows, text);
            }

            return text;
        }
    }
}
