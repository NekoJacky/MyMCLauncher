using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MyMCLauncher.Models;

namespace MyMCLauncher.Services.Auth;

/// <summary>
/// 离线账号的用户名校验、UUID 生成与账号创建。
/// </summary>
public static class OfflineAuthService
{
    /// <summary>
    /// 用户名最大长度（UTF-16 code unit，与 Java 的 <c>String.length()</c> 一致）。
    /// </summary>
    public const int MaxNameLength = 16;

    /// <summary>
    /// 校验用户名。
    /// </summary>
    /// <param name="name">原始输入。</param>
    /// <param name="trimmedName">去除首尾空白后的用户名。</param>
    /// <param name="error">错误信息；<see langword="null"/> 表示通过。</param>
    public static bool TryValidateName(string? name, out string trimmedName, out string? error)
    {
        trimmedName = (name ?? string.Empty).Trim();

        if (trimmedName.Length == 0)
        {
            error = "用户名不能为空。";
            return false;
        }

        if (trimmedName.Length > MaxNameLength)
        {
            error = $"用户名不能超过 {MaxNameLength} 个字符。";
            return false;
        }

        if (trimmedName.Any(char.IsWhiteSpace))
        {
            error = "用户名不能包含空白字符。";
            return false;
        }

        if (trimmedName.Any(char.IsControl))
        {
            error = "用户名不能包含控制字符。";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// 用户名包含非 ASCII 字符时返回提示；否则返回 <see langword="null"/>。
    /// </summary>
    public static string? GetNameWarning(string name)
    {
        if (name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return null;
        }

        return "用户名包含非 ASCII 字符，部分服务器或插件可能不支持。";
    }

    /// <summary>
    /// 按 Minecraft 离线规则生成 32 位小写 UUID。
    /// </summary>
    /// <remarks>
    /// 等价于 Java：
    /// <c>UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(UTF_8))</c>。
    /// </remarks>
    public static string CreateOfflineUuid(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("用户名不能为空。", nameof(name));
        }

        var input = Encoding.UTF8.GetBytes("OfflinePlayer:" + name);
        var hash = MD5.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // IETF variant

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 创建离线账号。
    /// </summary>
    public static Account CreateAccount(string name)
    {
        if (!TryValidateName(name, out var trimmedName, out var error))
        {
            throw new ArgumentException(error, nameof(name));
        }

        var uuid = CreateOfflineUuid(trimmedName);

        return new Account
        {
            Id = CreateAccountId(uuid),
            Type = AccountType.Offline,
            Name = trimmedName,
            Uuid = uuid,
            UserType = "legacy",
            AccessToken = string.Empty,
            RefreshToken = string.Empty,
            Xuid = string.Empty,
            AddedAt = DateTimeOffset.Now,
        };
    }

    /// <summary>
    /// 生成离线账号 ID。
    /// </summary>
    public static string CreateAccountId(string uuid) => $"offline:{uuid}";
}
