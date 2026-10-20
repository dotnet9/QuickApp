# 推荐应用配置

推荐目录的唯一维护源是 [`src/QuickApp.Core/RecommendedApps.json`](../src/QuickApp.Core/RecommendedApps.json)。构建时将 JSON 内嵌到 `QuickApp.Core` 程序集，程序直接读取它；发布目录不需要额外配置文件。修改后重新构建、发布 QuickApp，用户升级后即可看到新列表。

增添应用：在数组中增加一个对象。删除应用：删除对应对象。调整顺序：调整数组顺序。空数组 `[]` 表示不推荐任何应用。无需修改 C#、XAML、项目文件、翻译资源或原型脚本。

```json
[
  {
    "id": "notes",
    "name": "Notes",
    "displayName": "Notes · 笔记",
    "description": "本地笔记工具。",
    "localizations": {
      "en-US": {
        "displayName": "Notes",
        "description": "A local notes app."
      }
    },
    "icon": "notes.png",
    "owner": "example",
    "repo": "Notes",
    "installerSuffixes": {
      "win-x64": "-windows-x64-setup.exe",
      "linux-x64": "-linux-x64.deb",
      "linux-arm64": "-linux-arm64.deb",
      "osx-x64": "-macos-universal.dmg",
      "osx-arm64": "-macos-universal.dmg"
    },
    "installArgumentsWindows": "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES",
    "launcherPathsWindows": ["%LOCALAPPDATA%\\Programs\\Notes\\notes.exe"],
    "launcherPathsLinux": ["/usr/bin/notes"],
    "launcherPathsMacOS": ["/Applications/Notes.app"]
  }
]
```

| 字段 | 用途 |
| --- | --- |
| `id` | 唯一、稳定的标识，关联安装记录和 Dock 快捷方式；替换为独立产品时使用新标识 |
| `name` | Dock 快捷方式名称，也是缺少展示名时的默认名称 |
| `displayName`、`description` | 默认卡片名称与介绍，可省略；介绍默认为空 |
| `localizations` | 以界面语言为键（如 `en-US`）的名称与介绍；缺少的字段使用默认文案 |
| `icon` | `src/QuickApp/Assets/Recommended/` 下的 PNG 文件名；可省略，缺失时显示 QuickApp 图标 |
| `owner`、`repo` | GitHub 仓库；程序实时读取其最新稳定 Release |
| `installerSuffixes` | 系统架构与安装包文件名后缀的映射；只填写该应用实际支持的平台 |
| `installArgumentsWindows` | Windows 安装器参数；省略时打开安装器由用户安装，不能给非 Inno 安装器照抄示例参数 |
| `launcherPathsWindows`、`launcherPathsLinux`、`launcherPathsMacOS` | 安装后的候选启动路径，依次探测；Windows 路径支持环境变量 |

安装包文件名后缀不含版本号。例如 `-windows-x64-setup.exe` 可匹配 `Notes-v1.2.3-windows-x64-setup.exe`。下载地址与版本号不写进配置；Release 必须同时发布同名的 `.sha256` 校验文件。

需要专属图标时，将 PNG 放进 `src/QuickApp/Assets/Recommended/`，并在 `icon` 中填写文件名；项目通过通配符自动内嵌，不用逐个登记。删除条目无需删除用户已有的 Dock 快捷方式或安装记录，也不会卸载用户的软件。

运行 `scripts/build_prototype.ps1` 会从同一份 JSON 生成离线原型，并内嵌同一套图标。原型模拟 Windows x64 安装和 Dock 操作，不发起真实下载；其他平台的包由客户端按运行环境解析。

修改后运行 `dotnet test QuickApp.slnx --no-restore`、`scripts/build_prototype.ps1 -Check`，并在 `design/tests` 运行 `npm test`。测试从配置验证目录和页面，不绑定某个推荐应用或固定条目数量。
