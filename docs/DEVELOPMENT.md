# 开发与发布

本文承接 [README](../README.md)，记录 QuickApp 的环境要求、构建测试、发布打包、更新机制、项目结构与贡献规范。

## 环境

- Windows 10/11，Linux（x64/arm64）或 macOS（x64/arm64）
- .NET SDK 以 `global.json` 为准 或兼容的 .NET 10 SDK
- Windows SDK 和 MSVC 工具链（只在 Windows NativeAOT 发布时需要）

依赖版本集中在 [`Directory.Packages.props`](../Directory.Packages.props)，当前使用 Avalonia 12.1.4 和 ReactiveUI.Avalonia 12.1.2。

## 构建和测试

在仓库根目录执行：

```powershell
dotnet restore QuickApp.slnx
dotnet build QuickApp.slnx -c Debug --no-restore
dotnet test QuickApp.slnx --no-restore
```

只运行桌面项目：

```powershell
dotnet run --project src/QuickApp/QuickApp.csproj
```

不要开启 `InvariantGlobalization`：`App.axaml` 的 `SemiTheme` 传了 `Locale="zh-CN"`，其静态构造会 `new CultureInfo("zh-CN")`，在 Invariant 模式下直接抛 `CultureNotFoundException`，发布出来的程序连窗口都建不起来（错误只写进 `%LOCALAPPDATA%\QuickApp\logs\app.log`）；`Directory.Build.props` 因此显式设为 `false`。

构建输出在 `artifacts/bin`。Core 层的纯逻辑测试位于 [`tests/QuickApp.Core.Tests`](../tests/QuickApp.Core.Tests)。

## 发布

Windows x64 的自包含 NativeAOT 发布：

```powershell
scripts\publish-win-x64.bat 0.9.3
pwsh scripts/build_installer.ps1 -Version 0.9.3
```

发布文件位于 `artifacts/publish`，平台安装包和 SHA-256 校验文件位于 `artifacts/release`。

Windows x64 会生成 Inno Setup 安装包，Linux x64/arm64 会生成对应架构的 `.deb`，macOS x64/arm64 会生成 `.dmg`。Windows 安装包默认安装到 `Program Files\QuickApp`，升级时只替换程序文件，不会覆盖 `%LOCALAPPDATA%\QuickApp\config.json`；便携版仍可在程序目录放置 `portable.txt`。本地生成 Windows 安装包需要先安装 Inno Setup 6：

```powershell
pwsh scripts/build_installer.ps1 -Version 0.9.3
```

安装包和对应的 `.sha256` 文件会写入 `artifacts/release`。

Linux/macOS 在对应系统和架构上使用 NativeAOT 发布，安装包脚本将产物封装成对应平台安装包：

```powershell
pwsh scripts/publish_quickapp.ps1 -RuntimeIdentifier linux-x64 -Version 0.9.3
pwsh scripts/package_linux_deb.ps1 -RuntimeIdentifier linux-x64 -Version 0.9.3

pwsh scripts/publish_quickapp.ps1 -RuntimeIdentifier osx-arm64 -Version 0.9.3
pwsh scripts/package_macos_dmg.ps1 -RuntimeIdentifier osx-arm64 -Version 0.9.3
```

可用 RID：`win-x64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`，五个平台均使用 NativeAOT。`scripts/publish-win-x64.bat` 用于当前 Windows x64；其他平台须在匹配系统和架构上执行，不能在 Windows 上直接运行全部 RID 的原生编译。CI 使用五个原生 runner 分别发布。

## 持续集成与 Release

推送 `v*` 标签时，GitHub Release 会上传 Windows 安装包、Linux `.deb`、macOS `.dmg` 及其校验文件。常规推送和 Pull Request 会执行构建与测试工作流。

GitHub Actions 会在推送 `v*` 标签时分别构建 Windows、Linux 和 macOS 安装包，并创建包含 Windows 安装包、Linux `.deb`、macOS `.dmg` 与 SHA-256 校验文件的 Release。

每个版本的 Release 描述维护在 `.github/release-notes/v版本号.md`，例如 `.github/release-notes/v0.2.5.md`。打标签前先补充该文件；发布工作流会直接使用它作为 GitHub Release 正文，缺少说明时会停止发布。

## 更新检查

快捷菜单、托盘、独立“关于”窗口和设置中的手动检查共用同一个更新窗口，检查中、已是最新版和检查失败也在这里显示。Dock 更新提示提供查看入口，下载、取消和安装集中在更新窗口；关闭窗口保留后台下载，重新打开继续显示当前状态，重复检查同一安装包不会清掉下载结果。安装时保留窗口及视图模型，只有安装服务确认启动安装器并要求退出后才关闭应用；启动失败或取消提权时显示原因并允许重试。

Windows 上 Dock 置顶时，“关于”会继承原生置顶状态，但其 Avalonia `Topmost` 属性仍可能为 `false`。更新窗口必须沿完整 owner 链显式继承置顶，避免嵌套模态窗口落在已禁用的父窗口后面。复用更新窗口切换 owner 前先隐藏以解除旧模态关系，再用当前 owner 重新显示；CI 额外运行 `--desktop-modal` 验证原生 owner、Z 序、输入禁用及关闭恢复。

