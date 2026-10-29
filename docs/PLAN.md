# MyMCLauncher 开发规划文档

> 版本：v0.5
> 状态：M1 进行中（P0 主体完成，WebView2 依赖待合并）
> 范围：M1 启动功能（正版/离线登录、游戏目录选择、启动本地已安装版本）
> 配套文档：`docs/M1-WORKFLOW.md`（M1 实施流程规划）
> 登录方案：系统浏览器 + 本地回环（默认），WebView2 兜底

---

## 1. 项目现状

| 项目 | 说明 |
|------|------|
| 项目类型 | WPF 桌面应用（`WinExe`） |
| 目标框架 | `net10.0-windows` |
| 语言特性 | C#、`Nullable`、`ImplicitUsings` 开启 |
| 现有文件 | WPF 入口 + `Models/`、`Services/`（AppPaths / JsonStore / LogService / AppHost）+ `docs/` |
| 开发环境 | .NET SDK 10.0.401，WebView2 Runtime 已安装 |
| 本地 Minecraft | `%APPDATA%\.minecraft` 仅有 `runtime`（无 `versions`/`assets`/`libraries`） |
| 本地 Java | 无全局 Java；仅有 Mojang 运行时 `runtime/java-runtime-epsilon`（OpenJDK 25） |

**结论**：P0 基础设施主体已完成，进入 P1（设置、游戏目录、Java 检测）。

---

## 2. 目标与里程碑

### 2.1 M1（本期）— 可启动本地游戏

- 离线登录、微软正版登录
- 选择 `.minecraft` 游戏目录
- 扫描并选择本地已安装版本
- 构建 classpath / 参数并启动游戏
- 启动前完整性检查，缺失文件时给出明确清单
- 基础设置（Java 路径、内存）与日志

**非目标（M1 不做）**：任何下载功能、Java 自动安装、加载器安装、模组管理、版本隔离、启动器自动更新。

### 2.2 后续里程碑

| 里程碑 | 内容 |
|--------|------|
| M2 | 在线版本清单、游戏文件下载（client / libraries / assets / logging）、进度与校验 |
| M3 | Java 运行时自动获取、Forge/Fabric/NeoForge/Quilt 安装 |
| M4 | 模组与整合包管理、版本隔离、崩溃分析、皮肤展示、自动更新 |

---

## 3. 总体架构

### 3.1 设计原则

1. **界面与逻辑分离**：使用 WPF 原生框架（XAML + code-behind + 原生数据绑定），不引入 MVVM 库或第三方 UI 框架。
2. **服务化**：登录、版本解析、启动各成独立服务，便于单测与后续替换。
3. **下载可插拔**：M1 只做“检查 + 报告缺失”，M2 在同样的检查结果上挂下载器，避免返工。
4. **可诊断**：所有关键步骤写日志，启动失败必须能定位到具体文件/参数。

### 3.2 目录规划

```text
MyMCLauncher/
├─ App.xaml / App.xaml.cs
├─ MainWindow.xaml / MainWindow.xaml.cs
├─ MyMCLauncher.csproj
├─ Models/
│  ├─ Account.cs
│  ├─ LauncherSettings.cs
│  ├─ VersionJson.cs          # 原始版本 JSON 映射
│  └─ ResolvedVersion.cs      # 继承合并后的版本
├─ Services/
│  ├─ AppPaths.cs
│  ├─ JsonStore.cs
│  ├─ LogService.cs
│  ├─ SettingsService.cs
│  ├─ AccountService.cs
│  └─ Auth/
│     ├─ OfflineAuthService.cs
│     ├─ MicrosoftAuthService.cs
│     └─ XboxAuthClient.cs
├─ Minecraft/
│  ├─ VersionScanner.cs
│  ├─ VersionResolver.cs
│  ├─ RuleEvaluator.cs
│  ├─ LibraryResolver.cs
│  ├─ NativeExtractor.cs
│  ├─ ArgumentBuilder.cs
│  ├─ JavaLocator.cs
│  ├─ LaunchValidator.cs
│  └─ GameLauncher.cs
├─ Views/
│  ├─ AddAccountWindow.xaml / .cs
│  └─ MicrosoftLoginWindow.xaml / .cs
└─ Assets/                    # 图标、样式资源
```

