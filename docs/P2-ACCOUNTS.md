# P2 技术文档：离线账号与账号管理

> 阶段：P2
> 决策状态：已确认
> 实现状态：已完成（`feat/p2-offline-accounts`，待合并）
> 前置：P1 已完成并合并（设置、游戏目录、Java 检测）
> 范围：离线账号模型、离线 UUID、账号加密存储、账号增删切换、当前账号记忆、账号 UI
> 非目标：微软正版登录（P6）、皮肤/披风拉取、账号重命名、多账号同时在线

---

## 1. 目标与交付物

### 1.1 目标

P2 结束后应具备：

1. 可以添加离线账号，用户名经过校验
2. 按 Minecraft 规则生成离线 UUID（与服务器/其他启动器一致）
3. 账号列表可持久化到 `accounts.json`
4. 敏感字段（token）使用 DPAPI 加密
5. 账号可切换、删除，重启后记住上次账号
6. 主窗口显示当前账号，可打开「添加账号」窗口
7. 为 P6 微软登录预留字段与扩展点

### 1.2 交付物

| 文件 | 说明 |
|------|------|
| `Models/Account.cs` | 账号模型 |
| `Models/AccountType.cs` | 账号类型（离线 / 微软） |
| `Services/Auth/OfflineAuthService.cs` | 用户名校验、离线 UUID、离线账号创建 |
| `Services/Auth/SecretProtector.cs` | DPAPI 加解密 |
| `Services/Auth/AccountStore.cs` | `accounts.json` 读写、加密 DTO |
| `Services/Auth/AccountService.cs` | 账号增删切换、当前账号、事件 |
| `Views/AddAccountWindow.xaml(.cs)` | 添加账号窗口（离线 + 微软占位） |
| `MainWindow.xaml(.cs)` | 账号栏与状态摘要 |
| `Services/AppHost.cs` | 注册 `Accounts` |

### 1.3 验收标准

- 添加离线账号后重启，账号仍存在
- 相同用户名生成的 UUID 稳定，且与 Java `UUID.nameUUIDFromBytes` 一致
- 用户名非法时有明确提示
- 同名账号（忽略大小写）不会被重复添加
- 切换账号后重启，仍选中上次账号
- 删除当前账号后自动选择下一个或“未登录”
- `accounts.json` 中 token 不是明文
- `accounts.json` 损坏时可恢复（由 `JsonStore` 备份）
- `dotnet build` 0 error

---

## 2. 现状与约束

### 2.1 P1 已提供

| 能力 | 说明 |
|------|------|
| `SettingsService` | `settings.json`，含 `LastAccountId` |
| `JsonStore` | 原子写、损坏备份、camelCase |
| `LogService` | 文件 + UI 日志 |
| `AppHost` | 组合根，持有 `Paths` / `Json` / `Log` / `Settings` / `Java` / `Memory` |
| `MainWindow` | 内容区、账号区预留、设置入口、状态栏 |
| `SettingsWindow` | 目录 / Java / 内存 |
| `JavaLocator` | 已有可用的 Java 路径 |

### 2.2 设计原则

- 离线与微软账号共用 `Account` 模型，P6 只补充登录流程
- 业务逻辑与 UI 分离，`AccountService` 可独立测试
- 保存失败时回滚内存状态（沿用 P1 的 `Apply` 思路）
- 不记录 token 明文到日志
- 账号文件结构带 `schemaVersion`，便于迁移
- 不引入新 NuGet 依赖：DPAPI 使用 `System.Security.Cryptography.ProtectedData`（WindowsDesktop 已包含）

---

## 3. 架构设计

```text
AddAccountWindow ──┐
                   ├─ AccountService ── AccountStore ── JsonStore / AppPaths
MainWindow ────────┘        │                 └─ SecretProtector (DPAPI)
                            ├─ SettingsService（LastAccountId）
                            └─ LogService

OfflineAuthService
 ├─ 用户名校验
 ├─ 离线 UUID
 └─ 创建 Account
```