程序默认在启动时检查 GitHub Releases 的最新稳定版本，也可以在设置页手动检查，或关闭自动检查。更新提示会按当前系统和 CPU 架构选择下载资产：Windows x64 优先选择安装包，Linux 选择 `.deb`，macOS 选择 `.dmg`；没有匹配资产时打开 Release 页面，避免下载错误平台的文件。点击“下载”后界面显示进度，下载完成后还要再次点击“安装/打开安装包”；程序不会静默下载、校验并替换正在运行的程序。每个发布文件旁边的 `.sha256` 可用于完整性校验。

默认检查只访问 GitHub 网页端点：读取 `releases/latest` 的重定向头获取版本号，有新版本时再解析 `expanded_assets/{tag}` 安装包列表，并从 `releases.atom` 取该版本的正文作为「更新说明」（正文不在资产列表页出现，而走 API 容易限流，故用 feed），全程不消耗 API 配额。说明拉取失败只少一段文案，不影响新版本提示、下载与安装入口。版本号已确认而安装包列表暂不可用时，仍显示新版本并提供发布页入口；失败或空资产列表不会写入缓存，下次检查可重新解析。推荐应用同样读取网页端点，按内嵌目录的 `installerSuffixes` 选择当前平台安装包，并要求配对同名 `.sha256`。当前推荐 `dotnet9/Vex-Flutter`，使用独立的 `vex-flutter` 标识，不迁移旧 Vex 的安装记录。`UpdateChecker` 的 `allowApiFallback` 仅供需要 API 回退的调用方显式启用。

这种设计把检查更新和安装更新分开，避免覆盖便携模式、运行中的文件或用户配置。Windows 安装器升级时保留 `%LOCALAPPDATA%\QuickApp\config.json`，卸载也不会删除该配置目录。

推荐列表统一维护在 [`src/QuickApp.Core/RecommendedApps.json`](../src/QuickApp.Core/RecommendedApps.json)，作为程序集内嵌资源随程序发布。名称、各语言介绍、图标、仓库、平台安装包规则与启动路径均来自配置，客户端与原型没有应用专属分支。增删条目及图标维护方式见[推荐应用配置](RECOMMENDED_APPS.md)。

## 使用说明

首次启动会创建空配置，Dock 默认为空，可从系统应用搜索直接启动，或点 `＋` 加入 Dock；旧版本预置的内置程序条目会在加载老配置时自动清理。配置文件默认位于：

```text
%LOCALAPPDATA%\QuickApp\config.json
```

如果程序目录中存在 `portable.txt`，配置会改为保存在程序目录。图标缓存位于 `%LOCALAPPDATA%\QuickApp\icons`。

常用操作：

- `S` 或 `Ctrl+K`：打开搜索并自动聚焦输入框。
- `Enter`：启动第一个搜索结果。
- `1` 到 `9`：启动对应图标。
- `E`：进入编辑模式。
- `Esc`：关闭搜索、编辑或 Dock。
- 拖动图标区空白处：移动 Dock 并吸附到最近边缘。
- 右侧工具区：搜索、添加、钉住、更多操作和收起。

## 项目结构

```text
src/QuickApp.Core   平台无关的模型、搜索、启动计划、配置和停靠计算
src/QuickApp        Avalonia 桌面应用、窗口、视图模型和 Windows 平台服务
design              单入口 index.html 交互原型与共享样式、脚本
docs                开发文档与 README 演示动图
tests               Core 层单元测试
scripts             发布、打包和 Windows 安装包脚本
```

Core 层保持平台无关并可单测；Windows API 集中在 `src/QuickApp/Platform/Windows`。配置使用源生成的 `System.Text.Json` 上下文，保存时采用临时文件和备份文件策略。

## 当前限制

- Linux/macOS 暂不提供全局热键、开机启动、系统已安装应用索引、系统图标提取和 Windows 资源管理器定位；这些入口会安全降级并继续运行 Dock。
- Windows 开始菜单索引依赖可读的快捷方式和 URL 文件；不可读或没有开始菜单入口的程序不会出现在系统应用搜索结果中。
- Windows 图标提取和透明窗口效果依赖 Windows Shell、DPI 和显卡环境，异常时会回退到占位图标或普通画刷。
- Avalonia 的 Bitmap 保存 API 仍有弃用警告，不影响当前构建和运行。

## 贡献

修改界面时先同步`scripts/prototype-source` 与 `design/assets` 的交互原型维护源，再同步 Avalonia 实现。`design` 只保留这一个 HTML；新增界面必须通过实际 Dock、菜单、右键或设置入口打开，数据和交互状态互通，不能以独立演示页代替产品流程。样式和脚本放在 `design/assets`，运行 `scripts/build_prototype.ps1` 后内嵌到唯一交付文件 `design/index.html`；单文件复制到空目录后可离线打开。

原型用浏览器本地存储保存演示配置；启动系统程序、文件/目录选择、显示器迁移和下载安装用演示状态模拟，配置导入导出使用 JSON。原型不会操作真实 QuickApp 配置或安装软件，也不会切换系统输入法；客户端的 Windows 热键唤起会切到英文输入态。提交前运行 `dotnet test QuickApp.slnx --no-restore` 和 `git diff --check`，并从页面入口验证修改涉及的完整操作链路。