### 3.3 依赖

| 依赖 | 用途 | 说明 |
|------|------|------|
| `Microsoft.Web.WebView2` | 微软登录兜底（默认使用系统浏览器 + 本地回环） | 建议 `1.0.4314.x`；运行时缺失只影响兜底方案 |
| `System.Text.Json` | JSON 序列化 | 内置 |
| `System.Security.Cryptography.ProtectedData` | DPAPI 加密 token | 内置（Windows） |

---

## 4. M1 详细任务清单

### 4.1 基础设施

- [ ] 引入 `Microsoft.Web.WebView2` 包
- [x] `AppPaths`：配置目录 `%APPDATA%\MyMCLauncher\`、日志目录 `logs\`
- [x] `JsonStore`：统一读写 + 容错（损坏时备份并重建）
- [x] `LogService`：内存环形日志 + 落盘 + UI 实时推送
- [x] 全局异常捕获（`DispatcherUnhandledException` / `TaskScheduler.UnobservedTaskException`）
- [x] 使用 WPF 原生事件处理与数据绑定（不引入 MVVM 框架）

> 说明：`Microsoft.Web.WebView2` 依赖改动位于 `feat/引入依赖` 分支，待 P6 使用前合并。

### 4.2 设置与路径

- [ ] 游戏目录选择（`Microsoft.Win32.OpenFolderDialog`）
- [ ] 目录合法性校验（是否含 `versions`/`assets`/`libraries`，允许空目录并提示）
- [ ] 设置持久化（见 §6.1）
- [ ] Java 检测：
  - [ ] 环境变量 `JAVA_HOME`、`PATH`
  - [ ] 常见安装目录（`Program Files\Java`、Adoptium、Microsoft、Zulu 等）
  - [ ] 游戏目录下 `runtime/java-runtime-*/bin/java.exe`
  - [ ] 读取版本（优先 `release` 文件，兜底 `java -version`）
  - [ ] 手动指定 `java.exe`
  - [ ] 按 `javaVersion.majorVersion` 自动匹配，版本不符时警告并允许强制启动
- [ ] 内存设置（默认自动：物理内存 1/4，限制在 1024–8192 MB）

### 4.3 账号系统

#### 4.3.1 离线登录

- [ ] 用户名输入与校验（长度、非空、不允许空白）
- [ ] 生成离线 UUID：`MD5("OfflinePlayer:" + name)` → version‑3 UUID
- [ ] 构造离线会话：`accessToken="0"`、`userType="legacy"`、`xuid="0"`
- [ ] 账号持久化

#### 4.3.2 微软正版登录

- [ ] `IAuthorizationCodeProvider` 抽象（默认回环 / 兜底 WebView2）
- [ ] 系统浏览器 + 本地回环（默认）：
  - [ ] PKCE（S256）+ `state`
  - [ ] `HttpListener` 监听 `http://localhost:<port>/callback`
  - [ ] 打开系统默认浏览器，捕获授权码
- [ ] WebView2 兜底：加载同一授权页并提取授权码
- [ ] 令牌链：
  1. `login.microsoftonline.com/consumers/oauth2/v2.0/authorize` → 授权码
  2. `login.microsoftonline.com/consumers/oauth2/v2.0/token` → MSA access / refresh token
  3. `user.auth.xboxlive.com/user/authenticate` → XBL Token
  4. `xsts.auth.xboxlive.com/xsts/authorize` → XSTS Token + UHS
  5. `api.minecraftservices.com/authentication/login_with_xbox` → Minecraft Token
  6. `api.minecraftservices.com/minecraft/profile` → 玩家名 / UUID / 皮肤
- [ ] Refresh Token 刷新；启动前 token 过期自动续期
- [ ] 错误处理：
  - 未购买游戏（profile 404）
  - 无 Xbox 账号（`2148916233`）
  - 地区不支持（`2148916235`）
  - 儿童/家庭账号（`2148916238`）
  - 网络错误、回环失败、WebView2 运行时缺失（兜底）
