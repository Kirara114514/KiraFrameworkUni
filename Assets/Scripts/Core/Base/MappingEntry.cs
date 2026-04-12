using System;
using System.Collections.Generic;
using UnityEngine;

// 定义单个映射条目
[Serializable]
public class MappingEntry
{
    [Tooltip("完整的引用链，例如: [\"UI\", \"HUD\", \"HP\"]")]
    public List<string> PathKeys = new List<string>();

    [Tooltip("该引用链最终映射的字符串值（资源路径/事件ID等）")]
    public string FinalValue = "";
}