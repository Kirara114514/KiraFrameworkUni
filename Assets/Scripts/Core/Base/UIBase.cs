using UnityEngine;

/// <summary>
/// 所有可被 UIManager 管理的 UI 页面基类
/// </summary>
public abstract class UIBase : KiraObject
{
    // 强制子类声明自己应该属于哪个 Canvas 层级
    public abstract UILayer Layer { get; } 

    // 当前页面是否可见
    public bool IsVisible => gameObject.activeSelf;

    /// <summary>
    /// 页面显示时的生命周期函数。
    /// </summary>
    /// <param name="data">可选的参数数据</param>
    public virtual void OnShow(object data = null)
    {
        gameObject.SetActive(true);
        // 可在这里添加页面初始化、播放显示动画等逻辑
        Debug.Log($"UI: {GetType().Name} OnShow. Data: {data}");
    }

    /// <summary>
    /// 页面隐藏时的生命周期函数。
    /// </summary>
    public virtual void OnHide()
    {
        gameObject.SetActive(false);
        // 可在这里添加停止动画、保存数据等逻辑
        Debug.Log($"UI: {GetType().Name} OnHide.");
    }

    /// <summary>
    /// 页面销毁时的生命周期函数。
    /// </summary>
    public virtual void OnClose()
    {
        // 可在这里进行资源清理
        Debug.Log($"UI: {GetType().Name} OnClose.");
        Destroy(gameObject);
    }
}