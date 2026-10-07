using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using ReactiveUI;

namespace QuickApp.ViewModels;

/// <summary>推荐卡片的交互状态（原型 recommend 页签的 phase 状态机）。</summary>
public enum RecommendedCardPhase
{
    /// <summary>未安装：显示「安装」。</summary>
    Idle,

    /// <summary>正在解析最新发布信息。</summary>
    Resolving,

    /// <summary>下载中：进度条 + 取消。</summary>
    Downloading,

    /// <summary>安装中（静默安装进程运行）。</summary>
    Installing,

    /// <summary>已安装：徽标 + 加入/移出 Dock。</summary>
    Installed
}

/// <summary>
/// 设置 · 推荐页里的一张应用卡片。安装包地址永远来自 <see cref="IRecommendedAppsService"/>
/// 的实时解析（不落盘、不写死），安装后主程序路径探测一次并记入状态文件。
/// </summary>
public sealed class RecommendedAppCardViewModel : ViewModelBase
{
    private readonly RecommendedApp _app;
    private readonly IRecommendedAppsService _service;
    private readonly RecommendedAppsStateStore _stateStore;
    private readonly Action<RecommendedAppCardViewModel> _installCompleted;
    private readonly Action<RecommendedAppCardViewModel, bool> _dockMembershipChanged;
    private readonly Func<string, string?> _toast;

    private RecommendedCardPhase _phase = RecommendedCardPhase.Idle;
    private string? _latestTag;
    private long? _assetSize;
    private double _downloadProgress;
    private string? _error;
    private bool _inDock;
    private string? _launcherPath;
    private CancellationTokenSource? _downloadCancellation;

    public RecommendedAppCardViewModel(
        RecommendedApp app,
        IRecommendedAppsService service,
        RecommendedAppsStateStore stateStore,
        Action<RecommendedAppCardViewModel> installCompleted,
        Action<RecommendedAppCardViewModel, bool> dockMembershipChanged,
        Func<string, string?> toast)
    {
        _app = app;
        _service = service;
        _stateStore = stateStore;
        _installCompleted = installCompleted;
        _dockMembershipChanged = dockMembershipChanged;
        _toast = toast;
        Icon = LoadIcon(app.Id);

        InstallCommand = ReactiveCommand.CreateFromTask(InstallAsync);
        CancelCommand = ReactiveCommand.Create(CancelDownload);
        ToggleDockCommand = ReactiveCommand.Create(ToggleDock);
        OpenReleasePageCommand = ReactiveCommand.Create(OpenReleasePage);
    }

    public RecommendedApp App => _app;

    public string Id => _app.Id;

    /// <summary>卡片图标：Assets/rec-{id}.png 命名约定，缺失时回退 QuickApp logo；两者都不可用为 null（图标区留空）。</summary>
    public IImage? Icon { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(_app.DisplayName) ? _app.Name : _app.DisplayName!;

    public string Description => _app.Description ?? string.Empty;

    public ICommand InstallCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ToggleDockCommand { get; }

    public ICommand OpenReleasePageCommand { get; }

    public RecommendedCardPhase Phase
    {
        get => _phase;
        private set
        {
            if (Set(ref _phase, value))
            {
                RaisePhaseProperties();
            }
        }
    }

    /// <summary>目录里写死的版本没有意义，最新版本号以实时解析结果为准；解析中为空。</summary>
    public string? LatestTag
    {
        get => _latestTag;
        private set => Set(ref _latestTag, value);
    }

    /// <summary>版本徽标文本：解析中不显示，失败显示占位。</summary>
    public string VersionBadge => _latestTag ?? string.Empty;

    /// <summary>资产行：安装包名 · 大小 ·（已装版本）。</summary>
    public string MetaText
    {
        get
        {
            string asset = _assetSize is > 0
                ? FormatSize(_assetSize.Value)
                : string.Empty;
            string installed = InstalledRecord?.Version is { Length: > 0 } version
                ? "已装 " + version
                : string.Empty;
            return string.Join(" · ", new[] { asset, installed }.Where(part => part.Length > 0));
        }
    }

    public RecommendedInstallRecord? InstalledRecord { get; private set; }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => Set(ref _downloadProgress, value);
    }

    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>快捷方式是否已在 Dock（双向同步：编辑模式移除后按钮变回「加入 Dock」）。</summary>
    public bool InDock
    {
        get => _inDock;
        private set
        {
            if (Set(ref _inDock, value))
            {
                this.RaisePropertyChanged(nameof(InDock));
            }
        }
    }

    public bool IsIdle => Phase == RecommendedCardPhase.Idle;

    public bool IsResolving => Phase == RecommendedCardPhase.Resolving;

    public bool IsDownloading => Phase == RecommendedCardPhase.Downloading;

