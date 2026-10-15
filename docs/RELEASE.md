# QuickApp 发布流程

版本唯一来源为根目录 `Directory.Build.props` 的 `VersionPrefix`，当前为 `0.9.4`，附注标签为 `v0.9.4`。精选说明维护在 `.github/release-notes/v0.9.4.md`，标签附注复用该文件。

## 本机验证

```powershell
dotnet test QuickApp.slnx -c Release
pwsh scripts/sync_localization_keys.ps1 -Check
pwsh scripts/build_prototype.ps1 -Check
pwsh scripts/tests/release_installers_test.ps1
dotnet run --project scripts/ui-verification/UiVerification.csproj -c Release -- artifacts/ui-managed
pwsh scripts/publish.ps1 -RuntimeIdentifier win-x64 -Version 0.9.4
$process = Start-Process ./artifacts/publish/win-x64/net10.0-windows/QuickApp/QuickApp.exe -ArgumentList '--selfcheck' -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'NativeAOT runtime verification failed.' }
dotnet publish scripts/ui-verification/UiVerification.csproj -c Release -r win-x64 --self-contained true -m:1 -p:Version=0.9.4 -p:PublishAot=true -p:PublishTrimmed=true -p:IlcGenerateCompleteTypeMetadata=true -p:IlcTrimMetadata=false -p:IlcSingleThreaded=true -o artifacts/ui-native/win-x64
& ./artifacts/ui-native/win-x64/UiVerification.exe artifacts/ui-evidence/win-x64
```

同一仓库内的构建、测试与 NativeAOT 编译依次执行，避免中间程序集或 PDB 被原生编译器映射时发生写入冲突。界面验证使用实际视图与受控服务，不修改用户快捷项、不执行安装器。普通桌面、系统托盘、快捷键及真实安装仍需目标系统验证，离屏结果不替代这些交互。

## 提交与标签

直接在 `main` 按修改范围使用简体中文规范提交。检查通过后推送主分支，再创建新附注标签；不得移动已发布标签。

```powershell
git push origin main
git tag -a v0.9.4 -F .github/release-notes/v0.9.4.md
git push origin v0.9.4
```

本机开发可使用 `temp_nugets` 环境变量目录中的包验证，正式引用保持公开 NuGet 版本，不提交本地源、环境配置或跨仓库项目引用。

## 发布与验收

标签触发 Release 工作流：测试、T4、离线原型与 managed 实际界面通过后，五个原生 runner 发布 NativeAOT 正式程序并执行其 `--selfcheck`，再制作安装包。精选中文说明文件缺失时终止发布。

| RID | 精确安装包名称 |
| --- | --- |
| win-x64 | `QuickApp-v0.9.4-win-x64-setup.exe` |
| linux-x64 | `QuickApp-v0.9.4-linux-x64.deb` |
| linux-arm64 | `QuickApp-v0.9.4-linux-arm64.deb` |
| osx-x64 | `QuickApp-v0.9.4-osx-x64.dmg` |
| osx-arm64 | `QuickApp-v0.9.4-osx-arm64.dmg` |

每个安装包必须非空且有对应 `.sha256`；创建 Release 前重新计算真实文件摘要，并严格校验本产品、完整版本、五个 RID 与十项文件，拒绝额外文件。13 个受控正反场景进入普通 CI 和 Release。发布后核对全部工作流、中文说明、十项资产及校验文件；不能把推送成功当作发布成功。
