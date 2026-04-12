using System;

// 标记一个类是可被 ViewModel 引用的 Model
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class MVVMModelAttribute : Attribute
{
    public string ModelName { get; }

    public MVVMModelAttribute(string name = null)
    {
        // 允许可选名称，如果为 null 则使用类名
        ModelName = name;
    }
}

// 标记 Model 中一个可被 ViewModel 引用的字段或属性
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public class MVVMFieldAttribute : Attribute
{
    public string FieldName { get; }

    public MVVMFieldAttribute(string name = null)
    {
        // 允许可选名称，如果为 null 则使用字段/属性名
        FieldName = name;
    }
}

// 存储在 ScriptableObject 中的配置项
// 注意：我们在运行时也需要它，所以放在 Runtime 文件夹
[Serializable]
public struct ModelFieldBinding
{
    // 存储 Model 的全名 (例如: "MyApp.Models.HPModel")
    public string modelTypeName; 
    
    // 存储 Field 的名字 (例如: "CurrentHP")
    public string fieldName;     
}