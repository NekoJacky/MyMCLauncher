using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MyMCLauncher.Models;

namespace MyMCLauncher.Services.Java;

/// <summary>
/// 发现本机 Java 并读取版本信息。
/// </summary>
public sealed class JavaLocator
{
    private const int MaxSearchDepth = 4;

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex VersionRegex = new(
        @"version\s+""(?<version>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BitsRegex = new(
        @"(?<bits>32|64)-Bit",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly LogService _log;

    /// <summary>
    /// 初始化 Java 定位器。
    /// </summary>
    public JavaLocator(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// 扫描并检查所有候选 Java。
    /// </summary>
    /// <param name="savedJavaPath">设置中保存的 Java 路径。</param>
    /// <param name="gameDirectory">当前游戏目录，用于查找 Mojang 运行时。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public Task<IReadOnlyList<JavaInfo>> FindAllAsync(
        string? savedJavaPath,
        string? gameDirectory,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<JavaInfo>>(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            var candidates = new List<(string Path, JavaSource Source)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddCandidate(savedJavaPath, JavaSource.Saved, candidates, seen);

            AddEnvironmentJava("JAVA_HOME", JavaSource.JavaHome, candidates, seen);
            AddEnvironmentJava("JDK_HOME", JavaSource.JavaHome, candidates, seen);
            AddPathJava(candidates, seen);

            AddJavaInDirectory(CombineIfPresent(gameDirectory, "runtime"), JavaSource.MojangRuntime, candidates, seen);
            AddCommonDirectories(candidates, seen);

            var results = new List<JavaInfo>(candidates.Count);

            foreach (var (path, source) in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = await InspectCoreAsync(path, source, cancellationToken).ConfigureAwait(false);
                    if (info is not null)
                    {
                        results.Add(info);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Warn($"检查 Java 失败：{path}，{ex.Message}");
                }
            }

            stopwatch.Stop();

            var ordered = results
                .OrderByDescending(java => java.Version)
                .ThenByDescending(java => java.Is64Bit)
                .ThenBy(java => java.JavaPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _log.Info($"Java 扫描完成：候选 {candidates.Count}，可用 {ordered.Count}，耗时 {stopwatch.ElapsedMilliseconds} ms");
            return ordered;
        }, cancellationToken);
    }

    /// <summary>
    /// 检查单个 Java 可执行文件。
    /// </summary>
    public Task<JavaInfo?> InspectAsync(
        string javaPath,
        JavaSource source,
        CancellationToken cancellationToken = default)
        => InspectCoreAsync(javaPath, source, cancellationToken);

    private async Task<JavaInfo?> InspectCoreAsync(string javaPath, JavaSource source, CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(javaPath);
        }
        catch (Exception ex)
        {
            _log.Warn($"Java 路径无效：{javaPath}，{ex.Message}");
            return null;
        }

        if (!File.Exists(fullPath))
        {
            _log.Debug($"跳过不存在的 Java：{fullPath}");
            return null;
        }

        var javaHome = GetJavaHome(fullPath);
        var release = ReadReleaseFile(javaHome);

        JavaVersion? version = null;
        string? vendor = null;
        var architecture = "unknown";

        if (release is not null)
        {
            if (release.TryGetValue("JAVA_VERSION", out var releaseVersion)
                && JavaVersion.TryParse(releaseVersion, out var parsed))
            {
                version = parsed;
            }

            if (release.TryGetValue("IMPLEMENTOR", out var implementor) && !string.IsNullOrWhiteSpace(implementor))
            {
                vendor = implementor;
            }

            if (release.TryGetValue("OS_ARCH", out var releaseArch))
            {
                architecture = NormalizeArchitecture(releaseArch);
            }
        }

        if (version is null || architecture == "unknown")
        {
            var (rawVersion, processArch) = await RunJavaVersionAsync(fullPath, cancellationToken).ConfigureAwait(false);

            if (version is null && rawVersion is not null && JavaVersion.TryParse(rawVersion, out var parsed))
            {
                version = parsed;
            }

            if (architecture == "unknown" && processArch is not null)
            {
                architecture = processArch;
            }
        }

        if (version is null)
        {
            _log.Warn($"无法读取 Java 版本：{fullPath}");
            return null;
        }

        return new JavaInfo
        {
            JavaPath = fullPath,
            JavaHome = javaHome,
            Version = version.Value,
            Vendor = vendor,
            Architecture = architecture,
            Source = source,
        };
    }

