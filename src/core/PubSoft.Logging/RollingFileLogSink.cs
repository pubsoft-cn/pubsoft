using System.Buffers;

namespace PubSoft.Logging;

/// <summary>
/// NDJSON file sink with date/size rolling and retention. Files are named
/// <c>{prefix}-{yyyyMMdd}.log</c> (UTC date) with <c>_{NNN}</c> sequence suffixes for same-day size
/// rolls. The active segment rolls when the UTC date changes or the next batch would exceed the
/// size limit; an oversized single batch is still written whole rather than split. Retention runs
/// on roll boundaries only: files beyond the count limit (oldest first) or older than the age limit
/// are deleted. Sequential single-writer contract inherited from <see cref="AppLogger"/> batching --
/// sharing one directory across multiple logger instances is not coordinated.
/// </summary>
public sealed class RollingFileLogSink : ILogSink
{
    private static readonly char[] InvalidFilePrefixChars = ['/', '\\', '*', '?', ':'];

    private readonly string _directory;
    private readonly string _filePrefix;
    private readonly long _maxFileSizeBytes;
    private readonly int? _retainedFileCountLimit;
    private readonly TimeSpan? _retainedFileAgeLimit;
    private readonly TimeProvider _timeProvider;

    private bool _initialized;
    private DateOnly _currentDate;
    private int _currentSequence;
    private long _currentSize;

    /// <summary>Creates the sink; the directory is created lazily on first write.</summary>
    public RollingFileLogSink(
        string directory,
        string filePrefix = "app",
        long maxFileSizeBytes = 32 * 1024 * 1024,
        int? retainedFileCountLimit = 31,
        TimeSpan? retainedFileAgeLimit = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePrefix);
        if (filePrefix.IndexOfAny(InvalidFilePrefixChars) >= 0)
            throw new ArgumentException("File prefix must be a plain file-name stem without path separators or wildcards.", nameof(filePrefix));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        if (retainedFileCountLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(retainedFileCountLimit), "Retained file count limit must be positive when set.");
        if (retainedFileAgeLimit.HasValue && retainedFileAgeLimit.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retainedFileAgeLimit), "Retained file age limit must be positive when set.");

        _directory = directory;
        _filePrefix = filePrefix;
        _maxFileSizeBytes = maxFileSizeBytes;
        _retainedFileCountLimit = retainedFileCountLimit;
        _retainedFileAgeLimit = retainedFileAgeLimit;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.IsEmpty)
            return;

        var buffer = new ArrayBufferWriter<byte>(Math.Max(256, entries.Length * 256));
        for (var index = 0; index < entries.Length; index++)
        {
            JsonLogSink.WriteEntry(buffer, entries.Span[index]);
            buffer.GetSpan(1)[0] = (byte)'\n';
            buffer.Advance(1);
        }

        var now = _timeProvider.GetUtcNow();
        EnsureInitialized(now);
        var rolled = RollIfNeeded(now, buffer.WrittenCount);

        var path = CurrentPath();
        Directory.CreateDirectory(_directory);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        _currentSize += buffer.WrittenCount;

        // Evaluated after the write so the just-created segment is part of the retained set;
        // evaluating before the write would count one file short and keep one too many.
        if (rolled)
            ApplyRetention(now);
    }

    private void EnsureInitialized(DateTimeOffset now)
    {
        if (_initialized)
            return;

        _initialized = true;
        _currentDate = DateOnly.FromDateTime(now.UtcDateTime);
        _currentSequence = 0;
        _currentSize = 0;
        if (!Directory.Exists(_directory))
            return;

        // Resume after restart: adopt the newest same-day segment so existing content is appended,
        // never overwritten; if that segment is already at the size limit the next write rolls forward.
        var todaySuffix = $"-{_currentDate:yyyyMMdd}";
        string? newest = null;
        foreach (var file in Directory.GetFiles(_directory, $"{_filePrefix}-*.log"))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith($"{_filePrefix}{todaySuffix}", StringComparison.Ordinal))
                continue;
            if (newest is null || string.CompareOrdinal(name, newest) > 0)
                newest = name;
        }

        if (newest is not null)
        {
            var fullPath = Path.Combine(_directory, newest);
            _currentSize = new FileInfo(fullPath).Length;
            _currentSequence = ParseSequence(newest, todaySuffix);
        }
    }

    private bool RollIfNeeded(DateTimeOffset now, long incomingBytes)
    {
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        if (date != _currentDate)
        {
            _currentDate = date;
            _currentSequence = 0;
            _currentSize = 0;
            return true;
        }
        if (_currentSize > 0 && _currentSize + incomingBytes > _maxFileSizeBytes)
        {
            _currentSequence++;
            _currentSize = 0;
            return true;
        }
        return false;
    }

    private void ApplyRetention(DateTimeOffset now)
    {
        if (_retainedFileCountLimit is null && _retainedFileAgeLimit is null || !Directory.Exists(_directory))
            return;

        try
        {
            var files = Directory.GetFiles(_directory, $"{_filePrefix}-*.log")
                .Select(path => (Path: path, Name: Path.GetFileName(path)!))
                .Where(file => ParseDate(file.Name) is not null)
                .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                .ToArray();

            for (var index = 0; index < files.Length; index++)
            {
                var file = files[index];
                var beyondCount = _retainedFileCountLimit is { } count && index >= count;
                var beyondAge = _retainedFileAgeLimit is { } age
                    && now.UtcDateTime - ParseDate(file.Name)!.Value.ToDateTime(TimeOnly.MinValue) > age;
                if (!beyondCount && !beyondAge)
                    continue;

                try { File.Delete(file.Path); }
                catch (IOException) { } // pubsoft:exempt PUBSOFT041 "retention is janitorial -- a locked or racing file is retried at the next roll"
                catch (UnauthorizedAccessException) { } // pubsoft:exempt PUBSOFT041 "retention must not fail logging; permissions are a host concern"
            }
        }
        catch (IOException) // pubsoft:exempt PUBSOFT041 "retention scan failure must never break logging"
        {
        }
    }

    private int ParseSequence(string name, string todaySuffix)
    {
        // {prefix}-{date}.log -> 0; {prefix}-{date}_{NNN}.log -> NNN
        var expectedPlain = $"{_filePrefix}{todaySuffix}.log";
        if (string.Equals(name, expectedPlain, StringComparison.Ordinal))
            return 0;

        var stemEnd = name.IndexOf('_', _filePrefix.Length + todaySuffix.Length);
        if (stemEnd > 0
            && name.EndsWith(".log", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(stemEnd + 1, name.Length - stemEnd - 5), out var sequence))
            return sequence;
        return 0;
    }

    private DateOnly? ParseDate(string name)
    {
        // fixed layout: {prefix}-yyyyMMdd... -- date sits right after the prefix dash
        var prefixLength = _filePrefix.Length + 1;
        if (name.Length < prefixLength + 8
            || !int.TryParse(name.AsSpan(prefixLength, 4), out var year)
            || !int.TryParse(name.AsSpan(prefixLength + 4, 2), out var month)
            || !int.TryParse(name.AsSpan(prefixLength + 6, 2), out var day))
            return null;

        try { return new DateOnly(year, month, day); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private string CurrentPath()
    {
        var name = _currentSequence == 0
            ? $"{_filePrefix}-{_currentDate:yyyyMMdd}.log"
            : $"{_filePrefix}-{_currentDate:yyyyMMdd}_{_currentSequence:000}.log";
        return Path.Combine(_directory, name);
    }
}
