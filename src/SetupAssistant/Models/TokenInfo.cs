namespace SetupAssistant.Models;

/// <summary>Сведения о подключенном носителе электронной подписи (Rutoken, JaCarta и т.д.).</summary>
public sealed class TokenInfo
{
    public TokenType Type { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public bool DriverInstalled { get; set; }
}
