using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace CodexVtt.Core.Logging;

/// <summary>
/// Log locali su file con rotazione (ADR-0001, ADR-0006): nessun invio online. I messaggi del core
/// non contengono testo trascritto né percorsi completi.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly LogLevel _minimum;
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string directory, LogLevel minimum = LogLevel.Information, long maxBytes = 2 * 1024 * 1024, int maxFiles = 5)
    {
        _directory = directory;
        _minimum = minimum;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        Directory.CreateDirectory(directory);
    }

    public string CurrentFile => Path.Combine(_directory, "codexvtt.log");

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    public void Dispose() => _loggers.Clear();

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {category}: {message}{Environment.NewLine}");
        if (exception is not null)
            line += exception + Environment.NewLine;

        lock (_gate)
        {
            try
            {
                Rotate();
                File.AppendAllText(CurrentFile, line);
            }
            catch (IOException)
            {
                // Il logging non deve mai far fallire l'app.
            }
        }
    }

    private void Rotate()
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length < _maxBytes)
            return;

        var oldest = Path.Combine(_directory, $"codexvtt.{_maxFiles - 1}.log");
        if (File.Exists(oldest))
            File.Delete(oldest);
        for (var i = _maxFiles - 2; i >= 1; i--)
        {
            var source = Path.Combine(_directory, $"codexvtt.{i}.log");
            if (File.Exists(source))
                File.Move(source, Path.Combine(_directory, $"codexvtt.{i + 1}.log"));
        }

        File.Move(CurrentFile, Path.Combine(_directory, "codexvtt.1.log"));
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= owner._minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                owner.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
