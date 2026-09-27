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