`AppHost` 扩展：

```csharp
public sealed class AppHost
{
    public AppPaths Paths { get; }
    public JsonStore Json { get; }
    public LogService Log { get; }
    public SettingsService Settings { get; }
    public JavaLocator Java { get; }
    public MemoryAdvisor Memory { get; }
    public AccountService Accounts { get; }   // 新增
}
```

构造顺序：

```text
Paths → EnsureCreated → Json → Log → Settings → Accounts → Java → Memory
```

---

## 4. 数据模型

### 4.1 `AccountType`

```csharp
public enum AccountType
{
    Offline,
    Microsoft,
}
```

序列化为字符串（`JsonStringEnumConverter` 或 DTO 手动转换），避免数字枚举在后续插入新值时错位。

### 4.2 `Account`

```csharp
public sealed class Account
{
    public string Id { get; init; } = "";                 // offline:<uuid> / microsoft:<uuid>
    public AccountType Type { get; init; }
    public string Name { get; init; } = "";               // 显示名 / 游戏内名称
    public string Uuid { get; init; } = "";               // 32 位小写 hex，无连字符
    public string UserType { get; init; } = "legacy";     // legacy / msa
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string Xuid { get; init; } = "";               // 离线为空，启动时映射为 "0"
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? SkinUrl { get; set; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.Now;

    public bool IsOffline => Type == AccountType.Offline;

    public string EffectiveAccessToken => IsOffline ? "0" : AccessToken;
    public string EffectiveXuid => IsOffline ? "0" : Xuid;

    public string FormattedUuid => Uuid.Length == 32
        ? $"{Uuid[..8]}-{Uuid[8..12]}-{Uuid[12..16]}-{Uuid[16..20]}-{Uuid[20..]}"
        : Uuid;

    public string DisplayName => IsOffline ? $"{Name}（离线）" : Name;
}
```

说明：

- 离线账号的 `AccessToken`/`RefreshToken` 在存储中为空，P5 使用 `EffectiveAccessToken` 得到 `"0"`
- `Uuid` 统一存 32 位无连字符；界面显示用 `FormattedUuid`
- `UserType`：离线 `legacy`，微软 `msa`
- `AddedAt` 用于排序/展示

### 4.3 `accounts.json`

```json
{
  "schemaVersion": 1,
  "accounts": [
    {
      "id": "offline:5627dd98e6be3c21b8a8e92344183641",
      "type": "offline",
      "name": "Steve",
      "uuid": "5627dd98e6be3c21b8a8e92344183641",
      "userType": "legacy",
      "accessToken": "",
      "refreshToken": "",
      "xuid": "",
      "expiresAt": null,
      "skinUrl": null,
      "addedAt": "2026-10-30T22:00:00+08:00"
    }
  ]
}
```

- `accessToken` / `refreshToken`：DPAPI 加密后的 Base64；空字符串表示无
- `xuid`：P2 离线为空；P6 微软账号写入真实 XUID
- 顶层使用对象而不是数组，便于添加 `schemaVersion` 与未来字段

---

## 5. 离线 UUID 与用户名校验

### 5.1 离线 UUID

必须与 Java 的离线 UUID 完全一致：

```java
UUID.nameUUIDFromBytes(("OfflinePlayer:" + name)
    .getBytes(StandardCharsets.UTF_8));
```

C# 实现：

```csharp
public static string CreateOfflineUuid(string name)
{
    var input = Encoding.UTF8.GetBytes("OfflinePlayer:" + name);
    var hash = MD5.HashData(input);

    hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // IETF variant

    return Convert.ToHexString(hash).ToLowerInvariant();
}
```

注意：

- 使用 UTF-8，不要用系统默认编码
- MD5 仅用于 UUID 派生，不用于安全场景
- 版本位 `0x30`、变体位 `0x80` 必须设置
- 游戏参数 `--uuid` 使用无连字符格式

