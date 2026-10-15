using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReactiveUI;

namespace QuickApp.ViewModels;

/// <summary>
/// 视图模型基类。
/// 只用 ReactiveObject 的 RaisePropertyChanged，不依赖 RaiseAndSetIfChanged 的返回值
/// （ReactiveUI 24 起它返回字段类型而不是 bool，用它做 if 判断会在升级时炸）。
/// </summary>
public abstract class ViewModelBase : ReactiveObject
{
    protected ViewModelBase()
    {
        // 弱引用避免已经关闭的设置、条目编辑视图被全局语言事件保留。
        var reference = new System.WeakReference<ViewModelBase>(this);
        System.EventHandler<System.EventArgs>? handler = null;
        void Refresh()
        {
            if (reference.TryGetTarget(out var target)) target.OnCultureChanged();
            else CodeWF.Toolkit.Lang.I18nManager.Instance.CultureChanged -= handler;
        }
        handler = (_, _) =>
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Refresh();
            else Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        };
        CodeWF.Toolkit.Lang.I18nManager.Instance.CultureChanged += handler;
    }

    /// <summary>响应唯一全局语言变化，派生类可先同步偏好或重建选项。</summary>
    protected virtual void OnCultureChanged() => this.RaisePropertyChanged(string.Empty);

    /// <summary>值确实变了才通知，返回是否变化。</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        this.RaisePropertyChanged(propertyName);
        return true;
    }
}
