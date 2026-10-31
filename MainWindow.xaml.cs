using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    private bool _accountsInitialized;

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
        _host.Accounts.Changed += OnAccountsChanged;
        Closed += OnClosed;

        RefreshAccounts();
        _accountsInitialized = true;
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
        _host.Accounts.Changed -= OnAccountsChanged;
        Closed -= OnClosed;
    }

    private void OnAccountsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshAccounts));
            return;
        }

        RefreshAccounts();
    }

    private void RefreshAccounts()
    {
        var accounts = _host.Accounts.Accounts;
        var current = _host.Accounts.Current;
        var wasInitialized = _accountsInitialized;
        _accountsInitialized = false;

        try
        {
            AccountComboBox.ItemsSource = accounts;
            AccountComboBox.SelectedItem = current is null
                ? null
                : accounts.FirstOrDefault(account =>
                    string.Equals(account.Id, current.Id, StringComparison.Ordinal));

            var hasAccounts = accounts.Count > 0;
            AccountComboBox.Visibility = hasAccounts ? Visibility.Visible : Visibility.Collapsed;
            NoAccountText.Visibility = hasAccounts ? Visibility.Collapsed : Visibility.Visible;
            RemoveAccountButton.IsEnabled = current is not null;
        }
        finally
        {
            _accountsInitialized = wasInitialized;
        }

        UpdateStatusSummary();
    }

    private void AccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_accountsInitialized || AccountComboBox.SelectedItem is not Account account)
        {
            return;
        }

        if (string.Equals(_host.Accounts.Current?.Id, account.Id, StringComparison.Ordinal))
        {
            return;
        }

        if (!_host.Accounts.TrySetCurrent(account.Id, out var error))
        {
            _host.Log.Warn(error ?? "切换账号失败。");
            RefreshAccounts();
            return;
        }

        UpdateStatusSummary();
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddAccountWindow(_host)
        {
            Owner = this,
        };

        if (window.ShowDialog() == true)
        {
            RefreshAccounts();
        }
    }

    private void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        var current = _host.Accounts.Current;
        if (current is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"确定删除账号“{current.Name}”吗？",
            "删除账号",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (!_host.Accounts.TryRemove(current.Id, out var error))
        {
            MessageBox.Show(
                this,
                error ?? "删除账号失败。",
                "删除账号",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _host.Log.Info($"已删除账号：{current.Name}（{current.FormattedUuid}）");
        RefreshAccounts();
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
        var account = _host.Accounts.Current?.DisplayName ?? "未登录";

        StatusText.Text = $"账号：{account}    |    游戏目录：{directory}    |    Java：{java}    |    内存：{memory} MB";
        StatusText.ToolTip = $"账号：{account}\n游戏目录：{directory}\nJava：{(string.IsNullOrWhiteSpace(settings.JavaPath) ? "自动" : settings.JavaPath)}";
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
