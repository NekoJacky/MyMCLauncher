using MyMCLauncher.Models;

namespace MyMCLauncher.Services.Java;

/// <summary>
/// Java 版本号解析入口。
/// </summary>
public static class JavaVersionParser
{
    /// <summary>
    /// 尝试解析 Java 版本号。
    /// </summary>
    public static bool TryParse(string? raw, out JavaVersion version)
        => JavaVersion.TryParse(raw, out version);

    /// <summary>
    /// 解析 Java 版本号，失败时抛出 <see cref="FormatException"/>。
    /// </summary>
    public static JavaVersion Parse(string raw)
    {
        if (!JavaVersion.TryParse(raw, out var version))
        {
            throw new FormatException($"无法解析 Java 版本号：{raw}");
        }

        return version;
    }
}
