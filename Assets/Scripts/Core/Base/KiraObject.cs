using UnityEngine;
using System;
using Game.Framework;
using Game.Statics; // 必须引用接口所在的命名空间

/// <summary>
/// 业务基类：封装了事件系统的快速调用接口
/// </summary>
public abstract class KiraObject : MonoBehaviour
{
    #region 无参数事件调用示例
    /*
     * [注意] 只有在 SO 中 FinalValue 为空的节点（生成为类且继承 IKiraEventKey）才能作为 T 传入
     * [注册] RegisterEvent<KiraEventKey.GamePlay.GameStart>(OnGameStart);
     * [触发] FireEvent<KiraEventKey.GamePlay.GameStart>();
     */
    
    protected void RegisterEvent<T>(Action listener) where T : IKiraEventKey 
        => EventManager.Instance.RegisterEvent<T>(listener);
    
    protected void UnregisterEvent<T>(Action listener) where T : IKiraEventKey 
        => EventManager.Instance.UnregisterEvent<T>(listener);

    protected void FireEvent<T>() where T : IKiraEventKey 
        => EventManager.Instance.FireEvent<T>();
    #endregion

    #region 带参数事件调用示例
    /*
     * [注册] RegisterEvent<KiraEventKey.Player.OnHpChanged, float>(OnHpChanged);
     * [触发] FireEvent<KiraEventKey.Player.OnHpChanged, float>(80.5f);
     */

    protected void RegisterEvent<T, TParam>(Action<TParam> listener) where T : IKiraEventKey 
        => EventManager.Instance.RegisterEvent<T, TParam>(listener);

    protected void UnregisterEvent<T, TParam>(Action<TParam> listener) where T : IKiraEventKey 
        => EventManager.Instance.UnregisterEvent<T, TParam>(listener);

    protected void FireEvent<T, TParam>(TParam arg) where T : IKiraEventKey 
        => EventManager.Instance.FireEvent<T, TParam>(arg);
    #endregion
}