# 依赖清单与维护说明

> 适用项目：MyMCLauncher
> 更新规则：**每次新增、升级或移除依赖，必须同步更新本文档与「变更记录」**
> 最后更新：P0 完成

---

## 1. 开发环境

| 项目 | 版本 / 说明 |
|------|-------------|
| 操作系统 | Windows 10 / 11（目标平台） |
| .NET SDK | `10.0.401`（本机已安装 `10.0.400`、`10.0.401`） |
| 目标框架 | `net10.0-windows` |
| UI 框架 | WPF（`UseWPF=true`） |
| Git | 版本控制，远程 `https://github.com/NekoJacky/MyMCLauncher` |
| NuGet 源 | 默认 nuget.org（当前网络环境会重定向至 `nuget.azure.cn`） |

更新开发环境后，请同步修改本表。

---

## 2. 工程配置

`MyMCLauncher.csproj` 关键配置：

```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net10.0-windows</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<UseWPF>true</UseWPF>
```

说明：

- `WinExe`：桌面应用，启动时不显示控制台窗口
- `net10.0-windows`：使用 .NET 10 + Windows 桌面运行时
- `Nullable` / `ImplicitUsings`：开启可空引用类型与隐式 using

---

## 3. NuGet 直接依赖

| 包名 | 版本 | 用途 | 引入阶段 | 许可证 | 传递依赖 |
|------|------|------|----------|--------|----------|
| `Microsoft.Web.WebView2` | `1.0.4314.35` | 内嵌 Edge Chromium，用于微软登录兜底 | P0 引入，P6 兜底使用 | Microsoft 专有许可（包内 `LICENSE.txt`） | 无 |

### 3.1 使用方式（P6 预留）

```csharp
using Microsoft.Web.WebView2.Wpf;

// XAML
// <wv2:WebView2 x:Name="WebView" />
```

### 3.2 注意事项

- 版本固定为精确版本，不使用 `*` 或浮动版本
- 该包本身不含浏览器内核，运行期依赖 **WebView2 Runtime**（见 §5）
- 升级前需确认与 `net10.0-windows`、WPF 兼容，并实测登录流程

---

## 4. 框架内置能力（非 NuGet）

以下能力随 `Microsoft.NETCore.App` / `Microsoft.WindowsDesktop.App` 提供，**无需额外包**：

| 命名空间 / 类型 | 用途 | 计划阶段 |
|-----------------|------|----------|
| `System.Text.Json` | 配置、账号、版本 JSON 读写 | P0 / P3 |
| `System.Security.Cryptography.ProtectedData` | DPAPI 加密 token（Windows 桌面运行时已包含） | P2 / P6 |
| `Microsoft.Win32.OpenFolderDialog` | 选择 `.minecraft` 目录 | P1 |
| `System.Diagnostics.Process` / `ProcessStartInfo` | 启动游戏、读取 Java 版本 | P1 / P5 |
| `System.Net.Http` | 微软 / Xbox / Mojang API 请求 | P6 |
| `System.IO.Compression` | 解压 natives jar | P4 |
| `System.Security.Cryptography.MD5` | 生成离线 UUID | P2 |
| `System.Windows.*`（WPF） | 窗口、控件、数据绑定、Dispatcher | P0+ |
| `System.Collections.ObjectModel` | 日志与版本列表绑定 | P0+ |

> 已确认：`System.Security.Cryptography.ProtectedData` 在本机 `Microsoft.WindowsDesktop.App` 引用包中存在，可直接使用，**不需要**安装同名 NuGet 包。

---

## 5. 运行时外部依赖（不随启动器打包）

| 依赖 | 是否必需 | 用途 | 检测 / 处理策略 |
|------|----------|------|-----------------|
| WebView2 Runtime（Evergreen） | 微软登录兜底必需 | 承载登录页 | P6 启动登录前检测；缺失时提示下载安装 |
| Java 运行时 | 启动游戏必需 | 运行 Minecraft | P1 自动扫描 + 手动指定；按版本 `javaVersion` 校验 |
| Minecraft 游戏文件 | M1 必需 | `versions` / `assets` / `libraries` | 用户指定 `.minecraft` 目录；P4 做完整性检查 |
| 网络访问 | 微软登录必需 | `login.live.com` / `xboxlive.com` / `minecraftservices.com` | P6 统一超时与错误提示 |

### 5.1 WebView2 Runtime

- 下载地址：<https://developer.microsoft.com/microsoft-edge/webview2/>
- Windows 11 通常已预装
- 后续可评估打包 Evergreen Bootstrapper / Fixed Version

---

## 6. 依赖检查命令

```bash
# 查看直接与传递依赖
dotnet list package --include-transitive

# 查看 SDK / 运行时信息
dotnet --info

# 还原并构建
dotnet restore
dotnet build
```

`obj/project.assets.json` 为还原产物，已由 `.gitignore` 排除，不提交。

---

## 7. 版本管理策略

1. **精确版本**：`PackageReference` 使用明确版本号，禁止浮动版本
2. **集中管理（暂不启用）**：包数量增多后可引入 `Directory.Packages.props` 统一版本
3. **升级流程**：
   - 修改 `csproj` 版本号
   - `dotnet restore && dotnet build`
   - 运行受影响的完整流程（如 WebView2 → 微软登录）
   - 更新本文档 §3 与 §8
   - 单独提交：`chore(deps): bump <package> to <version>`
4. **最低支持版本**：跟随 .NET 10 生命周期；不使用已 EOL 的运行时

---

## 8. 安全与许可

| 项目 | 说明 |
|------|------|
| WebView2 许可 | Microsoft 专有许可，允许随应用分发运行时；详见包内 `LICENSE.txt` |
| .NET / WPF | MIT 许可 |
| 敏感数据 | 账号 token 使用 DPAPI（`CurrentUser`）加密，禁止明文落盘与写日志 |
| 遥测 | 当前未引入任何遥测或分析 SDK |
| 依赖审计 | 升级依赖时关注安全公告；后续可接入 `dotnet list package --vulnerable` |

---

## 9. 规划中的依赖（尚未引入）

| 依赖 | 用途 | 预计阶段 | 说明 |
|------|------|----------|------|
| `xunit` / `Microsoft.NET.Test.Sdk` / `coverlet.collector` | 单元测试与覆盖率 | P3 前（可选） | 用于规则、参数、继承解析测试 |
| 无其他计划 | — | M1 | 下载、Java 获取等功能优先使用框架内置 `HttpClient` |

> 原则：能使用 BCL 实现的不引入第三方包；引入前评估维护状态、许可证与体积。

---

## 10. 变更记录

| 阶段 | 变更 | 版本 | 说明 |
|------|------|------|------|
| P0 | 新增 `Microsoft.Web.WebView2` | `1.0.4314.35` | 为 P6 微软登录兜底预留；构建通过，无传递依赖 |
