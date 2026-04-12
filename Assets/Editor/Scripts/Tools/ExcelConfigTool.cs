using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using System;
using System.Reflection;
using Newtonsoft.Json;
using System.Globalization;

// ***************************************************************
// 核心配置工具（重构版）
//  - 创建模板：只创建 .xlsx，不再同步生成模型脚本
//  - 一键导表：遍历所有表 -> 生成/更新模型脚本 -> 触发编译 -> 编译后自动导出 JSON
//  - Fallback：若未触发编译（或极端情况下回调未执行），自动兜底直接导 JSON
//  - 修复：数值/文本数字健壮解析、按表头定位 ID 列、类型映射、日期安全格式化
// ***************************************************************

public class ExcelConfigTool : EditorWindow
{
    // =======================================
    // 1. 常量定义
    // =======================================
    private const string EXCELS_FOLDER_NAME = "Excels";
    private const string MODELS_FOLDER_PATH = "Assets/Scripts/Generated/GeneratedJsonScripts";
    private const string JSON_OUTPUT_PATH   = "Assets/Resources/ConfigJson";
    private const string JSON_GEN_PENDING_KEY = "ConfigTool_JsonGenPending";

    // Excel 头部行索引
    private const int ROW_MODEL_NAME = 0; // 第 1 行：ModelName:Class
    private const int ROW_CH_NAME    = 1; // 第 2 行：中文名
    private const int ROW_EN_NAME    = 2; // 第 3 行：英文名
    private const int ROW_DATA_TYPE  = 3; // 第 4 行：数据类型
    private const int ROW_DESC       = 4; // 第 5 行：描述
    private const int DATA_START_ROW = 5; // 第 6 行：数据区起始
    private const int FIELD_START_COL = 0;

    // 调试日志
    private const bool VerboseLog = false;

    // =======================================
    // 2. 数据结构与枚举
    // =======================================
    public enum FieldType { @string, @int, @float, @bool }
    public enum ProcessType { ModelScriptOnly, JsonDataOnly }

    public struct FieldConfig
    {
        public string ChineseName;
        public string EnglishName;
        public string DataType;
        public string Description;
    }

    private class FieldConfigData
    {
        public string ChineseName = "NewField";
        public string EnglishName = "NewField";
        public FieldType Type = FieldType.@string;
        public string Description = "";
    }

    // 生成 C# 类型映射（容错常见拼写）
    private static readonly Dictionary<string, string> TypeMap = new(StringComparer.OrdinalIgnoreCase) {
        ["string"] = "string",
        ["int"] = "int",
        ["integer"] = "int",
        ["float"] = "float",
        ["single"] = "float",
        ["double"] = "float", // 如表里有人写 double，也按 float 生成
        ["bool"] = "bool",
        ["boolean"] = "bool",
    };

    // =======================================
    // 3. 窗口变量
    // =======================================
    private string _configName = "新配置表";
    private string _modelName = "NewConfigModel";
    private List<FieldConfigData> _fields = new List<FieldConfigData>();
    private Vector2 _scrollPosition;

    // =======================================
    // 4. 菜单入口
    // =======================================
    [MenuItem("Kira工具/配置文件/0. 创建模板XlSX文件", false, 10)]
    public static void ShowWindow()
    {
        GetWindow<ExcelConfigTool>("Config Tool");
    }

    [MenuItem("Kira工具/配置文件/--- 自动化操作 ---", true)]
    private static bool ValidateAutomationSeparator() { return false; }

