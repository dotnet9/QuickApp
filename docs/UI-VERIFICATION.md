# QuickApp 0.9.3 界面验证

本轮在 Windows x64 使用正式 `scripts/publish.ps1` 发布 NativeAOT，并在 Avalonia Headless + Skia 中运行实际窗口。测试隔离配置、下载目录和安装服务，不修改用户快捷项、不真实安装软件。

## 已验证

- Core 189 项测试。
- 实际窗口 104 条状态断言，输出 41 张截图：八色 × 浅深两态、跟随系统独立保存、中英五设置页、四边 Dock、搜索与编辑模式、三类快捷项、独立关于和版本说明。
- 设置窗口标题栏只保留八个配色点、Ursa 三态主题按钮、Semi 语言图标和语言菜单；外观页去掉重复入口。实际点击 Ursa 内部按钮，检查浅色、深色、系统三态及持久化；通过真实 ListBox 选中色点检查配色绑定。
- 实际展开语言菜单并执行菜单项；检查 Ursa 内部按钮在中英和三态下的工具提示，提示文本绑定宿主 T4 资源，跟随全局语言即时刷新。
- 同一个已打开的新增快捷项窗口切换语言，验证下拉框关闭状态的 `SelectionBoxItem`，避免旧语种缓存。
- 更新服务使用受控实现覆盖新版本、当前版本、检查失败、平台无资产、下载就绪、取消和安装退出策略；真实安装器启动需用户主动执行。
- 单文件原型复制到空目录后验收 44 个状态，禁止外部资源请求，并断言无脚本错误；覆盖中英、八色、浅深、五设置页、关于和版本说明。
- T4 生成结果与 JSON 资源一致；宿主输出 JSON，仅使用一个 Toolkit 国际化管理器。
- 内嵌 SVG 先规范化为 UTF-8/LF，再编码为 Base64；分别使用 LF 和 CRLF 源文件验证产物一致，避免全新 Windows 检出后原型检查失败。
- macOS 的宿主 JSON 放入 `Contents/Resources/I18n`，应用兼容该标准路径，打包时在搬移后再次自检；Windows 使用显式进程等待读取 GUI 自检的实际退出码。
- 原型的独立窗口显示外边框、32 像素标题栏和三枚窗口按钮，并验证最小化与最大化交互；About 仅使用固定样例时间 `2026-10-15 00:00:00`，实际程序显示真实构建时间。

## 原型与实现对照

| 项目 | 原型与实际约束 |
| --- | --- |
| 设置窗口 | 侧栏页签、外观卡片、表单间距、圆角、文字层级与实际布局同步 |
| 外观 | 使用共享 StudioTheme 令牌快照，不通过色值判断主题；系统模式与配色分离 |
| 快捷项 | 类型下拉、目标与命令字段切换、实例数和工作目录、快捷键输入、确认取消 |
| 关于 | 独立窗口，版本、运行时、架构、编译时间、主页与仓库、许可和更新状态 |
| 下载交互 | 原型模拟状态；实际程序通过 Toolkit 执行请求、校验和受控安装 |

不以主观百分比代替验证结果。字体栅格化、窗口阴影、系统托盘、毛玻璃、系统文件选择器和真实鼠标拖拽仍受平台影响；离屏截图不能替代普通桌面操作验收。Linux 和 macOS 的 NativeAOT 与安装包由各自原生 CI runner 验证，本机未执行这些平台的安装。

## 复现

```powershell
dotnet test QuickApp.slnx -c Release
pwsh scripts/sync_localization_keys.ps1 -Check
pwsh scripts/build_prototype.ps1 -Check
dotnet run --project scripts/ui-verification/UiVerification.csproj -c Release -- artifacts/ui-verification
npm ci --prefix design/tests
npx --prefix design/tests playwright install chromium
node design/tests/audit-prototype.cjs
pwsh scripts/publish.ps1 -RuntimeIdentifier win-x64
artifacts/publish/win-x64/net10.0-windows/QuickApp/QuickApp.exe --selfcheck
```

仓库仅引用公开 NuGet 包，不提交本地源配置；开发时可按环境变量 `temp_nugets` 使用最新中间包。
