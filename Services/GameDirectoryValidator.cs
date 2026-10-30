using System;
using System.IO;
using System.Linq;

namespace MyMCLauncher.Services;

/// <summary>
/// 游戏目录状态。
/// </summary>
public enum GameDirectoryState
{
    /// <summary>未设置。</summary>
    NotSet,

    /// <summary>路径非法。</summary>
    Invalid,

    /// <summary>目录不存在。</summary>
    Missing,

    /// <summary>目录为空。</summary>
    Empty,

    /// <summary>看起来像 Minecraft 目录，但缺少可识别内容。</summary>
    LooksLikeMinecraft,

    /// <summary>包含至少一个版本。</summary>
    HasVersions,
}

/// <summary>
/// 游戏目录检查结果。
/// </summary>
public sealed record GameDirectoryCheck(GameDirectoryState State, string Message)
{
    /// <summary>
    /// 是否允许保存（空目录、Minecraft 目录或已有版本）。
    /// </summary>
    public bool IsUsable => State is GameDirectoryState.Empty
        or GameDirectoryState.LooksLikeMinecraft
        or GameDirectoryState.HasVersions;
}

/// <summary>
/// 校验用户选择的游戏目录。
/// </summary>
public static class GameDirectoryValidator
{
    /// <summary>
    /// 检查目录并返回状态与提示。
    /// </summary>
    public static GameDirectoryCheck Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new GameDirectoryCheck(GameDirectoryState.NotSet, "未设置游戏目录。");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new GameDirectoryCheck(GameDirectoryState.Invalid, "路径无效。");
        }

        if (IsDriveRoot(fullPath))
        {
            return new GameDirectoryCheck(GameDirectoryState.Invalid, "不能选择磁盘根目录，请选择 .minecraft 文件夹。");
        }

        if (!Directory.Exists(fullPath))
        {
            return new GameDirectoryCheck(GameDirectoryState.Missing, "目录不存在。");
        }

        var versionsDirectory = Path.Combine(fullPath, "versions");
        var hasVersions = Directory.Exists(versionsDirectory);
        var hasAssets = Directory.Exists(Path.Combine(fullPath, "assets"));
        var hasLibraries = Directory.Exists(Path.Combine(fullPath, "libraries"));

        if (hasVersions)
        {
            var versionCount = 0;
            try
            {
                versionCount = Directory.EnumerateDirectories(versionsDirectory).Count();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 无法统计时仍视为 Minecraft 目录
            }

            if (versionCount > 0)
            {
                return new GameDirectoryCheck(
                    GameDirectoryState.HasVersions,
                    $"已检测到 {versionCount} 个版本。");
            }
        }

        if (hasVersions || hasAssets || hasLibraries)
        {
            return new GameDirectoryCheck(
                GameDirectoryState.LooksLikeMinecraft,
                "已检测到 Minecraft 目录结构，但尚无可用版本。");
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(fullPath).Any())
            {
                return new GameDirectoryCheck(
                    GameDirectoryState.Empty,
                    "目录为空，将在下载游戏时创建文件。");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 无法读取时按可疑目录处理
        }

        return new GameDirectoryCheck(
            GameDirectoryState.LooksLikeMinecraft,
            "未检测到 versions / assets / libraries，请确认是否选择了 .minecraft 根目录。");
    }

    private static bool IsDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        return string.Equals(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
