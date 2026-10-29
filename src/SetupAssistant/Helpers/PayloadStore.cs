using System.IO;
using System.IO.Compression;
using System.Text;

namespace SetupAssistant.Helpers;

/// <summary>
/// Данные (конфиг, дистрибутивы), дописанные в конец exe: [exe][zip][подвал 16 байт: «SAPAYLD1» + длина zip].
/// Файлы распаковываются лениво, только когда они действительно нужны.
/// </summary>
public sealed class PayloadStore : IDisposable
{
    private const int FooterSize = 16;
    private const int MaxPaddingAfterFooter = 8;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SAPAYLD1");

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly object _sync = new();

    public string Id { get; }

    public int FileCount => _entries.Count;

    public long TotalSize { get; }

    private PayloadStore(ZipArchive zip)
    {
        _zip = zip;
        _entries = zip.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .ToDictionary(e => Normalize(e.FullName), StringComparer.OrdinalIgnoreCase);
        TotalSize = _entries.Values.Sum(e => e.Length);

        Id = _entries.TryGetValue(".payload-id", out var idEntry)
            ? new string(ReadText(idEntry).Where(char.IsLetterOrDigit).ToArray())
            : "default";
        if (Id.Length == 0)
        {
            Id = "default";
        }
    }

    /// <summary>Открывает данные из exe; null, если в файле их нет.</summary>
    public static PayloadStore? TryOpen(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return null;
        }

        FileStream? file = null;
        try
        {
            file = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var footerEnd = FindDataEnd(file);

            // Подписанный exe выровнен нулями перед таблицей сертификатов, поэтому подвал ищем в небольшом окне.
            for (var padding = 0; padding <= MaxPaddingAfterFooter; padding++)
            {
                var footerStart = footerEnd - FooterSize - padding;
                if (footerStart < 0)
                {
                    break;
                }

                var footer = new byte[FooterSize];
                file.Seek(footerStart, SeekOrigin.Begin);
                file.ReadExactly(footer);
                if (!footer.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                {
                    continue;
                }

                var length = BitConverter.ToInt64(footer, Magic.Length);
                var start = footerStart - length;
                if (length <= 0 || start < 0)
                {
                    break;
                }

                var zip = new ZipArchive(new SubStream(file, start, length), ZipArchiveMode.Read, leaveOpen: false);
                return new PayloadStore(zip);
            }
        }
        catch (Exception)
        {
            // Нет данных или файл повреждён — работаем как обычное приложение.
        }

        file?.Dispose();
        return null;
    }

    public bool Contains(string relativePath) => _entries.ContainsKey(Normalize(relativePath));

    /// <summary>Размер файла в данных exe без распаковки; null, если файла нет.</summary>
    public long? GetSize(string relativePath) =>
        _entries.TryGetValue(Normalize(relativePath), out var entry) ? entry.Length : null;

    public string? ReadAllText(string relativePath)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(Normalize(relativePath), out var entry) ? ReadText(entry) : null;
        }
    }

    /// <summary>Распаковывает файл в кэш (если ещё не распакован) и возвращает полный путь; null, если файла нет в данных.</summary>
    public string? Extract(string relativePath, string cacheRoot)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(Normalize(relativePath), out var entry))
            {
                return null;
            }

            var root = Path.GetFullPath(Path.Combine(cacheRoot, Id));
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (File.Exists(target) && new FileInfo(target).Length == entry.Length)
            {
                return target;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = target + ".tmp";
            using (var source = entry.Open())
            using (var destination = File.Create(temp))
            {
                source.CopyTo(destination);
            }

            File.Move(temp, target, overwrite: true);
            return target;
        }
    }

    public void Dispose() => _zip.Dispose();

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Конец данных: у подписанного exe это начало таблицы сертификатов, иначе конец файла.</summary>
    private static long FindDataEnd(FileStream file)
    {
        try
        {
            var buffer = new byte[8];
            file.Seek(0, SeekOrigin.Begin);
            file.ReadExactly(buffer, 0, 2);
            if (buffer[0] == 'M' && buffer[1] == 'Z')
            {
                file.Seek(0x3C, SeekOrigin.Begin);
                file.ReadExactly(buffer, 0, 4);
                var peOffset = BitConverter.ToInt32(buffer, 0);

                file.Seek(peOffset + 24, SeekOrigin.Begin);
                file.ReadExactly(buffer, 0, 2);
                var optionalMagic = BitConverter.ToUInt16(buffer, 0);

                const int securityDirectoryIndex = 4;
                var directoriesOffset = peOffset + 24 + (optionalMagic == 0x20B ? 112 : 96);
                file.Seek(directoriesOffset + securityDirectoryIndex * 8, SeekOrigin.Begin);
                file.ReadExactly(buffer, 0, 8);

                var certificateOffset = BitConverter.ToUInt32(buffer, 0);
                if (certificateOffset != 0 && certificateOffset < file.Length)
                {
                    return certificateOffset;
                }
            }
        }
        catch (Exception)
        {
            // Не PE-файл или нестандартный заголовок — считаем, что данные идут до конца файла.
        }

        return file.Length;
    }

    /// <summary>Окно [start, start+length) внутри файла: zip, дописанный к exe, хранит смещения от начала архива.</summary>
    private sealed class SubStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public SubStream(Stream inner, long start, long length)
        {
            _inner = inner;
            _start = start;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, _length);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = _length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(buffer.Length, remaining);
            _inner.Position = _start + _position;
            var read = _inner.Read(buffer[..toRead]);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _length + offset
            };
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
