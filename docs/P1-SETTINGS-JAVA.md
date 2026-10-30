# P1 技术文档：设置、游戏目录与 Java 检测

> 阶段：P1
> 决策状态：已确认
> 前置：P0 已完成（`AppPaths` / `JsonStore` / `LogService` / `AppHost` / 主窗口壳）
> 范围：设置持久化、游戏目录选择与校验、Java 自动/手动检测、内存设置、设置界面
> 非目标：版本扫描与解析（P3）、游戏下载（M2）、Java 运行时自动下载（M3）

---

## 1. 目标与交付物

### 1.1 目标

P1 结束后应具备：

1. 设置可持久化到 `%APPDATA%\MyMCLauncher\settings.json`，重启后恢复
2. 可以选择并校验 `.minecraft` 游戏目录
3. 自动发现本机 Java（环境变量 / PATH / 常见目录 / Mojang 运行时；P1 不扫描注册表）
4. 支持手动指定任意 Java 可执行文件（`java.exe` / `javaw.exe` 等）
5. 能读取并比较 Java 版本（如 `1.8.0_392`、`17.0.9`、`21`、`25-ea`）
6. 能根据物理内存给出默认内存值，并允许手动调整
7. 设置界面可用，扫描过程不阻塞 UI

### 1.2 交付物

| 文件 | 说明 |
|------|------|
| `Models/LauncherSettings.cs` | 设置模型 |
| `Models/JavaInfo.cs` | Java 候选信息 |
| `Models/JavaVersion.cs` | 版本号与比较 |
| `Services/SettingsService.cs` | 设置加载/保存/更新 |
| `Services/Java/JavaVersionParser.cs` | 版本号解析 |
| `Services/Java/JavaLocator.cs` | Java 扫描与信息读取 |
| `Services/Java/JavaSelector.cs` | 候选推荐逻辑 |
| `Services/MemoryAdvisor.cs` | 默认内存计算与校验 |
| `Views/SettingsWindow.xaml(.cs)` | 设置窗口 |
| `MainWindow.xaml(.cs)` | 增加设置入口与摘要 |
| `Services/AppHost.cs` | 注册 `Settings`、`Java`、`Memory` |

### 1.3 验收标准

- 选择目录、Java、内存后重启，设置仍生效
- 目录不存在 / 不像 `.minecraft` 时有明确提示
- 本机没有 `JAVA_HOME`、PATH 中也没有 Java 时，仍能发现 `<gameDir>/runtime` 下的 Java
- 手动指定任意 `java.exe` 后可正确读取版本
- Java 版本号解析覆盖 Java 8 / 9+ / EA / 带 build 号
- 扫描期间界面可响应，可取消
- 内存默认值 = 总内存 1/4，128 对齐，限制在 256–8192 MB
- 非法设置文件可恢复（由 `JsonStore` 备份）
- `dotnet build` 0 error

---

## 2. 现状与约束

### 2.1 P0 已提供

```text
AppHost
├─ Paths  : AppPaths      → Root / LogsDirectory / NativesDirectory / WebView2DataDirectory
├─ Json   : JsonStore     → 原子写、损坏备份、camelCase
└─ Log    : LogService    → 文件滚动 + UI 事件
```

- `MainWindow` 已有内容区占位、日志面板、状态栏
- 设置文件路径已定义为 `AppPaths.SettingsFile`

### 2.2 本机环境（联调参考）

| 项目 | 现状 |
|------|------|
| `JAVA_HOME` | 未设置 |
| PATH 中的 `java` | 不存在 |
| Mojang 运行时 | `%APPDATA%\.minecraft\runtime\java-runtime-epsilon\bin\java.exe` |
| 该运行时版本 | OpenJDK 25.0.1（Microsoft Build） |
| 游戏目录 | `%APPDATA%\.minecraft` 仅有 `runtime` |

> 当前环境适合验证“只能通过 Mojang runtime 发现 Java”的场景；多候选、32 位、不同版本需要额外准备测试数据。

### 2.3 设计原则

- 服务化：设置、扫描、推荐、内存各自独立，便于单测
- 异步：所有磁盘/进程扫描在后台线程执行，UI 线程只做绑定
- 容错：单个 Java 候选失败不影响整体扫描
- 可诊断：扫描来源、耗时、失败原因写入日志
- 不引入新依赖：使用 BCL（`Microsoft.Win32.Registry`、`Process`、`GlobalMemoryStatusEx` P/Invoke）

