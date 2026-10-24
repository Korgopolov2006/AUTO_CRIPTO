using System.IO;
using System.Text;
using Microsoft.Win32;

namespace SetupAssistant.Helpers;

/// <summary>
/// Резервная копия разделов реестра в формате .reg (как «Экспорт» в regedit): файл можно вернуть двойным щелчком.
/// Закрытые списком доступа подразделы читаются через <see cref="SystemPrivileges.OpenReadable"/>.
/// </summary>
public sealed class RegFileWriter : IDisposable
{
    private readonly StreamWriter _writer;

    /// <summary>
    /// Уже сохранённые разделы. Сохраняется только первое состояние: если раздел правится несколько раз (два элемента одного списка),
    /// при импорте файла последней должна оказаться исходная версия, а не промежуточная.
    /// </summary>
    private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);

    public RegFileWriter(string path)
    {
        var exists = File.Exists(path) && new FileInfo(path).Length > 0;
        _writer = new StreamWriter(path, append: true, new UnicodeEncoding(bigEndian: false, byteOrderMark: !exists));
        if (!exists)
        {
            _writer.Write("Windows Registry Editor Version 5.00\r\n\r\n");
        }
    }

    /// <summary>Записывает раздел со всеми подразделами.</summary>
    /// <param name="fullPath">Путь с полным именем куста: HKEY_LOCAL_MACHINE\…, HKEY_USERS\….</param>
    public void WriteTree(string fullPath, RegistryKey key)
    {
        WriteValues(fullPath, key);
        foreach (var name in key.GetSubKeyNames())
        {
            using var child = SystemPrivileges.OpenReadable(key, name);
            if (child is not null)
            {
                WriteTree($@"{fullPath}\{name}", child);
            }
        }
    }

    /// <summary>Записывает только значения раздела (для точечной правки значения или элемента списка).</summary>
    public void WriteValues(string fullPath, RegistryKey key)
    {
        if (!_written.Add(fullPath))
        {
            return;
        }

        var text = new StringBuilder();
        text.Append('[').Append(fullPath).Append("]\r\n");
        foreach (var name in key.GetValueNames())
        {
            text.Append(name.Length == 0 ? "@" : $"\"{Escape(name)}\"").Append('=')
                .Append(FormatValue(key.GetValueKind(name), key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)))
                .Append("\r\n");
        }

        _writer.Write(text.Append("\r\n").ToString());
    }

    /// <summary>Строка комментария (в .reg комментарии начинаются с «;»).</summary>
    public void WriteComment(string comment) => _writer.Write($"; {comment.Replace("\r", " ").Replace("\n", " ")}\r\n");

    public void Dispose() => _writer.Dispose();

    /// <summary>Полное имя раздела для .reg по сокращённой записи HKLM\…, HKCU\…, HKU\….</summary>
    public static string ExpandHiveName(string path)
    {
        var separator = path.IndexOf('\\');
        var prefix = separator < 0 ? path : path[..separator];
        var rest = separator < 0 ? string.Empty : path[separator..];
        return prefix.ToUpperInvariant() switch
        {
            "HKLM" => "HKEY_LOCAL_MACHINE" + rest,
            "HKCU" => "HKEY_CURRENT_USER" + rest,
            "HKU" => "HKEY_USERS" + rest,
            "HKCR" => "HKEY_CLASSES_ROOT" + rest,
            _ => path
        };
    }

    private static string FormatValue(RegistryValueKind kind, object? value) => kind switch
    {
        RegistryValueKind.String => $"\"{Escape(value as string ?? string.Empty)}\"",
        RegistryValueKind.DWord => $"dword:{unchecked((uint)Convert.ToInt32(value)):x8}",
        RegistryValueKind.QWord => "hex(b):" + Hex(BitConverter.GetBytes(Convert.ToInt64(value))),
        RegistryValueKind.ExpandString => "hex(2):" + Hex(Encoding.Unicode.GetBytes((value as string ?? string.Empty) + "\0")),
        RegistryValueKind.MultiString => "hex(7):" + Hex(Encoding.Unicode.GetBytes(
            string.Concat(((value as string[]) ?? Array.Empty<string>()).Select(s => s + "\0")) + "\0")),
        RegistryValueKind.Binary => "hex:" + Hex(value as byte[] ?? Array.Empty<byte>()),
        _ => "hex(0):" + Hex(value as byte[] ?? Array.Empty<byte>())
    };

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Hex(byte[] bytes) => string.Join(",", bytes.Select(b => b.ToString("x2")));
}
