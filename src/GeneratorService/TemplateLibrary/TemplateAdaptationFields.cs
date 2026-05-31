namespace GeneratorService.TemplateLibrary;

internal static class TemplateAdaptationFields
{
    public const string ProjectName = "ProjectName";
    public const string ConstructionUnit = "ConstructionUnit";
    public const string SupervisionUnit = "SupervisionUnit";
    public const string PartName = "PartName";
    public const string Capacity = "Capacity";
    public const string ConstructionDate = "ConstructionDate";

    public static readonly string[] RequiredFieldKeys =
    [
        ProjectName,
        ConstructionUnit,
        SupervisionUnit,
        PartName,
        Capacity,
        ConstructionDate
    ];

    public static readonly IReadOnlyDictionary<string, string[]> LabelAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [ProjectName] = ["工程名称", "项目名称"],
            [ConstructionUnit] = ["施工单位", "承包单位"],
            [SupervisionUnit] = ["监理单位"],
            [PartName] = ["检验批部位", "施工部位", "部位名称"],
            [Capacity] = ["检验批容量"],
            [ConstructionDate] = ["施工日期"]
        };

    public static readonly IReadOnlyDictionary<string, string> DisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProjectName] = "工程名称",
            [ConstructionUnit] = "施工单位",
            [SupervisionUnit] = "监理单位",
            [PartName] = "检验批部位",
            [Capacity] = "检验批容量",
            [ConstructionDate] = "施工日期"
        };

    public static string GetDisplayName(string fieldKey)
    {
        return DisplayNames.TryGetValue(fieldKey, out var name) ? name : fieldKey;
    }
}