---

## 3. 架构设计

### 3.1 模块关系

```text
SettingsWindow
 ├─ SettingsService ── JsonStore / AppPaths / LogService
 ├─ JavaLocator ───── JavaVersionParser / LogService
 ├─ JavaSelector
 └─ MemoryAdvisor

MainWindow
 ├─ 打开 SettingsWindow
 └─ 状态栏摘要（目录 / Java / 内存）
```

### 3.2 AppHost 扩展

```csharp
public sealed class AppHost
{
    public AppPaths Paths { get; }
    public JsonStore Json { get; }
    public LogService Log { get; }
    public SettingsService Settings { get; }   // 新增
    public JavaLocator Java { get; }           // 新增
    public MemoryAdvisor Memory { get; }       // 新增
}
```

构造顺序：

```text
Paths → EnsureCreated → Json → Log → Settings(Load) → Java → Memory
```

---

## 4. 数据模型

### 4.1 `LauncherSettings`

```csharp
public sealed class LauncherSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string GameDirectory { get; set; } = "";
    public string JavaPath { get; set; } = "";

    public bool AutoMemory { get; set; } = true;
    public int MemoryMb { get; set; }

    public string LastVersionId { get; set; } = "";
    public string LastAccountId { get; set; } = "";
}
```

字段说明：

| 字段 | 说明 |
|------|------|
| `SchemaVersion` | 配置结构版本，用于后续迁移 |
| `GameDirectory` | `.minecraft` 根目录；空表示未设置 |
| `JavaPath` | `java.exe` 完整路径；空表示自动选择 |
| `AutoMemory` | 是否使用建议内存 |
| `MemoryMb` | 手动/生效内存，`AutoMemory=true` 时可为 0 |
| `LastVersionId` | P3 使用 |
| `LastAccountId` | P2 使用 |

默认值策略：

- `GameDirectory`：若 `%APPDATA%\.minecraft` 存在，首次启动自动填入
- `MemoryMb`：首次启动写入建议值
- 其余为空

### 4.2 `settings.json`

```json
{
  "schemaVersion": 1,
  "gameDirectory": "C:\\Users\\<user>\\AppData\\Roaming\\.minecraft",
  "javaPath": "C:\\Users\\<user>\\AppData\\Roaming\\.minecraft\\runtime\\java-runtime-epsilon\\bin\\java.exe",
  "autoMemory": true,
  "memoryMb": 4096,
  "lastVersionId": "",
  "lastAccountId": ""
}
```

### 4.3 `JavaInfo` / `JavaVersion`

```csharp
public enum JavaSource
{
    Saved, JavaHome, Path, MojangRuntime, CommonDirectory, Registry, Manual
}

public sealed class JavaInfo
{
    public string JavaPath { get; init; } = "";     // ...\bin\java.exe
    public string JavaHome { get; init; } = "";     // ...\jdk-21
    public JavaVersion Version { get; init; }
    public string? Vendor { get; init; }
    public string Architecture { get; init; } = "unknown"; // x64 / x86 / arm64 / unknown
    public JavaSource Source { get; init; }

    public bool Is64Bit => Architecture == "x64";
    public string DisplayName => $"{Version} ({Architecture}) · {JavaPath}";
}

public readonly record struct JavaVersion(int Major, int Minor, int Patch, int Build, string Raw)
    : IComparable<JavaVersion>
{
    public static bool TryParse(string? raw, out JavaVersion version);
    public bool IsAtLeast(int major);
    public int CompareTo(JavaVersion other);
}
```

版本解析规则：

| 输入 | 结果 |
|------|------|
| `1.8.0_392` | 8.0.0.392 |
| `1.8.0_202-b08` | 8.0.0.202 |
| `1.8` | 8.0.0.0 |
| `9.0.1` | 9.0.1.0 |
| `11` | 11.0.0.0 |
| `17.0.9+9` | 17.0.9.9 |
| `21-ea` | 21.0.0.0 |
| `25-ea+36` | 25.0.0.36 |

规则：