- [ ] 账号管理：列表、切换、删除、记住上次账号
- [ ] 敏感字段 DPAPI 加密存储（`CurrentUser` 范围）

### 4.4 版本管理（仅本地读取）

- [ ] 扫描 `versions/*/*.json`
- [ ] 解析 `id`、`type`、`releaseTime`、`inheritsFrom`
- [ ] 展示版本列表，标记“已安装”，支持按时间/名称排序
- [ ] 加载失败（JSON 损坏）不影响其他版本
- [ ] 继承合并（`inheritsFrom`）：
  - [ ] 子版本优先：`mainClass`、`assets`、`assetIndex`、`downloads`、`javaVersion`、`minecraftArguments`
  - [ ] `libraries`：子版本在前、父版本追加，按 `group:artifact[:classifier]` 去重
  - [ ] `arguments.game` / `arguments.jvm`：父版本在前、子版本追加
  - [ ] 递归处理多级继承，检测循环继承
- [ ] 识别加载器类型（原版 / Forge / Fabric / NeoForge / Quilt）用于展示

### 4.5 启动核心

- [ ] 规则判断（`RuleEvaluator`）：
  - [ ] `os.name` / `os.arch` / `os.version`
  - [ ] `features`（默认全部 `false`，预留 demo/自定义分辨率等）
  - [ ] 语义：有 `rules` 时初始 `false`，按顺序应用“最后匹配规则”
- [ ] `LibraryResolver`：
  - [ ] 解析 `group:artifact:version[:classifier]` → Maven 相对路径
  - [ ] 优先使用 `downloads.artifact.path`，否则自行推导
  - [ ] 生成 classpath（分号分隔），去重并保持顺序
- [ ] Natives：
  - [ ] 旧版本（有 `natives` 字段）：解压分类器 jar 到临时目录，过滤 `META-INF` 与 `extract.exclude`
  - [ ] 新版本（1.19+ 独立 `natives-*` 分类器库）：加入 classpath，依赖 LWJGL 自解压
  - [ ] 准备 `natives_directory` 并传入 JVM 参数
- [ ] 启动前完整性检查（`LaunchValidator`），输出缺失清单：
  - [ ] `versions/<id>/<id>.jar`
  - [ ] `assets/indexes/<assetIndex>.json`
  - [ ] `assets/objects/**`（按索引抽样/全量校验）
  - [ ] `libraries/**` 所需 jar
  - [ ] 结论：可启动 / 缺文件（M2 将提供下载）
- [ ] 参数构建（`ArgumentBuilder`）：
  - [ ] 新格式 `arguments.jvm` / `arguments.game`
  - [ ] 旧格式 `minecraftArguments` + 默认 JVM 参数
  - [ ] 变量替换（见 §7.2）
  - [ ] 使用 `ProcessStartInfo.ArgumentList` 传参，避免引号/空格问题
- [ ] 启动进程（`GameLauncher`）：
  - [ ] 工作目录、`RedirectStandardOutput/Error`、隐藏窗口
  - [ ] 日志实时回传 UI
  - [ ] 退出码与异常提示、崩溃日志路径提示
  - [ ] 启动期间禁用启动按钮，支持“终止游戏”

### 4.6 界面

- [ ] 主窗口：
  - [ ] 顶部/侧边：账号卡片（头像/名字/类型）、切换与添加账号
  - [ ] 游戏目录选择、Java 选择、内存设置
  - [ ] 版本列表（名称、类型、加载器、时间）
  - [ ] 启动按钮、状态栏
  - [ ] 日志面板（可折叠/滚动）
- [ ] 添加账号窗口：离线登录 / 微软登录入口 / 已有账号列表
- [ ] 微软登录：系统浏览器（默认）/ WebView2 兜底窗口 + 加载/错误状态
- [ ] 基础视觉样式统一（颜色、字体、圆角、按钮状态）

### 4.7 实施阶段划分（P0–P7）

M1 按 8 个阶段推进，每个阶段要求「可构建、可验证、可回退」。详细拆解见 `docs/M1-WORKFLOW.md`；阶段细化文档：P0 → `docs/P0-FOUNDATION.md`。