**测试向量（已用本机 Java 25 验证）**

| 用户名 | UUID |
|--------|------|
| `Notch` | `b50ad385-829d-3141-a216-7e7d7539ba7f` |
| `Steve` | `5627dd98-e6be-3c21-b8a8-e92344183641` |
| `Alex` | `36532b5e-c442-3dbb-a24c-c7e55d0f979a` |
| `test_user` | `109fb397-58f2-3a9a-92c1-5a21ac5113c2` |

### 5.2 用户名校验

建议规则：

| 规则 | 说明 |
|------|------|
| 非空 | `Trim()` 后长度 > 0 |
| 长度 | ≤ 16（按 UTF-16 code unit，与 Java `String.length()` 一致） |
| 空白 | 不允许首尾空白；内部不允许空白 |
| 控制字符 | 拒绝 |
| 字符集 | 允许 Unicode；若包含非 `[A-Za-z0-9_]` 字符，提示部分服务器可能不支持 |
| 唯一性 | 忽略大小写判重；保留原始大小写 |

说明：

- 官方正版用户名是 3–16 位 `[A-Za-z0-9_]`，但离线模式服务器通常允许更宽
- 允许中文可以方便离线玩家，但部分服务端/插件可能异常，因此只警告不阻止
- 同名不同大小写会产生不同 UUID，为避免混淆，按忽略大小写判重

---

## 6. 账号存储与 DPAPI

### 6.1 `SecretProtector`

```csharp
public static class SecretProtector
{
    public static string Protect(string plaintext);
    public static bool TryUnprotect(string? ciphertext, out string plaintext);
}
```

实现要点：

- `ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser)`
- 固定 entropy：`Encoding.UTF8.GetBytes("MyMCLauncher.Accounts.v1")`
- 输出 Base64
- 空字符串不解密，直接返回空
- 解密失败（换用户/换机器/Profile 损坏）：记录警告，返回 `false`，调用方清空 token
- 不记录明文 token

> 注意：entropy 一旦发布不要修改，否则旧数据无法解密。

### 6.2 `AccountStore`

```csharp
public sealed class AccountStore
{
    public IReadOnlyList<Account> Load();
    public void Save(IReadOnlyList<Account> accounts);
}
```

- 文件：`AppPaths.AccountsFile`
- 使用 `JsonStore.Load` / `Save`
- 内部 DTO：`AccountsFile` + `StoredAccount`
- `Load`：
  - 文件不存在 → 空列表
  - 损坏 → `JsonStore` 已备份为 `.corrupt-*`，返回空列表
  - `SchemaVersion` 高于当前 → 记录警告并尽量读取
  - 每条账号解密 token；失败则清空并记录
  - 忽略 `Id`/`Uuid` 为空的无效记录
- `Save`：
  - 按当前 `schemaVersion` 写入
  - token 非空时加密
  - 原子写入由 `JsonStore` 保证

### 6.3 安全边界

- DPAPI `CurrentUser`：只有当前 Windows 用户能解密
- 复制 `accounts.json` 到另一台机器：离线账号仍可用，微软账号需重新登录
- 离线账号本身没有密码，不属于安全凭证
- 日志仅记录账号名、类型、UUID，不记录 token

---

## 7. `AccountService`

```csharp
public sealed class AccountService
{
    public IReadOnlyList<Account> Accounts { get; }
    public Account? Current { get; }
    public event Action? Changed;

    public AccountService(AccountStore store, SettingsService settings, LogService log);

    public void Load();
    public bool TryAddOffline(string name, out Account? account, out string? error);
    public bool TryRemove(string accountId, out string? error);
    public bool TrySetCurrent(string? accountId, out string? error);
    public bool TrySave(out string? error);
}
```

### 7.1 加载

