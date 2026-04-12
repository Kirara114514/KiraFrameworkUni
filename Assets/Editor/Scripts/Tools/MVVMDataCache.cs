using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using System;

// 用于存储 Model 字段的类型和名称
public struct MVVMFieldInfo
{
    public string FieldName;    // 字段的友好名称（来自特性）
    public string FieldType;    // 字段的 C# 完整类型名称（例如 "System.Int32"）
}

// 静态类，用于扫描特性并缓存数据
[InitializeOnLoad] 
public static class MVVMDataCache
{
    // Key: Model Class Full Name (string)
    // Value: List of MVVMFieldInfo (包含名称和类型)
    public static Dictionary<string, List<MVVMFieldInfo>> AllModelsAndFields { get; private set; } 
        = new Dictionary<string, List<MVVMFieldInfo>>(); 

    static MVVMDataCache()
    {
        ScanProjectForModels();
        EditorApplication.delayCall += ScanProjectForModels; 
    }

    public static void ScanProjectForModels()
    {
        AllModelsAndFields.Clear();
        Debug.Log("MVVM Data Cache: Starting Model and Field scan...");

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName.StartsWith("Assembly-CSharp") || 
                        a.FullName.Contains("YourProjectAssemblyName"));

        foreach (var assembly in assemblies)
        {
            try
            {
                var modelTypes = assembly.GetTypes()
                    .Where(t => t.GetCustomAttribute<MVVMModelAttribute>() != null && t.IsClass);

                foreach (var modelType in modelTypes)
                {
                    string modelKey = modelType.FullName;
                    var fieldInfos = new List<MVVMFieldInfo>();

                    var members = modelType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.GetCustomAttribute<MVVMFieldAttribute>() != null && 
                                   (m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property));

                    foreach (var member in members)
                    {
                        var attr = member.GetCustomAttribute<MVVMFieldAttribute>();
                        string fieldName = string.IsNullOrEmpty(attr.FieldName) ? member.Name : attr.FieldName;
                        
                        // --- 关键修改：获取字段或属性的类型 ---
                        Type memberType = null;
                        if (member.MemberType == MemberTypes.Field)
                            memberType = ((FieldInfo)member).FieldType;
                        else if (member.MemberType == MemberTypes.Property)
                            memberType = ((PropertyInfo)member).PropertyType;
                        
                        if (memberType != null)
                        {
                            fieldInfos.Add(new MVVMFieldInfo
                            {
                                FieldName = fieldName,
                                FieldType = GetFriendlyTypeName(memberType) // 使用友好名称
                            });
                        }
                    }

                    if (fieldInfos.Count > 0)
                    {
                        AllModelsAndFields[modelKey] = fieldInfos;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error scanning assembly {assembly.FullName}: {ex.Message}");
            }
        }
        Debug.Log($"MVVM Data Cache: Scan finished. Found {AllModelsAndFields.Count} Models.");
    }
    
    // 帮助方法：获取 C# 友好类型名称（如 int 而非 System.Int32）
    private static string GetFriendlyTypeName(Type type)
    {
        if (type == typeof(int)) return "int";
        if (type == typeof(float)) return "float";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(string)) return "string";
        if (type == typeof(void)) return "void";
        // TODO: 可在此处添加更多常用的映射，如 Vector3, Color等
        return type.FullName.Replace('+', '.'); // 替换嵌套类型名称中的 +
    }

    // 帮助方法：获取所有 Model 的完整名称 (供 Drawer 使用)
    public static string[] GetAllModelNames() 
    {
        return AllModelsAndFields.Keys.ToArray();
    }

    // 帮助方法：根据 Model 名称获取所有字段名 (供 Drawer 使用)
    public static string[] GetFieldsForModel(string modelFullName) 
    {
        if (AllModelsAndFields.TryGetValue(modelFullName, out var fields))
        {
            return fields.Select(f => f.FieldName).ToArray();
        }
        return new string[0];
    }
    
    // --- 新增帮助方法：根据 Model 名称和字段名称获取类型 ---
    public static string GetFieldType(string modelFullName, string fieldName)
    {
        if (AllModelsAndFields.TryGetValue(modelFullName, out var fields))
        {
            var info = fields.FirstOrDefault(f => f.FieldName == fieldName);
            if (info.FieldName != null)
            {
                return info.FieldType;
            }
        }
        // 如果找不到，返回 object 作为最后的fallback，并警告
        Debug.LogError($"MVVM Generator: Could not find type for Model '{modelFullName}' Field '{fieldName}'. Using 'object' as fallback.");
        return "object"; 
    }
}