    public bool IsInstalling => Phase == RecommendedCardPhase.Installing;

    public bool IsInstalled => Phase == RecommendedCardPhase.Installed;

    public string InstallButtonText => IsIdle ? "安装" : "重试";

    public string DockButtonText => InDock ? "移出 Dock" : "加入 Dock";

    public string InstallHint => IsResolving ? "正在获取最新版本信息…" : "正在下载安装包…";

    public string InstallingHint => IsInstalling
        ? "正在静默安装，完成后自动加入 Dock…"
        : "正在安装，完成后自动加入 Dock…";

    public void Initialize(bool inDock, RecommendedInstallRecord? record)
    {
        InDock = inDock;
        InstalledRecord = record;
        if (record is not null)
        {
            _launcherPath = record.LauncherPath;
            Phase = RecommendedCardPhase.Installed;
        }

        this.RaisePropertyChanged(nameof(MetaText));
        RaisePhaseProperties();
    }

    /// <summary>打开推荐页时轻量刷新：已解析过就直接用缓存，否则拉一次最新发布（仅在未安装态）。</summary>
    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (Phase is RecommendedCardPhase.Downloading or RecommendedCardPhase.Installing)
        {
            return;
        }

        Error = null;
        if (Phase == RecommendedCardPhase.Idle)
        {
            Phase = RecommendedCardPhase.Resolving;
        }

