using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using MyMCLauncher.Models;
using MyMCLauncher.Services;
using MyMCLauncher.Services.Java;

namespace MyMCLauncher.Views;

/// <summary>
/// 设置窗口：游戏目录、Java 运行时与内存。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly LauncherSettings _draft;
    private readonly ObservableCollection<JavaInfo> _javaCandidates = new();

    private CancellationTokenSource? _scanCts;
    private bool _initialized;
    private bool _isScanning;
    private bool _updatingMemory;
    private string _lastScannedDirectory = string.Empty;

    /// <summary>
    /// 初始化设置窗口。
    /// </summary>
    public SettingsWindow(AppHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _draft = host.Settings.CreateDraft();

        InitializeComponent();

        JavaComboBox.ItemsSource = _javaCandidates;
        Loaded += SettingsWindow_Loaded;
        Closed += SettingsWindow_Closed;
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        GameDirectoryBox.Text = _draft.GameDirectory;
        AutoMemoryCheckBox.IsChecked = _draft.AutoMemory;

        _initialized = true;

        UpdateDirectoryStatus();
        UpdateMemoryUi();

        await ScanJavaAsync();
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }

    private void GameDirectoryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        _draft.GameDirectory = GameDirectoryBox.Text;
        UpdateDirectoryStatus();
    }

    private async void GameDirectoryBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _isScanning)
        {
            return;
        }

        var directory = GameDirectoryBox.Text.Trim();
        if (string.Equals(directory, _lastScannedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _draft.GameDirectory = directory;
        await ScanJavaAsync();
    }

    private async void BrowseGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 .minecraft 目录",
            Multiselect = false,
        };

        var current = GameDirectoryBox.Text.Trim();
        dialog.InitialDirectory = Directory.Exists(current)
            ? current
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        GameDirectoryBox.Text = dialog.FolderName;
        _draft.GameDirectory = dialog.FolderName;
        UpdateDirectoryStatus();

        if (!_isScanning)
        {
            await ScanJavaAsync();
        }
    }

    private void JavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        if (JavaComboBox.SelectedItem is JavaInfo info)
        {
            _draft.JavaPath = info.JavaPath;
            UpdateJavaDetail();
        }
    }

    private async void RescanJava_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning)
        {
            return;
        }

        await ScanJavaAsync();
    }

    private async void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Java 可执行文件",
            Filter = "Java 可执行文件|java.exe;javaw.exe|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var javaPath = dialog.FileName;
        var fileName = Path.GetFileName(javaPath);

        if (!string.Equals(fileName, "java.exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, "javaw.exe", StringComparison.OrdinalIgnoreCase))
        {
            var confirm = MessageBox.Show(
                this,
                $"所选文件不是 java.exe 或 javaw.exe：\n{javaPath}\n\n仍要尝试使用吗？",
                "确认 Java 路径",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        JavaScanStatusText.Text = "正在检查...";

        try
        {
            var info = await _host.Java.InspectAsync(javaPath, JavaSource.Manual);
            if (info is null)
            {
                MessageBox.Show(
                    this,
                    "无法识别为可用的 Java 运行时。",
                    "Java",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                JavaScanStatusText.Text = "手动选择失败";
                return;
            }

            var existing = _javaCandidates.FirstOrDefault(candidate =>
                string.Equals(candidate.JavaPath, info.JavaPath, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _javaCandidates.Remove(existing);
            }

            _javaCandidates.Insert(0, info);
            JavaComboBox.SelectedItem = info;
            _draft.JavaPath = info.JavaPath;
            JavaScanStatusText.Text = "已添加手动选择的 Java";
            UpdateJavaDetail();
        }
        catch (Exception ex)
        {
            _host.Log.Error("手动检查 Java 失败", ex);
            MessageBox.Show(
                this,
                $"检查 Java 失败：{ex.Message}",
                "Java",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            JavaScanStatusText.Text = "手动选择失败";
        }
    }

    private void AutoMemory_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        _draft.AutoMemory = AutoMemoryCheckBox.IsChecked == true;
        UpdateMemoryUi();
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingMemory)
        {
            return;
        }

        _draft.MemoryMb = (int)Math.Round(MemorySlider.Value);
        UpdateMemoryText();
    }

    private async Task ScanJavaAsync()
    {
        if (_isScanning)
        {
            return;
        }

        _isScanning = true;
        SetJavaControlsEnabled(false);
        JavaScanStatusText.Text = "扫描中...";

        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        var directory = GameDirectoryBox.Text.Trim();
        _draft.GameDirectory = directory;

        try
        {
            var candidates = await _host.Java.FindAllAsync(_draft.JavaPath, directory, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _javaCandidates.Clear();
            foreach (var candidate in candidates)
            {
                _javaCandidates.Add(candidate);
            }

            var preferred = _draft.JavaPath;
            var selected = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.JavaPath, preferred, StringComparison.OrdinalIgnoreCase))
                ?? JavaSelector.SelectBest(candidates, null, preferred);

            JavaComboBox.SelectedItem = selected;
            _draft.JavaPath = selected?.JavaPath ?? string.Empty;
            JavaScanStatusText.Text = selected is null
                ? "未找到 Java，请手动选择"
                : $"找到 {candidates.Count} 个 Java";

            UpdateJavaDetail();
            _lastScannedDirectory = directory;
        }
        catch (OperationCanceledException)
        {
            JavaScanStatusText.Text = "扫描已取消";
        }
        catch (Exception ex)
        {
            _host.Log.Error("扫描 Java 失败", ex);
            JavaScanStatusText.Text = $"扫描失败：{ex.Message}";
        }
        finally
        {
            _isScanning = false;
            if (IsLoaded)
            {
                SetJavaControlsEnabled(true);
            }
        }
    }

    private void UpdateDirectoryStatus()
    {
        var check = GameDirectoryValidator.Check(GameDirectoryBox.Text);
        GameDirectoryStatusText.Text = check.Message;
        GameDirectoryStatusText.Foreground = check.State switch
        {
            GameDirectoryState.NotSet => Brushes.Gray,
            GameDirectoryState.Invalid => Brushes.Red,
            GameDirectoryState.Missing => Brushes.Red,
            GameDirectoryState.Empty => Brushes.DarkOrange,
            GameDirectoryState.LooksLikeMinecraft => Brushes.DarkOrange,
            _ => Brushes.Green,
        };
    }

    private void UpdateMemoryUi()
    {
        var recommended = _host.Memory.GetRecommendedMemoryMb();
        var auto = AutoMemoryCheckBox.IsChecked == true;

        _updatingMemory = true;
        try
        {
            if (auto)
            {
                MemorySlider.IsEnabled = false;
                MemorySlider.Value = recommended;
                _draft.MemoryMb = recommended;
            }
            else
            {
                MemorySlider.IsEnabled = true;
                var value = _host.Memory.Clamp(_draft.MemoryMb > 0 ? _draft.MemoryMb : recommended);
                MemorySlider.Value = value;
                _draft.MemoryMb = value;
            }
        }
        finally
        {
            _updatingMemory = false;
        }

        UpdateMemoryText();
    }

    private void UpdateMemoryText()
    {
        var value = (int)Math.Round(MemorySlider.Value);
        var total = _host.Memory.GetTotalPhysicalMemoryMb();

        MemoryValueText.Text = $"{value:N0} MB";

        if (AutoMemoryCheckBox.IsChecked == true)
        {
            MemoryHintText.Text = $"建议值：{value:N0} MB（物理内存 {total:N0} MB）";
        }
        else
        {
            MemoryHintText.Text = _host.Memory.Validate(value)
                ?? $"手动设置（物理内存 {total:N0} MB）";
        }
    }

    private void UpdateJavaDetail()
    {
        if (JavaComboBox.SelectedItem is not JavaInfo info)
        {
            JavaDetailText.Text = "未选择 Java。";
            return;
        }

        var vendor = string.IsNullOrWhiteSpace(info.Vendor) ? "未知" : info.Vendor;
        JavaDetailText.Text =
            $"来源：{GetSourceText(info.Source)}    厂商：{vendor}    架构：{info.Architecture}    路径：{info.JavaHome}";
    }

    private void SetJavaControlsEnabled(bool enabled)
    {
        RescanJavaButton.IsEnabled = enabled;
        BrowseJavaButton.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
        JavaComboBox.IsEnabled = enabled;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var directory = GameDirectoryBox.Text.Trim().Trim('"');
        var directoryCheck = GameDirectoryValidator.Check(directory);

        switch (directoryCheck.State)
        {
            case GameDirectoryState.Invalid:
                MessageBox.Show(
                    this,
                    directoryCheck.Message,
                    "游戏目录",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;

            case GameDirectoryState.Missing:
                if (MessageBox.Show(
                        this,
                        $"{directoryCheck.Message}\n\n是否创建该目录？",
                        "游戏目录",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        $"创建目录失败：{ex.Message}",
                        "游戏目录",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                break;

            case GameDirectoryState.NotSet:
                if (MessageBox.Show(
                        this,
                        "尚未设置游戏目录，仍要保存吗？",
                        "游戏目录",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                break;
        }

        if (string.IsNullOrWhiteSpace(_draft.JavaPath)
            && MessageBox.Show(
                this,
                "尚未选择 Java，仍要保存吗？",
                "Java",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var autoMemory = AutoMemoryCheckBox.IsChecked == true;
        var memory = autoMemory
            ? _host.Memory.GetRecommendedMemoryMb()
            : _host.Memory.Clamp((int)Math.Round(MemorySlider.Value));

        var warning = _host.Memory.Validate(memory);
        if (warning is not null
            && MessageBox.Show(
                this,
                $"{warning}\n\n仍要保存吗？",
                "内存",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _draft.GameDirectory = directory;
        _draft.AutoMemory = autoMemory;
        _draft.MemoryMb = memory;

        if (!_host.Settings.Apply(_draft, out var error))
        {
            MessageBox.Show(
                this,
                error ?? "保存设置失败。",
                "设置",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _host.Log.Info($"设置已更新：目录={_draft.GameDirectory}，Java={_draft.JavaPath}，内存={_draft.MemoryMb} MB");

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static string GetSourceText(JavaSource source) => source switch
    {
        JavaSource.Saved => "设置",
        JavaSource.JavaHome => "环境变量",
        JavaSource.Path => "PATH",
        JavaSource.MojangRuntime => "Mojang 运行时",
        JavaSource.CommonDirectory => "常见目录",
        JavaSource.Manual => "手动选择",
        JavaSource.Registry => "注册表",
        _ => source.ToString(),
    };
}