- 以 `1.` 开头时，第二段作为 major（Java 8 及以前）
- 其余情况第一段作为 major
- `_`、`-b`、`+` 后的数字作为 build
- 解析使用 `InvariantCulture`
- 非法输入返回 `false`，不抛异常

---

## 5. 设置服务

```csharp
public sealed class SettingsService
{
    public LauncherSettings Current { get; }
    public event Action? Changed;

    public SettingsService(AppPaths paths, JsonStore json, LogService log);

    public void Load();
    public bool TrySave(out string? error);
    public void Update(Action<LauncherSettings> mutate);
    public void ResetToDefaults();
}
```

行为：

- `Load`：`JsonStore.Load<LauncherSettings>(SettingsFile, 默认值工厂)`
- 读取后做规范化与校验：
  - 路径去引号、`Path.GetFullPath`
  - `MemoryMb` 夹到合法范围
  - `SchemaVersion` 低于当前版本时执行迁移（P1 仅 1）
- `TrySave`：
  - 写前再次规范化
  - 失败（磁盘满/只读）写日志并返回错误文本
  - 不在每次按键保存，只在窗口“保存/确定”时调用
- `Update`：修改后立即保存并触发 `Changed`
- 设置文件损坏由 `JsonStore` 负责备份为 `.corrupt-*`，随后使用默认值

---

## 6. 游戏目录

### 6.1 选择

- 使用 `Microsoft.Win32.OpenFolderDialog`（.NET 8+，WPF 可用）
- 初始目录：当前 `GameDirectory`，否则 `%APPDATA%`
- 允许选择空目录（用于 M2 后续下载）
- 路径统一 `Path.GetFullPath`，保留中文/空格

### 6.2 校验

```csharp
public enum GameDirectoryState
{
    NotSet,
    Missing,
    Empty,
    LooksLikeMinecraft,
    HasVersions,
    Invalid
}

public sealed record GameDirectoryCheck(GameDirectoryState State, string Message);
```

判定顺序：

1. 空字符串 → `NotSet`
2. 路径非法（含非法字符、根目录）→ `Invalid`
3. 目录不存在 → `Missing`（可提示“是否创建？”）
4. 存在但不含 `versions` / `assets` / `libraries`：
   - 目录为空 → `Empty`（允许，提示“尚无游戏文件”）
   - 非空 → `LooksLikeMinecraft` 但缺少必要子目录（警告）
5. 含 `versions` 或 `assets` 或 `libraries` → `LooksLikeMinecraft`
6. `versions` 下至少一个版本目录 → `HasVersions`

提示文案示例：

```text
未设置游戏目录
目录不存在
目录为空，将在下载游戏时创建文件
未检测到 versions/assets/libraries，请确认是否选择了 .minecraft 根目录
已检测到 N 个版本
```

### 6.3 写权限

P1 **不主动写入游戏目录**，理由：

- 避免在用户目录留下临时文件，触发杀毒/同步盘/备份
- 写测试成功不代表后续一定可写（TOCTOU），价值有限
- 目录可能是有意只读的（共享盘、归档目录）
- 真正写入（natives、下载、日志）时再报错，错误信息更准确、可操作

可选增强：只检查目录是否存在与结构，不检查写权限；后续出现写入失败时提示具体路径与原因。

### 6.4 变更影响

- 修改目录后：
  - 重新扫描 `<gameDir>/runtime` 下的 Java
  - 清空依赖目录的缓存状态（P3 版本列表）
  - 记录日志

---

## 7. Java 检测

### 7.1 扫描来源与优先级

| 优先级 | 来源 | 说明 |
|--------|------|------|
| 1 | `Settings.JavaPath` | 用户上次选择，优先验证 |
| 2 | `JAVA_HOME` | `<JAVA_HOME>\bin\java.exe` |
| 3 | `PATH` | 各目录下的 `java.exe` |
| 4 | Mojang 运行时 | `<gameDir>\runtime\**\bin\java.exe` |
| 5 | 常见安装目录 | `Program Files` / `Program Files (x86)` 下厂商目录 |
| 6 | 注册表（可选） | `JavaSoft` 等；HMCL 使用、PCL 未使用；P1 暂不实现（见 §15.1） |
| 7 | 手动指定 | 用户选择文件 |

常见目录参考：

