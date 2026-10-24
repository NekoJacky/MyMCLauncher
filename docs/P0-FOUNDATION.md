# P0 实施细化：工程骨架与基础设施

> 配套文档：`docs/M1-WORKFLOW.md`（P0）、`docs/PLAN.md`（§4.1、§4.7）
> 阶段目标：项目可构建、可运行，具备后续所有阶段依赖的公共能力
> 预计工作量：0.5 人日

---

## 1. 阶段目标与边界

### 1.1 目标

P0 结束后，应具备：

1. 干净的 Git 仓库与忽略规则
2. 可成功 restore / build 的工程（含 WebView2 依赖）
3. 统一的目录与命名空间结构
4. 配置目录、日志目录的统一定义与创建
5. 通用 JSON 读写（容错、原子写入）
6. 线程安全日志服务（内存 + 文件 + UI 事件）
7. 全局异常捕获与友好提示
8. 可运行的最小主窗口壳（日志面板 + 状态栏）

### 1.2 非目标（P0 不做）

- 不实现设置项、账号、版本、启动等任何业务逻辑
- 不做正式 UI 设计与视觉样式
- 不接入 WebView2 登录（仅引入包）
- 不做单实例、自动更新、托盘等增强
- 不做下载、Java 检测

### 1.3 交付物一览

| 文件 | 动作 | 说明 |
|------|------|------|
| `.gitignore` | 新增 | 忽略构建产物与 IDE 文件 |
| `MyMCLauncher.csproj` | 修改 | 引入 `Microsoft.Web.WebView2` |
| `App.xaml` | 修改 | 移除 `StartupUri`，改代码启动 |
| `App.xaml.cs` | 修改 | 组合根、全局异常、生命周期 |
| `Models/LogEntry.cs` | 新增 | 日志条目模型 |
| `Services/AppPaths.cs` | 新增 | 路径定义与创建 |
| `Services/JsonStore.cs` | 新增 | JSON 读写与容错 |
| `Services/LogService.cs` | 新增 | 日志服务 |
| `Services/AppHost.cs` | 新增 | 轻量组合根 |
| `MainWindow.xaml` | 修改 | 最小壳：内容区 + 日志 + 状态栏 |
| `MainWindow.xaml.cs` | 修改 | 订阅日志、日志面板操作 |

---

## 2. 前置检查

- [ ] `dotnet --version` 为 `10.0.x`
- [ ] WebView2 Runtime 已安装（开发机已确认）
- [ ] NuGet 源可用（当前环境会重定向至 `nuget.azure.cn`，以实际 restore 为准）
- [ ] 确认当前工程可构建：`dotnet build`

---

## 3. 工作拆解（按执行顺序）

### 3.1 版本控制初始化

**做什么**

- [ ] `git init`
- [ ] 新增 `.gitignore`
- [ ] 首次提交（仅现有脚手架 + `.gitignore`）

**`.gitignore` 内容**

```gitignore
bin/
obj/
.vs/
*.user
*.suo
*.userosscache
*.sln.docstates
TestResults/
```

> 说明：`logs/` 位于 `%APPDATA%`，不在仓库内，无需忽略。

**验收**：`git status` 干净，`bin/`、`obj/`、`.vs/` 不出现。

---

### 3.2 引入依赖

**做什么**

- [ ] `MyMCLauncher.csproj` 添加：

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.4314.35" />
</ItemGroup>
```

- [ ] `dotnet restore`（或直接 `dotnet build`）
- [ ] 确认 `obj/project.assets.json` 中包含该包

**注意**

- 当前 NuGet 请求可能被重定向到国内镜像，首次 restore 可能较慢
- 若 restore 失败：优先排查网络/源，不要急着降级包
- P0 只引入包，不写任何 WebView2 代码

**验收**：`dotnet build` 成功，包出现在依赖树中。

---

### 3.3 目录与命名空间

**做什么**

- [ ] 创建目录：`Models/`、`Services/`、`Services/Auth/`、`Minecraft/`、`Views/`、`Assets/`
- [ ] 统一命名空间：
  - `MyMCLauncher.Models`
  - `MyMCLauncher.Services`
  - `MyMCLauncher.Services.Auth`
  - `MyMCLauncher.Minecraft`
  - `MyMCLauncher.Views`

**约定**

- 文件名 = 类名，一个文件一个公共类型
- 服务类以 `Service` 结尾（`LogService`、`SettingsService`）
- 纯数据模型以名词结尾（`Account`、`VersionJson`）
- 不引入 MVVM 框架；事件与数据绑定使用 WPF 原生能力

**验收**：目录存在且被编译（可先放占位类或后续阶段填充）。

---

### 3.4 `AppPaths`

**职责**：集中定义运行期路径，避免散落的字符串拼接。

**接口设计**

```csharp
namespace MyMCLauncher.Services;