```text
AccountStore.Load()
  └─ 读取 settings.LastAccountId
       ├─ 找到 → Current = 该账号
       ├─ 未找到且有账号 → Current = 第一个
       └─ 没有账号 → Current = null
```

### 7.2 添加离线账号

```text
校验用户名
  └─ 忽略大小写查重
       ├─ 已存在 → 返回错误 / 选中已有账号
       └─ 创建 Account
            └─ 加入列表
                 └─ 保存 accounts.json
                      ├─ 成功 → 设为当前 → Changed
                      └─ 失败 → 回滚内存列表与当前账号
```

### 7.3 删除

```text
确认
  └─ 从列表移除
       └─ 若删除的是当前账号
            ├─ 有剩余 → 选择第一个
            └─ 无剩余 → Current = null
                 └─ 保存 accounts.json + settings.LastAccountId
                      ├─ 成功 → Changed
                      └─ 失败 → 回滚
```

### 7.4 切换

```text
TrySetCurrent(id)
  └─ 校验账号存在
       └─ Current = account
            └─ settings.LastAccountId = id
                 └─ 保存 settings
                      ├─ 成功 → Changed
                      └─ 失败 → 回滚 Current
```

### 7.5 事件

- `Changed` 在账号列表或当前账号变化并保存成功后触发
- 主窗口订阅并刷新账号栏/状态栏
- 事件在 UI 线程触发（`AccountService` 由 UI 调用）

---

## 8. UI 设计

### 8.1 主窗口账号栏

放在内容区左上角：

```text
┌──────────────────────────────────────────────────────────┐
│ [账号 ▼  Steve（离线）]  [添加账号]  [删除]        [设置] │
│                                                          │
│                内容区占位（P3 版本列表）                  │
└──────────────────────────────────────────────────────────┘
```

行为：

- `ComboBox` 绑定 `AccountService.Accounts`，显示 `DisplayName`
- 无账号时：显示“未登录”，删除按钮禁用
- 切换：`TrySetCurrent`
- 添加：打开 `AddAccountWindow`
- 删除：确认后 `TryRemove`
- 订阅 `Changed` 刷新，注意避免 `SelectionChanged` 递归

### 8.2 `AddAccountWindow`

```text
┌─ 添加账号 ───────────────────────────────┐
│ 离线登录                                 │
│ 用户名 [________________]                │
│ 提示：1–16 个字符，部分服务器不支持中文   │
│ [添加离线账号]                           │
│                                          │
│ 微软正版                                 │
│ [微软登录（P6 开放）]  （禁用）           │
│                                          │
│ 错误提示：……                             │
│                              [取消]      │
└──────────────────────────────────────────┘
```

- 用户名输入框回车触发添加
- 错误用红色文本显示，不弹重复对话框
- 添加成功后 `DialogResult = true`
- 微软按钮禁用并加 ToolTip

### 8.3 状态栏

P1 状态栏增加账号：

```text
账号：Steve（离线） | 游戏目录：... | Java：... | 内存：7936 MB
```

---

## 9. 关键流程

### 9.1 启动

```text
AppHost
  └─ AccountService.Load()
       ├─ accounts.json → 账号列表
       └─ settings.LastAccountId → Current
            └─ MainWindow 刷新账号栏与状态栏
```

### 9.2 添加离线账号

```text
主窗口“添加账号”
  └─ AddAccountWindow
       └─ 输入用户名 → 校验
            └─ OfflineAuthService.CreateAccount
                 └─ AccountService.TryAddOffline
                      ├─ 保存成功 → 关闭窗口 → 主窗口选中新账号
                      └─ 失败 → 窗口内显示错误
```

### 9.3 切换/删除

```text
选择 ComboBox → TrySetCurrent → 保存 settings → 刷新状态栏
删除 → 确认 → TryRemove → 选择下一个/未登录 → 保存 → 刷新
```

---

## 10. 任务拆解与提交计划

