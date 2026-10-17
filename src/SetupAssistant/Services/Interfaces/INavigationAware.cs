namespace SetupAssistant.Services.Interfaces;

/// <summary>ViewModel, которому нужно знать, что пользователь открыл его экран (экземпляр живёт между переходами).</summary>
public interface INavigationAware
{
    void OnNavigatedTo();

    /// <summary>Пользователь ушёл на другую страницу (экземпляр при этом остаётся жить).</summary>
    void OnNavigatedFrom();
}
