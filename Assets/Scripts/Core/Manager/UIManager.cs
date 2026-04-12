using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 顶级 UI 管理器 (静态类)，负责 UI 界面的分层、加载、显示和生命周期管理。
/// </summary>
public static class UIManager
{
    // 存储所有 Canvas 根节点的字典：键是 UILayer，值是对应的 Transform
    private static readonly Dictionary<UILayer, Transform> _layerRoots = new Dictionary<UILayer, Transform>();

    // 存储所有已打开的页面实例：键是页面的 Type，值是 UIBase 实例
    private static readonly Dictionary<Type, UIBase> _openedPages = new Dictionary<Type, UIBase>();

    private static Transform _uiRoot;
    
    // *******************************************************************
    // 1. 初始化
    // *******************************************************************

    /// <summary>
    /// 初始化 UIManager，创建所有 UI 层级 Canvas。
    /// 应该在游戏启动时调用一次。
    /// </summary>
    /// <param name="uiRoot">所有 Canvas 的父对象（通常是CanvasScaler所在的对象）</param>
    public static void Initialize(Transform uiRoot)
    {
        if (_uiRoot != null) return; // 防止重复初始化
        
        _uiRoot = uiRoot;
        Debug.Log("UIManager: Starting initialization.");

        // 遍历所有 UILayer 枚举值，创建或配置对应的 Canvas
        foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
        {
            GameObject layerGo = new GameObject($"Canvas_{layer}");
            layerGo.transform.SetParent(uiRoot, false);
            
            // 确保这是一个新的 Canvas，并设置其深度
            Canvas canvas = layerGo.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            
            // 关键：利用 UILayer 枚举值设置 sortingOrder 来实现 Canvas 分层
            // 每个层级之间留出间隔（例如10），确保其深度互不干扰
            canvas.sortingOrder = (int)layer * 10; 

            // 只有需要交互的层级才添加 GraphicRaycaster
            if (layer >= UILayer.FullScreen)
            {
                layerGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            _layerRoots[layer] = layerGo.transform;
        }
        
        Debug.Log($"UIManager initialized successfully with {_layerRoots.Count} layers.");
    }
    
    // *******************************************************************
    // 2. 核心显示/隐藏/关闭逻辑
    // *******************************************************************

    /// <summary>
    /// 显示一个 UI 页面。如果是第一次打开则加载实例化，否则直接调用 OnShow。
    /// </summary>
    /// <typeparam name="T">页面的具体类型，必须继承自 UIBase</typeparam>
    /// <param name="prefabPath">页面预制体的资源路径（例如：Resources/UI/SettingsPage）</param>
    /// <param name="data">传递给页面的可选数据</param>
    /// <returns>返回创建/获取的页面实例</returns>
    public static T Show<T>(string prefabPath, object data = null) where T : UIBase
    {
        Type pageType = typeof(T);

        // 1. 检查是否已经打开
        if (_openedPages.TryGetValue(pageType, out UIBase existingUI))
        {
            // 如果已打开，重新调用 OnShow（用于刷新或切换）
            T existingT = existingUI as T;
            existingT?.OnShow(data);
            return existingT;
        }

        // 2. 加载预制体（实际项目推荐使用 Addressables 或 AssetBundle）
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[UIManager] UI 预制体加载失败: {prefabPath}");
            return null;
        }

        // 3. 实例化并获取组件
        GameObject go = GameObject.Instantiate(prefab);
        T page = go.GetComponent<T>();

        if (page == null)
        {
            Debug.LogError($"[UIManager] 预制体 {prefabPath} 上没有找到组件 {pageType.Name}");
            GameObject.Destroy(go);
            return null;
        }
        
        // 4. 核心分层逻辑：设置父对象到正确的 Canvas 层级
        UILayer layer = page.Layer;
        if (_layerRoots.TryGetValue(layer, out Transform root))
        {
            // false 表示保持世界坐标不变，但因为是新实例，所以通常无影响
            go.transform.SetParent(root, false); 
        }
        else
        {
            Debug.LogError($"[UIManager] 未找到 UI 层级 Canvas: {layer}");
        }

        // 5. 注册并调用生命周期
        _openedPages[pageType] = page;
        page.OnShow(data); // 调用显示生命周期

        return page;
    }

    /// <summary>
    /// 隐藏一个 UI 页面 (不销毁)。
    /// </summary>
    public static void Hide<T>() where T : UIBase
    {
        Type pageType = typeof(T);
        if (_openedPages.TryGetValue(pageType, out UIBase page))
        {
            page.OnHide();
        }
    }

    /// <summary>
    /// 关闭并销毁一个 UI 页面。
    /// </summary>
    public static void Close<T>() where T : UIBase
    {
        Type pageType = typeof(T);
        if (_openedPages.TryGetValue(pageType, out UIBase page))
        {
            _openedPages.Remove(pageType);
            page.OnClose();
        }
    }
    
    // *******************************************************************
    // 3. 辅助函数
    // *******************************************************************

    /// <summary>
    /// 获取一个已打开的 UI 页面实例。
    /// </summary>
    public static T GetPage<T>() where T : UIBase
    {
        Type pageType = typeof(T);
        if (_openedPages.TryGetValue(pageType, out UIBase page))
        {
            return page as T;
        }
        return null;
    }

    /// <summary>
    /// 检查页面是否已打开。
    /// </summary>
    public static bool IsPageOpen<T>() where T : UIBase
    {
        return _openedPages.ContainsKey(typeof(T));
    }
}