> 进度：P0 主体完成（WebView2 依赖待合并），P1 未开始。

| 阶段 | 内容 | 主要产出 | 对应任务 | 依赖 |
|------|------|----------|----------|------|
| P0 | 工程骨架与基础设施 | 依赖、目录、配置/日志/异常 | §4.1 | — |
| P1 | 设置、游戏目录、Java 检测 | 设置持久化、Java 列表与匹配 | §4.2 | P0 |
| P2 | 离线账号与账号管理 | 离线登录、账号增删切换 | §4.3.1、§4.6 | P0、P1 |
| P3 | 版本扫描与继承解析 | 版本列表、合并后的版本模型 | §4.4 | P1 |
| P4 | 启动构建与完整性检查 | classpath、natives、参数、缺失清单 | §4.5 | P1、P2、P3 |
| P5 | 启动执行与日志 | 真实启动游戏、日志回传 | §4.5（进程部分）、§4.6 | P4 |
| P6 | 微软正版登录 | 系统浏览器 + 回环登录、令牌链、刷新（WebView2 兜底） | §4.3.2、§4.6 | P0、P1（可与 P2–P5 并行） |
| P7 | 界面整合与打磨 | 主界面、错误提示、验收 | §4.6、§10 | P1–P6 |

```text
P0 工程骨架
 │
 ├─► P1 设置/目录/Java ──────────────┐
 │                                   │
 ├─► P2 离线账号 ─────────────────┐  │
 │                                │  │
 └─► P3 版本扫描/继承解析 ──┐    │  │
                            │    │  │
                            ▼    ▼  ▼
                         P4 启动构建
                            │
                            ▼
                         P5 启动执行（离线端到端）
                            │
                            ▼
                         P6 微软登录
                            │
                            ▼
                         P7 整合打磨 → M1 验收
```

**阶段退出条件（摘要）**

- **P0**：`dotnet build` 通过；配置/日志目录正确生成；全局异常可提示并落盘
- **P1**：目录/Java/内存设置可持久化；能识别 `runtime` 下的 Java 并给出路径+版本
- **P2**：离线账号可增删切换；相同用户名 UUID 稳定；token 非明文
- **P3**：至少 1 个原版 + 1 个加载器版本解析通过；循环继承/损坏 JSON 不崩溃
- **P4**：完整版本缺失清单为空；人为删文件能准确列出缺失项；命令行可人工核对
- **P5**：离线登录 + 本地完整版本成功进入游戏；日志实时可见；可终止游戏
- **P6**：微软账号登录/刷新/启动成功；错误码有可读提示
- **P7**：PLAN §10 验收标准全部通过；首次使用流程可走通

**里程碑检查点**

| 检查点 | 条件 | 意义 |
|--------|------|------|
| M1‑Alpha | P0–P5 完成 | 离线核心链路打通 |
| M1‑Beta | P6 完成 | 正版能力打通 |
| M1‑RC | P7 完成 | 验收标准全部通过 |
| M1‑Done | 文档更新、已知问题记录 | 阶段关闭 |

**关键约束**

- P4 复杂度最高，建议先原版、后 Forge/Fabric，并预留最多时间
- P6 可提前开发，但联调放在 P5 之后，避免同时排查登录与启动问题
- 测试数据优先使用现有其他启动器的游戏目录（当前 `%APPDATA%\.minecraft` 缺少 `versions/assets/libraries`）

---

## 5. 关键流程

### 5.1 微软登录流程

**默认：系统浏览器 + 本地回环**

```text
用户点击“微软登录”
  └─ 生成 PKCE（code_verifier / code_challenge）与 state
       └─ 启动本地 HttpListener（http://localhost:<port>/callback）
            └─ 打开系统默认浏览器 → Microsoft 授权页
                 （consumers 端点，client_id + PKCE + redirect_uri）
                 └─ 用户登录并同意
                      └─ 浏览器重定向到 http://localhost:<port>/callback?code=...
                           └─ HttpListener 捕获 code，校验 state
                                └─ token 端点换取 MSA access/refresh token
                                     └─ XBL 认证 → XSTS 认证
                                          └─ Minecraft 登录 → 获取 Minecraft Token
                                               └─ 获取 profile（名称/UUID）
                                                    └─ 保存账号（DPAPI 加密）
```

