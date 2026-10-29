using System.IO;
using System.Text;
using MyMCLauncher.Models;

namespace MyMCLauncher.Services;

/// <summary>
/// 线程安全的日志服务，同时写入内存、日志文件，并通过事件通知界面。
/// </summary>
public sealed class LogService : IDisposable
{
    private const int MaxRecentEntries = 1000;

    private readonly object _sync = new();
    private readonly Queue<LogEntry> _recent = new();
    private readonly string _logsDirectory;

    private StreamWriter? _writer;
    private DateTime _writerDate;
    private bool _disposed;

    /// <summary>
    /// 初始化日志服务。
    /// </summary>
    /// <param name="logsDirectory">日志目录，不存在时会自动创建。</param>
    public LogService(string logsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);

        _logsDirectory = Path.GetFullPath(logsDirectory);
        Directory.CreateDirectory(_logsDirectory);
    }

    /// <summary>
    /// 每写入一条日志时触发。事件在锁外执行，可在任意线程触发。
    /// </summary>
    public event Action<LogEntry>? LogEmitted;

    /// <summary>
    /// 日志目录。
    /// </summary>
    public string LogsDirectory => _logsDirectory;

    /// <summary>
    /// 最低记录级别，低于该级别的日志会被忽略。
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// 最近的日志快照，最多保留 <see cref="MaxRecentEntries"/> 条。
    /// </summary>
    public IReadOnlyList<LogEntry> Recent
    {
        get
        {
            lock (_sync)
            {
                return _recent.ToArray();
            }
        }
    }

    /// <summary>
    /// 写入调试日志。
    /// </summary>
    public void Debug(string message) => Write(LogLevel.Debug, message, null);

    /// <summary>
    /// 写入信息日志。
    /// </summary>
    public void Info(string message) => Write(LogLevel.Info, message, null);

    /// <summary>
    /// 写入警告日志。
    /// </summary>
    public void Warn(string message, Exception? exception = null) => Write(LogLevel.Warn, message, exception);

    /// <summary>
    /// 写入错误日志。
    /// </summary>
    public void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);

    /// <summary>
    /// 按指定级别写入日志。
    /// </summary>
    public void Log(LogLevel level, string message, Exception? exception = null) => Write(level, message, exception);

    /// <summary>
    /// 将缓冲区内容刷入日志文件。
    /// </summary>
    public void Flush()
    {
        lock (_sync)
        {
            try
            {
                _writer?.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                System.Diagnostics.Debug.WriteLine($"[LogService] 刷新日志失败：{ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeWriterLocked();
        }
    }

    private void Write(LogLevel level, string message, Exception? exception)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Level = level,
            Message = message ?? string.Empty,
            Exception = exception?.ToString(),
        };

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _recent.Enqueue(entry);
            while (_recent.Count > MaxRecentEntries)
            {
                _recent.Dequeue();
            }

            WriteToFileLocked(entry);
        }

        Emit(entry);
    }

    private void Emit(LogEntry entry)
    {
        var handlers = LogEmitted;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<LogEntry>)handler)(entry);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogService] 日志订阅者异常：{ex}");
            }
        }
    }

    private void WriteToFileLocked(LogEntry entry)
    {
        try
        {
            EnsureWriterLocked(entry.Time);
            _writer!.WriteLine(Format(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine($"[LogService] 写入日志文件失败：{ex.Message}");
            DisposeWriterLocked();
        }
    }

    private void EnsureWriterLocked(DateTime time)
    {
        var date = time.Date;
        if (_writer is not null && _writerDate == date)
        {
            return;
        }

        DisposeWriterLocked();

        var path = Path.Combine(_logsDirectory, $"launcher-{date:yyyyMMdd}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        _writerDate = date;
    }

    private void DisposeWriterLocked()
    {
        if (_writer is null)
        {
            return;
        }

        try
        {
            _writer.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine($"[LogService] 关闭日志文件失败：{ex.Message}");
        }
        finally
        {
            _writer = null;
        }
    }

    private static string Format(LogEntry entry)
    {
        var line = $"[{entry.Time:yyyy-MM-dd HH:mm:ss.fff}] [{GetLevelText(entry.Level)}] {entry.Message}";

        if (!string.IsNullOrEmpty(entry.Exception))
        {
            line += Environment.NewLine + entry.Exception;
        }

        return line;
    }

    private static string GetLevelText(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO ",
        LogLevel.Warn => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "INFO ",
    };
}
