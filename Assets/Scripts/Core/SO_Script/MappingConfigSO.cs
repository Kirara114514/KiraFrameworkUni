using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MappingConfig", menuName = "KiraStatics/Static Mapping Configuration")]
public class MappingConfigSO : ScriptableObject
{
    [Tooltip("生成的静态类名，例如：MyResources 或 MyEvents")]
    public string RootClassName = "MyStatics";

    public List<MappingEntry> Entries = new List<MappingEntry>();
}