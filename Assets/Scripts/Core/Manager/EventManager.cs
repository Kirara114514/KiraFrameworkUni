using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Statics;

namespace Game.Framework
{
    /// <summary>
    /// 泛型事件中心
    /// </summary>
    public class EventManager
    {
        public static EventManager Instance { get; } = new EventManager();
        private EventManager() { }

        // 基础事件字典
        private readonly Dictionary<Type, Action> _eventDict = new Dictionary<Type, Action>();
        // 带参事件字典
        private readonly Dictionary<Type, Delegate> _eventArgDict = new Dictionary<Type, Delegate>();

        #region 无参数事件接口

        /// <summary>
        /// 注册无参事件。T 必须继承自 IKiraEventKey。
        /// </summary>
        public void RegisterEvent<T>(Action listener) where T : IKiraEventKey
        {
            Type eventType = typeof(T);
            if (_eventDict.ContainsKey(eventType))
                _eventDict[eventType] += listener;
            else
                _eventDict.Add(eventType, listener);
        }

        /// <summary>
        /// 注销无参事件。
        /// </summary>
        public void UnregisterEvent<T>(Action listener) where T : IKiraEventKey
        {
            Type eventType = typeof(T);
            if (_eventDict.TryGetValue(eventType, out Action currentDel))
            {
                currentDel -= listener;
                if (currentDel == null) _eventDict.Remove(eventType);
                else _eventDict[eventType] = currentDel;
            }
        }

        /// <summary>
        /// 触发无参事件。
        /// </summary>
        public void FireEvent<T>() where T : IKiraEventKey
        {
            Type eventType = typeof(T);
            if (_eventDict.TryGetValue(eventType, out Action thisEvent))
            {
                // 执行副本，防止回调内修改字典导致崩溃
                thisEvent?.Invoke();
            }
        }

        #endregion

        #region 带参数事件接口

        /// <summary>
        /// 注册带参事件。T 必须继承自 IKiraEventKey。
        /// </summary>
        public void RegisterEvent<T, TParam>(Action<TParam> listener) where T : IKiraEventKey
        {
            Type eventType = typeof(T);
            if (_eventArgDict.TryGetValue(eventType, out Delegate d))
            {
                if (d is Action<TParam> existingAction)
                    _eventArgDict[eventType] = existingAction + listener;
                else
                    Debug.LogError($"[EventManager] 注册冲突！事件 {eventType.Name} 的参数类型不匹配。");
            }
            else
            {
                _eventArgDict.Add(eventType, listener);
            }
        }

        /// <summary>
        /// 注销带参事件。
        /// </summary>
        public void UnregisterEvent<T, TParam>(Action<TParam> listener) where T : IKiraEventKey
        {
            Type eventType = typeof(T);
            if (_eventArgDict.TryGetValue(eventType, out Delegate d))
            {
                if (d is Action<TParam> existingAction)
                {
                    var newDel = existingAction - listener;
                    if (newDel == null) _eventArgDict.Remove(eventType);
                    else _eventArgDict[eventType] = newDel;
                }
            }
        }

        /// <summary>
        /// 触发带参事件。
        /// </summary>
        public void FireEvent<T, TParam>(TParam arg) where T : IKiraEventKey
        {
            Type eventType = typeof(T);
            if (_eventArgDict.TryGetValue(eventType, out Delegate d))
            {
                if (d is Action<TParam> callback)
                {
                    callback.Invoke(arg);
                }
                else
                {
                    Debug.LogError($"[EventManager] 参数类型不匹配！事件: {eventType.Name}, 期望: {d.GetType().GetGenericArguments()[0].Name}, 传入: {typeof(TParam).Name}");
                }
            }
        }

        #endregion
        /// <summary>
        /// 危险操作：清除所有注册的事件
        /// </summary>
        public void ClearAllEvents()
        {
            _eventDict.Clear();
            _eventArgDict.Clear();
        }
    }
}