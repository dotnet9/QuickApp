using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Ursa.Controls;
using CodeWF.Avalonia.Controls.Themes;
using CodeWF.Toolkit.Core.UpdateChecking;
using CodeWF.Toolkit.Lang;
using QuickApp;
using QuickApp.Core.I18n;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using QuickApp.ViewModels;
using QuickApp.Views;
using ReactiveUI.Avalonia;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-verification");
Directory.CreateDirectory(output);
int count=0;
AppBuilder.Configure<App>().UseSkia().UseReactiveUI(_=>{}).With(new FontManagerOptions
{
    FontFallbacks = [new FontFallback { FontFamily = new FontFamily("Microsoft YaHei UI") }]
}).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var fixture = Path.Combine(output, "fixture");
Directory.CreateDirectory(fixture);
File.WriteAllText(Path.Combine(fixture,"portable.txt"), "");
var store = new ConfigStore(fixture);
var config = store.Load();
config.Settings.CheckUpdates = false;
config.Items = [new() { Id="fixture", Name="README", Target="https://github.com/dotnet9/QuickApp", Kind=ItemKind.Web }];
Check(store.Save(config),"Isolated configuration initialized");
var updates = new FakeUpdates(fixture);
var model = new DockViewModel(store,new FakeLauncher(),new FakeIcons(),new FakeApps(),updates,
    new UpdateDownloader(new HttpClient(),new UpdateServiceOptions { Repository="QuickApp" }),new FakeAutoStart(),"QuickApp");
