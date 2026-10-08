using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using QuickApp.Platform.Windows;

namespace QuickApp.Platform;

/// <summary>
/// 根据当前系统选择平台实现。Windows 继续使用现有 Win32 服务，
/// 其它系统先使用不调用平台 API 的安全降级实现。
/// </summary>
public static class PlatformServices
{
    public static ISingleInstance CreateSingleInstance(Action<string>? log = null)
        => OperatingSystem.IsWindows()
            ? new SingleInstanceService(log)
            : new PortableSingleInstanceService(log);

    public static IHotkeyService CreateHotkeyService(Action<string>? log = null)
        => OperatingSystem.IsWindows()
            ? new HotkeyService(log)
            : new PortableHotkeyService(log);

    public static IAutoStartService CreateAutoStartService(Action<string>? log = null)
        => OperatingSystem.IsWindows()
            ? new AutoStartService()
            : new PortableAutoStartService(log);

    public static IInstalledAppProvider CreateInstalledAppProvider(Action<string>? log = null)
        => OperatingSystem.IsWindows()
            ? new InstalledAppProvider(log)
            : new PortableInstalledAppProvider(log);

    public static IIconProvider CreateIconProvider(string cacheDirectory, Action<string>? log = null)
        => OperatingSystem.IsWindows()
            ? new IconProvider(cacheDirectory, log)
            : new PortableIconProvider(log);
}

/// <summary>
/// 跨平台的最小单实例实现。使用独占锁文件避免依赖平台命名 Mutex，
/// 并通过临时目录中的信号文件通知已有实例。
/// </summary>
internal sealed class PortableSingleInstanceService : ISingleInstance
{
    private static readonly string LockPath = Path.Combine(Path.GetTempPath(), "QuickApp.SingleInstance.lock");
    private static readonly string SignalPath = Path.Combine(Path.GetTempPath(), "QuickApp.SingleInstance.activate");

    private readonly Action<string>? _log;
    private readonly FileStream? _lock;
    private Thread? _listener;
    private volatile bool _disposed;

    public PortableSingleInstanceService(Action<string>? log = null)
    {
        _log = log;
        try
        {
            _lock = new FileStream(
                LockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.DeleteOnClose);
            IsFirstInstance = true;
            TryDeleteSignal();
        }
        catch (IOException)
        {
            IsFirstInstance = false;
        }
        catch (UnauthorizedAccessException)
        {
            // 无法创建锁文件时让应用继续启动，避免把权限问题变成启动崩溃。
            IsFirstInstance = true;
            _log?.Invoke("无法创建单实例锁文件，将继续启动。");
        }
    }

    public bool IsFirstInstance { get; }

    public void SignalExistingInstance()
    {
        if (IsFirstInstance)
        {
            return;
        }

        try
        {
            File.WriteAllText(SignalPath, DateTime.UtcNow.Ticks.ToString());
        }
        catch (Exception ex)
        {
            _log?.Invoke("唤醒已有实例失败：" + ex.Message);
        }
    }

    public void Listen(Action onActivated)
    {
        if (!IsFirstInstance || onActivated is null || _listener is not null)
        {
            return;
        }

        _listener = new Thread(() =>
        {
            while (!_disposed)
            {
                Thread.Sleep(250);
                if (_disposed || !File.Exists(SignalPath))
                {
                    continue;
                }

                TryDeleteSignal();
                try
                {
                    onActivated();
                }
                catch (Exception ex)
                {
                    _log?.Invoke("处理唤醒请求失败：" + ex.Message);
                }
            }
        })
        {
            IsBackground = true,
            Name = "QuickApp.SingleInstance"
        };

        _listener.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_listener is { IsAlive: true } listener && !ReferenceEquals(listener, Thread.CurrentThread))
        {
            listener.Join(TimeSpan.FromSeconds(1));
        }

        _lock?.Dispose();
        if (IsFirstInstance)
        {
            TryDeleteSignal();
            try
            {
                File.Delete(LockPath);
            }
            catch
            {
                // 锁文件会在进程退出后自动释放，删除失败不影响下一次启动。
            }
        }
    }

    private static void TryDeleteSignal()
    {
        try
        {
            File.Delete(SignalPath);
        }
        catch
        {
            // 信号文件是尽力清理的临时状态。
        }
    }
}

/// <summary>非 Windows 平台的热键占位实现，避免错误调用 Win32 API。</summary>
internal sealed class PortableHotkeyService : IHotkeyService
{
    private readonly Action<string>? _log;

    public PortableHotkeyService(Action<string>? log = null) => _log = log;

    public string? LastParseError { get; private set; }

    public IReadOnlyList<string> RegisterBindings(IReadOnlyList<HotkeyBinding> bindings)
        => bindings.Select(binding => binding.Name + "：当前平台暂不支持全局热键。").ToArray();

    public bool TryRegister(string gesture, Action callback, out string? error)
    {
        if (!HotkeyGesture.TryParse(gesture, out _, out string? parseError))
        {
            LastParseError = parseError;
            error = parseError;
            return false;
        }

        LastParseError = null;
        error = "当前平台暂不支持全局热键。";
        _log?.Invoke(error);
        return false;
    }

    public void Dispose()
    {
    }
}

/// <summary>非 Windows 平台的开机启动占位实现。</summary>
internal sealed class PortableAutoStartService : IAutoStartService
{
    private readonly Action<string>? _log;

    public PortableAutoStartService(Action<string>? log = null) => _log = log;

    public bool IsEnabled(string appName) => false;

    public bool SetEnabled(string appName, bool enabled, string executablePath, out string? error)
    {
        error = "当前平台暂不支持开机启动。";
        _log?.Invoke(error);
        return false;
    }
}

/// <summary>非 Windows 平台暂不扫描系统应用，搜索结果为空而不是误报。</summary>
internal sealed class PortableInstalledAppProvider : IInstalledAppProvider
{
    private readonly Action<string>? _log;

    public PortableInstalledAppProvider(Action<string>? log = null) => _log = log;

    /// <summary>不扫描就没有来源变化，显式空访问器避免“从未触发”告警。</summary>
    public event Action? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<LauncherItem> GetInstalledApps()
    {
        _log?.Invoke("当前平台暂不支持扫描已安装应用。");
        return Array.Empty<LauncherItem>();
    }
}

/// <summary>非 Windows 平台暂不提取系统图标，由视图显示内置占位图标。</summary>
internal sealed class PortableIconProvider : IIconProvider
{
    private readonly Action<string>? _log;

    public PortableIconProvider(Action<string>? log = null) => _log = log;

    public Task<string?> GetIconFileAsync(LauncherItem item, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
