# 开发与发布

本文承接 [README](../README.md)，记录 QuickApp 的环境要求、构建测试、发布打包、更新机制、项目结构与贡献规范。

## 环境

- Windows 10/11，Linux（x64/arm64）或 macOS（x64/arm64）
- .NET SDK `10.0.401` 或兼容的 .NET 10 SDK
- Windows SDK 和 MSVC 工具链（只在 Windows NativeAOT 发布时需要）

依赖版本集中在 [`Directory.Packages.props`](../Directory.Packages.props)，当前使用 Avalonia 12.1.3 和 ReactiveUI.Avalonia 12.1.2。

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

构建输出在 `artifacts/bin`。Core 层的纯逻辑测试位于 [`tests/QuickApp.Core.Tests`](../tests/QuickApp.Core.Tests)。

## 发布

Windows x64 的自包含 NativeAOT 发布：

```powershell
scripts\publish-win-x64.bat 0.1.0
pwsh scripts/build_installer.ps1 -Version 0.1.0 -Force
```

发布文件位于 `artifacts/publish`，平台安装包和 SHA-256 校验文件位于 `artifacts/release`。

Windows x64 会生成 Inno Setup 安装包，Linux x64/arm64 会生成对应架构的 `.deb`，macOS x64/arm64 会生成 `.pkg` 和 `.dmg`。Windows 安装包默认安装到 `Program Files\\QuickApp`，升级时只替换程序文件，不会覆盖 `%APPDATA%\\QuickApp\\config.json`；便携版仍可在程序目录放置 `portable.txt`。本地生成 Windows 安装包需要先安装 Inno Setup 6：

```powershell
pwsh scripts/build_installer.ps1 -Version 0.2.2 -Force
```

安装包和对应的 `.sha256` 文件会写入 `artifacts/release`。

Linux/macOS 的自包含单文件发布（不启用 NativeAOT，安装包脚本会把它们封装成对应平台安装包）：

```powershell
pwsh scripts/publish_quickapp.ps1 -RuntimeIdentifier linux-x64 -Version 0.2.2
pwsh scripts/package_linux_deb.ps1 -RuntimeIdentifier linux-x64 -Version 0.2.2 -Force

pwsh scripts/publish_quickapp.ps1 -RuntimeIdentifier osx-arm64 -Version 0.2.2
pwsh scripts/package_macos_dmg.ps1 -RuntimeIdentifier osx-arm64 -Version 0.2.2 -Force
```

可用 RID：`win-x64`、`win-x86`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。`scripts/publish-all.bat` 一次发布全部 RID、`scripts/publish-win-x64.bat` 只发 win-x64；Windows x64 使用 NativeAOT，其余 RID 使用自包含单文件。

## 持续集成与 Release

推送 `v*` 标签时，GitHub Release 会上传 Windows 安装包、Linux `.deb`、macOS `.pkg/.dmg` 及其校验文件。常规推送和 Pull Request 会执行构建与测试工作流。

GitHub Actions 会在推送 `v*` 标签时分别构建 Windows、Linux 和 macOS 安装包，并创建包含 Windows 安装包、Linux `.deb`、macOS `.pkg/.dmg` 与 SHA-256 校验文件的 Release。

每个版本的 Release 描述维护在 `.github/release-notes/v版本号.md`，例如 `.github/release-notes/v0.2.5.md`。打标签前先补充该文件；发布工作流会直接使用它作为 GitHub Release 正文，缺少说明时会停止发布。

## 更新检查

程序默认在启动时检查 GitHub Releases 的最新稳定版本，也可以在设置页手动检查，或关闭自动检查。更新提示会按当前系统和 CPU 架构选择下载资产：Windows x64 优先选择安装包，Linux 选择 `.deb`，macOS 优先选择 `.pkg`、回退到 `.dmg`；没有匹配资产时打开 Release 页面，避免下载错误平台的文件。点击“下载”后界面显示进度，下载完成后还要再次点击“安装/打开安装包”；程序不会静默下载、校验并替换正在运行的程序。每个发布文件旁边的 `.sha256` 可用于完整性校验。

这种设计把检查更新和安装更新分开，避免覆盖便携模式、运行中的文件或用户配置。Windows 安装器升级时保留 `%APPDATA%\QuickApp\config.json`，卸载也不会删除该配置目录。

## 使用说明

首次启动会创建默认配置并显示几个 Windows 内置项目。配置文件默认位于：

```text
%APPDATA%\QuickApp\config.json
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
design              HTML 原型（每界面一个文件）与共享样式、脚本
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

修改界面时先同步 [`design`](../design) 下的对应原型页，再同步 Avalonia 实现。提交前运行 `dotnet test QuickApp.slnx --no-restore` 和 `git diff --check`。