public sealed class AppPaths
{
    public string Root { get; }              // 默认 %APPDATA%\MyMCLauncher
    public string LogsDirectory { get; }     // Root\logs
    public string NativesDirectory { get; }  // Root\natives
    public string WebView2DataDirectory { get; } // Root\webview2
    public string SettingsFile { get; }      // Root\settings.json
    public string AccountsFile { get; }      // Root\accounts.json

    public AppPaths(string? root = null);
    public void EnsureCreated();
}
```

**实现要点**

- 默认根目录：`Environment.GetFolderPath(SpecialFolder.ApplicationData)` + `MyMCLauncher`
- 支持环境变量覆盖（便于测试）：`MYMCLAUNCHER_HOME`
- 全部使用 `Path.Combine`，兼容中文/空格路径
- `EnsureCreated()` 创建 `Root`、`LogsDirectory`、`NativesDirectory`、`WebView2DataDirectory`
- 创建失败时抛出带路径信息的异常，由 `AppHost` 记录日志并提示

**验收**

- 首次启动后上述目录自动创建
- 设置 `MYMCLAUNCHER_HOME` 后，路径随之切换（手工验证）

---

### 3.5 `JsonStore`

**职责**：统一的 JSON 读写入口，处理损坏文件与原子写入。

**接口设计**

```csharp
namespace MyMCLauncher.Services;

public sealed class JsonStore
{
    // 文件不存在或损坏时返回 defaultFactory()（默认 default）
    public T Load<T>(string path, Func<T>? defaultFactory = null);

    // 原子写入：临时文件 + 覆盖移动
    public void Save<T>(string path, T value);

    public bool TryLoad<T>(string path, out T? value);
}
```

**实现要点**

- `JsonSerializerOptions`：
  - `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
  - `PropertyNameCaseInsensitive = true`
  - `WriteIndented = true`
  - `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`
  - `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`（中文可读）
- 原子写入：
  1. 写入 `path + ".tmp"`
  2. `File.Move(tmp, path, overwrite: true)`
  3. 异常时清理临时文件
- 损坏处理：
  - 捕获 `JsonException`
  - 将原文件重命名为 `path + ".corrupt-yyyyMMddHHmmss"`
  - 记录警告日志（P0 可先 `Debug.WriteLine`，由 `AppHost` 注入 `LogService` 后改为正式日志）
  - 返回默认值
- 目录不存在时自动创建
- 不做并发写保护（P0 单线程使用）；若需要，可用 `lock` 或 `SemaphoreSlim`，在 P1 再评估

**验收**

- 正常对象可保存并读回
- 手工写入非法 JSON 后：程序不崩溃、原文件被备份、返回默认值
- 保存过程中不产生半截文件（观察 `.tmp` 行为）

---

### 3.6 `LogService`

**职责**：线程安全的日志记录，供业务、UI、文件共用。

**接口设计**

```csharp
namespace MyMCLauncher.Services;

public enum LogLevel { Debug, Info, Warn, Error }

public sealed class LogEntry
{
    public DateTime Time { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; }
    public string? Exception { get; init; }
}

public sealed class LogService : IDisposable
{
    public event Action<LogEntry>? LogEmitted;
    public IReadOnlyList<LogEntry> Recent { get; }
    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public LogService(string logsDirectory);
    public void Debug(string message);
    public void Info(string message);
    public void Warn(string message, Exception? ex = null);
    public void Error(string message, Exception? ex = null);
    public void Log(LogLevel level, string message, Exception? ex = null);
    public void Flush();
    public void Dispose();
}
```

