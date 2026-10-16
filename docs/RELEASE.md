# QuickApp 发布流程

版本唯一来源为根目录 `Directory.Build.props` 的 `VersionPrefix`。发布前从该属性读取版本，使用 `v<版本>` 附注标签，并维护对应的 `.github/release-notes/v<版本>.md` 精选说明；标签附注复用该文件。

## 本机验证

```powershell
[xml] $properties = Get-Content -LiteralPath Directory.Build.props -Raw
$version = $properties.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText.Trim()
$tag = "v$version"
$notes = ".github/release-notes/$tag.md"

dotnet test QuickApp.slnx -c Release
pwsh scripts/sync_localization_keys.ps1 -Check
pwsh scripts/build_prototype.ps1 -Check
pwsh scripts/tests/publish_entry_test.ps1
pwsh scripts/tests/release_installers_test.ps1
dotnet run --project scripts/ui-verification/UiVerification.csproj -c Release -- artifacts/ui-managed
pwsh scripts/publish.ps1 -RuntimeIdentifier win-x64
$process = Start-Process ./artifacts/publish/win-x64/net10.0-windows/QuickApp/QuickApp.exe -ArgumentList '--selfcheck' -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'NativeAOT runtime verification failed.' }
dotnet publish scripts/ui-verification/UiVerification.csproj -c Release -r win-x64 --self-contained true -m:1 "-p:Version=$version" -p:PublishAot=true -p:PublishTrimmed=true -p:IlcGenerateCompleteTypeMetadata=true -p:IlcTrimMetadata=false -p:IlcSingleThreaded=true -o artifacts/ui-native/win-x64
& ./artifacts/ui-native/win-x64/UiVerification.exe artifacts/ui-evidence/win-x64
```

同一仓库内的构建、测试与 NativeAOT 编译依次执行，避免中间程序集或 PDB 被原生编译器映射时发生写入冲突。界面验证使用实际视图与受控服务，不修改用户快捷项、不执行安装器。普通桌面、系统托盘、快捷键及真实安装仍需目标系统验证，离屏结果不替代这些交互。

## 提交与标签

直接在 `main` 按修改范围使用简体中文规范提交。检查通过后推送主分支，再创建新附注标签；不得移动已发布标签。

```powershell
git push origin main
git tag -a $tag -F $notes
git push origin $tag
```

本机开发可使用 `temp_nugets` 环境变量目录中的包验证，正式引用保持公开 NuGet 版本，不提交本地源、环境配置或跨仓库项目引用。

## 发布与验收

标签触发 Release 工作流：测试、T4、离线原型与 managed 实际界面通过后，五个原生 runner 发布 NativeAOT 正式程序并执行其 `--selfcheck`，再制作安装包。精选中文说明文件缺失时终止发布。

| RID | 精确安装包名称 |
| --- | --- |
| win-x64 | `QuickApp-v<版本>-win-x64-setup.exe` |
| linux-x64 | `QuickApp-v<版本>-linux-x64.deb` |
| linux-arm64 | `QuickApp-v<版本>-linux-arm64.deb` |
| osx-x64 | `QuickApp-v<版本>-osx-x64.dmg` |
| osx-arm64 | `QuickApp-v<版本>-osx-arm64.dmg` |

每个安装包必须非空且有对应 `.sha256`；创建 Release 前重新计算真实文件摘要，并严格校验本产品、完整版本、五个 RID 与十项文件，拒绝额外文件。13 个受控正反场景进入普通 CI 和 Release。发布后核对全部工作流、中文说明、十项资产及校验文件；不能把推送成功当作发布成功。
