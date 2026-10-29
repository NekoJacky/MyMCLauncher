namespace MyMCLauncher.Models;

/// <summary>
/// 日志级别。
/// </summary>
public enum LogLevel
{
    /// <summary>调试信息。</summary>
    Debug,

    /// <summary>常规信息。</summary>
    Info,

    /// <summary>警告。</summary>
    Warn,

    /// <summary>错误。</summary>
    Error,
}

/// <summary>
/// 一条日志记录。
/// </summary>
public sealed class LogEntry
{
    /// <summary>
    /// 记录时间。
    /// </summary>
    public DateTime Time { get; init; }

    /// <summary>
    /// 日志级别。
    /// </summary>
    public LogLevel Level { get; init; }

    /// <summary>
    /// 日志内容。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 关联异常的完整文本。
    /// </summary>
    public string? Exception { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        var level = Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warn => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "INFO ",
        };

        return $"[{Time:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {Message}";
    }
}