```text
%ProgramFiles%\Java
%ProgramFiles%\Eclipse Adoptium
%ProgramFiles%\Microsoft
%ProgramFiles%\Zulu
%ProgramFiles%\Amazon Corretto
%ProgramFiles%\BellSoft
%ProgramFiles%\Semeru
%ProgramFiles(x86)%\ 下同类目录
```

### 7.2 扫描实现要点

- 后台线程执行（`Task.Run`），支持 `CancellationToken`
- 限制目录深度（建议 ≤ 3），避免全盘递归
- 跳过：
  - 网络驱动器 / UNC 路径（`DriveInfo.DriveType == Network`）
  - 重解析点（`ReparsePoint`），防止符号链接循环
- 每个候选独立 `try/catch`，失败写 `Debug/Warn` 日志，不中断整体
- 结果按 `Path.GetFullPath` + 不区分大小写去重
- 扫描结束记录：候选数量、耗时、各来源数量
- 缓存本次结果，支持“重新扫描”

### 7.3 读取 Java 信息

优先读取 `<javaHome>\release`：

```text
JAVA_VERSION="25.0.1"
OS_ARCH="x86_64"
IMPLEMENTOR="Microsoft"
```

解析规则：

- 按行读取，首个 `=` 分割，去除引号
- `JAVA_VERSION` → `JavaVersion`
- `OS_ARCH`：`x86_64`/`amd64` → `x64`；`x86`/`i386` → `x86`；`aarch64` → `arm64`
- `IMPLEMENTOR` → `Vendor`

`release` 不存在或缺少版本时，回退执行：

```text
java.exe -version
```

- `CreateNoWindow = true`、不显示窗口
- 同时重定向 stdout/stderr，异步读取，避免缓冲区死锁
- 超时 5 秒，超时后 `Kill(entireProcessTree: true)`
- 从 stderr 用正则提取 `version "([^"]+)"`
- 输出可能包含 `64-Bit Server VM`，可作为架构兜底
- 编码使用 UTF-8 读取；版本号是 ASCII，不受控制台代码页影响

### 7.4 推荐逻辑

```csharp
public static JavaInfo? SelectBest(
    IEnumerable<JavaInfo> candidates,
    int? requiredMajor,
    string? preferredPath);
```

顺序：

1. `preferredPath` 存在且满足 `requiredMajor` → 直接使用
2. `Major == requiredMajor` 中版本最高者（优先 x64）
3. `Major > requiredMajor` 中版本最低者（避免跨代过大）
4. 无 `requiredMajor` 时：x64 中版本最高者
5. 仍无 → `null`，UI 提示手动选择

> P1 只实现推荐 API；`requiredMajor` 在 P3 从版本 JSON 的 `javaVersion.majorVersion` 传入。

### 7.5 参考实现对比

| 项目 | Java 检测 | 说明 |
|------|-----------|------|
| HMCL | 注册表 + Program Files + PATH + 官方 runtime + 用户手动 + 缓存 | 查 `HKLM\SOFTWARE\JavaSoft\...` 等键 |
| PCL | 候选目录（含其他启动器）+ `JDK_HOME`/`JAVA_HOME` | 不扫注册表、不扫 PATH；排除重解析点/特殊路径；并行 4 路 |

结论：候选目录 + 环境变量已能覆盖大多数情况；注册表是补充手段，不是必需。详见 §16。

---

## 8. 内存设置

### 8.1 默认值

```csharp
public static class MemoryAdvisor
{
    public static int GetRecommendedMemoryMb();      // 总内存 1/4，128 对齐，256–8192
    public static int Clamp(int requestedMb);
    public static string? Validate(int requestedMb); // 返回警告文本，null 表示正常
}
```

获取物理内存：

- 优先 `GlobalMemoryStatusEx` P/Invoke（`ullTotalPhys`）
- 失败回退 `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`
- 全部按 `long`/`ulong` 计算，避免 32 位溢出

规则：

- 总内存 ≥ 32 GB：建议值固定 8192 MB
- 否则：`max(round(总内存 / 4 / 128) * 128, 256)` MB（与 HMCL 一致）
- 用户可覆盖，合法范围 256–65536 MB
- 超过物理内存 50% 时显示警告（不阻止保存）
- `AutoMemory = true` 时每次启动重新计算
- 参考：HMCL 使用总内存 1/4；PCL 使用可用内存 + 模组数量做渐进分配，P1 暂无版本/模组信息，暂不采用

