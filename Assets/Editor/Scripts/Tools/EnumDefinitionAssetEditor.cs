// 文件名: Assets/Editor/EnumDefinitionAssetEditor.cs (替换现有内容)

using UnityEditor;
using UnityEngine;
using System.Text;
using System.IO;
using System.Collections.Generic;

[CustomEditor(typeof(EnumDefinitionAsset))]
public class EnumDefinitionAssetEditor : Editor
{
    private EnumDefinitionAsset targetAsset;
    
    // 建议的生成路径，相对于 Assets 文件夹
    private const string GENERATED_CODE_PATH = "Assets/Scripts/Generated/GeneratedEnums/"; 

    private void OnEnable()
    {
        targetAsset = (EnumDefinitionAsset)target;

        // 【核心优化点】：同步文件名到枚举名称
        
        // 1. 获取 SO 实例的文件名 (去除可能的文件扩展名)
        string assetFileName = targetAsset.name;

        // 2. 检查当前 SO 文件名是否与 enumName 不同
        // 且文件名不是默认创建时带的 "NewEnumDefinition" 前缀
        if (!string.Equals(targetAsset.enumName, assetFileName) && 
            !assetFileName.StartsWith("NewEnumDefinition"))
        {
            // 3. 将文件名赋值给 enumName 字段
            // 注意：这里需要使用 EditorUtility.SetDirty 来标记 ScriptableObject 已修改，
            // 以确保在下次保存项目时数据能被持久化。
            targetAsset.enumName = assetFileName;
            EditorUtility.SetDirty(targetAsset);
        }
    }

    public override void OnInspectorGUI()
    {
        // 确保 Inspector 可以修改 ScriptableObject
        serializedObject.Update(); 

        // 绘制默认的属性
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        
        // 核心功能按钮
        if (GUILayout.Button("一键生成/更新枚举脚本", GUILayout.Height(30)))
        {
            // 确保 Inspector 中的修改被保存到 ScriptableObject
            serializedObject.ApplyModifiedProperties();
            
            // 执行代码生成
            GenerateEnumCode(targetAsset);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("生成路径:", GENERATED_CODE_PATH, EditorStyles.miniLabel);

        // 最终应用所有属性修改
        serializedObject.ApplyModifiedProperties();
    }
    
    /// <summary>
    /// 根据 ScriptableObject 的数据生成 C# 枚举代码并写入文件。（此处代码不变）
    /// </summary>
    private static void GenerateEnumCode(EnumDefinitionAsset asset)
    {
        // --- 1. 基础校验 ---
        if (asset == null || string.IsNullOrWhiteSpace(asset.enumName))
        {
            Debug.LogError("【Enum Generator】错误：Enum 名称不能为空。请填写 Enum Name 字段。");
            return;
        }
        
        // 清洗枚举名，确保它是有效的 C# 标识符
        string sanitizedEnumName = SanitizeIdentifier(asset.enumName);
        if (string.IsNullOrWhiteSpace(sanitizedEnumName))
        {
            Debug.LogError($"【Enum Generator】错误：输入的 Enum 名称 '{asset.enumName}' 无法转换为有效的 C# 标识符。");
            return;
        }
        
        // --- 2. 构建 C# 代码字符串 ---
        var sb = new StringBuilder();
        sb.AppendLine("//---------------------------------------------------------");
        sb.AppendLine("//    此文件由 Unity Inspector 工具自动生成，请勿手动修改！");
        sb.AppendLine("//    基于定义资产: " + AssetDatabase.GetAssetPath(asset));
        sb.AppendLine("//---------------------------------------------------------");
        sb.AppendLine();

        sb.AppendLine($"public enum {sanitizedEnumName}");
        sb.AppendLine("{");

        // 记录已处理的成员，防止重复
        HashSet<string> processedMembers = new HashSet<string>();
        
        // 3. 添加枚举成员
        if (asset.enumMembers != null)
        {
            foreach (var memberName in asset.enumMembers)
            {
                if (string.IsNullOrWhiteSpace(memberName)) continue;

                string validName = SanitizeIdentifier(memberName);
                
                // 确保成员名有效且不重复
                if (!string.IsNullOrWhiteSpace(validName) && !processedMembers.Contains(validName))
                {
                    sb.AppendLine($"    {validName},"); // 暂时都加上逗号，后面处理最后一个
                    processedMembers.Add(validName);
                }
            }
        }
        
        // 移除最后一个成员后面的逗号 (如果存在)
        if (processedMembers.Count > 0)
        {
             // 找到最后一个添加的逗号的索引
            int lastCommaIndex = sb.ToString().LastIndexOf(',');
            if (lastCommaIndex > 0)
            {
                sb.Remove(lastCommaIndex, 1); // 移除该逗号
            }
        }

        sb.AppendLine("}");

        // --- 4. 确定并创建保存路径 ---
        if (!Directory.Exists(GENERATED_CODE_PATH))
        {
            Directory.CreateDirectory(GENERATED_CODE_PATH);
        }

        string filePath = Path.Combine(GENERATED_CODE_PATH, sanitizedEnumName + ".cs");

        // --- 5. 写入文件 ---
        try
        {
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"【Enum Generator】成功生成枚举脚本: <color=lime>{filePath}</color>");
            
            // 强制 Unity 重新编译和刷新 AssetDatabase
            AssetDatabase.Refresh(); 
        }
        catch (System.Exception e)
        {
            Debug.LogError($"【Enum Generator】写入文件失败: {e.Message}");
        }
    }

    /// <summary>
    /// 辅助函数：清洗输入字符串，确保它是一个有效的 C# 标识符。
    /// </summary>
    private static string SanitizeIdentifier(string input)
    {
        // 移除空格、连字符等非标准字符，并用下划线代替
        input = input.Trim().Replace(" ", "_").Replace("-", "_").Replace(".", "_");
        
        // 使用 StringBuilder 只保留有效的 C# 标识符字符（字母、数字、下划线）
        StringBuilder cleaned = new StringBuilder();
        foreach (char c in input)
        {
            // 简单规则：只保留字母、数字和下划线
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                cleaned.Append(c);
            }
        }

        // 确保不以数字开头 (C# 标识符不能以数字开头)
        if (cleaned.Length > 0 && char.IsDigit(cleaned[0]))
        {
            cleaned.Insert(0, '_');
        }
        
        return cleaned.ToString();
    }
}