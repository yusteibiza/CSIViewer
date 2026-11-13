using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CSIViewer.Infrastructure.Logging;

/// <summary>
/// Thread-safe JSON-lines file logger with rotation, retention limits,
/// structured properties, scopes and secret redaction. Every operation is
/// best effort: a logging failure never propagates to the application.
/// </summary>
public sealed class JsonLinesFileLoggerProvider : ILoggerProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _directory;
    private readonly string _prefix;
    private readonly LoggingOptions _options;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string? _currentPath;
    private int _writesSinceCleanup;
    private int _consecutiveFailures;
    private DateTimeOffset _disabledUntil = DateTimeOffset.MinValue;

    public JsonLinesFileLoggerProvider(
        string directory,
        LoggingOptions? options = null,
        string filePrefix = "csiviewer")
    {
        _directory = directory;
        _prefix = string.IsNullOrWhiteSpace(filePrefix) ? "csiviewer" : filePrefix;
        _options = options ?? new LoggingOptions();
        try
        {
            Directory.CreateDirectory(_directory);
            LogFileRetention.Cleanup(
                _directory,
                _prefix,
                _options.RetentionDays,
                _options.MaxTotalBytes);
        }
        catch
        {
            // Logging must never prevent startup.
        }
    }

    public LoggingOptions Options => _options;

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(this, categoryName, _scopeProvider);

    internal bool IsLevelEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && logLevel >= _options.MinimumLevel;

    internal void Write(string line)
    {
        if (DateTimeOffset.UtcNow < _disabledUntil)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                var today = DateTime.UtcNow.ToString("yyyyMMdd");
                var path = Path.Combine(_directory, $"{_prefix}-{today}.log");
                if (_writer is null || !string.Equals(_currentPath, path, StringComparison.Ordinal))
                {
                    OpenWriter(path);
                }

                if (_writer!.BaseStream.Length >= _options.MaxFileBytes)
                {
                    Rotate(path);
                    OpenWriter(path);
                }

                _writer.WriteLine(line);
                _consecutiveFailures = 0;
                _writesSinceCleanup++;
                if (_writesSinceCleanup >= 256)
                {
                    _writesSinceCleanup = 0;
                    LogFileRetention.Cleanup(
                        _directory,
                        _prefix,
                        _options.RetentionDays,
                        _options.MaxTotalBytes);
                }
            }
            catch
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= 5)
                {
                    _disabledUntil = DateTimeOffset.UtcNow.AddMinutes(1);
                    CloseWriter();
                }
            }
        }
    }

    private void OpenWriter(string path)
    {
        CloseWriter();
        _currentPath = path;
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new System.Text.UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    private void Rotate(string path)
    {
        try
        {
            CloseWriter();
            var rotated = Path.Combine(
                _directory,
                $"{_prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log");
            if (File.Exists(rotated))
            {
                rotated = Path.Combine(
                    _directory,
                    $"{_prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.log");
            }

            File.Move(path, rotated, overwrite: true);
            LogFileRetention.Cleanup(
                _directory,
                _prefix,
                _options.RetentionDays,
                _options.MaxTotalBytes);
        }
        catch
        {
            // If rotation fails, continue appending to the same file.
        }
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Nothing else to do.
        }

        _writer = null;
        _currentPath = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CloseWriter();
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly JsonLinesFileLoggerProvider _provider;
        private readonly string _category;
        private readonly LoggerExternalScopeProvider _scopes;

        public FileLogger(
            JsonLinesFileLoggerProvider provider,
            string category,
            LoggerExternalScopeProvider scopes)
        {
            _provider = provider;
            _category = category;
            _scopes = scopes;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            _scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => _provider.IsLevelEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                AddState(properties, state);
                var scopes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                _scopes.ForEachScope(AddScope, scopes);

                var record = new Dictionary<string, object?>
                {
                    ["ts"] = DateTimeOffset.UtcNow,
                    ["level"] = logLevel.ToString(),
                    ["category"] = _category,
                    ["eventId"] = eventId.Id,
                    ["event"] = string.IsNullOrWhiteSpace(eventId.Name) ? null : eventId.Name,
                    ["message"] = SensitiveDataRedactor.Redact(formatter(state, exception)),
                    ["properties"] = properties,
                    ["scopes"] = scopes,
                    ["exceptionType"] = exception?.GetType().FullName,
                    ["exceptionMessage"] = exception is null
                        ? null
                        : SensitiveDataRedactor.Redact(Truncate(exception.Message, 4096)),
                    ["exception"] = exception is null
                        ? null
                        : SensitiveDataRedactor.Redact(Truncate(exception.ToString(), 32768)),
                    ["pid"] = Environment.ProcessId,
                    ["tid"] = Environment.CurrentManagedThreadId
                };

                _provider.Write(JsonSerializer.Serialize(record, JsonOptions));
            }
            catch
            {
                // Never let logging bring down the application.
            }
        }

        private static void AddState<TState>(
            Dictionary<string, object?> destination,
            TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> values)
            {
                return;
            }

            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    continue;
                }

                destination[pair.Key] = NormalizeValue(pair.Key, pair.Value);
            }
        }

        private static void AddScope(
            object? scope,
            Dictionary<string, object?> destination)
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                {
                    destination[pair.Key] = NormalizeValue(pair.Key, pair.Value);
                }

                return;
            }

            if (scope is string text && !string.IsNullOrWhiteSpace(text))
            {
                destination["scope"] = SensitiveDataRedactor.Redact(text);
            }
        }

        private static string? NormalizeValue(string key, object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (SensitiveDataRedactor.IsSensitiveKey(key))
            {
                return "***";
            }

            if (value is byte[] bytes)
            {
                return $"<binary:{bytes.Length} bytes>";
            }

            var text = value.ToString();
            return text is null ? null : SensitiveDataRedactor.Redact(text);
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
