namespace MyMCLauncher.Models;

/// <summary>
/// 启动器设置。
/// </summary>
public sealed class LauncherSettings
{
    /// <summary>
    /// 配置结构版本，用于后续迁移。
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// `.minecraft` 根目录；空字符串表示未设置。
    /// </summary>
    public string GameDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Java 可执行文件完整路径；空字符串表示自动选择。
    /// </summary>
    public string JavaPath { get; set; } = string.Empty;

    /// <summary>
    /// 是否自动计算内存。
    /// </summary>
    public bool AutoMemory { get; set; } = true;

    /// <summary>
    /// 手动或生效内存（MB）；<see cref="AutoMemory"/> 为 <see langword="true"/> 时可为 0。
    /// </summary>
    public int MemoryMb { get; set; }

    /// <summary>
    /// 上次选择的版本，P3 使用。
    /// </summary>
    public string LastVersionId { get; set; } = string.Empty;

    /// <summary>
    /// 上次选择的账号，P2 使用。
    /// </summary>
    public string LastAccountId { get; set; } = string.Empty;

    /// <summary>
    /// 创建当前设置的浅拷贝，便于编辑后取消。
    /// </summary>
    public LauncherSettings Clone() => (LauncherSettings)MemberwiseClone();
}
