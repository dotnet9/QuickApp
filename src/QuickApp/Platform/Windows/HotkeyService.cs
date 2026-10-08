using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using QuickApp.Core.Services;

namespace QuickApp.Platform.Windows;

/// <summary>Dock 与条目快捷键共用一个 message-only 窗口和消息线程。</summary>
public sealed class HotkeyService : IHotkeyService
{
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private Thread? _thread;
    private int _nativeThreadId;
    private int _generation;
    private bool _disposed;

    public HotkeyService(Action<string>? log = null) => _log = log;
    public string? LastParseError { get; private set; }

    public bool TryRegister(string gesture, Action callback, out string? error)
    {
        LastParseError = HotkeyGesture.TryParse(gesture, out _, out string? parseError) ? null : parseError;
        IReadOnlyList<string> errors = RegisterBindings(new[] { new HotkeyBinding(gesture, "QuickApp", callback) });
        error = errors.Count > 0 ? errors[0] : null;
        return error is null;
    }

    public IReadOnlyList<string> RegisterBindings(IReadOnlyList<HotkeyBinding> bindings)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopThread();
            var errors = new List<string>();
            var parsed = new List<(HotkeyBinding Binding, HotkeyGesture Gesture)>();
            var seen = new HashSet<HotkeyGesture>();
            foreach (HotkeyBinding binding in bindings)
            {
                if (!HotkeyGesture.TryParse(binding.Gesture, out HotkeyGesture gesture, out string? error))
                    errors.Add(binding.Name + "：" + error);
                else if (!seen.Add(gesture)) errors.Add(binding.Name + "：快捷键重复。");
                else parsed.Add((binding, gesture));
            }
            if (parsed.Count == 0) return errors;
            int generation = _generation;
            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => MessageLoop(parsed, errors, ready, generation))
            { IsBackground = true, Name = "QuickApp.Hotkeys" };
            _thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(3)))
            {
                StopThread();
                return new[] { "快捷键注册超时，请重试。" };
            }
            return errors.ToArray();
        }
    }

    private void MessageLoop(List<(HotkeyBinding Binding, HotkeyGesture Gesture)> bindings,
        List<string> errors, ManualResetEventSlim ready, int generation)
    {
        IntPtr hwnd = IntPtr.Zero;
        var registered = new Dictionary<int, HotkeyBinding>();
        bool initialized = false;
        try
        {
            _nativeThreadId = NativeMethods.GetCurrentThreadId();
            hwnd = NativeMethods.CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0,
                NativeMethods.HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < bindings.Count; i++)
            {
                var (binding, gesture) = bindings[i];
                uint modifiers = MapModifiers(gesture.Modifiers) | NativeMethods.ModNoRepeat;
                if (hwnd != IntPtr.Zero && NativeMethods.RegisterHotKey(hwnd, i + 1, modifiers, (uint)gesture.VirtualKey))
                    registered[i + 1] = binding;
                else errors.Add(binding.Name + "（" + binding.Gesture + "）：快捷键已被其他程序占用或注册失败。");
            }
            initialized = true;
            ready.Set();
            if (registered.Count == 0 || generation != Volatile.Read(ref _generation)) return;
            while (NativeMethods.GetMessageW(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == NativeMethods.WmHotkey && registered.TryGetValue((int)msg.wParam, out HotkeyBinding? binding))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_disposed && generation == Volatile.Read(ref _generation)) binding.Callback();
                    });
                }
                else
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessageW(ref msg);
                }
            }
        }
        catch (Exception ex)
        {
            string error = "注册快捷键失败：" + ex.Message;
            _log?.Invoke(error);
            if (!initialized) { errors.Add(error); ready.Set(); }
        }
        finally
        {
            foreach (int id in registered.Keys) NativeMethods.UnregisterHotKey(hwnd, id);
            if (hwnd != IntPtr.Zero) NativeMethods.DestroyWindow(hwnd);
            _nativeThreadId = 0;
        }
    }

    private static uint MapModifiers(HotkeyModifiers modifiers)
        => ((modifiers & HotkeyModifiers.Ctrl) != 0 ? NativeMethods.ModControl : 0)
         | ((modifiers & HotkeyModifiers.Alt) != 0 ? NativeMethods.ModAlt : 0)
         | ((modifiers & HotkeyModifiers.Shift) != 0 ? NativeMethods.ModShift : 0)
         | ((modifiers & HotkeyModifiers.Win) != 0 ? NativeMethods.ModWin : 0);

    private void StopThread()
    {
        Interlocked.Increment(ref _generation);
        Thread? thread = _thread;
        if (thread is not null && thread.IsAlive)
        {
            int id = Volatile.Read(ref _nativeThreadId);
            if (id != 0) NativeMethods.PostThreadMessage(id, NativeMethods.WmQuit, IntPtr.Zero, IntPtr.Zero);
            if (!thread.Join(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("快捷键线程未能退出。");
        }
        _thread = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopThread();
        }
    }
}
