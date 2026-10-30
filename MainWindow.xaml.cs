using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using MyMCLauncher.Models;
using MyMCLauncher.Services;
using MyMCLauncher.Views;

namespace MyMCLauncher;

/// <summary>
/// 主窗口：当前仅提供内容区占位、日志面板与状态栏。
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxLogItems = 1000;

    private readonly AppHost _host;
    private readonly ObservableCollection<LogEntry> _logItems = new();

    /// <summary>
    /// 初始化主窗口。
    /// </summary>
    public MainWindow(AppHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));

        InitializeComponent();

        LogList.ItemsSource = _logItems;
        foreach (var entry in _host.Log.Recent)
        {
            _logItems.Add(entry);
        }

        _host.Log.LogEmitted += OnLogEmitted;
        Closed += OnClosed;

        UpdateStatusSummary();
    }

    private void OnLogEmitted(LogEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => AppendLog(entry)));
            return;
        }

        AppendLog(entry);
    }

    private void AppendLog(LogEntry entry)
    {
        _logItems.Add(entry);
        while (_logItems.Count > MaxLogItems)
        {
            _logItems.RemoveAt(0);
        }

        if (_logItems.Count > 0)
        {
            LogList.ScrollIntoView(_logItems[^1]);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _host.Log.LogEmitted -= OnLogEmitted;
        Closed -= OnClosed;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_host)
        {
            Owner = this,
        };

        if (window.ShowDialog() == true)
        {
            UpdateStatusSummary();
        }
    }

    private void UpdateStatusSummary()
    {
        var settings = _host.Settings.Current;
        var directory = string.IsNullOrWhiteSpace(settings.GameDirectory) ? "未设置" : settings.GameDirectory;
        var java = "未设置";

        if (!string.IsNullOrWhiteSpace(settings.JavaPath))
        {
            var javaHome = Path.GetDirectoryName(Path.GetDirectoryName(settings.JavaPath));
            java = string.IsNullOrEmpty(javaHome) ? settings.JavaPath : Path.GetFileName(javaHome);
        }

        var memory = settings.AutoMemory ? _host.Memory.GetRecommendedMemoryMb() : settings.MemoryMb;

        StatusText.Text = $"游戏目录：{directory}    |    Java：{java}    |    内存：{memory} MB";
        StatusText.ToolTip = $"游戏目录：{directory}\nJava：{(string.IsNullOrWhiteSpace(settings.JavaPath) ? "自动" : settings.JavaPath)}";
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _logItems.Clear();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _logItems.Select(entry => entry.ToString())));
        }
        catch (Exception ex)
        {
            _host.Log.Warn("复制日志到剪贴板失败", ex);
        }
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_host.Paths.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _host.Paths.LogsDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _host.Log.Warn("打开日志目录失败", ex);
        }
    }
}