var host = new FakeHost();model.AttachHost(host);
var settings = new SettingsWindow();settings.Attach(model);settings.Show();Pump();
ControlLocaleVerification.Run(settings, ["zh-CN", "en-US"]);
var vm=(SettingsViewModel)settings.DataContext!;
var tray=new TrayViewModel(model,()=>{},()=>{});
var traySettings=new NativeMenuItem { Header=tray.SettingsText };
tray.PropertyChanged+=(_,e)=>{if(string.IsNullOrEmpty(e.PropertyName)||e.PropertyName==nameof(tray.SettingsText))traySettings.Header=tray.SettingsText;};
Check(vm.PaletteOptions.Count==8 && vm.LanguageOptions.Count==2,"Eight palettes and two languages");
var paletteSelector=settings.GetVisualDescendants().OfType<ListBox>().Single(control=>control.Classes.Contains("title-palettes"));
Check(paletteSelector.Items.Count==8,"Title bar exposes all eight palette dots");
var themeToggle=settings.GetVisualDescendants().OfType<ThemeToggleButton>().Single();
vm.ThemeMode=StudioThemeMode.System;Pump();
foreach(var expected in new[]{StudioThemeMode.Light,StudioThemeMode.Dark,StudioThemeMode.System})
{
    themeToggle.GetVisualDescendants().OfType<Button>().Single(button=>button.Name=="PART_ThemeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
    Check(vm.ThemeMode==expected && store.Load().Settings.Theme==expected.ToString().ToLowerInvariant(),"Actual Ursa theme click persists "+expected);
}
foreach(var palette in vm.PaletteOptions.ToArray())
{
    paletteSelector.SelectedItem=palette;
    foreach(var dark in new[]{false,true})
    {
        vm.SetThemeCommand.Execute(dark?"dark":"light");settings.ShowTab("appearance");Pump();
        Check(App.ThemeController.Palette.ToString()==palette.Key,"Actual shared palette");
        Check(model.TextBrush is ISolidColorBrush { Color.A: 255 },"Visible text in every palette");
        var background=(ISolidColorBrush)model.SettingsBackgroundBrush;
        Check(background.Color != ((ISolidColorBrush)model.TextBrush).Color,"Distinct background and text");
        Capture(settings,$"appearance-{palette.Key}-{(dark?"dark":"light")}.png");
    }
}
vm.SelectedPalette=vm.PaletteOptions.Single(option=>option.Key=="DotNet");Pump();
vm.SetThemeCommand.Execute("system");Pump();model.Save();
Check(store.Load().Settings.Theme=="system","System mode persists separately from palette");
Check(model.Settings.Palette=="DotNet" && App.ThemeController.Palette==StudioPalette.DotNet,"Switching to system mode preserves the explicit palette");
var about=new AboutWindow { DataContext=vm };about.Show();Pump();
Check(!about.GetVisualDescendants().OfType<Button>().Single(button=>button.Name=="PART_MinimizeButton").IsEnabled && !about.GetVisualDescendants().OfType<Button>().Single(button=>button.Name=="PART_MaximizeRestoreButton").IsEnabled,"About respects disabled minimize and maximize capabilities");
foreach(var culture in new[]{"en-US","zh-CN"})
{
    var selectedPalette=model.Settings.Palette;
    var languageButton=settings.GetVisualDescendants().OfType<Button>().Single(button=>button.Flyout is MenuFlyout);
    var languageMenu=(MenuFlyout)languageButton.Flyout!;languageMenu.ShowAt(languageButton);Pump();
    var languageItem=languageMenu.ItemsSource!.Cast<AppearanceOption>().Single(option=>option.Key==culture);
    Check(languageMenu.IsOpen && languageItem.SelectCommand is not null,"Language popup opens with the actual language catalog");
    languageItem.SelectCommand!.Execute(null);languageMenu.Hide();Pump();
    foreach(var mode in new[]{StudioThemeMode.Light,StudioThemeMode.Dark,StudioThemeMode.System})
    {
        vm.ThemeMode=mode;Pump();
        var key=mode switch{StudioThemeMode.Light=>Language.LightTheme,StudioThemeMode.Dark=>Language.DarkTheme,_=>Language.SystemTheme};
        var themeButton=themeToggle.GetVisualDescendants().OfType<Button>().Single(button=>button.Name=="PART_ThemeButton");
        Check(ToolTip.GetTip(themeButton) is TextBlock text && text.Text==AppText.Get(key),"Actual Ursa tooltip follows "+culture+" / "+mode);
    }
    Check(model.Settings.Palette==selectedPalette,"Changing language preserves selected palette");
    Check(App.ThemeController.Palette==StudioPalette.DotNet,"Language changes preserve the actual shared palette");
    Check(store.Load().Settings.Language==culture,"Host language preference survives reload");
    Check(about.Title==AppText.Get(Language.About),"Existing about window changes language");
    Check(traySettings.Header==AppText.Get(Language.Settings),"Existing tray menu changes language");
    Check(vm.PlatformText.Contains(".NET " + Environment.Version.Major) && vm.VersionText == "v" + typeof(App).Assembly.GetName().Version!.ToString(3),"About shows actual app metadata");
    Capture(about,$"about-{culture}.png");
    foreach(var tab in new[]{"general","appearance","data","recommend","about"})
    {
        settings.ShowTab(tab);Pump();Capture(settings,$"settings-{tab}-{culture}.png");
        Check(settings.GetVisualDescendants().OfType<TextBlock>().Any(text=>!string.IsNullOrWhiteSpace(text.Text)),"Every settings route has actual content");
        var originalWidth=settings.Width;var originalHeight=settings.Height;
        settings.Width=settings.MinWidth;settings.Height=settings.MinHeight;Pump();
        Capture(settings,$"minimum-settings-{tab}-{culture}.png");
        var selectedTab=settings.GetVisualDescendants().OfType<TabItem>().Single(item=>item.IsSelected);
        Check(selectedTab.Bounds.Width==155,"Navigation keeps its fixed width in both languages");
        if(tab=="data")
        {
            foreach(var name in new[]{"ImportButton","ExportButton","OpenConfigFolderButton","ReloadConfigButton","StorageModeButton"})
            {
                var button=settings.FindControl<Button>(name)!;var position=button.TranslatePoint(default,settings);
                Check(position is {} point && point.X>=0 && point.Y>=0 && point.X+button.Bounds.Width<=settings.Bounds.Width && point.Y+button.Bounds.Height<=settings.Bounds.Height,"Minimum data page keeps the complete action visible: "+name);
            }
            settings.FindControl<Button>("ReloadConfigButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check(File.Exists(store.ConfigFile) && store.Load().Items.Count==1,"Minimum-width reload action executes against isolated configuration");
        }
        settings.Width=originalWidth;settings.Height=originalHeight;Pump();
    }
}
var dock=new DockWindow();model.Settings.Pinned=true;dock.Attach(model);dock.Show();Pump();
foreach(var edge in Enum.GetValues<DockEdge>())
{
    model.SetEdgeCommand.Execute(edge);Pump();
    Capture(dock,$"dock-{edge}.png");
    Check(dock.GetVisualDescendants().OfType<Button>().Any(button=>button.IsVisible),"Actual Dock commands remain visible on every edge");
}
model.SetEdgeCommand.Execute(DockEdge.Top);model.IsSearchOpen=true;Pump();Capture(dock,"dock-search.png");
model.IsEditMode=true;Pump();Capture(dock,"dock-edit.png");
var editor=new ItemDialogWindow();editor.Attach(model,new LauncherItem { Name="Fixture",Target="https://example.test",Kind=ItemKind.Web },false);editor.Show();Pump();
foreach(var kind in Enum.GetValues<ItemKind>())
{
    editor.FindControl<ComboBox>("KindBox")!.SelectedIndex=(int)kind;Pump();
    Capture(editor,$"editor-{kind}.png");
}
I18nManager.Instance.Culture=CultureInfo.GetCultureInfo("en-US");Pump();
Check(editor.Title==AppText.Get(Language.AddItem),"Open item editor changes language");
var kindBox=editor.FindControl<ComboBox>("KindBox")!;
Check(kindBox.SelectedItem as string==AppText.Get(Language.Command) && kindBox.SelectionBoxItem as string==AppText.Get(Language.Command),"Closed type selector refreshes its selected display when language changes");
Capture(editor,"editor-en-US.png");editor.Close();dock.Close();model.AttachHost(host);
model.SearchQuery="missing";model.IsSearchOpen=true;Pump();Check(model.Items.Count==0,"Actual search filters shortcuts");
model.SearchQuery="README";Pump();Check(model.Items.Count==1,"Actual search restores matching shortcut");
Complete(model.CheckUpdateAsync(silent:true));Check(model.UpdateAvailable && model.CanDownloadUpdate,"Check exposes matching verified package");
UpdateNotesWindow.ShowFor(settings,model);Pump();
var releaseNotes=settings.OwnedWindows.OfType<UpdateNotesWindow>().Single();
Capture(releaseNotes,"release-notes.png");releaseNotes.Close();
Check(about.GetVisualDescendants().OfType<TextBlock>().Any(text=>text.Text==model.UpdateResultText),"Actual about text leaves checking state");
Capture(about,"update-available.png");
model.DownloadUpdateCommand.Execute(null);Pump();Check(model.IsUpdateReady,"Download goes through injected Toolkit facade");
Capture(about,"update-downloaded.png");
model.InstallUpdateCommand.Execute(null);Pump();Check(updates.InstallCalls==1 && host.ExitCalls==0,"Controlled installation honors service exit policy");
updates.Mode="latest";Complete(model.CheckUpdateAsync(silent:true));Check(!model.UpdateAvailable && model.UpdateResultText.Contains(model.VersionText),"Current version clears obsolete update actions");
updates.Mode="failure";Complete(model.CheckUpdateAsync(silent:true));Check(model.UpdateResultText.Contains("controlled network failure"),"Network failure is visible and distinct from current version");
updates.Mode="unsupported";Complete(model.CheckUpdateAsync(silent:true));Check(model.NeedsUpdatePage && !model.CanDownloadUpdate,"Unsupported platform offers release page instead of unsafe download");
updates.Mode="available";Complete(model.CheckUpdateAsync(silent:true));updates.DelayDownload=true;
model.DownloadUpdateCommand.Execute(null);Pump();Check(model.IsDownloadingUpdate,"Pending download exposes progress and cancel");
model.CancelUpdateDownloadCommand.Execute(null);Pump();Check(!model.IsDownloadingUpdate && !model.IsUpdateReady,"Cancellation returns to a retryable state");
Check(File.Exists(store.ConfigFile),"Isolated settings remain intact");
about.Close();settings.Close();
Console.WriteLine($"PASS: {count} assertions; screenshots in {output}");

void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);count++;}
void Pump(){Dispatcher.UIThread.RunJobs();Thread.Sleep(30);Dispatcher.UIThread.RunJobs();}
void Complete(Task task){var deadline=DateTime.UtcNow.AddSeconds(20);while(!task.IsCompleted && DateTime.UtcNow<deadline)Pump();task.GetAwaiter().GetResult();Pump();}
void Capture(Window window,string name){Pump();window.CaptureRenderedFrame()!.Save(Path.Combine(output,name));}

