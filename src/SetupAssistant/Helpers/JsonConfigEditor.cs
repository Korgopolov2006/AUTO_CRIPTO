using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SetupAssistant.Helpers;

/// <summary>Одна правка Config.json: путь к значению («MinVersions», «CryptoProCsp») и новое значение (строка, число или bool).</summary>
public sealed record ConfigEdit(string[] Path, object Value)
{
    public string PathText => string.Join(".", Path);
}

/// <summary>Результат применения правок: новый текст и пути, которых в файле не оказалось.</summary>
public sealed record ConfigEditResult(string Text, IReadOnlyList<string> Missing);

/// <summary>
/// Точечная правка значений в Config.json без перезаписи всего файла: комментарии, порядок и отступы остаются как были.
/// Меняются только существующие скалярные значения; нового в файл не добавляется.
/// </summary>
public static class JsonConfigEditor
{
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static ConfigEditResult Apply(string text, IEnumerable<ConfigEdit> edits)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var spans = FindSpans(bytes);
        var missing = new List<string>();
        var replacements = new List<(int Start, int Length, byte[] Value)>();

        foreach (var edit in edits)
        {
            if (!spans.TryGetValue(string.Join("\u0001", edit.Path), out var span))
            {
                missing.Add(edit.PathText);
                continue;
            }

            replacements.Add((span.Start, span.Length, Encoding.UTF8.GetBytes(Serialize(edit.Value))));
        }

        // С конца к началу, чтобы смещения предыдущих значений не сдвигались.
        foreach (var (start, length, value) in replacements.OrderByDescending(r => r.Start))
        {
            bytes = bytes[..start].Concat(value).Concat(bytes[(start + length)..]).ToArray();
        }

        return new ConfigEditResult(Encoding.UTF8.GetString(bytes), missing);
    }

    private static string Serialize(object value) => value switch
    {
        bool b => b ? "true" : "false",
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string s => JsonSerializer.Serialize(s, WriterOptions),
        _ => throw new ArgumentException($"Неподдерживаемый тип значения: {value.GetType().Name}")
    };

    /// <summary>Находит байтовые границы каждого скалярного значения. Ключ — путь из имён свойств через \u0001.</summary>
    private static Dictionary<string, (int Start, int Length)> FindSpans(byte[] bytes)
    {
        var result = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(bytes, ReaderOptions);
        var containers = new List<string?>();
        string? pending = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    pending = reader.GetString();
                    break;

                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    containers.Add(pending);
                    pending = null;
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    containers.RemoveAt(containers.Count - 1);
                    break;

                default:
                    // Значения внутри массивов (pending == null) не правим; корневой контейнер имеет имя null и в путь не входит.
                    if (pending is not null && containers.Skip(1).All(c => c is not null))
                    {
                        var path = containers.Skip(1).Append(pending);
                        var length = reader.TokenType == JsonTokenType.String ? reader.ValueSpan.Length + 2 : reader.ValueSpan.Length;
                        result[string.Join("\u0001", path)] = ((int)reader.TokenStartIndex, length);
                    }

                    pending = null;
                    break;
            }
        }

        return result;
    }
}
