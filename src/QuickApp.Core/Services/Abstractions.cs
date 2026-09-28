using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>启动一个快捷项。</summary>
public interface ILauncher
{
    /// <summary>成功返回 true；失败把原因写入 <paramref name="error"/>。</summary>
    bool TryStart(LauncherItem item, out string? error);
}

/// <summary>开机启动开关（Windows 实现写 HKCU\...\Run）。</summary>
public interface IAutoStartService
{
    bool IsEnabled(string appName);

    bool SetEnabled(string appName, bool enabled, string executablePath, out string? error);
}

/// <summary>单实例：第二个进程负责把已有窗口唤到前台并退出。</summary>
public interface ISingleInstance : IDisposable
{
    /// <summary>本次是否是第一个实例。</summary>
    bool IsFirstInstance { get; }

    /// <summary>通知已有实例「把自己显示出来」。</summary>
    void SignalExistingInstance();

    /// <summary>监听来自后续实例的唤醒请求，回调在后台线程触发。</summary>
    void Listen(Action onActivated);
}

/// <summary>全局热键。实现自带消息窗口，调用方只给键位描述与回调。</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>注册热键（如 "Ctrl+Alt+Space"）。成功后按下热键会触发 <paramref name="callback"/>（已在 UI 线程）。</summary>
    bool TryRegister(string gesture, Action callback, out string? error);

    /// <summary>解析失败的键位描述；注册成功后为 null。</summary>
    string? LastParseError { get; }
}

/// <summary>取图标（Windows 实现走 Shell API，缓存到磁盘）。</summary>
public interface IIconProvider
{
    /// <summary>返回图标缓存文件的绝对路径；取不到时返回 null。</summary>
    Task<string?> GetIconFileAsync(LauncherItem item, CancellationToken cancellationToken = default);
}

/// <summary>枚举操作系统中可启动、但尚未加入 QuickApp 配置的应用。</summary>
public interface IInstalledAppProvider
{
    /// <summary>返回一份稳定排序的系统应用快照；无法访问系统目录时返回空集合。</summary>
    IReadOnlyList<LauncherItem> GetInstalledApps();
}

/// <summary>检查 GitHub Releases 更新。</summary>
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellationToken = default);
}

/// <summary>更新检查结果，区分无更新和网络/API 失败。</summary>
public sealed record UpdateCheckResult(UpdateInfo? Update, bool Succeeded, string? Error)
{
    public static UpdateCheckResult Latest() => new(null, true, null);

    public static UpdateCheckResult Failed(string error) => new(null, false, error);
}

/// <summary>下载更新资产并报告进度。</summary>
public interface IUpdateDownloader
{
    Task<UpdateDownloadResult> DownloadAsync(
        UpdateInfo update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percentage => TotalBytes is > 0
        ? BytesReceived * 100d / TotalBytes.Value
        : null;
}

public sealed record UpdateDownloadResult(string FilePath, string FileName, long BytesReceived);

/// <summary>开机自检更新的结果。</summary>
public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string Title,
    string? Notes,
    string PageUrl,
    string? AssetUrl,
    string? AssetName);
