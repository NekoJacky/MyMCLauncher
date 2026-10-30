using System.Globalization;
using System.Text.RegularExpressions;

namespace MyMCLauncher.Models;

/// <summary>
/// 规范化的 Java 版本号。
/// </summary>
/// <param name="Major">主版本，例如 8、17、21。</param>
/// <param name="Minor">次版本。</param>
/// <param name="Patch">补丁版本。</param>
/// <param name="Build">构建/更新号，例如 <c>1.8.0_392</c> 中的 392。</param>
/// <param name="Raw">原始版本字符串。</param>
public readonly record struct JavaVersion(int Major, int Minor, int Patch, int Build, string Raw)
    : IComparable<JavaVersion>
{
    private static readonly Regex VersionPattern = new(
        @"^(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?:[._+uU-](?<build>\d+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 尝试解析 Java 版本号。
    /// </summary>
    /// <remarks>
    /// 支持 <c>1.8.0_392</c>、<c>1.8.0_202-b08</c>、<c>9.0.1</c>、<c>11</c>、
    /// <c>17.0.9+9</c>、<c>21-ea</c>、<c>25-ea+36</c>、<c>8u392</c> 等格式。
    /// </remarks>
    public static bool TryParse(string? raw, out JavaVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim().Trim('"').Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (value.StartsWith("version ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["version ".Length..].Trim();
        }

        value = value.Replace("-ea", string.Empty, StringComparison.OrdinalIgnoreCase)
                     .Replace("-internal", string.Empty, StringComparison.OrdinalIgnoreCase);

        var match = VersionPattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        if (!TryGetInt(match, "major", out var major))
        {
            return false;
        }

        TryGetInt(match, "minor", out var minor);
        TryGetInt(match, "patch", out var patch);
        TryGetInt(match, "build", out var build);

        // Java 8 及更早使用 1.x.y 形式，把第二段作为主版本。
        if (major == 1 && match.Groups["minor"].Success)
        {
            version = new JavaVersion(minor, patch, 0, build, raw.Trim().Trim('"'));
        }
        else
        {
            version = new JavaVersion(major, minor, patch, build, raw.Trim().Trim('"'));
        }

        return true;
    }

    /// <summary>
    /// 是否至少为指定主版本。
    /// </summary>
    public bool IsAtLeast(int major) => Major >= major;

    /// <inheritdoc />
    public int CompareTo(JavaVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : Build.CompareTo(other.Build);
    }

    /// <inheritdoc />
    public override string ToString()
        => string.IsNullOrWhiteSpace(Raw)
            ? $"{Major}.{Minor}.{Patch}"
            : Raw;

    private static bool TryGetInt(Match match, string groupName, out int value)
    {
        value = 0;
        var group = match.Groups[groupName];
        return group.Success && int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
