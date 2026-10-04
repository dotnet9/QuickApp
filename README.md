# QuickApp

<p align="center">
  <img src="logo.svg" width="88" alt="QuickApp logo">
</p>

[![ci](https://github.com/dotnet9/QuickApp/actions/workflows/ci.yml/badge.svg)](https://github.com/dotnet9/QuickApp/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

QuickApp 是一个跨平台的快捷应用 Dock：停靠在屏幕上、下、左、右任意一条边，平时只占一排图标，搜索即达 —— 应用、网址、命令行一键启动（Windows 功能最全，Linux / macOS 提供核心功能）。

## 界面预览

动图录制自 [`design`](design) 目录的 HTML 界面原型，Avalonia 实现与原型保持一致。

| 四边停靠 · 悬停放大 · 点击启动 | 拼音搜索 · 系统应用 · 加入 Dock |
| :---: | :---: |
| ![](docs/gifs/dock.gif) | ![](docs/gifs/search.gif) |
| ![](docs/gifs/edit.gif) | ![](docs/gifs/settings.gif) |
| **编辑模式 · 拖动排序 · 就地改名** | **设置窗口 · 外观实时预览** |

搜索支持中文名、拼音全拼 / 首字母、路径与命令参数；开始菜单里未配置的应用可直接启动，或点 `＋` 加入 Dock。此外还有：多显示器定位、全局热键、触边唤出、移除可撤销、配置导入导出、便携模式、开机启动、托盘菜单和按平台选包的更新检查。

## 快速开始

从 [Releases](https://github.com/dotnet9/QuickApp/releases) 下载安装包（Windows 安装包 / Linux `.deb` / macOS `.pkg`、`.dmg`），或源码直接运行：

```powershell
dotnet run --project src/QuickApp/QuickApp.csproj
```

| 按键 | 作用 |
| --- | --- |
| `S` / `Ctrl+K` | 打开搜索 |
| `Enter` | 启动第一个结果 |
| `1` – `9` | 启动对应图标 |
| `E` / `Esc` | 进入 / 退出编辑模式 |

拖动图标区空白处可移动 Dock，松手自动吸附到最近的边缘。

## 开发

需要 .NET 10 SDK（`10.0.401`）：

```powershell
dotnet build QuickApp.slnx   # 构建
dotnet test QuickApp.slnx    # 测试
```

界面按「原型先行」维护：先改 [`design`](design) 里对应的零依赖 HTML 原型页（入口 [design/index.html](design/index.html)），再同步 Avalonia 实现。环境要求、发布打包、项目结构与贡献规范见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)。

## 许可证

[MIT](LICENSE)

## 发布

标准发布流程与发布说明规范见 [docs/RELEASE.md](docs/RELEASE.md)。