sealed class FakeUpdates(string root):IUpdateService
{
    public UpdateServiceOptions Options {get;}=new(){Repository="QuickApp",ProductName="QuickApp"};
    public Version CurrentVersion=>new(0,9,3);public string CurrentVersionText=>"0.9.3";public int InstallCalls; public string Mode="available"; public bool DelayDownload;
    public Task<UpdateCheckResult> CheckAsync(Version? current=null,CancellationToken cancellationToken=default)
        => Task.FromResult(Mode switch
        {
            "latest" => UpdateCheckResult.Latest(),
            "failure" => UpdateCheckResult.Failed("controlled network failure"),
            "unsupported" => new UpdateCheckResult(new(new(9,0,0),"v9.0.0","QuickApp",null,"https://example.test/releases",null,null),true,null),
            _ => new UpdateCheckResult(new(new(9,0,0),"v9.0.0","QuickApp",null,"https://example.test/releases","https://example.test/setup.exe","QuickApp-win-x64-setup.exe","https://example.test/setup.exe.sha256"),true,null)
        });
    public async Task<UpdateDownloadResult> DownloadAsync(UpdateInfo info,IProgress<UpdateDownloadProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        if(DelayDownload) await Task.Delay(Timeout.Infinite,cancellationToken);
        var path=Path.Combine(root,info.AssetName!);File.WriteAllText(path,"controlled fixture");return new UpdateDownloadResult(path,info.AssetName!,18);
    }
    public Task<UpdateInstallResult> InstallAsync(UpdateDownloadResult download,CancellationToken cancellationToken=default){InstallCalls++;return Task.FromResult(new UpdateInstallResult(UpdateInstallKind.Manual,"Controlled installation") { ShouldExitApplication=false });}
}
sealed class FakeLauncher:ILauncher{public bool TryStart(LauncherItem item,out string? error){error=null;return true;}}
sealed class FakeIcons:IIconProvider{public Task<string?> GetIconFileAsync(LauncherItem item,CancellationToken cancellationToken=default)=>Task.FromResult<string?>(null);}
sealed class FakeApps:IInstalledAppProvider{public IReadOnlyList<LauncherItem> GetInstalledApps()=>[];public event Action? Changed{add{}remove{}}}
sealed class FakeAutoStart:IAutoStartService{public bool IsEnabled(string appName)=>false;public bool SetEnabled(string appName,bool enabled,string executablePath,out string? error){error=null;return true;}}
sealed class FakeHost:IDockHost{public int ExitCalls;public void ApplyEdge(DockEdge edge){} public void MonitorSelectionChanged(){} public void AutoHideDelayChanged(){} public void ShowSettings(){} public void Exit()=>ExitCalls++;public void OpenUrl(string url){}}
