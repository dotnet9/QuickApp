using System;
using System.Threading;
using QuickApp.Core.Services;

namespace QuickApp.Platform.Windows;

/// <summary>
/// 单实例：命名 Mutex 判断本次是否第一实例，命名事件用于让后续实例唤醒已有窗口。
/// 名称带 Local\ 前缀，避免和别的用户会话互相干扰。
/// </summary>
public sealed class SingleInstanceService : ISingleInstance
{
    private const string MutexName = @"Local\QuickApp.SingleInstance.Mutex";
    private const string EventName = @"Local\QuickApp.SingleInstance.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly Action<string>? _log;
    private Thread? _listener;
    private volatile bool _disposed;

    public SingleInstanceService(Action<string>? log = null)
    {
        _log = log;

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
    }

    public bool IsFirstInstance { get; }

    public void SignalExistingInstance()
    {
        try
        {
            _activateEvent.Set();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"唤醒已有实例失败：{ex.Message}");
        }
    }

    public void Listen(Action onActivated)
    {
        if (!IsFirstInstance || onActivated is null)
        {
            return;
        }

        _listener = new Thread(() =>
        {
            while (!_disposed)
            {
                try
                {
                    if (!_activateEvent.WaitOne(TimeSpan.FromMilliseconds(500)))
                    {
                        continue;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    onActivated();
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"处理唤醒请求失败：{ex.Message}");
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
        _activateEvent.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 非拥有线程释放，忽略
        }

        _mutex.Dispose();
    }
}
