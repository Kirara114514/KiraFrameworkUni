using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 用于存储需要生成的枚举的定义数据。
/// 可以在 Project 视图中右键 -> Create -> Tools/Enum Generator Definition 来创建实例。
/// </summary>
[CreateAssetMenu(fileName = "NewEnumDefinition", menuName = "KiraEnum/New Enum")]
public class EnumDefinitionAsset : ScriptableObject
{
    [Tooltip("生成的枚举的名称，例如：'GameStates' 或 'ItemTypes'")]
    public string enumName = "NewCustomEnum";

    [Tooltip("枚举的成员列表，每个字符串将作为枚举的一个值。")]
    public List<string> enumMembers = new List<string>
    {
        "DefaultValueA",
        "DefaultValueB"
    };
    
    // 可以在这里添加其他配置，例如命名空间等
}