---

## 9. UI 设计

### 9.1 入口

- 主窗口右上角增加“设置”按钮（或菜单）
- 状态栏显示摘要：

```text
游戏目录：C:\...\.minecraft  |  Java：25 (x64)  |  内存：4096 MB
```

### 9.2 `SettingsWindow`

模态窗口，分区布局：

```text
┌─ 设置 ─────────────────────────────────────┐
│ 游戏目录                                    │
│ [C:\Users\...\.minecraft        ] [浏览]   │
│ ✓ 已检测到 3 个版本                         │
│                                             │
│ Java 运行时                                 │
│ [OpenJDK 25 (x64) · ...\java.exe      ▼]   │
│ [重新扫描] [手动选择]                       │
│ 来源：Mojang 运行时                         │
│                                             │
│ 内存                                        │
│ [x] 自动（建议 4096 MB）                    │
│ [=====|--------] 4096 MB                    │
│                                             │
│                      [取消] [保存]          │
└─────────────────────────────────────────────┘
```

交互：

- 打开时加载当前设置
- 启动后台扫描 Java，显示“扫描中…”并禁用“保存”
- 扫描结果填充下拉框，选中当前 `JavaPath`
- 目录变更后重新扫描 runtime Java
- 保存前校验目录与内存，失败则聚焦到对应控件并提示
- 关闭窗口取消扫描（`CancellationTokenSource.Cancel`）

### 9.3 线程

- 扫描/读取版本在后台线程
- UI 更新统一 `Dispatcher`（`async/await` 回到 UI 上下文即可）
- 禁止 `.Result` / `.Wait()`

---

## 10. 关键流程

### 10.1 启动

```text
AppHost 创建
  └─ SettingsService.Load()
       ├─ 成功 → 规范化 + 校验
       └─ 失败/损坏 → 默认值（JsonStore 已备份）
  └─ MainWindow 显示摘要
```

### 10.2 打开设置

```text
打开 SettingsWindow
  └─ 绑定当前设置
       └─ 异步扫描 Java
            ├─ 读取 release / 回退 java -version
            └─ 去重、排序、推荐
                 └─ 填充下拉框
```

### 10.3 保存

```text
点击保存
  └─ 校验目录
       └─ 校验内存
            └─ SettingsService.TrySave()
                 ├─ 成功 → 触发 Changed → 关闭窗口
                 └─ 失败 → 弹窗 + 日志，保持窗口
```

---

## 11. 任务拆解与提交计划

| # | 内容 | 提交信息建议 |
|---|------|--------------|
| 1 | `LauncherSettings` + `SettingsService` + AppHost 注册 | `feat(P1): add settings model and service` |
| 2 | `JavaVersion` + `JavaVersionParser` + 单测 | `feat(P1): add Java version parsing` |
| 3 | `JavaInfo` + `JavaLocator`（release 优先、进程回退） | `feat(P1): add Java locator and inspection` |
| 4 | `JavaSelector` + `MemoryAdvisor` | `feat(P1): add Java selection and memory advisor` |
| 5 | `SettingsWindow` + 主窗口入口/摘要 | `feat(P1): add settings window and main window integration` |
| 6 | 文档勾选、验收记录 | `docs(P1): update progress and notes` |

建议分支：`feat/p1-settings-java-detection`（阶段内按任务提交，阶段结束统一 PR）。

---

## 12. 风险点

