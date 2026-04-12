using System.Collections.Generic;
using Newtonsoft.Json;
using System;

/// <summary>通用配置数据接口</summary>
public interface IConfigData { int ID { get; } }

/// <summary>
/// 配置表数据模型：NewConfigModel
/// (此文件由工具自动生成，请勿手动修改)
/// </summary>
public class NewConfigModel : IConfigData
{
    [JsonProperty("ID")]
    public int ID { get; set; }
    [JsonProperty("Name")]
    public string Name { get; set; }
}

/// <summary>配置表数据列表容器</summary>
public class NewConfigModelContainer
{
    [JsonProperty("newConfigModelList")]
    public List<NewConfigModel> Items { get; set; } = new List<NewConfigModel>();
}
