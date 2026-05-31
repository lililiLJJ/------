using DocumentFormat.OpenXml.Packaging;
using GeneratorService.Models;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateMappingService
{
    private readonly TemplateAdaptationRepository _repository;
    private readonly TemplateValidationService _validationService;

    public TemplateMappingService(
        TemplateAdaptationRepository repository,
        TemplateValidationService validationService)
    {
        _repository = repository;
        _validationService = validationService;
    }

    public bool ShouldUseAdaptation(TemplateResolution template)
    {
        return string.Equals(template.ModuleId, "gd_installation_2024", StringComparison.OrdinalIgnoreCase);
    }

    public TemplateApplyResult Apply(string filePath, TemplateResolution template, IReadOnlyDictionary<string, string> fields)
    {
        _validationService.EnsureReadyForGeneration(template);
        var detail = _repository.GetAdaptationDetail(template);
        var mappings = detail.Mappings
            .Where(item => item.IsEnabled)
            .ToDictionary(item => item.FieldKey, StringComparer.OrdinalIgnoreCase);
        var replacements = BuildPlaceholderReplacements(mappings, fields);
        var writeReceipts = new List<TemplateFieldWriteReceipt>();

        using (var document = SpreadsheetDocument.Open(filePath, true))
        {
            var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少 WorkbookPart。");
            TemplateWorkbookHelper.ReplaceSimplePlaceholders(workbookPart, replacements);

            foreach (var fieldKey in TemplateAdaptationFields.RequiredFieldKeys)
            {
                if (!mappings.TryGetValue(fieldKey, out var mapping))
                {
                    continue;
                }

                var expectedValue = ResolveFieldValue(fieldKey, fields);
                var mode = NormalizeMode(mapping.Mode);
                var locations = new List<string>();

                if (mode is "Cell" or "Hybrid")
                {
                    foreach (var target in mapping.Targets)
                    {
                        var worksheetPart = TemplateWorkbookHelper.ResolveWorksheetPart(workbookPart, target.WorksheetName);
                        if (worksheetPart is null)
                        {
                            continue;
                        }

                        TemplateWorkbookHelper.WriteCellText(worksheetPart.Worksheet, target.CellReference, expectedValue);
                        worksheetPart.Worksheet.Save();
                        locations.Add(FormatTarget(target));
                    }
                }
                else if (mode == "Placeholder")
                {
                    locations.AddRange(mapping.PlaceholderTokens.Select(item => $"{{{{{item}}}}}"));
                }

                writeReceipts.Add(new TemplateFieldWriteReceipt(fieldKey, mode, expectedValue, locations));
            }

            workbookPart.Workbook.Save();
        }

        var fieldResults = VerifyWrittenValues(filePath, template, fields, mappings);
        var failed = fieldResults.Where(item => !item.Success).ToArray();
        if (failed.Length > 0)
        {
            throw new TemplateAdaptationException(
                $"模板“{template.TemplateName}”写入校验失败：{string.Join("；", failed.Select(item => item.Message))}",
                template.TemplateNodeId,
                template.TemplateName,
                failed.Select(item => item.FieldKey).ToArray(),
                "error");
        }

        return new TemplateApplyResult(writeReceipts, fieldResults);
    }

    public IReadOnlyList<TemplateFieldTestResult> VerifyWrittenValues(
        string filePath,
        TemplateResolution template,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyDictionary<string, TemplateFieldMappingInfo>? mappingsOverride = null)
    {
        var detail = _repository.GetAdaptationDetail(template);
        var mappings = mappingsOverride ?? detail.Mappings
            .Where(item => item.IsEnabled)
            .ToDictionary(item => item.FieldKey, StringComparer.OrdinalIgnoreCase);
        var workbook = TemplateWorkbookHelper.Inspect(filePath);
        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少 WorkbookPart。");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var results = new List<TemplateFieldTestResult>();

        foreach (var fieldKey in TemplateAdaptationFields.RequiredFieldKeys)
        {
            if (!mappings.TryGetValue(fieldKey, out var mapping))
            {
                continue;
            }

            var expectedValue = ResolveFieldValue(fieldKey, fields);
            var mode = NormalizeMode(mapping.Mode);
            var targets = new List<string>();
            var actualValues = new List<string>();

            if (mode is "Placeholder" or "Hybrid")
            {
                foreach (var token in mapping.PlaceholderTokens)
                {
                    targets.Add($"{{{{{token}}}}}");
                    if (workbook.Placeholders.Any(item => string.Equals(item, token, StringComparison.OrdinalIgnoreCase)))
                    {
                        actualValues.Add(expectedValue);
                    }
                }
            }

            if (mode is "Cell" or "Hybrid")
            {
                foreach (var target in mapping.Targets)
                {
                    var worksheetPart = TemplateWorkbookHelper.ResolveWorksheetPart(workbookPart, target.WorksheetName);
                    if (worksheetPart is null)
                    {
                        continue;
                    }

                    actualValues.Add(TemplateWorkbookHelper.ReadCellText(worksheetPart, target.CellReference, sharedStrings));
                    targets.Add(FormatTarget(target));
                }
            }

            if (actualValues.Count == 0)
            {
                results.Add(new TemplateFieldTestResult(
                    fieldKey,
                    expectedValue,
                    mode,
                    targets,
                    actualValues,
                    false,
                    $"{TemplateAdaptationFields.GetDisplayName(fieldKey)}没有找到任何写入结果。"));
                continue;
            }

            var failedValues = actualValues.Where(item => !string.Equals(item, expectedValue, StringComparison.Ordinal)).ToArray();
            results.Add(new TemplateFieldTestResult(
                fieldKey,
                expectedValue,
                mode,
                targets,
                actualValues,
                failedValues.Length == 0,
                failedValues.Length == 0
                    ? $"{TemplateAdaptationFields.GetDisplayName(fieldKey)}写入成功。"
                    : $"{TemplateAdaptationFields.GetDisplayName(fieldKey)}存在不一致值：{string.Join("、", failedValues)}"));
        }

        return results;
    }

    public static IReadOnlyDictionary<string, string> BuildCanonicalFields(IReadOnlyDictionary<string, string> fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TemplateAdaptationFields.ProjectName] = Pick(fields, TemplateAdaptationFields.ProjectName, "projectName", "工程名称"),
            [TemplateAdaptationFields.ConstructionUnit] = Pick(fields, TemplateAdaptationFields.ConstructionUnit, "constructorUnitName", "施工单位"),
            [TemplateAdaptationFields.SupervisionUnit] = Pick(fields, TemplateAdaptationFields.SupervisionUnit, "supervisorUnitName", "监理单位"),
            [TemplateAdaptationFields.PartName] = Pick(fields, TemplateAdaptationFields.PartName, "partName", "检验批部位", "施工部位", "部位名称"),
            [TemplateAdaptationFields.Capacity] = Pick(fields, TemplateAdaptationFields.Capacity, "capacity", "检验批容量"),
            [TemplateAdaptationFields.ConstructionDate] = Pick(fields, TemplateAdaptationFields.ConstructionDate, "constructionDate", "施工日期")
        };

        return result;
    }

    private static Dictionary<string, string> BuildPlaceholderReplacements(
        IReadOnlyDictionary<string, TemplateFieldMappingInfo> mappings,
        IReadOnlyDictionary<string, string> fields)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in mappings.Values)
        {
            var value = ResolveFieldValue(item.FieldKey, fields);
            foreach (var token in item.PlaceholderTokens)
            {
                replacements[token] = value;
            }
        }

        return replacements;
    }

    private static string ResolveFieldValue(string fieldKey, IReadOnlyDictionary<string, string> fields)
    {
        return BuildCanonicalFields(fields).TryGetValue(fieldKey, out var value) ? value : "";
    }

    private static string Pick(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value))
            {
                return value?.Trim() ?? "";
            }
        }

        return "";
    }

    private static string NormalizeMode(string mode)
    {
        return mode switch
        {
            "Placeholder" or "Cell" or "Hybrid" => mode,
            _ => "Cell"
        };
    }

    private static string FormatTarget(TemplateFieldTarget target)
    {
        return string.IsNullOrWhiteSpace(target.WorksheetName)
            ? target.CellReference
            : $"{target.WorksheetName}!{target.CellReference}";
    }
}

public sealed record TemplateApplyResult(
    IReadOnlyList<TemplateFieldWriteReceipt> WriteReceipts,
    IReadOnlyList<TemplateFieldTestResult> VerificationResults);

public sealed record TemplateFieldWriteReceipt(
    string FieldKey,
    string Mode,
    string ExpectedValue,
    IReadOnlyList<string> Locations);