| # | 风险 | 影响 | 概率 | 应对 |
|---|------|------|------|------|
| 1 | Java 版本号格式多样（`1.8.0_392`、`17.0.9+9`、`21-ea`） | 解析失败或排序错误 | 高 | 用表格中的样例做单测；解析失败仅跳过该候选并记录日志 |
| 2 | `release` 文件缺失或字段不全 | 无法判断版本/架构 | 中 | 回退 `java -version`；解析失败时标记 `unknown`，不阻塞其他候选 |
| 3 | `java -version` 进程卡死或输出过多 | UI 卡顿/挂起 | 低 | 5 秒超时 + `Kill(entireProcessTree)` + 异步读取双流 |
| 4 | 扫描 `Program Files` 慢或无权限 | 扫描时间长、异常 | 中 | 限制深度、逐目录捕获异常、后台执行、可取消 |
| 5 | PATH 中含网络路径 / 断开的映射盘 | `File.Exists` 长时间阻塞 | 中 | 跳过 `DriveType.Network` 与 UNC 路径 |
| 6 | 符号链接/目录联接导致循环 | 扫描无限递归 | 低 | 跳过 `ReparsePoint`，限制深度 |
| 7 | 同一 Java 通过多个来源重复发现 | 下拉框重复项 | 高 | `GetFullPath` + 忽略大小写去重 |
| 8 | 32 位 Java 与高内存设置不匹配 | 启动失败/OOM | 中 | 显示架构；x86 时提示内存上限风险，允许手动改 |
| 9 | 注册表访问被拒绝或 32/64 视图差异 | 漏检 | 中 | 同时检查 `Registry64`/`Registry32`，异常捕获后继续 |
| 10 | 用户选错目录（选到父目录/子目录） | 后续版本扫描失败 | 中 | 目录状态分级提示；允许空目录但明确警告 |
| 11 | 路径含中文、空格或超长 | IO 异常 | 中 | 全程 `Path.Combine`/`GetFullPath`；避免手工拼接；记录完整路径 |
| 12 | 设置文件损坏或写入失败 | 设置丢失 | 低 | `JsonStore` 备份 + `TrySave` 错误提示；内存中保留当前值 |
| 13 | 内存建议值不合理（小内存机器） | 卡顿/OOM | 中 | 按 HMCL 公式夹到 256–8192；超过 50% 物理内存时警告 |
| 14 | UI 线程执行扫描/进程读取 | 界面冻结 | 中 | 全部异步；禁止同步等待 |
| 15 | 保存的 `JavaPath` 被删除或移动 | 启动时找不到 Java | 中 | 启动/打开设置时验证；失效则回退自动扫描 |
| 16 | 设置结构后续变更 | 旧配置不兼容 | 低 | `SchemaVersion` + 迁移函数 |
| 17 | 文化差异导致数字解析错误 | 版本比较错误 | 低 | 统一 `InvariantCulture` |
| 18 | 用户指定非 `java.exe` 可执行文件 | 运行非预期程序 | 低 | 允许但提示确认；记录日志；P5 启动前再校验 |

---

## 13. 测试计划

### 13.1 单元测试

- `JavaVersionParser`：覆盖 §4.3 全部样例 + 非法输入
- `JavaVersion.CompareTo`：8 vs 11 vs 17 vs 21；同 major 不同 build
- `MemoryAdvisor`：2/4/8/16/32/64 GB、下限 256、128 对齐、上限 8192
- `SettingsService`：默认值、往返序列化、非法字段纠正、损坏文件恢复
- `JavaSelector`：指定路径优先、精确 major、更高 major、无匹配

### 13.2 手工测试

| 场景 | 预期 |
|------|------|
| 本机无 `JAVA_HOME`/PATH Java | 能发现 Mojang runtime 的 Java 25 |
| 手动选择 `java-runtime-epsilon\bin\java.exe` | 显示 25 (x64) |
| 选择不存在的目录 | 提示目录不存在 |
| 选择空目录 | 允许，提示尚无游戏文件 |
| 选择 `%APPDATA%`（父目录） | 警告未检测到 versions/assets/libraries |
| 保存后重启 | 目录/Java/内存恢复 |
| 扫描期间关闭窗口 | 扫描取消，无异常 |
| 删除已保存的 Java | 打开设置时提示无效并回退 |
| 设置文件写入非法 JSON | 备份 `.corrupt-*`，使用默认值 |

### 13.3 性能目标

- 典型环境 Java 扫描 < 2 秒
- 扫描期间 UI 可拖动/关闭
- 日志包含每个来源的候选数量与耗时

---

## 14. 已确认决策

| # | 事项 | 决定 |
|---|------|------|
| 1 | 设置界面 | 独立 `SettingsWindow`（见 §9） |
| 2 | 注册表扫描 | P1 暂不实现，保留 `JavaSource.Registry` 扩展点（见 §15.1） |
| 3 | 自定义 Java | 允许任意 Java 可执行文件；文件名不是 `java.exe` 时提示确认 |
| 4 | 内存默认 | 采用 HMCL 公式（见 §8.1、§15.2） |
| 5 | 首次启动目录 | `%APPDATA%\.minecraft` 存在时自动填入 |
| 6 | 写权限 | 不做写入测试；首次实际写入失败时提示（见 §6.3、§15.3） |
| 7 | 多 Java 推荐 | 保存路径 → 精确 major → 更高 major（见 §7.4） |

