using System;
using System.Windows;
using System.Windows.Controls;
using MyMCLauncher.Services;
using MyMCLauncher.Services.Auth;

namespace MyMCLauncher.Views;

/// <summary>
/// 添加账号窗口：P2 仅支持离线登录，微软登录在 P6 开放。
/// </summary>
public partial class AddAccountWindow : Window
{
    private const string DefaultHint = "1–16 个字符；中文用户名在部分服务器可能不可用。";

    private readonly AppHost _host;

    /// <summary>
    /// 初始化添加账号窗口。
    /// </summary>
    public AddAccountWindow(AppHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));

        InitializeComponent();

        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void UsernameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var name = UsernameBox.Text.Trim();
        if (name.Length == 0)
        {
            HintText.Text = DefaultHint;
            return;
        }

        HintText.Text = OfflineAuthService.GetNameWarning(name) ?? DefaultHint;
    }

    private void AddOffline_Click(object sender, RoutedEventArgs e)
    {
        var name = UsernameBox.Text;

        if (!OfflineAuthService.TryValidateName(name, out var trimmedName, out var validationError))
        {
            ErrorText.Text = validationError;
            UsernameBox.Focus();
            return;
        }

        if (!_host.Accounts.TryAddOffline(trimmedName, out var account, out var error))
        {
            ErrorText.Text = error ?? "添加账号失败。";
            return;
        }

        _host.Log.Info($"已添加离线账号：{account?.Name}（{account?.FormattedUuid}）");

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
