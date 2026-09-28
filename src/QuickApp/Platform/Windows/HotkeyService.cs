using System;
using System.Threading;
using Avalonia.Threading;
using QuickApp.Core.Services;

namespace QuickApp.Platform.Windows;

/// <summary>
/// 全局热键：专用线程上建一个 message-only 窗口接收 WM_HOTKEY。
/// 不子类化 Avalonia 主窗口的过程函数，也不占用主线程的消息循环，
/// 回调经 Dispatcher 切回 UI 线程后触发。
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 1;

    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private Thread? _thread;
    private IntPtr _hwnd;
    private int _nativeThreadId;
    private bool _disposeRequested;

    public HotkeyService(Action<string>? log = null) => _log = log;

    public string? LastParseError { get; private set; }

    public bool TryRegister(string gesture, Action callback, out string? error)
    {
        lock (_gate)
        {
            if (_thread is not null && _thread.IsAlive)
            {
                error = "热键已注册，不能重复注册。";
                return false;
            }

            if (!HotkeyGesture.TryParse(gesture, out HotkeyGesture parsed, out string? parseError))
            {
                LastParseError = parseError;
                error = parseError;
                return false;
            }

            LastParseError = null;

            uint modifiers = MapModifiers(parsed.Modifiers);
            var ready = new ManualResetEventSlim(false);
            var registered = false;
            string? registrationError = null;

            _hwnd = IntPtr.Zero;
            _thread = new Thread(() => MessageLoop(modifiers, (uint)parsed.VirtualKey, callback, ready, value =>
            {
                registered = value;
                registrationError = value ? null : "热键注册失败，可能已被其它程序占用。";
            }))
            {
                IsBackground = true,
                Name = "QuickApp.Hotkey"
            };
            _thread.Start();

            // 窗口创建与 RegisterHotKey 在热键线程上，等它给出结果（最多 3 秒）
            ready.Wait(TimeSpan.FromSeconds(3));

            if (!registered)
            {
                StopThread();
                error = registrationError ?? "热键窗口创建失败。";
                return false;
            }

            error = null;
            return true;
        }
    }

    private uint MapModifiers(HotkeyModifiers modifiers)
    {
        uint value = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            value |= NativeMethods.ModAlt;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            value |= NativeMethods.ModControl;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            value |= NativeMethods.ModShift;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            value |= NativeMethods.ModWin;
        }

        return value;
    }

    private void MessageLoop(uint modifiers, uint virtualKey, Action callback, ManualResetEventSlim ready, Action<bool> report)
    {
        _nativeThreadId = NativeMethods.GetCurrentThreadId();

        // 预定义的 STATIC 类 + HWND_MESSAGE 父窗口 = 不显示、不占任务栏的 message-only 窗口
        IntPtr hwnd = NativeMethods.CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0,
            NativeMethods.HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        bool ok = hwnd != IntPtr.Zero && NativeMethods.RegisterHotKey(hwnd, HotkeyId, modifiers, virtualKey);
        if (ok)
        {
            _hwnd = hwnd;
        }

        report(ok);
        ready.Set();

        if (!ok)
        {
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(hwnd);
            }

            _nativeThreadId = 0;
            return;
        }

        try
        {
            while (NativeMethods.GetMessageW(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == NativeMethods.WmHotkey && msg.wParam == HotkeyId)
                {
                    try
                    {
                        Dispatcher.UIThread.Post(callback);
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke("分发热键回调失败：" + ex.Message);
                    }

                    continue;
                }

                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }
        }
        finally
        {
            NativeMethods.UnregisterHotKey(hwnd, HotkeyId);
            NativeMethods.DestroyWindow(hwnd);
            _nativeThreadId = 0;
        }
    }

    private void StopThread()
    {
        Thread? thread = _thread;
        if (thread is not null && thread.IsAlive)
        {
            int nativeThreadId = Volatile.Read(ref _nativeThreadId);
            if (nativeThreadId != 0)
            {
                NativeMethods.PostThreadMessage(nativeThreadId, NativeMethods.WmQuit, IntPtr.Zero, IntPtr.Zero);
            }

            thread.Join(TimeSpan.FromSeconds(1));
        }

        _thread = null;
        _hwnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposeRequested)
            {
                return;
            }

            _disposeRequested = true;
            StopThread();
        }
    }
}
