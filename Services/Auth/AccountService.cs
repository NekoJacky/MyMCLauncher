using System;
using System.Collections.Generic;
using System.Linq;
using MyMCLauncher.Models;

namespace MyMCLauncher.Services.Auth;

/// <summary>
/// 账号增删、切换与当前账号管理。
/// </summary>
public sealed class AccountService
{
    private readonly object _sync = new();
    private readonly AccountStore _store;
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly List<Account> _accounts = new();

    /// <summary>
    /// 初始化账号服务并加载本地账号。
    /// </summary>
    public AccountService(AccountStore store, SettingsService settings, LogService log)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        ReloadFromStore();
    }

    /// <summary>
    /// 当前账号快照。
    /// </summary>
    public IReadOnlyList<Account> Accounts
    {
        get
        {
            lock (_sync)
            {
                return _accounts.ToArray();
            }
        }
    }

    /// <summary>
    /// 当前选中的账号。
    /// </summary>
    public Account? Current { get; private set; }

    /// <summary>
    /// 账号列表或当前账号变化并保存成功后触发。
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 添加离线账号，并设为当前账号。
    /// </summary>
    public bool TryAddOffline(string name, out Account? account, out string? error)
    {
        account = null;

        if (!OfflineAuthService.TryValidateName(name, out var trimmedName, out error))
        {
            return false;
        }

        var changed = false;

        lock (_sync)
        {
            var existing = FindByNameLocked(trimmedName);
            if (existing is not null)
            {
                account = existing;
                error = "该用户名已存在。";
                return false;
            }

            var newAccount = OfflineAuthService.CreateAccount(trimmedName);
            _accounts.Add(newAccount);

            if (!TrySaveLocked(out error))
            {
                _accounts.Remove(newAccount);
                return false;
            }

            account = newAccount;

            if (!TrySetCurrentLocked(newAccount, out var currentError))
            {
                _log.Warn($"账号已添加，但切换当前账号失败：{currentError}");
            }

            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke();
        }

        return true;
    }

    /// <summary>
    /// 删除账号；若删除的是当前账号，自动选择剩余账号中的第一个或清空。
    /// </summary>
    public bool TryRemove(string accountId, out string? error)
    {
        var changed = false;

        lock (_sync)
        {
            var account = _accounts.FirstOrDefault(item =>
                string.Equals(item.Id, accountId, StringComparison.Ordinal));

            if (account is null)
            {
                error = "账号不存在。";
                return false;
            }

            var index = _accounts.IndexOf(account);
            _accounts.RemoveAt(index);

            if (!TrySaveLocked(out error))
            {
                _accounts.Insert(index, account);
                return false;
            }

            if (string.Equals(Current?.Id, account.Id, StringComparison.Ordinal))
            {
                var next = _accounts.FirstOrDefault();

                if (!TrySetCurrentLocked(next, out var currentError))
                {
                    _log.Warn($"账号已删除，但更新当前账号失败：{currentError}");
                    Current = next;
                }
            }

            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke();
        }

        return true;
    }

    /// <summary>
    /// 切换当前账号。
    /// </summary>
    public bool TrySetCurrent(string? accountId, out string? error)
    {
        Account? target = null;

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            lock (_sync)
            {
                target = _accounts.FirstOrDefault(item =>
                    string.Equals(item.Id, accountId, StringComparison.Ordinal));
            }

            if (target is null)
            {
                error = "账号不存在。";
                return false;
            }
        }

        lock (_sync)
        {
            if (!TrySetCurrentLocked(target, out error))
            {
                return false;
            }
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 保存当前账号列表。
    /// </summary>
    public bool TrySave(out string? error)
    {
        lock (_sync)
        {
            return TrySaveLocked(out error);
        }
    }

    private void ReloadFromStore()
    {
        var loaded = _store.Load();

        lock (_sync)
        {
            _accounts.Clear();
            _accounts.AddRange(loaded);

            var lastAccountId = _settings.Current.LastAccountId;
            Current = _accounts.FirstOrDefault(account =>
                    string.Equals(account.Id, lastAccountId, StringComparison.Ordinal))
                ?? _accounts.FirstOrDefault();

            if (Current is not null
                && !string.Equals(lastAccountId, Current.Id, StringComparison.Ordinal))
            {
                PersistCurrentAccountId(Current.Id);
            }
            else if (Current is null && !string.IsNullOrEmpty(lastAccountId))
            {
                PersistCurrentAccountId(string.Empty);
            }
        }
    }

    private bool TrySaveLocked(out string? error)
    {
        try
        {
            _store.Save(_accounts);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"保存账号失败：{ex.Message}";
            _log.Error(error, ex);
            return false;
        }
    }

    private bool TrySetCurrentLocked(Account? account, out string? error)
    {
        var previous = Current;

        var draft = _settings.Current.Clone();
        draft.LastAccountId = account?.Id ?? string.Empty;

        if (!_settings.Apply(draft, out error))
        {
            Current = previous;
            return false;
        }

        Current = account;
        return true;
    }

    private void PersistCurrentAccountId(string accountId)
    {
        try
        {
            var draft = _settings.Current.Clone();
            draft.LastAccountId = accountId;

            if (!_settings.Apply(draft, out var error))
            {
                _log.Warn($"更新当前账号失败：{error}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"更新当前账号失败：{ex.Message}");
        }
    }

    private Account? FindByNameLocked(string name)
        => _accounts.FirstOrDefault(account =>
            string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase));
}