    // 整合后的“一键导表”
    [MenuItem("Kira工具/配置文件/1. 一键导表（自动生成模型 + JSON）", false, 12)]
    public static void GenerateAllIntegrated()
    {
        Debug.Log("🚀 一键导表：生成/更新模型脚本 → 触发编译 → 编译后自动生成 JSON（含兜底机制）");

        // Step 1：遍历所有 Excel，为其生成/更新模型脚本；统计是否有实际变更
        bool anyScriptChanged = GenerateModelsForAllExcels(out int totalExcels, out int succeeded);

        if (totalExcels == 0)
        {
            EditorUtility.DisplayDialog("提示", "Excels 文件夹中没有找到任何 .xlsx 文件。", "确定");
            return;
        }

        // Step 2：设置待生成 JSON 的标记
        EditorPrefs.SetBool(JSON_GEN_PENDING_KEY, true);

        // Step 3：如果有脚本变更，刷新以触发编译；否则直接导 JSON
        if (anyScriptChanged)
        {
            AssetDatabase.Refresh(); // 触发编译/导入
            // Step 4：保险机制——若没触发编译，在超时后直接导 JSON
            CompileWatcher.StartFallbackTimer(timeoutSeconds: 8.0);
            Debug.Log($"📦 模型脚本已写入（变更 {succeeded}/{totalExcels}），等待编译回调自动导出 JSON...");
        }
        else
        {
            // 没有任何脚本变更：通常不会触发编译/回调，直接导 JSON
            Debug.Log("ℹ️ 未检测到模型脚本变更，直接生成 JSON（无须编译）。");
            try
            {
                ProcessAllConfigs(ProcessType.JsonDataOnly);
                AssetDatabase.Refresh();
                EditorPrefs.DeleteKey(JSON_GEN_PENDING_KEY);
                Debug.Log("🎉 JSON 已直接生成并刷新（无脚本变更）。");
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ 直接生成 JSON 失败：{e.Message}");
            }
        }
    }

    // 仍保留：仅生成 JSON（当你只改数据时很好用）
    [MenuItem("Kira工具/配置文件/2. 仅生成 JSON 数据 (无需编译)", false, 13)]
    public static void GenerateOnlyJsonMenuItem()
    {
        ProcessAllConfigs(ProcessType.JsonDataOnly);
        AssetDatabase.Refresh();
        Debug.Log("🎉 JSON 数据已直接生成并刷新。");
    }

