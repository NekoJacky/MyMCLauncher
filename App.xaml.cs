using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MyMCLauncher.Services;

namespace MyMCLauncher;

/// <summary>
/// 应用入口：初始化组合根、注册全局异常处理并启动主窗口。
/// </summary>
public partial class App : Application
{
    private int _reporting;

    /// <summary>
    /// 当前应用的服务组合根。
    /// </summary>
    public AppHost Host { get; private set; } = null!;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Host = new AppHost();
            AppHost.Current = Host;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"启动器初始化失败：{ex.Message}",
                "MyMCLauncher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        RegisterExceptionHandlers();

        var window = new MainWindow(Host);
        MainWindow = window;
        window.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        UnregisterExceptionHandlers();

        try
        {
            Host?.Log.Info("主窗口已关闭，启动器退出");
            Host?.Dispose();
        }
        catch
        {
            // 退出阶段不再抛出异常
        }

        base.OnExit(e);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void UnregisterExceptionHandlers()
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatal("界面线程发生未处理异常", e.Exception);
        e.Handled = true;

        if (!Dispatcher.HasShutdownStarted)
        {
            Shutdown(1);
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ReportFatal("发生未处理异常，程序即将退出", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Host?.Log.Error("未观察的 Task 异常", e.Exception);
        e.SetObserved();
    }

    private void ReportFatal(string title, Exception? exception)
    {
        if (Interlocked.Exchange(ref _reporting, 1) == 1)
        {
            return;
        }

        try
        {
            Host?.Log.Error(title, exception);
            Host?.Log.Flush();
        }
        catch
        {
            // 忽略日志失败
        }

        var detail = exception?.Message ?? "未知错误";
        var logPath = Host?.Paths.LogsDirectory ?? "（未知）";

        try
        {
            MessageBox.Show(
                $"{title}\n\n{detail}\n\n日志目录：{logPath}",
                "MyMCLauncher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // 忽略弹窗失败
        }
    }
}