---

## 15. 决策说明

### 15.1 注册表扫描

**作用**

MSI 安装器可以把 JDK 装到自定义目录，且不写 `JAVA_HOME`/`PATH`；注册表是唯一线索。

**参考实现**

- HMCL 会查：
  - `HKLM\SOFTWARE\JavaSoft\Java Runtime Environment`
  - `HKLM\SOFTWARE\JavaSoft\Java Development Kit`
  - `HKLM\SOFTWARE\JavaSoft\JRE`
  - `HKLM\SOFTWARE\JavaSoft\JDK`
- PCL 不查注册表，只扫候选目录 + `JDK_HOME`/`JAVA_HOME`。

**决定**

P1 不实现注册表扫描，理由：

- 已有候选目录 + 环境变量覆盖绝大多数安装方式
- 注册表有 32/64 视图、权限、陈旧项等额外复杂度
- 保留 `JavaSource.Registry` 枚举与扩展点，后续确有漏检再加

### 15.2 内存默认公式

参考见 §16.1。采用 HMCL 的公式（§8.1）：

```text
总内存 ≥ 32 GB → 8192 MB
否则 → max(round(总内存 / 4 / 128) * 128, 256) MB
```

PCL 的方案更精细（可用内存 + 模组数量 + 渐进分配），但依赖版本/模组信息，P1 暂不具备；可在 P3/P4 后升级为按版本/模组数量推荐。

### 15.3 写权限测试

不做，理由见 §6.3。

---

## 16. 参考实现对比

> 参考仓库：HMCL（`HMCL-dev/HMCL`）、PCL（`Meloong-Git/PCL`）当前 `main` 分支。

### 16.1 HMCL

**Java 检测**（`HMCL/.../java/JavaManager.java`）

- 注册表：`HKLM\SOFTWARE\JavaSoft\Java Runtime Environment`、`Java Development Kit`、`JRE`、`JDK`
- `Program Files` / `Program Files (x86)`
- `PATH`
- Mojang 官方 runtime
- 用户手动添加的 Java
- 缓存（`javaCache.json`）
- 选择时按版本约束与平台兼容性筛选

**内存**（`HMCL/.../setting/GameSettings.java`）

```text
totalMemoryMB >= 32768 ? 8192 : max(round(totalMemoryMB / 4 / 128) * 128, 256)
```

- `autoMemory` 默认 `true`
- `maxMemory` 默认使用建议值
- 最小内存默认不设置

### 16.2 PCL

**Java 检测**（`PCLCS/Java.cs`）

- 不查注册表
- 候选目录包括：
  - `.minecraft\runtime`
  - `.hmcl\java`、ATLauncher、ModrinthApp、PrismLauncher、CurseForge、`.jdks`、SDKMAN、FTB
  - Microsoft Store / 官方启动器 runtime
  - `Program Files\Java`、`Eclipse Adoptium`、`Amazon Corretto`、`Zulu`、`Microsoft\jdk-*`
- 环境变量：`JDK_HOME`、`JAVA_HOME`（不扫 `PATH`）
- 排除重解析点与 `javapath`/`system32` 等特殊路径
- 并行 4 路扫描
- 排序：候选目录优先，其次 Java 主版本最接近 21

**内存**（`PageSetupLaunch.xaml.vb` / `PageInstanceSetup.xaml.vb`）

- 自动模式基于**可用物理内存**，并按实例类型和 Mod 数量分阶段分配
- 普通版本目标：最低 0.5 GB，目标 1.5 / 2.5 / 4 GB
- OptiFine：0.5 / 1.5 / 3 / 5 GB
- 可安装 Mod：按 Mod 数量线性增加
- 分配比例：到 T1 100%、T2 70%、T3 40%，再额外 15%
- 手动模式滑条按 0.1 / 0.5 / 1 / 2 GB 分档
- 默认全局自动；实例默认跟随全局