**兜底：内嵌 WebView2**

```text
系统浏览器不可用 / 无法本地回环 / 用户选择兜底
  └─ 打开 WebView2 加载同一授权页
       └─ 拦截跳转或捕获回环回调，提取 code
            └─ 后续令牌链与默认方案完全一致
```

### 5.2 启动流程

```text
选择版本 + 账号
  └─ 校验设置（目录、Java）
       └─ 账号 token 校验/刷新
            └─ 读取版本 JSON → 解析继承 → 合并
                 └─ 规则求值 → 解析 libraries → 生成 classpath / natives
                      └─ 完整性检查
                           ├─ 缺失 → 展示缺失清单，结束
                           └─ 完整 → 构建 JVM/游戏参数
                                └─ 启动 java 进程 → 日志回传 → 结束/退出码
```

---

## 6. 数据与配置格式

### 6.1 `settings.json`

```json
{
  "gameDirectory": "C:\\Users\\<user>\\AppData\\Roaming\\.minecraft",
  "javaPath": "",
  "autoMemory": true,
  "memoryMb": 4096,
  "lastVersionId": "1.20.1",
  "lastAccountId": "offline:Steve"
}
```

### 6.2 `accounts.json`

```json
[
  {
    "id": "microsoft:<uuid>",
    "type": "microsoft",
    "name": "Steve",
    "uuid": "0123456789abcdef0123456789abcdef",
    "userType": "msa",
    "accessToken": "<DPAPI 加密>",
    "refreshToken": "<DPAPI 加密>",
    "xuid": "2535xxxxxxxxxxxx",
    "expiresAt": "2026-01-01T00:00:00Z",
    "skinUrl": ""
  },
  {
    "id": "offline:Steve",
    "type": "offline",
    "name": "Steve",
    "uuid": "<离线 UUID（无连字符）>",
    "userType": "legacy",
    "accessToken": "0",
    "xuid": "0"
  }
]
```

### 6.3 运行时目录

```text
%APPDATA%\MyMCLauncher\
├─ settings.json
├─ accounts.json
├─ logs\
│  └─ launcher-YYYYMMDD.log
└─ natives\<versionId>\      # 旧版本 natives 解压目录
```

---

## 7. 技术备忘

### 7.1 微软登录端点

| 步骤 | 端点 |
|------|------|
| 授权 | `https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize` |
| 换 token | `https://login.microsoftonline.com/consumers/oauth2/v2.0/token` |
| XBL | `https://user.auth.xboxlive.com/user/authenticate` |
| XSTS | `https://xsts.auth.xboxlive.com/xsts/authorize` |
| MC 登录 | `https://api.minecraftservices.com/authentication/login_with_xbox` |
| MC 资料 | `https://api.minecraftservices.com/minecraft/profile` |

**Azure 应用配置（公开信息）**

- `client_id`：`cbca0412-33b7-40de-b845-12e8c453b816`
- 账户类型：仅个人 Microsoft 账户 → `consumers` 端点（若为多租户 + 个人，则改用 `common`）
- `redirect_uri`：`http://localhost:<port>/callback`（注册为 `http://localhost`，允许任意本地端口）
- `scope`：`XboxLive.signin offline_access openid profile`
- `PKCE`：`S256`；`state`：随机生成并校验
- 公共客户端，不保存 client secret

- XSTS 请求：`SandboxId="RETAIL"`，`RelyingParty="rp://api.minecraftservices.com/"`，`TokenType="JWT"`
- MC 登录 `identityToken`：`"XBL3.0 x=<uhs>;<xstsToken>"`
- XBL `RpsTicket` 对 MSA 需要 `"d="` 前缀；失败时回退为原始 token
- Minecraft Token 约 24 小时有效；过期用 refresh token 续期后重走 XBL/XSTS/MC

### 7.2 参数变量