    // =======================================
    // 5. OnGUI
    // =======================================
    private void OnEnable()
    {
        if (_fields.Count == 0)
        {
            _fields.Add(new FieldConfigData { ChineseName = "序号", EnglishName = "ID", Type = FieldType.@int, Description = "唯一ID" });
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("配置模板创建工具", EditorStyles.largeLabel);
        EditorGUILayout.Space(10);

        DrawXLSXCreationSection();

        EditorGUILayout.Space(30);
        EditorGUILayout.LabelField("自动化操作（请使用菜单栏触发）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1) 使用 'Tools/Config/1. 一键导表（自动生成模型 + JSON）' 完成全流程；\n" +
            "2) 仅修改 Excel 数据时，用 'Tools/Config/2. 仅生成 JSON 数据'。",
            MessageType.Info
        );
    }

    private void DrawXLSXCreationSection()
    {
        EditorGUILayout.LabelField("创建配置模板 (XLSX)", EditorStyles.boldLabel);

        _configName = EditorGUILayout.TextField("1. 配置表文件名 (.xlsx):", _configName);
        _modelName  = EditorGUILayout.TextField("2. 映射脚本类名 (英文):", _modelName);

        CheckAndWarnConflicts(_configName, _modelName, checkScriptExistence: true);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("3. 字段定义:", EditorStyles.miniLabel);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(160));

        for (int i = 0; i < _fields.Count; i++)
        {
            var field = _fields[i];
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("X", GUILayout.Width(20))) { _fields.RemoveAt(i); break; }
            field.ChineseName = EditorGUILayout.TextField(field.ChineseName, GUILayout.Width(100));
            field.EnglishName = EditorGUILayout.TextField(field.EnglishName, GUILayout.Width(120));
            field.Type = (FieldType)EditorGUILayout.EnumPopup(field.Type, GUILayout.Width(70));
            field.Description = EditorGUILayout.TextField(field.Description);
            _fields[i] = field;
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("➕ Add New Field")) _fields.Add(new FieldConfigData());
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        if (GUILayout.Button("📝 创建 XLSX 模板", GUILayout.Height(30)))
        {
            GenerateXLSXFileOnly();
        }
    }

    // =======================================
    // 6. 模板生成（仅 XLSX）
    // =======================================
    private void GenerateXLSXFileOnly()
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(_modelName, @"[\u4e00-\u9fa5]"))
        {
            EditorUtility.DisplayDialog("错误", "映射脚本类名不允许包含中文字符。", "确定");
            return;
        }

        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string savePath = Path.Combine(projectRoot, EXCELS_FOLDER_NAME);
        if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

        string fullExcelPath = Path.Combine(savePath, $"{_configName}.xlsx");
        if (File.Exists(fullExcelPath))
        {
            if (!EditorUtility.DisplayDialog("文件已存在", $"文件 '{_configName}.xlsx' 已存在，是否覆盖？", "覆盖", "取消")) return;
        }

        FieldConfig[] configs = _fields.Select(f => new FieldConfig
        {
            ChineseName = f.ChineseName,
            EnglishName = f.EnglishName,
            DataType    = f.Type.ToString().Replace("@", ""),
            Description = f.Description
        }).ToArray();

        WriteXLSXFile(fullExcelPath, _modelName, configs);
    }

    private void WriteXLSXFile(string fullExcelPath, string modelName, FieldConfig[] configs)
    {
        XSSFWorkbook workbook = new XSSFWorkbook();
        ISheet sheet = workbook.CreateSheet("Sheet1");

        IDataFormat dataFormat = workbook.CreateDataFormat();
        ICellStyle intStyle   = workbook.CreateCellStyle(); intStyle.DataFormat   = dataFormat.GetFormat("0");
        ICellStyle floatStyle = workbook.CreateCellStyle(); floatStyle.DataFormat = dataFormat.GetFormat("0.00");
        ICellStyle textStyle  = workbook.CreateCellStyle(); textStyle.DataFormat  = dataFormat.GetFormat("@");

        // 第 0 行写 ModelName
        IRow modelNameRow = sheet.CreateRow(ROW_MODEL_NAME);
        modelNameRow.CreateCell(FIELD_START_COL).SetCellValue($"ModelName:{modelName}");

        // 表头
        IRow chNameRow = sheet.CreateRow(ROW_CH_NAME);
        IRow enNameRow = sheet.CreateRow(ROW_EN_NAME);
        IRow typeRow   = sheet.CreateRow(ROW_DATA_TYPE);
        IRow descRow   = sheet.CreateRow(ROW_DESC);

        for (int i = 0; i < configs.Length; i++)
        {
            int colIndex = FIELD_START_COL + i;
            var field = configs[i];
            string dataType = field.DataType.ToLower();

            chNameRow.CreateCell(colIndex).SetCellValue(field.ChineseName);
            enNameRow.CreateCell(colIndex).SetCellValue(field.EnglishName);
            typeRow.CreateCell(colIndex).SetCellValue(field.DataType);
            descRow.CreateCell(colIndex).SetCellValue(field.Description);

            ICellStyle styleToApply = textStyle;
            if (dataType == "int" || dataType == "integer") styleToApply = intStyle;
            else if (dataType == "float" || dataType == "single" || dataType == "double") styleToApply = floatStyle;

            chNameRow.GetCell(colIndex).CellStyle = styleToApply;
            enNameRow.GetCell(colIndex).CellStyle = styleToApply;
            typeRow.GetCell(colIndex).CellStyle   = styleToApply;
            descRow.GetCell(colIndex).CellStyle   = styleToApply;

            // 设置默认列样式，方便后续输入
            sheet.SetDefaultColumnStyle(colIndex, styleToApply);
        }

        // 示例数据
        IRow exampleRow = sheet.CreateRow(DATA_START_ROW);
        for (int i = 0; i < configs.Length; i++)
        {
            int colIndex = FIELD_START_COL + i;
            var type = configs[i].DataType.ToLower();
            var exampleCell = exampleRow.CreateCell(colIndex);

            ICellStyle styleToApply = textStyle;
            if (type == "int" || type == "integer") styleToApply = intStyle;
            else if (type == "float" || type == "single" || type == "double") styleToApply = floatStyle;
            exampleCell.CellStyle = styleToApply;

            if (type == "int" || type == "integer") exampleCell.SetCellValue(i == 0 ? 1 : 0);
            else if (type == "float" || type == "single" || type == "double") exampleCell.SetCellValue(1.0);
            else if (type == "bool" || type == "boolean") exampleCell.SetCellValue("TRUE");
            else exampleCell.SetCellValue("示例值");
        }

        try
        {
            using (FileStream fs = new FileStream(fullExcelPath, FileMode.Create, FileAccess.Write))
                workbook.Write(fs);

            Debug.Log($"✅ [创建] XLSX 模板成功：{fullExcelPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ [创建] 保存 XLSX 失败: {e.Message}");
        }
    }

    // =======================================
    // 7. 一键导表：为所有 Excel 生成/更新模型脚本（返回是否有变更）
    // =======================================
    private static bool GenerateModelsForAllExcels(out int total, out int changedOrCreated)
    {
        total = 0;
        changedOrCreated = 0;

        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string excelDir = Path.Combine(projectRoot, EXCELS_FOLDER_NAME);
        if (!Directory.Exists(excelDir))
        {
            EditorUtility.DisplayDialog("错误", $"未找到 Excels 文件夹：{excelDir}", "确定");
            Debug.LogError($"❌ [生成模型] Excel 文件夹不存在。");
            return false;
        }

        string[] excelFiles = Directory.GetFiles(excelDir, "*.xlsx", SearchOption.TopDirectoryOnly);
        total = excelFiles.Length;
        if (total == 0)
        {
            Debug.LogWarning("⚠️ [生成模型] 未找到任何 .xlsx 文件。");
            return false;
        }

        bool anyChanged = false;

        foreach (string fullExcelPath in excelFiles)
        {
            string excelFileName = Path.GetFileName(fullExcelPath);
            string modelName = GetModelNameFromExcel(fullExcelPath);
            if (string.IsNullOrEmpty(modelName))
            {
                Debug.LogError($"❌ [生成模型] {excelFileName} 无法读取 Model 类名（第 1 行需 'ModelName:YourClass'）。");
                continue;
            }

            if (ProcessExcelForModel(fullExcelPath, modelName, out bool changed))
            {
                if (changed)
                {
                    anyChanged = true;
                    changedOrCreated++;
                }
            }
        }

        Debug.Log($"📄 [生成模型] 处理完毕：共 {total} 个，变更 {changedOrCreated} 个。");
        return anyChanged;
    }

    // =======================================
    // 8. 批量处理（通用）
    // =======================================
    public static void ProcessAllConfigs(ProcessType type)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string excelDir = Path.Combine(projectRoot, EXCELS_FOLDER_NAME);

        if (!Directory.Exists(excelDir))
        {
            EditorUtility.DisplayDialog("错误", $"未找到 Excels 文件夹: {excelDir}", "确定");
            Debug.LogError($"❌ [批量] 无法开始 {type}：Excel 文件夹不存在。");
            return;
        }

        string[] excelFiles = Directory.GetFiles(excelDir, "*.xlsx", SearchOption.TopDirectoryOnly);
        if (excelFiles.Length == 0)
        {
            EditorUtility.DisplayDialog("警告", "Excels 文件夹中没有 .xlsx 文件。", "确定");
            Debug.LogWarning($"⚠️ [批量] 未找到任何 .xlsx。");
            return;
        }

        int successCount = 0;
        Debug.Log($"--- 开始 {type}，总计 {excelFiles.Length} 个 Excel ---");

        foreach (string fullExcelPath in excelFiles)
        {
            string excelFileName = Path.GetFileName(fullExcelPath);
            string modelName = GetModelNameFromExcel(fullExcelPath);
            if (string.IsNullOrEmpty(modelName))
            {
                Debug.LogError($"❌ [跳过] {excelFileName} 无法读取 Model 类名（第 1 行需 'ModelName:YourClass'）。");
                continue;
            }

            if (type == ProcessType.ModelScriptOnly)
            {
                if (ProcessExcelForModel(fullExcelPath, modelName, out _)) successCount++;
            }
            else // JsonDataOnly
            {
                if (ProcessExcelForJson(fullExcelPath, modelName)) successCount++;
            }
        }

        Debug.Log($"🎉 {type} 完成：成功 {successCount}/{excelFiles.Length}");
    }

    private static string GetModelNameFromExcel(string excelPath)
    {
        try
        {
            using (FileStream fs = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                XSSFWorkbook workbook = new XSSFWorkbook(fs);
                ISheet sheet = workbook.GetSheetAt(0);
                IRow modelNameRow = sheet.GetRow(ROW_MODEL_NAME);
                ICell modelNameCell = modelNameRow?.GetCell(FIELD_START_COL);
                string cellValue = modelNameCell?.ToString().Trim();
                if (!string.IsNullOrEmpty(cellValue) &&
                    cellValue.StartsWith("ModelName:", StringComparison.OrdinalIgnoreCase))
                {
                    return cellValue.Substring("ModelName:".Length).Trim();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ [解析] 无法读取 ModelName（{Path.GetFileName(excelPath)}）: {e.Message}");
        }
        return null;
    }

    // =======================================
    // 9. 生成 C# 模型脚本
    // =======================================
    private static bool ProcessExcelForModel(string excelPath, string modelName, out bool changed)
    {
        changed = false;
        var definitions = ParseExcelHeader(excelPath);
        if (definitions == null || definitions.Count == 0) return false;

        string scriptContent = GenerateModelScriptContent(modelName, definitions);
        string fullScriptPath = Path.Combine(MODELS_FOLDER_PATH, $"{modelName}.cs");
        if (!Directory.Exists(MODELS_FOLDER_PATH)) Directory.CreateDirectory(MODELS_FOLDER_PATH);

        try
        {
            changed = WriteAllTextIfChanged(fullScriptPath, scriptContent, new UTF8Encoding(false));
            Debug.Log($"✅ [Model] {(changed ? "生成/更新" : "保持不变")} {modelName}.cs");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ [Model] 保存失败 ({modelName}): {e.Message}");
            return false;
        }
    }

    private static bool WriteAllTextIfChanged(string path, string content, Encoding encoding)
    {
        if (File.Exists(path))
        {
            string existing = File.ReadAllText(path, encoding);
            if (existing == content) return false; // 未改变
        }
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content, encoding);
        return true; // 新建或更新
    }

    // =======================================
    // 10. 生成 JSON
    // =======================================
    private static bool ProcessExcelForJson(string excelPath, string modelName)
    {
        // 精确匹配短名
        Type modelType = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => SafeGetTypes(a))
                            .FirstOrDefault(t => t.Name == modelName);
        if (modelType == null)
        {
            Debug.LogError($"❌ [JSON] 找不到 Model 类型 '{modelName}'，请先编译模型脚本。");
            return false;
        }

        var dataList = ParseExcelData(excelPath, modelType);
        if (dataList == null) return false;

        string containerName = $"{modelName}Container";
        Type containerType = AppDomain.CurrentDomain.GetAssemblies()
                                  .SelectMany(a => SafeGetTypes(a))
                                  .FirstOrDefault(t => t.Name == containerName);
        if (containerType == null)
        {
            Debug.LogError($"❌ [JSON] 找不到容器类型 '{containerName}'。");
            return false;
        }

        object containerInstance = Activator.CreateInstance(containerType);
        var itemsProperty = containerType.GetProperty("Items");
        if (itemsProperty == null)
        {
            Debug.LogError($"❌ [JSON] 容器 {containerName} 缺少 Items 属性。");
            return false;
        }

        var listType = typeof(List<>).MakeGenericType(modelType);
        var listInstance = Activator.CreateInstance(listType);
        var addMethod = listType.GetMethod("Add");
        foreach (var item in dataList) addMethod.Invoke(listInstance, new[] { item });
        itemsProperty.SetValue(containerInstance, listInstance);

        string jsonString = JsonConvert.SerializeObject(containerInstance, Formatting.Indented);

        string jsonFileName = Path.GetFileNameWithoutExtension(excelPath) + ".json";
        string fullJsonPath = Path.Combine(JSON_OUTPUT_PATH, jsonFileName);
        if (!Directory.Exists(JSON_OUTPUT_PATH)) Directory.CreateDirectory(JSON_OUTPUT_PATH);

        try
        {
            File.WriteAllText(fullJsonPath, jsonString, Encoding.UTF8);
            Debug.Log($"✅ [JSON] 生成成功：{fullJsonPath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ [JSON] 保存失败 ({jsonFileName}): {e.Message}");
            return false;
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch { return Enumerable.Empty<Type>(); }
    }

    // =======================================
    // 11. 解析（表头 & 数据）
    // =======================================
    private static List<FieldConfig> ParseExcelHeader(string excelPath)
    {
        List<FieldConfig> fieldDefinitions = new List<FieldConfig>();
        try
        {
            using (FileStream fs = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                XSSFWorkbook workbook = new XSSFWorkbook(fs);
                ISheet sheet = workbook.GetSheetAt(0);

                IRow enNameRow = sheet.GetRow(ROW_EN_NAME);
                IRow typeRow   = sheet.GetRow(ROW_DATA_TYPE);

                if (enNameRow == null || typeRow == null)
                {
                    Debug.LogError($"❌ [解析] 表头缺少英文名/类型行：{Path.GetFileName(excelPath)}");
                    return null;
                }

                int lastCellNum = Math.Max(enNameRow.LastCellNum, typeRow.LastCellNum);

                for (int i = FIELD_START_COL; i < lastCellNum; i++)
                {
                    ICell enNameCell = enNameRow.GetCell(i);
                    ICell typeCell = typeRow.GetCell(i);
                    if (enNameCell == null || string.IsNullOrWhiteSpace(enNameCell.ToString())) continue;

                    fieldDefinitions.Add(new FieldConfig
                    {
                        EnglishName = enNameCell.ToString().Trim(),
                        DataType    = typeCell?.ToString().Trim() ?? "string",
                        Description = ""
                    });
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ [解析] 表头失败（{Path.GetFileName(excelPath)}）: {e.Message}");
            return null;
        }
        return fieldDefinitions;
    }

    private static List<object> ParseExcelData(string excelPath, Type modelType)
    {
        List<object> dataList = new List<object>();

        // 仅取带 JsonProperty 的公共属性
        var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                  .Where(p => p.GetCustomAttribute<JsonPropertyAttribute>() != null)
                                  .ToList();

        var headerDefinitions = ParseExcelHeader(excelPath);
        if (headerDefinitions == null)
        {
            Debug.LogError($"❌ [解析] 无法获取 Excel 表头定义，跳过：{Path.GetFileName(excelPath)}");
            return null;
        }

        // 找到 ID 列位置
        int idColOffset = headerDefinitions.FindIndex(h => h.EnglishName.Equals("ID", StringComparison.OrdinalIgnoreCase));
        if (idColOffset < 0)
        {
            Debug.LogError("❌ [解析] 缺少 ID 列（英文列名必须包含 ID）");
            return null;
        }

        // 建立属性查找（按名称忽略大小写）
        var propByName = properties.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        try
        {
            using (FileStream fs = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                XSSFWorkbook workbook = new XSSFWorkbook(fs);
                ISheet sheet = workbook.GetSheetAt(0);

                for (int rowIdx = DATA_START_ROW; rowIdx <= sheet.LastRowNum; rowIdx++)
                {
                    IRow row = sheet.GetRow(rowIdx);
                    if (row == null) continue;

                    // 读取 ID
                    ICell idCell = row.GetCell(FIELD_START_COL + idColOffset);
                    var idObj = GetCellValue(idCell, typeof(int));
                    if (idObj is not int id || id <= 0)
                    {
                        if (VerboseLog)
                        {
                            string raw = idCell != null ? $"(NPOI:{idCell.CellType}, Raw:'{idCell.ToString().Trim()}')" : "(空)";
                            Debug.LogWarning($"[解析] 跳过第 {rowIdx + 1} 行：ID 无效。解析值:{idObj} {raw}");
                        }
                        continue;
                    }

                    object dataInstance = Activator.CreateInstance(modelType);

                    // 按表头把每一列写进模型
                    for (int h = 0; h < headerDefinitions.Count; h++)
                    {
                        int currentCol = FIELD_START_COL + h;
                        string excelColName = headerDefinitions[h].EnglishName;
                        var prop = propByName.TryGetValue(excelColName, out var p) ? p : null;
                        if (prop == null) continue;

                        ICell cell = row.GetCell(currentCol);
                        object cellValue = GetCellValue(cell, prop.PropertyType);

                        // string 允许空赋值；值类型解析失败则跳过（保持默认值）
                        if (cellValue != null || prop.PropertyType == typeof(string))
                            prop.SetValue(dataInstance, cellValue);
                    }

                    dataList.Add(dataInstance);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ [解析] 数据处理异常（{Path.GetFileName(excelPath)}）: {e.Message}");
            return null;
        }

        return dataList;
    }

    // =======================================
    // 12. 单元格取值（关键修复）
    // =======================================
    private static object GetCellValue(ICell cell, Type targetType)
    {
        if (cell == null) return null;

        if (VerboseLog)
            Debug.Log($"[调试] GetCellValue: CellType={cell.CellType}, Value='{cell}', TargetType={targetType.Name}");

        // 公式先计算
        if (cell.CellType == CellType.Formula)
        {
            try
            {
                var eval = new XSSFFormulaEvaluator(cell.Sheet.Workbook);
                var cv = eval.Evaluate(cell);
                switch (cv.CellType)
                {
                    case CellType.Numeric: return CoerceNumeric(cv.NumberValue, targetType);
                    case CellType.String:  return CoerceFromString(cv.StringValue, targetType);
                    case CellType.Boolean: return CoerceFromBool(cv.BooleanValue, targetType);
                    default: return null;
                }
            }
            catch { /* 忽略公式计算错误 */ }
        }

        switch (cell.CellType)
        {
            case CellType.Numeric:
            {
                if (DateUtil.IsCellDateFormatted(cell))
                {
                    if (targetType == typeof(string))
                    {
                        // 用序列值转成 DateTime，避免依赖某些版本的 DateCellValue 重载问题
                        var dt = NPOI.SS.UserModel.DateUtil.GetJavaDate(cell.NumericCellValue);
                        return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'");
                    }
                    return null;
                }
                return CoerceNumeric(cell.NumericCellValue, targetType);
            }

            case CellType.Boolean:
                return CoerceFromBool(cell.BooleanCellValue, targetType);

            case CellType.String:
                return CoerceFromString(cell.StringCellValue, targetType);

            default:
                return null;
        }
    }

    private static object CoerceNumeric(double number, Type targetType)
    {
        if (targetType == typeof(int))
            return Convert.ToInt32(Math.Round(number, 0, MidpointRounding.AwayFromZero));
        if (targetType == typeof(float))
            return (float)number;
        if (targetType == typeof(bool))
            return number != 0;
        if (targetType == typeof(string))
            return number.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    private static object CoerceFromString(string s, Type targetType)
    {
        if (s == null) return null;
        s = s.Trim();

        if (targetType == typeof(string)) return s;

        if (targetType == typeof(int))
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                return iv;
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dv))
                return Convert.ToInt32(Math.Round(dv, 0, MidpointRounding.AwayFromZero));
            return null;
        }

        if (targetType == typeof(float))
        {
            if (float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var fv))
                return fv;
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dv))
                return (float)dv;
            return null;
        }

        if (targetType == typeof(bool))
        {
            var up = s.ToUpperInvariant();
            if (up == "TRUE" || up == "1") return true;
            if (up == "FALSE" || up == "0") return false;
            return null;
        }

        return null;
    }

    private static object CoerceFromBool(bool b, Type targetType)
    {
        if (targetType == typeof(bool)) return b;
        if (targetType == typeof(int)) return b ? 1 : 0;
        if (targetType == typeof(float)) return b ? 1f : 0f;
        if (targetType == typeof(string)) return b ? "TRUE" : "FALSE";
        return null;
    }

    // =======================================
    // 13. 代码生成
    // =======================================
    private static string GenerateModelScriptContent(string className, List<FieldConfig> definitions)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Newtonsoft.Json;");
        sb.AppendLine("using System;");
        sb.AppendLine("");

        sb.AppendLine("/// <summary>通用配置数据接口</summary>");
        sb.AppendLine("public interface IConfigData { int ID { get; } }");
        sb.AppendLine("");

        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// 配置表数据模型：{className}");
        sb.AppendLine("/// (此文件由工具自动生成，请勿手动修改)");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public class {className} : IConfigData");
        sb.AppendLine("{");

        foreach (var def in definitions)
        {
            string propertyName = def.EnglishName;
            string mapped = TypeMap.TryGetValue(def.DataType, out var csType) ? csType : "string";
            string description = string.IsNullOrWhiteSpace(def.Description) ? "" : $" // {def.Description}";

            if (propertyName.Equals("ID", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"    [JsonProperty(\"{def.EnglishName}\")]");
                sb.AppendLine($"    public int ID {{ get; set; }}{description}");
            }
            else
            {
                sb.AppendLine($"    [JsonProperty(\"{def.EnglishName}\")]");
                sb.AppendLine($"    public {mapped} {propertyName} {{ get; set; }}{description}");
            }
        }
        sb.AppendLine("}");
        sb.AppendLine("");

        string containerName = $"{className}Container";
        string listPropertyName = char.ToLowerInvariant(className[0]) + className.Substring(1) + "List";

        sb.AppendLine("/// <summary>配置表数据列表容器</summary>");
        sb.AppendLine($"public class {containerName}");
        sb.AppendLine("{");
        sb.AppendLine($"    [JsonProperty(\"{listPropertyName}\")]");
        sb.AppendLine($"    public List<{className}> Items {{ get; set; }} = new List<{className}>();");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // =======================================
    // 14. 冲突检查
    // =======================================
    private void CheckAndWarnConflicts(string configName, string modelName, bool checkScriptExistence)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string excelPath = Path.Combine(projectRoot, EXCELS_FOLDER_NAME, $"{configName}.xlsx");

        if (File.Exists(excelPath))
        {
            EditorGUILayout.HelpBox($"注意: 配置表 '{configName}.xlsx' 已存在，创建将覆盖。", MessageType.Info);
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(modelName, @"[\u4e00-\u9fa5]"))
        {
            EditorGUILayout.HelpBox($"严重警告: 映射脚本类名 '{modelName}' 含中文，请修改！", MessageType.Error);
        }
        else if (checkScriptExistence)
        {
            string scriptPath = Path.Combine(MODELS_FOLDER_PATH, $"{modelName}.cs");
            if (File.Exists(scriptPath))
            {
                EditorGUILayout.HelpBox($"提示: '{modelName}.cs' 可能已存在（仅提示，不会在此流程生成脚本）。", MessageType.Warning);
            }
        }
    }

    // =======================================
    // 15. 编译监视（兜底定时器）
    // =======================================
    private static class CompileWatcher
    {
        private static bool _running = false;
        private static double _deadline = 0;

        public static void StartFallbackTimer(double timeoutSeconds)
        {
            if (_running) return;
            _running = true;
            _deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            // 若开始编译了，等待 AssetPostprocessor 执行后续逻辑，再停止监控
            if (EditorApplication.isCompiling)
            {
                Stop();
                return;
            }

            // 超时仍未编译 → 直接导 JSON，并清除待生成标记，避免重复
            if (EditorApplication.timeSinceStartup >= _deadline)
            {
                try
                {
                    if (EditorPrefs.GetBool(JSON_GEN_PENDING_KEY, false))
                    {
                        EditorPrefs.DeleteKey(JSON_GEN_PENDING_KEY);
                        ExcelConfigTool.ProcessAllConfigs(ExcelConfigTool.ProcessType.JsonDataOnly);
                        AssetDatabase.Refresh();
                        Debug.Log("⚠️ 未触发编译，已启用兜底：直接生成 JSON 完成。");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"❌ 兜底直接生成 JSON 失败：{e.Message}");
                }
                finally
                {
                    Stop();
                }
            }
        }

        private static void Stop()
        {
            if (!_running) return;
            _running = false;
            EditorApplication.update -= Update;
        }
    }
}

