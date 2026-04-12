using UnityEditor;
using UnityEngine;
using System.Linq;
using System;

[CustomPropertyDrawer(typeof(ModelFieldBinding))]
public class ModelFieldBindingDrawer : PropertyDrawer
{
    private const float Padding = 5f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var modelNameProp = property.FindPropertyRelative("modelTypeName");
        var fieldNameProp = property.FindPropertyRelative("fieldName");

        EditorGUI.BeginProperty(position, label, property);
        
        // 绘制标签，并计算剩余空间
        position = EditorGUI.PrefixLabel(position, label);
        
        // 空间分割
        var totalWidth = position.width;
        var modelRect = new Rect(position.x, position.y, totalWidth / 2 - Padding / 2, position.height);
        var fieldRect = new Rect(position.x + totalWidth / 2 + Padding / 2, position.y, totalWidth / 2 - Padding / 2, position.height);

        // --- 1. Model Type 下拉框 ---
        var allModelNames = MVVMDataCache.GetAllModelNames();
        var currentModelName = modelNameProp.stringValue;
        int currentModelIndex = Array.IndexOf(allModelNames, currentModelName);
        
        // --- Bug 修正：处理 MISSING 状态 ---
        if (currentModelIndex < 0 && !string.IsNullOrEmpty(currentModelName))
        {
            // 如果列表中没有当前值，索引设置为 -1，并添加到临时列表
            string missingName = currentModelName + " (MISSING)";
            Array.Resize(ref allModelNames, allModelNames.Length + 1);
            allModelNames[allModelNames.Length - 1] = missingName;
            currentModelIndex = allModelNames.Length - 1;
            GUI.color = Color.red; // 标记缺失项
        }
        else if (currentModelIndex < 0)
        {
             // 如果当前值为空，且列表不为空，默认选中第一个
             currentModelIndex = allModelNames.Length > 0 ? 0 : -1;
        }
        
        // 渲染 Model 下拉框
        int newModelIndex = EditorGUI.Popup(modelRect, currentModelIndex, allModelNames);
        GUI.color = Color.white;

        bool modelChanged = false;
        
        // --- Bug 修正：确保首次或唯一 Model 时能赋值 ---
        if (allModelNames.Length > 0 && newModelIndex >= 0)
        {
            // 检查是否有真正的变更，或者当前值为空需要初始化
            if (newModelIndex != currentModelIndex || string.IsNullOrEmpty(modelNameProp.stringValue))
            {
                // 仅在 Model 名称不是 MISSING 的情况下更新
                if (newModelIndex < allModelNames.Length && !allModelNames[newModelIndex].EndsWith(" (MISSING)"))
                {
                    modelNameProp.stringValue = allModelNames[newModelIndex];
                    fieldNameProp.stringValue = string.Empty; // Model 变更或初始化，清空 Field Name
                    property.serializedObject.ApplyModifiedProperties();
                    modelChanged = true;
                }
            }
        }
        
        // 如果 ModelName 仍然为空，我们无法继续
        if (string.IsNullOrEmpty(modelNameProp.stringValue) || modelNameProp.stringValue.EndsWith(" (MISSING)"))
        {
            // 禁用 Field 下拉框
            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.Popup(fieldRect, 0, new []{ "Select Model First" });
            EditorGUI.EndDisabledGroup();
            EditorGUI.EndProperty();
            return;
        }


        // --- 2. Field Name 下拉框 (依赖 Model Type) ---
        var selectedModelName = modelNameProp.stringValue;
        var allFieldNames = MVVMDataCache.GetFieldsForModel(selectedModelName);
        var currentFieldName = fieldNameProp.stringValue;
        int currentFieldIndex = Array.IndexOf(allFieldNames, currentFieldName);
            
        // 检查 Field 是否缺失
        if (currentFieldIndex < 0 && !string.IsNullOrEmpty(currentFieldName) && !modelChanged)
        {
            string missingFieldName = currentFieldName + " (MISSING)";
            Array.Resize(ref allFieldNames, allFieldNames.Length + 1);
            allFieldNames[allFieldNames.Length - 1] = missingFieldName;
            currentFieldIndex = allFieldNames.Length - 1;
            GUI.color = Color.red;
        }
        else if (currentFieldIndex < 0)
        {
             // 如果当前值为空，且列表不为空，默认选中第一个
             currentFieldIndex = allFieldNames.Length > 0 ? 0 : -1;
        }

        // 渲染 Field 下拉框
        int newFieldIndex = EditorGUI.Popup(fieldRect, currentFieldIndex, allFieldNames);
        GUI.color = Color.white;

        // --- Bug 修正：确保首次或唯一 Field 时能赋值 ---
        if (allFieldNames.Length > 0 && newFieldIndex >= 0)
        {
            // 检查是否有真正的变更，或者当前值为空需要初始化
            if (newFieldIndex != currentFieldIndex || string.IsNullOrEmpty(fieldNameProp.stringValue) || modelChanged)
            {
                 // 仅在 Field 名称不是 MISSING 的情况下更新
                if (newFieldIndex < allFieldNames.Length && !allFieldNames[newFieldIndex].EndsWith(" (MISSING)"))
                {
                    // Field 改变，更新值
                    fieldNameProp.stringValue = allFieldNames[newFieldIndex];
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
        }
        else if (allFieldNames.Length == 0)
        {
             // 如果没有字段可选，清空当前字段值
             if (!string.IsNullOrEmpty(fieldNameProp.stringValue))
             {
                 fieldNameProp.stringValue = string.Empty;
                 property.serializedObject.ApplyModifiedProperties();
             }
             // 绘制不可选的提示
             EditorGUI.BeginDisabledGroup(true);
             EditorGUI.Popup(fieldRect, 0, new []{ "No Fields Found" });
             EditorGUI.EndDisabledGroup();
        }

        EditorGUI.EndProperty();
    }
}