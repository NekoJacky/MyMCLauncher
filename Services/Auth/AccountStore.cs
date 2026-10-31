using System;
using System.Collections.Generic;
using System.Linq;
using MyMCLauncher.Models;

namespace MyMCLauncher.Services.Auth;

/// <summary>
/// 负责 <c>accounts.json</c> 的读写，以及 token 的 DPAPI 加解密。
/// </summary>
public sealed class AccountStore
{
    /// <summary>当前账号文件结构版本。</summary>
    public const int CurrentSchemaVersion = 1;

    private readonly AppPaths _paths;
    private readonly JsonStore _json;
    private readonly LogService _log;

    /// <summary>
    /// 初始化账号存储。
    /// </summary>
    public AccountStore(AppPaths paths, JsonStore json, LogService log)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _json = json ?? throw new ArgumentNullException(nameof(json));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// 读取全部账号。文件不存在、损坏或记录无效时跳过对应内容。
    /// </summary>
    public IReadOnlyList<Account> Load()
    {
        var file = _json.Load(_paths.AccountsFile, () => new AccountsFile());

        if (file.SchemaVersion > CurrentSchemaVersion)
        {
            _log.Warn($"账号文件版本 {file.SchemaVersion} 高于当前支持版本 {CurrentSchemaVersion}，将尽量读取。");
        }

        var storedAccounts = file.Accounts ?? new List<StoredAccount>();
        var accounts = new List<Account>(storedAccounts.Count);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stored in storedAccounts)
        {
            if (stored is null)
            {
                continue;
            }

            try
            {
                var account = FromStored(stored);
                if (account is null)
                {
                    continue;
                }

                if (!ids.Add(account.Id))
                {
                    _log.Warn($"忽略重复账号：{account.Id}");
                    continue;
                }

                accounts.Add(account);
            }
            catch (Exception ex)
            {
                _log.Warn($"读取账号失败：{stored.Id}，{ex.Message}");
            }
        }

        _log.Info($"账号已加载：{accounts.Count} 个");
        return accounts;
    }

    /// <summary>
    /// 保存全部账号。
    /// </summary>
    public void Save(IReadOnlyList<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var file = new AccountsFile
        {
            SchemaVersion = CurrentSchemaVersion,
            Accounts = accounts.Select(ToStored).ToList(),
        };

        _json.Save(_paths.AccountsFile, file);
        _log.Info($"账号已保存：{file.Accounts.Count} 个");
    }

    private Account? FromStored(StoredAccount stored)
    {
        if (string.IsNullOrWhiteSpace(stored.Id)
            || string.IsNullOrWhiteSpace(stored.Uuid)
            || string.IsNullOrWhiteSpace(stored.Name))
        {
            _log.Warn($"忽略无效账号记录：{stored.Id}");
            return null;
        }

        if (!Enum.TryParse<AccountType>(stored.Type, ignoreCase: true, out var type)
            || !Enum.IsDefined(type))
        {
            _log.Warn($"忽略未知账号类型：{stored.Type}（{stored.Name}）");
            return null;
        }

        var userType = string.IsNullOrWhiteSpace(stored.UserType)
            ? (type == AccountType.Offline ? "legacy" : "msa")
            : stored.UserType;

        return new Account
        {
            Id = stored.Id,
            Type = type,
            Name = stored.Name,
            Uuid = stored.Uuid,
            UserType = userType,
            AccessToken = UnprotectToken(stored.AccessToken, stored.Name, nameof(Account.AccessToken)),
            RefreshToken = UnprotectToken(stored.RefreshToken, stored.Name, nameof(Account.RefreshToken)),
            Xuid = stored.Xuid ?? string.Empty,
            ExpiresAt = stored.ExpiresAt,
            SkinUrl = stored.SkinUrl,
            AddedAt = stored.AddedAt,
        };
    }

    private StoredAccount ToStored(Account account) => new()
    {
        Id = account.Id,
        Type = account.Type.ToString(),
        Name = account.Name,
        Uuid = account.Uuid,
        UserType = account.UserType,
        AccessToken = ProtectToken(account.AccessToken, account.Name, nameof(Account.AccessToken)),
        RefreshToken = ProtectToken(account.RefreshToken, account.Name, nameof(Account.RefreshToken)),
        Xuid = account.Xuid,
        ExpiresAt = account.ExpiresAt,
        SkinUrl = account.SkinUrl,
        AddedAt = account.AddedAt,
    };

    private string ProtectToken(string token, string accountName, string field)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        try
        {
            return SecretProtector.Protect(token);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException
            or PlatformNotSupportedException)
        {
            _log.Error($"加密账号令牌失败：{accountName} / {field}", ex);
            throw new InvalidOperationException($"无法加密账号令牌（{field}）。", ex);
        }
    }

    private string UnprotectToken(string? token, string accountName, string field)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        if (SecretProtector.TryUnprotect(token, out var plaintext))
        {
            return plaintext;
        }

        _log.Warn($"账号令牌解密失败，已清空：{accountName} / {field}");
        return string.Empty;
    }
}

internal sealed class AccountsFile
{
    public int SchemaVersion { get; set; } = AccountStore.CurrentSchemaVersion;

    public List<StoredAccount>? Accounts { get; set; } = new();
}

internal sealed class StoredAccount
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = nameof(AccountType.Offline);

    public string Name { get; set; } = string.Empty;

    public string Uuid { get; set; } = string.Empty;

    public string UserType { get; set; } = "legacy";

    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string Xuid { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAt { get; set; }

    public string? SkinUrl { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}
