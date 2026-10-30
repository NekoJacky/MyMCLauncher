using System;
using System.IO;
using MyMCLauncher.Models;

namespace MyMCLauncher.Services;

/// <summary>
/// 负责启动器设置的加载、规范化、保存与默认值。
/// </summary>
public sealed class SettingsService
{
    /// <summary>
    /// 当前配置结构版本。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private const int MinMemoryMb = 0;
    private const int MaxMemoryMb = 65536;

    private readonly AppPaths _paths;
    private readonly JsonStore _json;
    private readonly LogService _log;

    /// <summary>
    /// 初始化设置服务并立即加载配置。
    /// </summary>
    public SettingsService(AppPaths paths, JsonStore json, LogService log)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _json = json ?? throw new ArgumentNullException(nameof(json));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        Load();
    }

    /// <summary>
    /// 当前设置。修改后需调用 <see cref="TrySave"/> 或 <see cref="Apply"/>。
    /// </summary>
    public LauncherSettings Current { get; private set; } = new();

    /// <summary>
    /// 设置保存成功后触发。
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 从磁盘加载设置；文件不存在或损坏时使用默认值。
    /// </summary>
    public void Load()
    {
        var settings = _json.Load(_paths.SettingsFile, CreateDefaults);
        Normalize(settings);
        Current = settings;
        _log.Info($"设置已加载：{_paths.SettingsFile}");
    }

    /// <summary>
    /// 保存当前设置。
    /// </summary>
    /// <param name="error">失败时的错误信息。</param>
    public bool TrySave(out string? error)
    {
        Normalize(Current);

        try
        {
            _json.Save(_paths.SettingsFile, Current);
            error = null;
            _log.Info("设置已保存");
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            error = $"保存设置失败：{ex.Message}";
            _log.Error(error, ex);
            return false;
        }
    }

    /// <summary>
    /// 应用一份新设置并保存。
    /// </summary>
    public bool Apply(LauncherSettings settings, out string? error)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var previous = Current;
        Current = settings;

        if (TrySave(out error))
        {
            return true;
        }

        Current = previous;
        return false;
    }

    /// <summary>
    /// 修改当前设置并立即保存。
    /// </summary>
    public void Update(Action<LauncherSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var draft = Current.Clone();
        mutate(draft);
        Apply(draft, out _);
    }

    /// <summary>
    /// 恢复默认设置并保存。
    /// </summary>
    public void ResetToDefaults()
    {
        Apply(CreateDefaults(), out _);
    }

    /// <summary>
    /// 创建当前设置的副本，供设置窗口编辑。
    /// </summary>
    public LauncherSettings CreateDraft() => Current.Clone();

    private LauncherSettings CreateDefaults()
    {
        var defaults = new LauncherSettings
        {
            SchemaVersion = CurrentSchemaVersion,
        };

        var minecraftDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");

        if (Directory.Exists(minecraftDirectory))
        {
            defaults.GameDirectory = minecraftDirectory;
        }

        return defaults;
    }

    private void Normalize(LauncherSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.GameDirectory = NormalizePath(settings.GameDirectory, "游戏目录");
        settings.JavaPath = NormalizePath(settings.JavaPath, "Java 路径");
        settings.MemoryMb = Math.Clamp(settings.MemoryMb, MinMemoryMb, MaxMemoryMb);
        settings.LastVersionId ??= string.Empty;
        settings.LastAccountId ??= string.Empty;
    }

    private string NormalizePath(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            trimmed = Environment.ExpandEnvironmentVariables(trimmed);
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex)
        {
            _log.Warn($"{label}无效，已清空：{path}，{ex.Message}");
            return string.Empty;
        }
    }
}
