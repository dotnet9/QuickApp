using System;
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

/// <summary>全局热键。</summary>
public interface IHotkeyService : IDisposable
{
    bool TryRegister(string gesture, IntPtr windowHandle, out string? error);
}

/// <summary>取图标（Windows 实现走 Shell API，缓存到磁盘）。</summary>
public interface IIconProvider
{
    /// <summary>返回图标缓存文件的绝对路径；取不到时返回 null。</summary>
    Task<string?> GetIconFileAsync(LauncherItem item, CancellationToken cancellationToken = default);
}

/// <summary>检查 GitHub Releases 更新。</summary>
public interface IUpdateChecker
{
    Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default);
}

/// <summary>开机自检更新的结果。</summary>
public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string Title,
    string? Notes,
    string PageUrl,
    string? AssetUrl,
    string? AssetName);