    private void AddEnvironmentJava(
        string variableName,
        JavaSource source,
        List<(string Path, JavaSource Source)> candidates,
        HashSet<string> seen)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var home = value.Trim().Trim('"');
        AddCandidate(Path.Combine(home, "bin", "java.exe"), source, candidates, seen);
    }

    private void AddPathJava(List<(string Path, JavaSource Source)> candidates, HashSet<string> seen)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length == 0 || IsSpecialOrNetworkPath(directory))
            {
                continue;
            }

            AddCandidate(Path.Combine(directory, "java.exe"), JavaSource.Path, candidates, seen);
        }
    }

    private void AddCommonDirectories(
        List<(string Path, JavaSource Source)> candidates,
        HashSet<string> seen)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        AddJavaInDirectory(CombineIfPresent(appData, ".minecraft", "runtime"), JavaSource.MojangRuntime, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(appData, ".hmcl", "java"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(appData, "PrismLauncher", "java"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(appData, "ModrinthApp", "meta", "java_versions"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(appData, "ATLauncher", "runtimes", "minecraft"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(userProfile, ".jdks"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(userProfile, "curseforge", "minecraft", "Install", "runtime"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(localAppData, ".ftba", "bin", "runtime"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(localAppData, "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(programFilesX86, "Minecraft Launcher", "runtime"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(programFilesX86, "Minecraft", "runtime"), JavaSource.CommonDirectory, candidates, seen);

        AddJavaInDirectory(CombineIfPresent(programFiles, "Java"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(programFiles, "Eclipse Adoptium"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(programFiles, "Amazon Corretto"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(programFiles, "Zulu"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(programFiles, "BellSoft"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(programFiles, "Semeru"), JavaSource.CommonDirectory, candidates, seen);
        AddJavaInDirectory(CombineIfPresent(programFiles, "Microsoft"), JavaSource.CommonDirectory, candidates, seen);
    }

    private void AddJavaInDirectory(
        string? directory,
        JavaSource source,
        List<(string Path, JavaSource Source)> candidates,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if (IsSpecialOrNetworkPath(directory))
        {
            _log.Debug($"跳过特殊或网络目录：{directory}");
            return;
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = MaxSearchDepth,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
                MatchCasing = MatchCasing.CaseInsensitive,
            };

            foreach (var file in Directory.EnumerateFiles(directory, "java.exe", options))
            {
                AddCandidate(file, source, candidates, seen);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.Warn($"扫描 Java 目录失败：{directory}，{ex.Message}");
        }
    }

    private static void AddCandidate(
        string? path,
        JavaSource source,
        List<(string Path, JavaSource Source)> candidates,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath) && seen.Add(fullPath))
            {
                candidates.Add((fullPath, source));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 路径无效时忽略该候选
        }
    }

    private static string? CombineIfPresent(string? root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
        }

        return current;
    }

    private static bool IsSpecialOrNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        if (path.Contains(@"\javapath", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\system32", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root)
                && root.Length >= 2
                && root[1] == ':'
                && new DriveInfo(root).DriveType == DriveType.Network)
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return true;
        }

        return false;
    }

    private static string GetJavaHome(string javaPath)
    {
        var binDirectory = Path.GetDirectoryName(javaPath);
        if (string.IsNullOrEmpty(binDirectory))
        {
            return string.Empty;
        }

        return Path.GetDirectoryName(binDirectory) ?? binDirectory;
    }

    private Dictionary<string, string>? ReadReleaseFile(string javaHome)
    {
        if (string.IsNullOrWhiteSpace(javaHome))
        {
            return null;
        }

        var path = Path.Combine(javaHome, "release");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(path))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"');

                if (key.Length > 0)
                {
                    values[key] = value;
                }
            }

            return values;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"读取 release 文件失败：{path}，{ex.Message}");
            return null;
        }
    }

    private async Task<(string? Version, string? Architecture)> RunJavaVersionAsync(
        string javaPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return (null, null);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProcessTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _log.Warn($"读取 Java 版本超时：{javaPath}");
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    _log.Debug($"终止 Java 进程失败：{ex.Message}");
                }
            }

            string stdout = string.Empty;
            string stderr = string.Empty;

            try
            {
                stdout = await stdoutTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                _log.Debug($"读取 Java stdout 失败：{ex.Message}");
            }

            try
            {
                stderr = await stderrTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                _log.Debug($"读取 Java stderr 失败：{ex.Message}");
            }

            var output = stderr + Environment.NewLine + stdout;
            var versionMatch = VersionRegex.Match(output);
            var bitsMatch = BitsRegex.Match(output);

            var version = versionMatch.Success ? versionMatch.Groups["version"].Value : null;
            var architecture = bitsMatch.Success
                ? (bitsMatch.Groups["bits"].Value == "64" ? "x64" : "x86")
                : null;

            return (version, architecture);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _log.Warn($"执行 Java 失败：{javaPath}，{ex.Message}");
            return (null, null);
        }
    }

    private static string NormalizeArchitecture(string? architecture)
    {
        if (string.IsNullOrWhiteSpace(architecture))
        {
            return "unknown";
        }

        return architecture.Trim().ToLowerInvariant() switch
        {
            "x86_64" or "amd64" or "x64" => "x64",
            "x86" or "i386" or "i686" or "x86_32" => "x86",
            "aarch64" or "arm64" => "arm64",
            var other => other,
        };
    }
}
