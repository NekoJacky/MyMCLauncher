using System;
using MyMCLauncher.Services.Java;

namespace MyMCLauncher.Services;

/// <summary>
/// 轻量组合根：集中创建并持有应用级服务。
/// </summary>
public sealed class AppHost : IDisposable
{
    /// <summary>
    /// 当前应用实例，通常在 <c>App.OnStartup</c> 中赋值。
    /// </summary>
    public static AppHost Current { get; internal set; } = null!;

    /// <summary>
    /// 初始化组合根及其依赖。
    /// </summary>
    public AppHost()
    {
        Paths = new AppPaths();
        Paths.EnsureCreated();

        Json = new JsonStore();
        Log = new LogService(Paths.LogsDirectory);

        Json.Warning += message => Log.Warn(message);

        Log.Info("MyMCLauncher 启动");
        Log.Info($"Root = {Paths.Root}");
        Log.Info($"OS = {Environment.OSVersion.VersionString}，64 位进程 = {Environment.Is64BitProcess}");

        Settings = new SettingsService(Paths, Json, Log);
        Java = new JavaLocator(Log);
    }

    /// <summary>
    /// 路径集合。
    /// </summary>
    public AppPaths Paths { get; }

    /// <summary>
    /// 通用 JSON 读写。
    /// </summary>
    public JsonStore Json { get; }

    /// <summary>
    /// 日志服务。
    /// </summary>
    public LogService Log { get; }

    /// <summary>
    /// 设置服务。
    /// </summary>
    public SettingsService Settings { get; }

    /// <summary>
    /// Java 定位服务。
    /// </summary>
    public JavaLocator Java { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Log.Info("MyMCLauncher 退出");
        Log.Dispose();
    }
}
