using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

/// <summary>
/// 监听 Prefab 资源的导入和移动，以处理重名问题并同步更新场景中的实例名称。
/// </summary>
public class PrefabCreationWatcher : AssetPostprocessor
{
    /// <summary>
    /// 存储需要延迟处理的 Prefab GUID 和新的名称。
    /// Key: Prefab GUID, Value: New Name
    /// </summary>
    private static Dictionary<string, string> s_prefabsToRenameInScene = new Dictionary<string, string>();

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // 清空列表以避免重复注册延迟调用
        s_prefabsToRenameInScene.Clear();

        // 1. 处理被导入/新建的 Prefab
        ProcessAssets(importedAssets);
        
        // 2. 处理被移动的 Prefab (movedAssets 包含了移动后的新路径)
        ProcessAssets(movedAssets);

        // --- 核心修复：延迟调用场景修改 ---
        if (s_prefabsToRenameInScene.Count > 0)
        {
            // 延迟到资源处理流程完成后，在主线程中安全地修改场景中的对象
            // 确保在 Unity 的主循环中执行场景操作，而不是在 AssetPostprocessor 的线程中
            EditorApplication.delayCall += RenameSceneInstancesDelayed;
        }
    }

    /// <summary>
    /// 核心处理逻辑：检测重名并重命名 Prefab 资产本身。
    /// </summary>
    private static void ProcessAssets(string[] assetPaths)
    {
        foreach (string assetPath in assetPaths)
        {
            if (!assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                continue;
            
            // 尝试加载资产并获取其内部名字
            var createdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (createdPrefab == null) continue;
            string initialName = createdPrefab.name;
            
            // 排除系统生成的 Prefab 名称（例如：从场景拖入时未手动改名的默认名）
            if (initialName.Contains(" (") && initialName.EndsWith(")")) continue;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string[] allPrefabsGuids = AssetDatabase.FindAssets("t:Prefab");
            
            // 遍历所有已存在的 Prefab 检查内部名称是否冲突
            foreach (var guid in allPrefabsGuids)
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(guid);
                if (existingPath == assetPath) // 跳过自己
                    continue;

                var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(existingPath);
                if (existingPrefab == null) continue;

                // 检查是否与已有的 Prefab 的内部名字重名
                string existingName = existingPrefab.name;
                if (existingName.Equals(initialName, System.StringComparison.OrdinalIgnoreCase))
                {
                    // 检测到重名，为新建的 Prefab 生成唯一的新名字
                    string dir = Path.GetDirectoryName(assetPath);
                    string newName = GetSafeUniquePrefabName(dir, initialName);
                    string newPath = Path.Combine(dir, newName + ".prefab").Replace("\\", "/");
                    
                    // 1. 文件改名/移动
                    string moveError = AssetDatabase.MoveAsset(assetPath, newPath);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        Debug.LogError($"[PrefabCreationWatcher] 移动资源失败: {moveError}");
                        continue;
                    }

                    // 2. 立即重新获取 GUID（虽然 MoveAsset 后 GUID 不变，但路径变了）
                    string prefabGuid = AssetDatabase.AssetPathToGUID(newPath);

                    // 3. 重新加载 Prefab 并改动其内部名字
                    createdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(newPath);
                    if (createdPrefab != null)
                    {
                        createdPrefab.name = newName;
                        EditorUtility.SetDirty(createdPrefab);
                        AssetDatabase.SaveAssets(); // 必须保存资产

                        // 收集信息，用于延迟处理场景实例
                        // 使用 GUID 作为 Key，避免路径在后续处理中再次变化导致查找失败
                        if (!s_prefabsToRenameInScene.ContainsKey(prefabGuid))
                        {
                            s_prefabsToRenameInScene.Add(prefabGuid, newName);
                        }
                        
                        Debug.Log($"[PrefabCreationWatcher] 已重命名重复 Prefab 文件及内部名称 → {newPath}");
                    }

                    EditorUtility.DisplayDialog(
                        "Prefab 自动改名",
                        $"你创建的 Prefab 名称 “{initialName}” 已存在。\n\n" +
                        $"系统已自动将其重命名为：\n“{newName}”",
                        "确定");
                    
                    // 因为我们修改了当前遍历的资产，为避免复杂性，处理完当前 assetPath 即可。
                    // 如果还需要处理其他同名的，需要更复杂的逻辑来避免重复重命名。
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 在资源导入流程完成后，延迟执行场景实例的改名操作。
    /// </summary>
    private static void RenameSceneInstancesDelayed()
    {
        EditorApplication.delayCall -= RenameSceneInstancesDelayed; // 移除委托，确保只执行一次

        if (s_prefabsToRenameInScene.Count == 0) return;

        Debug.Log("[PrefabCreationWatcher] 延迟调用：开始同步所有场景实例名称。");
        
        // 标记撤销操作的组
        Undo.SetCurrentGroupName("Auto Rename Prefab Instances");
        int undoGroupIndex = Undo.GetCurrentGroup();

        foreach (var kvp in s_prefabsToRenameInScene)
        {
            string prefabGuid = kvp.Key;
            string newName = kvp.Value;

            // 查找所有加载场景中的所有实例
            var instancesInScene = FindSceneInstancesByPrefabGuid(prefabGuid);

            foreach (var instanceInScene in instancesInScene)
            {
                // 确保它是 Prefab 的根实例
                if (PrefabUtility.GetNearestPrefabInstanceRoot(instanceInScene) == instanceInScene)
                {
                    // 注册完整的对象撤销
                    Undo.RegisterCompleteObjectUndo(instanceInScene, $"Rename Prefab Instance: {newName}");
                    
                    // 执行改名
                    instanceInScene.name = newName;
                    
                    // 标记场景修改
                    EditorSceneManager.MarkSceneDirty(instanceInScene.scene);
                    
                    Debug.Log($"[PrefabCreationWatcher] 已同步场景物体名称：{instanceInScene.name} (GUID: {prefabGuid})");
                }
            }
        }
        
        // 刷新 Hierarchy 视图
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        // 清除临时记录
        s_prefabsToRenameInScene.Clear();
    }

    /// <summary>
    /// 尝试找到场景中与 prefab 对应的**所有**实例（用于同步改名）。
    /// </summary>
    private static GameObject[] FindSceneInstancesByPrefabGuid(string prefabGuid)
    {
        var instances = new List<GameObject>();
        
        // 遍历所有已加载的场景
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            Scene currentScene = EditorSceneManager.GetSceneAt(i);
            
            // 遍历场景中的所有根对象及其子对象
            var rootGameObjects = currentScene.GetRootGameObjects();
            foreach (var root in rootGameObjects)
            {
                // 使用 GetComponentsInChildren (includeInactive: true) 查找所有 Transform
                var allTransforms = root.GetComponentsInChildren<Transform>(true);

                foreach (var t in allTransforms)
                {
                    GameObject go = t.gameObject;
                    
                    // 必须是 Prefab 实例的根对象
                    GameObject rootInstance = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                    if (rootInstance == null || rootInstance != go) continue;

                    // 获取 Prefab 资产的路径
                    string prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    if (string.IsNullOrEmpty(prefabAssetPath)) continue;

                    // 比较 GUID
                    if (AssetDatabase.AssetPathToGUID(prefabAssetPath) == prefabGuid)
                    {
                        instances.Add(go);
                    }
                }
            }
        }

        return instances.ToArray();
    }

    /// <summary>
    /// 生成一个在指定目录下唯一的新 Prefab 文件名。
    /// </summary>
    private static string GetSafeUniquePrefabName(string directory, string baseName)
    {
        // 移除不安全字符
        baseName = SanitizeName(baseName);
        
        int index = 1;
        string newName = baseName;

        // 查找是否有文件路径冲突，如果有，加上后缀
        if (File.Exists(Path.Combine(directory, newName + ".prefab")))
        {
            newName = $"{baseName}_Copy{index}";
            while (File.Exists(Path.Combine(directory, newName + ".prefab")))
            {
                index++;
                newName = $"{baseName}_Copy{index}";
            }
        }
        return newName;
    }

    /// <summary>
    /// 清理名称，确保其适合作为文件或 GameObject 名称。
    /// </summary>
    private static string SanitizeName(string name)
    {
        // 仅保留字母、数字和下划线
        string cleaned = new string(name
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray());

        if (string.IsNullOrEmpty(cleaned))
            cleaned = "Unnamed";

        // 确保第一个字符是字母，或前面加上一个字母
        if (!char.IsLetter(cleaned[0]))
            cleaned = "P" + cleaned;

        return cleaned;
    }
}