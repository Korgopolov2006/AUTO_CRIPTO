namespace SetupAssistant.Models;

/// <summary>Результат одной предварительной проверки перед установкой.</summary>
/// <param name="Status">Ok — всё хорошо, Warning — можно продолжать, Error — продолжать нельзя.</param>
public sealed record PreflightCheck(string Name, StatusLevel Status, string Message)
{
    public bool IsBlocking => Status == StatusLevel.Error;
}