| # | 内容 | 提交信息建议 | 状态 |
|---|------|--------------|------|
| 1 | `Account` / `AccountType` / `OfflineAuthService` + UUID 测试向量 | `feat(P2): add account models and offline UUID` | 已完成 |
| 2 | `SecretProtector` + `AccountStore` + 加密 DTO | `feat(P2): add encrypted account storage` | 已完成 |
| 3 | `AccountService` + AppHost 注册 + 设置联动 | `feat(P2): add account service and current account` | 已完成 |
| 4 | `AddAccountWindow` + 主窗口账号栏/状态栏 | `feat(P2): add account UI` | 已完成 |
| 5 | 文档勾选与实现记录 | `docs(P2): update progress and notes` | 本次提交 |

建议分支：`feat/p2-offline-accounts`（阶段内按任务提交，阶段结束统一 PR）。

---

## 11. 风险点

| # | 风险 | 影响 | 概率 | 应对 |
|---|------|------|------|------|
| 1 | 离线 UUID 算法不一致（编码/版本位/变体位） | 玩家在服务器上的 UUID 与预期不同 | 高 | 用 Java 生成测试向量；单测覆盖 `Notch`/`Steve`/`Alex`/`test_user` |
| 2 | 用户名规则过严/过松 | 玩家无法使用想要的名字，或服务器拒绝 | 中 | 默认 1–16、允许 Unicode、对非 ASCII 仅警告；后续可按反馈调整 |
| 3 | 同名不同大小写产生不同 UUID | 账号列表混乱 | 中 | 忽略大小写判重；保留原始大小写 |
| 4 | DPAPI 解密失败（换用户/换机器/Profile 损坏） | 微软 token 失效 | 中 | 清空 token、保留账号、提示重新登录；离线账号不受影响 |
| 5 | token 明文进入日志或配置 | 账号泄露 | 中 | 只记录账号名/类型/UUID；存储统一 DPAPI；代码审查 |
| 6 | `accounts.json` 损坏 | 账号丢失 | 低 | `JsonStore` 备份为 `.corrupt-*`；启动时回退空列表并提示 |
| 7 | 保存失败后内存状态已改变 | UI 与磁盘不一致 | 中 | 沿用 P1 的回滚策略：先保存，失败恢复旧列表/旧当前账号 |
| 8 | 删除当前账号后 Current 悬空 | 启动/状态栏异常 | 中 | 删除后选择第一个或 null；写入 settings |
| 9 | 当前账号 ID 失效 | 启动时无选中账号 | 低 | 回退第一个账号并记录日志 |
| 10 | 用户名含 emoji/组合字符 | 长度判断与显示异常 | 低 | 按 UTF-16 长度限制；不额外规范化 |
| 11 | `SchemaVersion` 升级 | 旧文件无法读取 | 低 | 先实现 v1，迁移函数预留 |
| 12 | DPAPI entropy 被误改 | 旧 token 全部失效 | 低 | 固定常量并注释；发布后不修改 |
| 13 | 账号文件被其他程序占用/只读 | 保存失败 | 低 | 错误提示；内存回滚 |
| 14 | 未来微软账号字段不足 | P6 需要改模型 | 中 | 现在就加入 `RefreshToken` / `Xuid` / `ExpiresAt` / `SkinUrl` |
| 15 | UI 事件递归（ComboBox 刷新触发 SelectionChanged） | 当前账号被误改 | 中 | 刷新时用 `_refreshing` 标志屏蔽事件 |
| 16 | 离线账号被误认为安全凭证 | 用户误解 | 低 | UI/文档注明“离线账号无密码，仅用于离线服务器” |

---

## 12. 测试计划

### 12.1 单元测试

- `OfflineAuthService.CreateOfflineUuid`：§5.1 全部向量
- 用户名校验：空、空白、超长、控制字符、中文、同名不同大小写
- `SecretProtector`：加解密往返、空字符串、篡改 Base64 失败
- `AccountStore`：保存/加载往返、损坏文件、token 非明文、解密失败回退
- `AccountService`：添加、重复、删除当前、切换、保存失败回滚、重启恢复

