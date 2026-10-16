# QuickApp

<p align="center">
  <img src="logo.svg" width="88" alt="QuickApp logo">
</p>

[![ci](https://github.com/dotnet9/QuickApp/actions/workflows/ci.yml/badge.svg)](https://github.com/dotnet9/QuickApp/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

QuickApp 是一个跨平台的系统快捷启动器，用常驻 Dock 统一打开应用、文件、目录、网址和常用命令。支持四边停靠、搜索和 Windows 全局快捷键，平时只占一排图标（Windows 功能最全，Linux / macOS 提供核心功能）。

## 界面预览

以下静态图来自 `0.9.6` 版本的实际 Windows NativeAOT 程序的 Skia 离屏运行；历史动图来自交互原型，两者分别标注。`0.9.7` 更新安装包构建流程，应用界面沿用该版本。

| 实际外观设置 · .NET 紫 | 实际关于窗口 · 英文 |
| :---: | :---: |
| ![](docs/media/appearance-dotnet.png) | ![](docs/media/about-en.png) |

标题栏直接展示八个配色点、Ursa 三态主题按钮和语言菜单，包含 .NET 紫、极光、余烬；模式与配色独立保存。语言切换覆盖已打开的窗口、主题提示、下拉框和托盘文案。更新检查、下载、SHA-256 校验与安装统一使用 CodeWF.Toolkit。逐项验证和已知边界见 [界面验证记录](docs/UI-VERIFICATION.md)。

| 四边停靠 · 悬停放大 · 点击启动 | 拼音搜索 · 系统应用 · 加入 Dock |
| :---: | :---: |
| ![](docs/gifs/dock.gif) | ![](docs/gifs/search.gif) |
| ![](docs/gifs/edit.gif) | ![](docs/gifs/settings.gif) |
| **编辑模式 · 拖动排序 · 就地改名** | **设置窗口 · 外观实时预览** |

搜索支持中文名、拼音全拼 / 首字母、路径与命令参数；开始菜单里未配置的应用可直接启动，或点 `＋` 加入 Dock。此外还有：多显示器定位、全局热键、触边唤出、移除可撤销、配置导入导出、便携模式、开机启动、托盘菜单、按平台选包的更新检查（发现新版本直接弹出版本说明卡片，可就地下载并安装），以及设置里的「推荐」——一键安装作者自研软件（安装包实时取自各仓库最新发布），装完自动加入 Dock。

## 快速开始

在「更多」里添加文件、目录、网址或命令；右键已有条目选择「编辑…」，可修改名称、目标路径、启动参数、工作目录和快捷键，程序类条目还能填写「启动数量」实现一次点击多开（微信等）。搜索结果区域最高 300，超过后纵向滚动。

打开目录可以直接添加目录，无需写命令。Windows 下在条目编辑中设置 `Ctrl+Alt+D` 等组合键，QuickApp 运行时即可直接打开，Dock 收起也有效。命令可选择 CMD 或 PowerShell；新命令默认在终端运行并保留输出，例如把 `git status` 配合仓库工作目录保存为常用操作。

从 [Releases](https://github.com/dotnet9/QuickApp/releases) 下载安装包（Windows x64 `.exe` / Linux x64、arm64 `.deb` / macOS x64、arm64 `.dmg`），或源码直接运行：

```powershell
dotnet run --project src/QuickApp/QuickApp.csproj
```

| 按键 | 作用 |
| --- | --- |
| `Ctrl+Alt+Space`（Windows） | 唤起并聚焦搜索；搜索中再次按下收起，钉住时保持展开 |
| `S` / `Ctrl+K` | 打开搜索 |
| `Enter` | 启动第一个结果 |
| `1` – `9` | 启动对应图标 |
| `E` / `Esc` | 进入 / 退出编辑模式 |

拖动图标区空白处可移动 Dock，松手自动吸附到最近的边缘并记住沿边位置；展开、收起和唤出把手都跟随该位置，屏幕尺寸变化后按比例还原。

## 开发

需要 .NET 10 SDK（版本以 [global.json](global.json) 为准）：

```powershell
dotnet build QuickApp.slnx   # 构建
dotnet test QuickApp.slnx    # 测试
```

交互原型唯一交付文件为 [design/index.html](design/index.html)，复制到空目录即可离线打开。维护源位于 `scripts/prototype-source` 和 `design/assets`，修改后运行 `scripts/build_prototype.ps1` 重新内嵌，禁止直接修改生成文件。所有界面通过 Dock、菜单、右键或设置打开，共用数据和状态。环境要求、发布打包、项目结构与贡献规范见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)。

## 许可证

[MIT](LICENSE)

## 发布

标准发布流程与发布说明规范见 [docs/RELEASE.md](docs/RELEASE.md)。

## 统一安装体验

安装包采用统一 .NET 品牌紫设计：Windows 使用异形原生向导、真实进度核心和双语许可；macOS 使用品牌 DMG 背景与 Finder 拖放布局；Linux 提供原生 DEB 及 AppStream 品牌信息。各仓库保存自包含安装器源码，交付 EXE / DEB / DMG 和 SHA-256，不以 ZIP 代替安装包。实现、维护流程与平台验收边界见 [安装器说明](scripts/installer/README.md)。