// ***************************************************************
// 编译/导入回调：一键导表后，编译完成自动生成 JSON
// ***************************************************************
public class ConfigAssetPostprocessor : AssetPostprocessor
{
    private const string JSON_GEN_PENDING_KEY = "ConfigTool_JsonGenPending";

    public static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (EditorPrefs.GetBool(JSON_GEN_PENDING_KEY, false))
        {
            // 这里不判断 isCompiling，由编译完成后 delayCall 去跑
            EditorApplication.delayCall += ExecuteJsonGenerationOnce;
        }
    }

    private static void ExecuteJsonGenerationOnce()
    {
        // 二次确认：只执行一次
        if (!EditorPrefs.GetBool(JSON_GEN_PENDING_KEY, false)) return;

        // 清除标记，避免重复
        EditorPrefs.DeleteKey(JSON_GEN_PENDING_KEY);

        try
        {
            Debug.Log("🚀 编译完成，自动执行 JSON 数据生成...");
            ExcelConfigTool.ProcessAllConfigs(ExcelConfigTool.ProcessType.JsonDataOnly);
            AssetDatabase.Refresh();
            Debug.Log("🎉 一键导表成功：JSON 已在编译后自动生成。");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 自动 JSON 生成失败: {e.Message}");
        }
    }
}