> 未建立 xUnit 项目；以上用例通过临时控制台与 WPF 冒烟测试验证。

### 12.2 手工测试

| 场景 | 预期 |
|------|------|
| 添加 `Steve` | 显示 `Steve（离线）`，UUID 为 `5627dd98...` |
| 再次添加 `steve` | 提示已存在，不重复添加 |
| 添加中文用户名 | 允许，提示部分服务器可能不支持 |
| 切换账号并重启 | 仍选中上次账号 |
| 删除当前账号 | 自动选中下一个；无账号时显示未登录 |
| 用文本编辑器查看 `accounts.json` | token 字段为空或 Base64，不是明文 |
| 删除/损坏 `accounts.json` | 启动不崩溃，备份生成 |
| 用另一个 Windows 用户打开 | 微软 token 解密失败，账号仍在 |

### 12.3 UI 冒烟

- 主窗口账号栏正常显示
- 添加账号窗口可打开、校验、关闭
- 无账号时删除按钮禁用
- 状态栏账号信息随切换更新

---

## 13. 已确认决策

| # | 事项 | 决定 |
|---|------|------|
| 1 | 用户名规则 | 1–16 UTF-16 字符，允许 Unicode，非 ASCII 仅警告 |
| 2 | 判重 | 忽略大小写；保留原始大小写 |
| 3 | 账号 ID | `offline:<uuid32>` / `microsoft:<uuid32>` |
| 4 | 当前账号存放 | `settings.json` 的 `LastAccountId`（已存在） |
| 5 | 离线 token | 存储为空；P5 用 `EffectiveAccessToken` 映射为 `"0"` |
| 6 | DPAPI 加密字段 | 仅 `AccessToken` / `RefreshToken`；XUID 明文 |
| 7 | 微软登录占位 | `AddAccountWindow` 中显示禁用按钮，标注 P6 开放 |
| 8 | 账号文件结构 | 对象 + `schemaVersion` + `accounts` 数组 |
| 9 | 添加账号后自动切换 | 是；添加成功后直接设为当前账号 |

---

## 14. 验收标准（Definition of Done）

1. 可添加离线账号，UUID 与 Java 算法一致
2. 用户名非法/重复时有明确提示
3. 账号可切换、删除，重启后记住上次账号
4. 删除当前账号后自动回退
5. `accounts.json` 中无明文 token
6. 损坏文件可恢复，不导致启动失败
7. 主窗口账号栏与状态栏正确显示
8. 添加账号窗口包含微软登录占位
9. `dotnet build` 0 error
10. 文档更新并记录实现差异

---

## 15. 实现记录

### 15.1 提交

| 提交 | 内容 |
|------|------|
| `6c38e9c` | 账号模型与离线 UUID |
| `1e2fed6` | 加密账号存储 |
| `fde3fb5` | 账号服务与当前账号 |
| `e5021c8` | 账号 UI |
| 本次提交 | 文档更新 |

### 15.2 与设计的差异

- 未建立 xUnit 项目；使用临时控制台与 WPF 冒烟测试验证
- 离线 token 存储为空，由 `EffectiveAccessToken` 映射为 `"0"`
- 重复用户名仅提示错误，不自动切换到已有账号
- 账号保存失败时回滚列表；设置保存失败时记录警告

### 15.3 验证结果

- UUID：`Notch` / `Steve` / `Alex` / `test_user` 与 Java 25 结果一致
- DPAPI：加解密往返成功；篡改 token 解密失败并清空
- `accounts.json`：token 无明文；损坏文件生成 `.corrupt-*` 备份
- 账号：添加、重复、切换、删除、重启恢复均通过
- UI：`MainWindow` + `AddAccountWindow` 冒烟通过