**实现要点**

- 线程安全：`lock` 保护内存队列与文件写入
- 内存：`Queue<LogEntry>`，上限 1000 条，超出丢弃最旧
- 文件：`logsDirectory/launcher-yyyyMMdd.log`
  - 按天切换（写入时检查日期）
  - `StreamWriter` 使用 `new UTF8Encoding(false)`、`AutoFlush = true`、`FileShare.ReadWrite`
  - 写入格式：`[yyyy-MM-dd HH:mm:ss.fff] [INFO ] message`
  - 异常附加换行后的 `ToString()`
- `LogEmitted` 在锁外触发，避免 UI 处理时长时间持锁
- `Dispose`/`Flush` 确保退出前落盘
- 文件打开失败（被占用/无权限）时：降级为仅内存日志，并尝试写到备用文件一次

**验收**

- UI 与文件都能看到日志
- 多线程同时写日志不抛异常、不丢内容（可临时用 `Parallel.For` 验证）
- 跨天时生成新文件（可临时改系统日期或缩短验证周期，非必需）

---

### 3.7 全局异常处理

**职责**：兜底捕获异常，记录并提示，避免静默退出。

**做什么**

- [ ] `App.OnStartup` 注册：
  - `DispatcherUnhandledException`
  - `AppDomain.CurrentDomain.UnhandledException`
  - `TaskScheduler.UnobservedTaskException`
- [ ] 统一处理逻辑：
  1. `LogService.Error(...)` 写入异常
  2. `Flush()`
  3. `MessageBox.Show` 展示简要信息 + 日志路径
  4. UI 线程异常：标记 `Handled = true` 后按需 `Shutdown(1)`（建议直接退出，避免状态不一致）
  5. 非 UI 线程异常：记录后由进程终止
  6. `UnobservedTaskException`：记录并 `SetObserved()`

**注意**

- 异常处理本身不能再抛异常（`try/catch` 包裹日志与弹窗）
- `MessageBox` 需在 UI 线程调用；非 UI 线程使用 `Dispatcher.Invoke`
- 不要在异常处理里访问尚未初始化的服务

**验收**

- 临时抛出一个未处理异常：弹窗出现、日志有记录、程序按预期退出
- 在 `Task` 中抛出未观察异常：日志有记录且不导致进程崩溃

---

### 3.8 组合根 `AppHost`

**职责**：集中创建并持有服务实例，供 UI 与后续阶段使用。

**接口设计**

```csharp
namespace MyMCLauncher.Services;

public sealed class AppHost : IDisposable
{
    public AppPaths Paths { get; }
    public JsonStore Json { get; }
    public LogService Log { get; }
    public static AppHost Current { get; private set; } = null!;

    public AppHost();
    public void Dispose();
}
```

**实现要点**

- 构造顺序：`Paths` → `EnsureCreated()` → `Json` → `Log`
- 创建后写一条启动日志：版本、路径、OS、架构
- `Current` 在 `App.OnStartup` 中赋值
- P0 只暴露上述三个服务；P1/P2 再追加 `Settings`、`Accounts`

**验收**：`AppHost.Current` 可在主窗口访问，日志中能看到初始化信息。

---

### 3.9 应用启动与主窗口最小壳

**App.xaml**

- 移除 `StartupUri="MainWindow.xaml"`
- 保留 `Application.Resources`（后续放样式）

**App.xaml.cs**

```text
OnStartup:
  - 创建 AppHost，赋值 Current
  - 注册全局异常
  - new MainWindow(AppHost.Current) → Show()
OnExit:
  - 记录退出日志
  - AppHost.Dispose()（Flush 日志）
```

**MainWindow**

- 标题 `MyMCLauncher`，`Width=1100`、`Height=700`，`MinWidth=900`、`MinHeight=600`
- `WindowStartupLocation=CenterScreen`
- 布局：

```text
┌─────────────────────────────────────────────┐
│ 内容区（占位：后续放账号/目录/版本/启动）      │
├─────────────────────────────────────────────┤
│ 日志面板（ListBox，可滚动，上限 1000 条）      │
│  [清空] [复制] [打开日志目录]                 │
├─────────────────────────────────────────────┤
│ 状态栏：就绪 / 当前阶段信息                    │
└─────────────────────────────────────────────┘
```

