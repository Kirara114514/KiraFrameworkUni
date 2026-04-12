using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// MVVM 架构中所有 ViewModel 的基类。
/// 实现了 INotifyPropertyChanged 接口，提供属性变更通知机制。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// 当属性值发生变化时触发的事件。
    /// </summary>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// 触发 PropertyChanged 事件。
    /// </summary>
    /// <param name="propertyName">发生变更的属性名称。由 [CallerMemberName] 自动填充。</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 检查 backing field (支持字段) 的值是否与新值不同，如果不同，则设置新值并触发属性变更通知。
    /// </summary>
    /// <typeparam name="T">属性的类型。</typeparam>
    /// <param name="backingField">backing field 的引用。</param>
    /// <param name="newValue">属性的新值。</param>
    /// <param name="propertyName">发生变更的属性名称。由 [CallerMemberName] 自动填充。</param>
    /// <returns>如果值已更改并触发了通知，则返回 true；否则返回 false。</returns>
    protected bool SetProperty<T>(ref T backingField, T newValue, [CallerMemberName] string propertyName = null)
    {
        // 1. 使用 EqualityComparer<T>.Default.Equals 进行值比较，安全地处理 null 值和值类型。
        // 如果新值与旧值相同，则不进行任何操作。
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(backingField, newValue))
        {
            return false;
        }

        // 2. 值不同，设置新的值。
        backingField = newValue;

        // 3. 触发属性变更通知。
        OnPropertyChanged(propertyName);
        
        return true;
    }
}