using System;
using System.Security.Cryptography;
using System.Text;

namespace MyMCLauncher.Services.Auth;

/// <summary>
/// 使用 Windows DPAPI（当前用户）加密账号令牌。
/// </summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MyMCLauncher.Accounts.v1");

    /// <summary>
    /// 加密明文；空字符串原样返回。
    /// </summary>
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// 尝试解密。空字符串视为成功的空值。
    /// </summary>
    public static bool TryUnprotect(string? ciphertext, out string plaintext)
    {
        plaintext = string.Empty;

        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            return true;
        }

        try
        {
            var encrypted = Convert.FromBase64String(ciphertext);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException
            or CryptographicException
            or ArgumentException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