**MainWindow.xaml.cs**

- 构造函数接收 `AppHost`
- 订阅 `Log.LogEmitted`，通过 `Dispatcher.BeginInvoke` 追加到 `ObservableCollection<LogEntry>`
- 超过上限时移除最旧项，避免内存增长
- 关闭时取消订阅
- “打开日志目录”：`Process.Start("explorer.exe", logsDir)`
- “复制”：把当前日志文本复制到剪贴板
- 保留无参构造函数供设计器使用：`public MainWindow() : this(AppHost.Current) { }`（需处理 `Current` 未初始化的情况）

**验收**

- 启动后窗口正常显示
- 日志面板实时出现启动日志
- 三个按钮可用

---

### 3.10 可选：测试项目骨架（建议但不阻塞）

- [ ] 新建 `MyMCLauncher.Tests`（xUnit）
- [ ] 覆盖 `AppPaths` 路径拼接、`JsonStore` 容错/原子写入、`LogService` 并发写入

> 若 P0 时间紧，可推迟到 P3 前，与规则/参数解析的测试一起补。

---

## 4. 启动后的目录与日志形态

```text
%APPDATA%\MyMCLauncher\
├─ logs\
│  └─ launcher-20250101.log
├─ natives\
├─ webview2\
├─ settings.json        # P1 产生
└─ accounts.json        # P2 产生
```

日志示例：

```text
[2025-01-01 12:00:00.123] [INFO ] MyMCLauncher 启动
[2025-01-01 12:00:00.124] [INFO ] Root = C:\Users\<user>\AppData\Roaming\MyMCLauncher
[2025-01-01 12:00:00.124] [INFO ] OS = Microsoft Windows 10.0.26100, x64
[2025-01-01 12:00:00.200] [INFO ] 主窗口已显示
```

---

## 5. 自测清单（P0 门禁）

- [ ] `dotnet build` 0 error
- [ ] 启动后 4 个目录自动创建
- [ ] 日志文件生成，内容包含启动信息
- [ ] 日志面板实时显示且不卡顿
- [ ] 日志超过 1000 条时旧条目被裁剪
- [ ] 非法 JSON 文件被备份为 `.corrupt-*`，程序继续运行
- [ ] 人为抛出 UI 异常：弹窗 + 日志 + 退出
- [ ] 人为抛出 Task 异常：日志记录，进程不崩溃
- [ ] 关闭程序后日志已落盘
- [ ] `git status` 干净（构建产物未入库）

---

## 6. 风险与应对

| 风险 | 影响 | 应对 |
|------|------|------|
| WebView2 包 restore 慢/失败 | 阻塞 P0 | 切换 NuGet 源；必要时 P0 先不加包，P6 前补 |
| 日志文件被外部程序占用 | 写入失败 | `FileShare.ReadWrite` + 失败降级仅内存 |
| 日志无限增长 | 磁盘占用 | 按天滚动；后续可加保留天数清理 |
| 非 UI 线程更新界面 | 崩溃 | 统一 `Dispatcher.BeginInvoke` |
| 配置目录无写权限 | 启动失败 | 明确错误提示并给出路径；支持 `MYMCLAUNCHER_HOME` 覆盖 |
| 异常处理中再次抛异常 | 无法诊断 | 异常处理内部 `try/catch` 全包裹 |
| 中文/空格路径 | 文件访问异常 | 全程 `Path.Combine`，`ArgumentList`（P4/P5 使用） |

---

## 7. 提交与交接

**建议提交信息**

```text
chore(P0): bootstrap project skeleton, paths, json store, logging and global error handling
```

**P1 可直接使用的能力**

- `AppHost.Current.Paths`：游戏目录、配置路径
- `AppHost.Current.Json`：`settings.json` 读写
- `AppHost.Current.Log`：全流程日志
- 主窗口内容区已预留，可直接放入设置控件

**P0 完成标志**

P0 自测清单全部通过，且 `docs/PLAN.md` §4.1 对应任务已勾选。
