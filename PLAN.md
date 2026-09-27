# QuickApp 开发计划与交接说明

> 本文写给下一台电脑/下一个 AI 会话：接手前先通读，避免重复踩坑。
> 原型：`design/index.html`（单文件 HTML，所有视觉 token 与交互的唯一权威参照）。

## 1. 项目是什么

Windows 快捷启动 Dock（原型为四边可停靠）。Avalonia 12 + ReactiveUI 重写版，NativeAOT 发布。
仓库：`github.com/dotnet9/QuickApp`（SSH：`git@github.com:dotnet9/QuickApp.git`）。

## 2. 技术栈与构建

- .NET 10、Avalonia 12.1.3、ReactiveUI.Avalonia 12.1.2（中央包管理 `Directory.Packages.props`）
- 构建：`dotnet build QuickApp.slnx`；测试：`dotnet test QuickApp.slnx`（68 例，全部应绿）
- 发布：`src/QuickApp/Properties/PublishProfiles/*.pubxml` + `scripts/package_quickapp.ps1`
- CI：`.github/workflows/release.yml`——**push tag `v*` 自动 AOT 发布并创建 GitHub Release**（版本号取 tag）；`ci.yml` 随常规推送跑构建测试
- 远端走 SSH（本机 https 443 连不上 GitHub，SSH 认证已配好）

## 3. 架构与关键机制

```
src/QuickApp.Core          平台无关领域层（模型/纯逻辑服务，可单测）
  Models/                  AppConfig、AppSettings、LauncherItem、Enums
  Services/                DockPlacement（停靠数学）、ItemQuery（搜索/名称/命令文本）、
                           HotkeyGesture（热键解析）、ConfigStore（JSON 原子写）、
                           LaunchPlanner/ProcessLauncher、UpdateChecker、AppPaths
src/QuickApp               Avalonia 前端
  Views/                   DockWindow（主窗口）、SettingsWindow、CommandDialogWindow、ToastWindow
  ViewModels/              DockViewModel（中枢）、ItemViewModel、SettingsViewModel（包 Dock 的画刷）
  Theme/                   Palette（配色 token，代码便于运行时重算）、Icons/IconCatalog（几何图标）
  Styles/Controls.axaml    自绘 ControlTheme（TileButton/IconButton/RemoveButton/Segment/LinkButton）
                           + ThemeDictionaries（深浅两套 token，DynamicResource 引用）
  Platform/Windows/        HotkeyService、SingleInstanceService、AutoStartService、
                           IconProvider、ClipboardInterop、NativeMethods（全部 Win32 集中在此）
tests/QuickApp.Core.Tests  68 例单测
```

- **窗口形态**：DockWindow 是逐像素透明异形窗（`TransparencyLevelHint=Transparent` + `TransparencyBackgroundFallback=Transparent`）。面板圆角外全透明；投影画在 12px 透明边距内与桌面混合。**不要再申请 acrylic**——它是整窗矩形，会在面板外留一圈「小边框」并吃掉圆角（曾踩坑，已改掉）
- **停靠定位**：`DockPlacement`（纯数学、物理像素、可单测）。收起位移 `HiddenOffset` 的 `beyondEdge` = 工作区到屏幕边界的距离（任务栏高度），否则下边缘收起会残留一条压在任务栏上
- **显隐**：`IsDockVisible` 驱动；收起动画是 DispatcherTimer 逐帧改 `Position`；触边唤出 `CheckEdgeReveal` 150ms 光标轮询（热区 6px，不受钉住影响）；自动隐藏倒计时在唤出/鼠标离开/关闭搜索编辑时都要 `ScheduleAutoHide`
- **配置**：`%APPDATA%\QuickApp\config.json`（exe 旁有 `portable.txt` 则绿色模式），JSON 源生成（`AppJsonContext`），原子写 + .bak + 坏文件隔离
- **全局热键**：`HotkeyService` 专用线程 + message-only 窗口收 `WM_HOTKEY`，回调经 `Dispatcher.UIThread.Post`；不要子类化 Avalonia 窗口过程
- **图标**：Shell API 提取缓存到 LocalAppData；`LauncherItem.IconKey` 手选占位图标（IconCatalog）优先于真实图标
- **主题**：`RequestedThemeVariant` 跟随设置；Fluent 菜单资源（MenuItemBackgroundPointerOver 等）与 `OverlayCornerRadius` 已覆盖

