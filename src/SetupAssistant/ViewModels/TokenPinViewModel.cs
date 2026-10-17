using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Смена PIN-кода носителя (пароля электронной подписи) — как в «Панели управления Рутокен».</summary>
public partial class TokenPinViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private const int MinPinLength = 6;
    private const int MaxPinLength = 32;

    private readonly ITokenPinService _pinService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigationService;
    private readonly DispatcherTimer _scanTimer;
    private bool _scanRunning;

    public ObservableCollection<PinTokenInfo> Tokens { get; } = new();

    public IReadOnlyList<PinKindOption> KindOptions { get; } = new[]
    {
        new PinKindOption(PinKind.User, "PIN-код пользователя (пароль подписи)"),
        new PinKindOption(PinKind.Administrator, "PIN-код администратора")
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToken), nameof(CanChange), nameof(PinWarning), nameof(HasPinWarning))]
    private PinTokenInfo? _selectedToken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange), nameof(PinWarning), nameof(HasPinWarning))]
    private PinKind _selectedKind = PinKind.User;

    /// <summary>Найден хотя бы один драйвер носителей (Рутокен или JaCarta).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoDriver), nameof(ShowForm))]
    private bool _hasDriver;

    [ObservableProperty]
    private string _statusText = "Поиск носителей…";

    [ObservableProperty]
    private string? _driverProblem;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    [ObservableProperty]
    private bool _resultSuccess;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange))]
    private bool _isChanging;

    public bool HasToken => SelectedToken is not null;

    public bool HasNoDriver => !HasDriver;

    /// <summary>Форму показываем, когда драйвер установлен (носитель может быть ещё не подключён — тогда подсказка в строке состояния).</summary>
    public bool ShowForm => HasDriver;

    public bool HasResult => !string.IsNullOrEmpty(ResultMessage);

    /// <summary>PIN заблокирован или меняется сейчас — кнопка недоступна.</summary>
    public bool CanChange => SelectedToken is { } token && !token.IsPinLocked(SelectedKind) && !IsChanging;

    public string? PinWarning
    {
        get
        {
            if (SelectedToken is not { } token)
            {
                return null;
            }

            if (token.IsPinLocked(SelectedKind))
            {
                return "PIN-код заблокирован после неверных попыток. Разблокировать его можно PIN-кодом администратора в «Панели управления Рутокен».";
            }

            if (token.IsFinalTry(SelectedKind))
            {
                return "Осталась последняя попытка ввода PIN-кода! При ошибке носитель заблокируется — вводите внимательно.";
            }

            if (token.IsCountLow(SelectedKind))
            {
                return "Попыток ввода PIN-кода осталось мало: неверный ввод приближает блокировку носителя.";
            }

            return SelectedKind == PinKind.User && token.UserPinToBeChanged
                ? "На носителе стоит PIN-код по умолчанию — рекомендуется сменить его."
                : null;
        }
    }

    public bool HasPinWarning => PinWarning is not null;

    public TokenPinViewModel(
        ITokenPinService pinService,
        IDialogService dialogService,
        INotificationService notifications,
        INavigationService navigationService)
    {
        _pinService = pinService;
        _dialogService = dialogService;
        _notifications = notifications;
        _navigationService = navigationService;

        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _scanTimer.Tick += async (_, _) => await ScanAsync();
    }

    partial void OnResultMessageChanged(string value) => OnPropertyChanged(nameof(HasResult));

    public void OnNavigatedTo()
    {
        ResultMessage = string.Empty;
        _ = ScanAsync();
        _scanTimer.Start();
    }

    /// <summary>F5: заново найти подключённые носители.</summary>
    public void Refresh() => _ = ScanAsync();

    public void OnNavigatedFrom() => _scanTimer.Stop();

    [RelayCommand]
    private async Task ScanAsync()
    {
        // Во время смены PIN носитель не опрашиваем: лишние обращения могут помешать операции.
        if (_scanRunning || IsChanging)
        {
            return;
        }

        _scanRunning = true;
        try
        {
            var result = await _pinService.ScanAsync();
            HasDriver = result.Providers.Count > 0;
            DriverProblem = result.Problem;

            SyncTokens(result.Tokens);
            StatusText = !HasDriver
                ? "Драйверы носителей не установлены"
                : Tokens.Count == 0
                    ? "Носитель не подключён — подключите его к USB-порту"
                    : Tokens.Count == 1
                        ? "Носитель подключён"
                        : $"Подключено носителей: {Tokens.Count}";
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось получить список носителей: {ex.Message}";
        }
        finally
        {
            _scanRunning = false;
        }
    }

    /// <summary>Обновляет список, не сбрасывая выбор пользователя, если выбранный носитель всё ещё подключён.</summary>
    private void SyncTokens(IReadOnlyList<PinTokenInfo> found)
    {
        var selected = SelectedToken;

        var changed = found.Count != Tokens.Count
                      || found.Where((t, i) => !t.Equals(Tokens[i])).Any();
        if (!changed)
        {
            return;
        }

        Tokens.Clear();
        foreach (var token in found)
        {
            Tokens.Add(token);
        }

        SelectedToken = selected is null
            ? Tokens.FirstOrDefault()
            : Tokens.FirstOrDefault(t => t.IsSameToken(selected)) ?? Tokens.FirstOrDefault();
    }

    /// <summary>Проверка введённых значений до обращения к носителю: лишняя неверная попытка стоит счётчика попыток.</summary>
    public static string? Validate(string currentPin, string newPin, string confirmPin)
    {
        if (string.IsNullOrEmpty(currentPin))
        {
            return "Введите текущий PIN-код.";
        }

        if (string.IsNullOrEmpty(newPin))
        {
            return "Введите новый PIN-код.";
        }

        if (newPin.Length < MinPinLength || newPin.Length > MaxPinLength)
        {
            return $"Длина нового PIN-кода — от {MinPinLength} до {MaxPinLength} символов.";
        }

        if (newPin.Any(c => c < 0x20 || c > 0x7E))
        {
            return "Новый PIN-код может содержать только латинские буквы, цифры и спецсимволы: русские буквы неудобно вводить в других программах.";
        }

        if (newPin == currentPin)
        {
            return "Новый PIN-код должен отличаться от текущего.";
        }

        if (newPin == "12345678")
        {
            return "Этот PIN-код стоит на новых носителях по умолчанию и слишком прост. Придумайте другой.";
        }

        return newPin == confirmPin ? null : "Новый PIN-код и подтверждение не совпадают.";
    }

    /// <summary>Меняет PIN-код; возвращает true при успехе — страница после этого очищает поля ввода.</summary>
    public async Task<bool> ChangePinAsync(string currentPin, string newPin, string confirmPin)
    {
        ResultMessage = string.Empty;

        if (SelectedToken is not { } token || !CanChange)
        {
            return false;
        }

        var problem = Validate(currentPin, newPin, confirmPin);
        if (problem is not null)
        {
            ResultSuccess = false;
            ResultMessage = problem;
            return false;
        }

        var who = SelectedKind == PinKind.User ? "пользователя" : "администратора";
        if (!_dialogService.Confirm(
                $"Изменить PIN-код {who} на носителе «{token.DisplayName}»?\n\n" +
                "Запомните новый PIN-код: восстановить его нельзя, а после нескольких неверных вводов носитель блокируется.",
                "Смена PIN-кода"))
        {
            return false;
        }

        IsChanging = true;
        try
        {
            var result = await _pinService.ChangePinAsync(token, SelectedKind, currentPin, newPin);
            ResultSuccess = result.Success;
            ResultMessage = result.Message;

            if (result.Success)
            {
                _notifications.Show(ToastKind.Success, "PIN-код изменён", token.DisplayName);
            }

            return result.Success;
        }
        finally
        {
            IsChanging = false;
            // Счётчик попыток мог измениться (неверный PIN) — сразу обновляем состояние носителя.
            _ = ScanAsync();
        }
    }

    [RelayCommand]
    private void OpenSetup() => _navigationService.NavigateTo<SetupWizardViewModel>();
}

/// <summary>Пункт выбора, какой PIN-код менять.</summary>
public sealed record PinKindOption(PinKind Value, string Title);
