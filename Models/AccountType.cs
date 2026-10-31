using System.Text.Json.Serialization;

namespace MyMCLauncher.Models;

/// <summary>
/// 账号类型。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountType
{
    /// <summary>离线账号。</summary>
    Offline,

    /// <summary>微软正版账号。</summary>
    Microsoft,
}
