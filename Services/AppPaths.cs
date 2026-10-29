using System.IO;

namespace MyMCLauncher.Services;

/// <summary>
/// 集中定义启动器运行期使用的目录与文件路径。
/// </summary>
public sealed class AppPaths
{
    /// <summary>
    /// 用于覆盖默认根目录的环境变量名，便于测试或多实例部署。
    /// </summary>
    public const string HomeEnvironmentVariable = "MYMCLAUNCHER_HOME";

    private const string DefaultDirectoryName = "MyMCLauncher";

    /// <summary>
    /// 初始化路径集合。
    /// </summary>
    /// <param name="root">
    /// 配置根目录。为 <see langword="null"/> 或空时依次使用
    /// <c>MYMCLAUNCHER_HOME</c> 环境变量、<c>%APPDATA%\MyMCLauncher</c>。
    /// </param>
    public AppPaths(string? root = null)
    {
        var resolvedRoot = root;

        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            resolvedRoot = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                throw new InvalidOperationException("无法获取当前用户的 ApplicationData 目录。");
            }

            resolvedRoot = Path.Combine(appData, DefaultDirectoryName);
        }
        else
        {
            resolvedRoot = Environment.ExpandEnvironmentVariables(resolvedRoot);
        }

        Root = Path.GetFullPath(resolvedRoot);

        LogsDirectory = Path.Combine(Root, "logs");
        NativesDirectory = Path.Combine(Root, "natives");
        WebView2DataDirectory = Path.Combine(Root, "webview2");
        SettingsFile = Path.Combine(Root, "settings.json");
        AccountsFile = Path.Combine(Root, "accounts.json");
    }

    /// <summary>
    /// 配置根目录，默认 <c>%APPDATA%\MyMCLauncher</c>。
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// 日志目录。
    /// </summary>
    public string LogsDirectory { get; }

    /// <summary>
    /// 旧版本 natives 解压目录。
    /// </summary>
    public string NativesDirectory { get; }

    /// <summary>
    /// WebView2 用户数据目录。
    /// </summary>
    public string WebView2DataDirectory { get; }

    /// <summary>
    /// 设置文件路径。
    /// </summary>
    public string SettingsFile { get; }

    /// <summary>
    /// 账号文件路径。
    /// </summary>
    public string AccountsFile { get; }

    /// <summary>
    /// 创建运行期必需的目录。重复调用是安全的。
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(NativesDirectory);
        Directory.CreateDirectory(WebView2DataDirectory);
    }

    /// <inheritdoc />
    public override string ToString() => Root;
}