## 4. Avalonia 12 的坑（与 11 差异，已趟过）

- `WindowDecorations`（旧 `SystemDecorations`）、`WindowDecorations.None`
- 缓动类是 `BackEaseOut` 等独立类（没有 `BackEase EasingMode` 组合写法）
- 透明级别变化走 `ActualTransparencyLevelProperty` 依赖属性（没有 `TransparencyLevelChanged` 事件）
- `Bitmap.Save` 有 CS0618 过时警告（可换 BitmapEncoderOptions，未处理）
- Button 类处理器在冒泡阶段吞 `PointerPressed`：要在按钮上先于按钮自身处理指针事件，必须 `AddHandler(..., RoutingStrategies.Tunnel)`
- 捕获指针会把后续事件的命中源重定向：编辑模式「按下不捕获、位移超阈值才捕获」，否则双击手势的 `e.Source` 会错

## 5. 当前进度（对齐原型 design/index.html）

已完成：四边停靠/竖排布局、拖 ⠿ 手柄吸附（带吸附气泡）、搜索行（回车运行首项）、
编辑模式（拖动排序/双击改名/× 移除+撤销）、更换图标选择器、添加文件/命令行对话框、
右键菜单与更多菜单、托盘（含开机启动开关）、触边唤出+自动隐藏+收起彻底（含下边缘任务栏）、
全局热键、单实例、图标提取与选择器、配置导入导出、更新检查（GitHub Releases）、
键盘导航（方向键/Enter/1-9/S/Ctrl+K/E/Esc）、悬浮 Toast、逐像素透明异形窗与投影、
原型配色 token/字体/圆角/阴影/按下悬停反馈、设置窗口卡片化。

**已知保留差异**：ToggleSwitch 用 Fluent 模板（原型是 38×22 自绘小开关）；滚动条隐藏但
渐隐/指示条仅在内容溢出时出现（原型一致）。

## 6. 待办（建议下一步）

1. 托盘菜单显示状态勾选（钉住/开机启动当前态）；设置项 `MonitorIndex`（显示器选择）尚无 UI
2. 拖文件到 Dock 添加项（旧 WPF 版有，原型未画；DragDrop 已在瓦片上开 `AllowDrop`）
3. Toast 目前单条覆盖式，可升级队列；移除撤销目前一次一条
4. 多显示器打磨：拖动吸附已按光标所在屏计算，但 `ResolveScreen` 仍优先配置屏/主屏，跨屏体验可再验证
5. 发布流水线验证：tag `v0.2.0` 已推送触发 release.yml；若 Release 资产缺失，检查 Actions 日志（GitHub API 匿名限流，程序内「检查更新」走同一 API）
6. 悬停气泡（仅图标模式下的 name+target 浮层）目前用 ToolTip 近似，原型是自绘 bubble

## 7. 给 AI 的验证方法（本机跑通的套路）

- 启动：`start artifacts\bin\QuickApp\debug\QuickApp.exe`；日志：`%LOCALAPPDATA%\QuickApp\logs\app.log`
- 截图：PowerShell `System.Drawing` `CopyFromScreen`（物理像素，本机 3440x1440 DPI 100%）
- 鼠标自动化：PowerShell P/Invoke `SetCursorPos` + `mouse_event`（坐标=物理像素）；双击=两次 press/release 间隔 ~130ms；拖拽=down 后逐像素 move 再 up
- 交互验证要点：编辑模式（E 或铅笔）下 × 在瓦片左上（横向 Dock）；收起后触边 1s 内应唤出；移开后 0.7s 自动收起
- 单实例：直接 `start` 第二个进程只会唤醒已有窗口

## 8. 约定

- Core 层保持平台无关与可单测；Win32 全部进 `NativeMethods`；AOT 安全优先（图标/JSON/反射面）
- 中文注释与提交信息；每个提交一个主题；提交前 `dotnet test` 应全绿
- 图标几何只写 M/C/L/Z（手写贝塞尔近似圆弧），避免 AOT 下路径解析器差异
- 配色/尺寸一律从原型 CSS token 换算，不要凭感觉调
