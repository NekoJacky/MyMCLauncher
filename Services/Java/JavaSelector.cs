using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyMCLauncher.Models;

namespace MyMCLauncher.Services.Java;

/// <summary>
/// 从多个 Java 候选中挑选最合适的一个。
/// </summary>
public static class JavaSelector
{
    /// <summary>
    /// 选择 Java。
    /// </summary>
    /// <param name="candidates">候选列表。</param>
    /// <param name="requiredMajor">版本要求的主版本；<see langword="null"/> 表示不限制。</param>
    /// <param name="preferredPath">优先使用的路径（通常来自设置）。</param>
    public static JavaInfo? SelectBest(
        IEnumerable<JavaInfo> candidates,
        int? requiredMajor,
        string? preferredPath)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var list = candidates as IReadOnlyList<JavaInfo> ?? candidates.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var preferred = list.FirstOrDefault(candidate =>
                PathsEqual(candidate.JavaPath, preferredPath)
                && (requiredMajor is null || candidate.Version.Major >= requiredMajor));

            if (preferred is not null)
            {
                return preferred;
            }
        }

        if (requiredMajor is int required)
        {
            var exact = list
                .Where(candidate => candidate.Version.Major == required)
                .OrderByDescending(candidate => candidate.Is64Bit)
                .ThenByDescending(candidate => candidate.Version)
                .FirstOrDefault();

            if (exact is not null)
            {
                return exact;
            }

            var higher = list
                .Where(candidate => candidate.Version.Major > required)
                .OrderBy(candidate => candidate.Version.Major)
                .ThenByDescending(candidate => candidate.Is64Bit)
                .ThenByDescending(candidate => candidate.Version)
                .FirstOrDefault();

            if (higher is not null)
            {
                return higher;
            }
        }

        return list
            .OrderByDescending(candidate => candidate.Is64Bit)
            .ThenByDescending(candidate => candidate.Version)
            .FirstOrDefault();
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
