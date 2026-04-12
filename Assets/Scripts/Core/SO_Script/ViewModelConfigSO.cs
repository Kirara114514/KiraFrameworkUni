using UnityEngine;
using System.Collections.Generic;

// 用于在 Inspector 中配置 ViewModel 的 SO
[CreateAssetMenu(fileName = "NewVMSO", menuName = "KiraMVVM/ViewModel_SO")]
public class ViewModelConfigSO : ScriptableObject
{
    public string ViewModelName = "NewViewModel";

    [Tooltip("配置 ViewModel 要引用的 Model 字段")]
    public List<ModelFieldBinding> Bindings = new List<ModelFieldBinding>();
}