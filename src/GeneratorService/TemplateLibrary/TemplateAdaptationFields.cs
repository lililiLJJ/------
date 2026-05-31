using GeneratorService.Models;

namespace GeneratorService.TemplateLibrary;

internal static class TemplateAdaptationFields
{
    public const string ModuleStatusManaged = "Managed";
    public const string ModuleStatusUnmanaged = "Unmanaged";
    public const string ModuleStatusReadOnly = "ReadOnly";

    public const string AdaptationStatusUnconfigured = "Unconfigured";
    public const string AdaptationStatusPartial = "Partial";
    public const string AdaptationStatusCompleted = "Completed";
    public const string AdaptationStatusError = "Error";

    public const string ValueSourceBusinessData = "BusinessData";
    public const string ValueSourceDefaultValue = "DefaultValue";

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

    public static bool IsSystemField(string fieldKey)
    {
        return RequiredFieldKeys.Contains(fieldKey, StringComparer.OrdinalIgnoreCase);
    }

    public static int GetDefaultSortOrder(string fieldKey)
    {
        for (var index = 0; index < RequiredFieldKeys.Length; index++)
        {
            if (string.Equals(RequiredFieldKeys[index], fieldKey, StringComparison.OrdinalIgnoreCase))
            {
                return (index + 1) * 10;
            }
        }

        return 1000;
    }

    public static string GetDefaultValueSource(string fieldKey)
    {
        return IsSystemField(fieldKey) ? ValueSourceBusinessData : ValueSourceBusinessData;
    }

    public static bool IsManagedModule(string moduleId)
    {
        return string.Equals(moduleId, "gd_installation_2024", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveModuleStatus(string moduleId)
    {
        return IsManagedModule(moduleId) ? ModuleStatusManaged : ModuleStatusUnmanaged;
    }

    public static TemplateFieldMappingInfo CreateDefaultMapping(string fieldKey)
    {
        return new TemplateFieldMappingInfo(
            fieldKey,
            GetDisplayName(fieldKey),
            "Cell",
            [],
            [],
            GetDefaultValueSource(fieldKey),
            "",
            GetDefaultSortOrder(fieldKey),
            IsSystemField(fieldKey),
            null,
            true,
            false,
            null,
            0);
    }
}
