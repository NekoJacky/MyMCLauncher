namespace MyMCLauncher.Models;

/// <summary>
/// 一个启动器账号。离线与微软账号共用该模型。
/// </summary>
public sealed class Account
{
    /// <summary>
    /// 稳定标识：<c>offline:&lt;uuid&gt;</c> 或 <c>microsoft:&lt;uuid&gt;</c>。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 账号类型。
    /// </summary>
    public AccountType Type { get; init; }

    /// <summary>
    /// 显示名 / 游戏内名称。微软账号改名时允许更新。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 32 位小写 UUID，无连字符。
    /// </summary>
    public string Uuid { get; init; } = string.Empty;

    /// <summary>
    /// 游戏参数中的 <c>user_type</c>：离线为 <c>legacy</c>，微软为 <c>msa</c>。
    /// </summary>
    public string UserType { get; init; } = "legacy";

    /// <summary>
    /// 访问令牌。离线账号存储为空。
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// 刷新令牌。离线账号存储为空。
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Xbox 用户 ID。离线账号存储为空。
    /// </summary>
    public string Xuid { get; init; } = string.Empty;

    /// <summary>
    /// 令牌过期时间。
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// 皮肤地址（P6 使用）。
    /// </summary>
    public string? SkinUrl { get; set; }

    /// <summary>
    /// 添加时间。
    /// </summary>
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// 是否为离线账号。
    /// </summary>
    public bool IsOffline => Type == AccountType.Offline;

    /// <summary>
    /// 启动游戏时实际使用的访问令牌。
    /// </summary>
    public string EffectiveAccessToken => IsOffline ? "0" : AccessToken;

    /// <summary>
    /// 启动游戏时实际使用的 XUID。
    /// </summary>
    public string EffectiveXuid => IsOffline ? "0" : Xuid;

    /// <summary>
    /// 带连字符的 UUID，用于显示。
    /// </summary>
    public string FormattedUuid => Uuid.Length == 32
        ? $"{Uuid[..8]}-{Uuid[8..12]}-{Uuid[12..16]}-{Uuid[16..20]}-{Uuid[20..]}"
        : Uuid;

    /// <summary>
    /// 界面显示名称。
    /// </summary>
    public string DisplayName => IsOffline ? $"{Name}（离线）" : Name;
}