        try
        {
            RecommendedRelease? release = await _service.ResolveAsync(_app, force, cancellationToken).ConfigureAwait(true);
            if (release is null)
            {
                LatestTag = null;
                _assetSize = null;
                Error = "暂时无法获取版本信息";
                if (Phase == RecommendedCardPhase.Resolving)
                {
                    Phase = RecommendedCardPhase.Idle;
                }
                return;
            }

            LatestTag = release.Tag;
            _assetSize = release.AssetSize;
            if (Phase == RecommendedCardPhase.Resolving)
            {
                Phase = InstalledRecord is not null ? RecommendedCardPhase.Installed : RecommendedCardPhase.Idle;
            }
        }
        catch (OperationCanceledException)
        {
            if (Phase == RecommendedCardPhase.Resolving)
            {
                Phase = InstalledRecord is not null ? RecommendedCardPhase.Installed : RecommendedCardPhase.Idle;
            }
        }
        catch (Exception ex)
        {
            Error = "版本信息获取失败";
            if (Phase == RecommendedCardPhase.Resolving)
            {
                Phase = InstalledRecord is not null ? RecommendedCardPhase.Installed : RecommendedCardPhase.Idle;
            }
            _ = ex;
        }
        finally
        {
            this.RaisePropertyChanged(nameof(MetaText));
            this.RaisePropertyChanged(nameof(VersionBadge));
        }
    }

    private async Task InstallAsync()
    {
        if (Phase is RecommendedCardPhase.Downloading or RecommendedCardPhase.Installing or RecommendedCardPhase.Resolving)
        {
            return;
        }

        Error = null;
        Phase = RecommendedCardPhase.Resolving;
        RecommendedRelease? release = await _service.ResolveAsync(_app, force: true).ConfigureAwait(true);
        if (release?.AssetUrl is null || release.ChecksumUrl is null)
        {
            Phase = InstalledRecord is not null ? RecommendedCardPhase.Installed : RecommendedCardPhase.Idle;
            Error = "没有匹配当前系统的安装包";
            _toast?.Invoke(DisplayName + "：没有匹配当前系统的安装包");
            return;
        }

        var update = new UpdateInfo(
            VersionUtil.Parse(release.Tag) ?? new Version(0, 0, 0),
            release.Tag,
            DisplayName,
            null,
            release.PageUrl,
            release.AssetUrl,
            release.AssetName,
            release.ChecksumUrl,
            release.AssetSize);

        Phase = RecommendedCardPhase.Downloading;
        DownloadProgress = 0;
        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                DownloadProgress = value.Percentage ?? DownloadProgress;
            });

            Core.Services.UpdateDownloadResult result = await DownloadAsync(update, progress, _downloadCancellation.Token).ConfigureAwait(true);

            Phase = RecommendedCardPhase.Installing;
            _toast?.Invoke(DisplayName + " 下载完成，正在安装");
            bool installerOk = await RunInstallerAsync(result.FilePath).ConfigureAwait(true);

            Phase = RecommendedCardPhase.Installed;
            _launcherPath = ProbeLauncherPath();
            var record = new RecommendedInstallRecord
            {
                AppId = _app.Id,
                Version = release.Tag,
                LauncherPath = _launcherPath,
                InstalledAt = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            InstalledRecord = record;
            _stateStore.Upsert(record);
            this.RaisePropertyChanged(nameof(MetaText));

            // 静默安装成功或探测到主程序 → 自动加入 Dock；
            // 手动安装（无静默参数/非 Windows）不确认不动 Dock，等用户点「加入 Dock」再探测。
            bool silentInstall = OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(_app.InstallArgumentsWindows);
            if (installerOk && (silentInstall || _launcherPath is not null))
            {
                SetInDock(true);
                _installCompleted(this);
                _toast?.Invoke("已安装 " + DisplayName + "，快捷方式已加入 Dock");
            }
            else if (installerOk)
            {
                _toast?.Invoke(DisplayName + " 安装引导已完成，确认装好后点「加入 Dock」");
            }
            else
            {
                _toast?.Invoke(DisplayName + " 安装未完成，可重试或从发布页手动安装");
            }
        }
        catch (OperationCanceledException)
        {
            Phase = InstalledRecord is not null ? RecommendedCardPhase.Installed : RecommendedCardPhase.Idle;
            _toast?.Invoke("已取消下载 " + DisplayName);
        }
        catch (Exception ex)
        {
            Phase = InstalledRecord is not null ? RecommendedCardPhase.Installed : RecommendedCardPhase.Idle;
            Error = "下载或安装失败";
            _toast?.Invoke(DisplayName + " 安装失败：" + ex.Message);
        }
        finally
        {
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
            RaisePhaseProperties();
        }
    }

    /// <summary>实际下载由 DockViewModel 注入（复用自更新下载器与 HTTP 客户端）。</summary>
    public Func<UpdateInfo, IProgress<UpdateDownloadProgress>, CancellationToken, Task<UpdateDownloadResult>> DownloadAsync { get; set; }
        = static (_, _, _) => throw new InvalidOperationException("下载器未初始化");

    /// <summary>实际安装由 DockViewModel 注入（平台相关：Windows 静默安装、macOS 挂载、Linux 安装 deb）。</summary>
    public Func<string, Task<bool>> RunInstallerAsync { get; set; }
        = static _ => throw new InvalidOperationException("安装器未初始化");

    private void CancelDownload() => _downloadCancellation?.Cancel();

    /// <summary>安装后探测主程序路径（配置里给候选，磁盘上选第一个存在的）。</summary>
    private string? ProbeLauncherPath()
    {
        foreach (string candidate in _app.LauncherPathsForCurrentPlatform())
        {
            string expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (System.IO.File.Exists(expanded) || System.IO.Directory.Exists(expanded))
            {
                return expanded;
            }
        }

        return null;
    }

    private void ToggleDock()
    {
        if (!InDock && _launcherPath is null)
        {
            // 手动安装的路径未知：加入时再探测一次，探测到就回写状态
            _launcherPath = ProbeLauncherPath();
            if (_launcherPath is not null && InstalledRecord is not null)
            {
                InstalledRecord.LauncherPath = _launcherPath;
                _stateStore.Upsert(InstalledRecord);
            }
        }

        SetInDock(!InDock);
    }

    private void SetInDock(bool value)
    {
        if (InDock == value)
        {
            return;
        }

        InDock = value;
        this.RaisePropertyChanged(nameof(DockButtonText));
        _dockMembershipChanged(this, value);
    }

    private void OpenReleasePage()
    {
        string url = $"https://github.com/{_app.Owner}/{_app.Repo}/releases/latest";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _toast?.Invoke("打开发布页失败：" + ex.Message);
        }
    }

    private void RaisePhaseProperties()
    {
        this.RaisePropertyChanged(nameof(Phase));
        this.RaisePropertyChanged(nameof(IsIdle));
        this.RaisePropertyChanged(nameof(IsResolving));
        this.RaisePropertyChanged(nameof(IsDownloading));
        this.RaisePropertyChanged(nameof(IsInstalling));
        this.RaisePropertyChanged(nameof(IsInstalled));
        this.RaisePropertyChanged(nameof(InstallButtonText));
        this.RaisePropertyChanged(nameof(InstallHint));
        this.RaisePropertyChanged(nameof(InstallingHint));
        this.RaisePropertyChanged(nameof(DockButtonText));
    }

    private static string FormatSize(long bytes)
    {
        const long OneMb = 1024 * 1024;
        if (bytes >= OneMb)
        {
            return (bytes / (double)OneMb).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }

        return Math.Max(1, bytes / 1024) + " KB";
    }

    private static IImage? LoadIcon(string appId)
    {
        try
        {
            return new Avalonia.Media.Imaging.Bitmap(
                Avalonia.Platform.AssetLoader.Open(new Uri($"avares://QuickApp/Assets/rec-{appId}.png")));
        }
        catch
        {
            try
            {
                return new Avalonia.Media.Imaging.Bitmap(
                    Avalonia.Platform.AssetLoader.Open(new Uri("avares://QuickApp/Assets/logo.png")));
            }
            catch
            {
                return null;
            }
        }
    }
}
