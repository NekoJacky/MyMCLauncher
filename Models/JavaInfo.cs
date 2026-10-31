namespace MyMCLauncher.Models;

/// <summary>
/// Java 候选来源。
/// </summary>
public enum JavaSource
{
    /// <summary>设置中保存的路径。</summary>
    Saved,

    /// <summary><c>JAVA_HOME</c> / <c>JDK_HOME</c>。</summary>
    JavaHome,

    /// <summary><c>PATH</c> 中的 Java。</summary>
    Path,

    /// <summary>Mojang 官方运行时目录。</summary>
    MojangRuntime,

    /// <summary>常见安装目录或其他启动器的 Java 目录。</summary>
    CommonDirectory,

    /// <summary>用户手动选择。</summary>
    Manual,

    /// <summary>注册表（预留）。</summary>
    Registry,
}

/// <summary>
/// 一个可用的 Java 运行时。
/// </summary>
public sealed class JavaInfo
{
    /// <summary>
    /// <c>java.exe</c> 的完整路径。
    /// </summary>
    public string JavaPath { get; init; } = string.Empty;

    /// <summary>
    /// Java 根目录。
    /// </summary>
    public string JavaHome { get; init; } = string.Empty;

    /// <summary>
    /// 解析后的版本号。
    /// </summary>
    public JavaVersion Version { get; init; }

    /// <summary>
    /// 厂商信息（来自 <c>release</c> 文件）。
    /// </summary>
    public string? Vendor { get; init; }

    /// <summary>
    /// 架构：<c>x64</c> / <c>x86</c> / <c>arm64</c> / <c>unknown</c>。
    /// </summary>
    public string Architecture { get; init; } = "unknown";

    /// <summary>
    /// 发现来源。
    /// </summary>
    public JavaSource Source { get; init; }

    /// <summary>
    /// 是否为 64 位 Java。
    /// </summary>
    public bool Is64Bit => string.Equals(Architecture, "x64", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 用于界面显示的文本。
    /// </summary>
    public string DisplayName => $"{Version} ({Architecture}) · {JavaPath}";
}