| 变量 | 来源 |
|------|------|
| `${auth_player_name}` | 账号名 |
| `${version_name}` | 版本 id |
| `${game_directory}` | 游戏目录 |
| `${assets_root}` | `<gameDir>/assets` |
| `${assets_index_name}` | `assetIndex.id` |
| `${auth_uuid}` | UUID（无连字符） |
| `${auth_access_token}` | 账号 token |
| `${user_type}` | `msa` / `legacy` |
| `${auth_xuid}` | XUID（离线为 `0`） |
| `${version_type}` | 版本 `type` |
| `${user_properties}` | `{}` |
| `${natives_directory}` | natives 解压目录 |
| `${classpath}` / `${classpath_separator}` | 依赖路径 / `;` |
| `${library_directory}` | `<gameDir>/libraries` |
| `${launcher_name}` / `${launcher_version}` | 固定值 |
| `${game_assets}` | 旧版本资源目录（兼容） |

### 7.3 版本差异

| 版本区间 | 注意点 |
|----------|--------|
| ≤ 1.12 | 使用 `minecraftArguments`；JVM 参数需自行补 `-Djava.library.path`、`-cp` |
| 1.13+ | 使用 `arguments.jvm` / `arguments.game` |
| ≤ 1.18 | natives 通过 `natives` 字段 + `downloads.classifiers` 解压 |
| 1.19+ | natives 以独立分类器库存在，进 classpath，由 LWJGL 自解压 |
| 1.20.5+ | 要求 Java 21+（按 `javaVersion.majorVersion` 判断） |

---

## 8. 风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| Azure 应用配置错误或 `client_id` 变更 | 微软登录不可用 | `client_id` / `redirect_uri` 集中配置，登录前自检并记录脱敏日志 |
| 本地回环端口被占用 / 被安全软件拦截 | 无法接收授权回调 | 使用动态端口；失败时提示并回退 WebView2 |
| 国内访问 `login.microsoftonline.com` / Xbox API 不稳定 | 登录失败/超时 | 明确错误提示、超时与重试、代理设置预留 |
| WebView2 Runtime 缺失 | 兜底方案不可用 | 默认系统浏览器流程不受影响；仍检测并提示安装链接 |
| Token 明文存储 | 账号泄露 | DPAPI（CurrentUser）加密 |
| 版本 JSON 差异大（Forge/Fabric 等） | 启动失败 | 以官方合并规则为准，覆盖多级继承与规则求值测试 |
| Java 版本不匹配 | 启动即崩 | 读 `javaVersion` 校验，警告并允许强制启动 |
| 本地文件不完整 | 启动失败且原因不明 | 启动前完整性检查，输出缺失清单 |
| 默认内存过大/过小 | 卡顿或 OOM | 自动按物理内存 1/4 取值并限制范围 |

---

## 9. 已确认决策

| # | 事项 | 决定 |
|---|------|------|
| 1 | 微软登录方式与 client_id | 自注册 Azure 应用 `cbca0412-33b7-40de-b845-12e8c453b816`（仅个人账户）；默认系统浏览器 + 本地回环 + PKCE，WebView2 兜底 |
| 2 | 启动前完整性检查 | M1 实现，输出缺失文件清单 |
| 3 | UI 技术路线 | 使用 WPF 原生框架（XAML + code-behind + 原生数据绑定），不引入 MVVM 库或第三方 UI 框架 |
| 4 | 版本隔离 | M1 不做，统一使用 `.minecraft` 作为 gameDir；待下载功能完成后再评估 |

---

## 10. M1 验收标准（Definition of Done）

1. 可选择 `.minecraft` 目录并持久化，重启后仍生效。
2. 可离线登录并成功启动一个本地文件完整的版本。
3. 可微软正版登录，登录后能启动游戏，token 过期可自动刷新。
4. 账号可添加、切换、删除，敏感信息加密存储。
5. 可自动检测 Java 并手动指定；版本不匹配时有提示。
6. 启动前能列出缺失的 client jar / libraries / assets。
7. 游戏日志可在界面查看并落盘，启动失败可定位到原因。
8. 启动/终止流程稳定，异常不会导致启动器崩溃。
