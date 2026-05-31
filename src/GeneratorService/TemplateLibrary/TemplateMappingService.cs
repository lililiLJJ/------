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
        return TemplateAdaptationFields.IsManagedModule(template.ModuleId);
    }

    public TemplateApplyResult Apply(string filePath, TemplateResolution template, IReadOnlyDictionary<string, string> fields)
    {
        _validationService.EnsureReadyForGeneration(template);
        var detail = _repository.GetAdaptationDetail(template);
        var mappings = detail.Mappings
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.IsSystemField ? 0 : 1)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var replacements = BuildPlaceholderReplacements(mappings, fields);
        var writeReceipts = new List<TemplateFieldWriteReceipt>();

        using (var document = SpreadsheetDocument.Open(filePath, true))
        {
            var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少 WorkbookPart。");
            TemplateWorkbookHelper.ReplaceSimplePlaceholders(workbookPart, replacements);

            foreach (var mapping in mappings)
            {
                var expectedValue = ResolveFieldValue(mapping, fields);
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

                if (mode is "Placeholder" or "Hybrid")
                {
                    locations.AddRange(mapping.PlaceholderTokens.Select(item => $"{{{{{item}}}}}"));
                }

                writeReceipts.Add(new TemplateFieldWriteReceipt(mapping.FieldKey, mode, expectedValue, locations));
            }

            workbookPart.Workbook.Save();
        }

        var fieldResults = VerifyWrittenValues(
            filePath,
            template,
            fields,
            mappings.ToDictionary(item => item.FieldKey, StringComparer.OrdinalIgnoreCase));
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
        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少 WorkbookPart。");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var results = new List<TemplateFieldTestResult>();

        foreach (var mapping in mappings.Values
                     .OrderBy(item => item.IsSystemField ? 0 : 1)
                     .ThenBy(item => item.SortOrder)
                     .ThenBy(item => item.FieldKey, StringComparer.OrdinalIgnoreCase))
        {
            var expectedValue = ResolveFieldValue(mapping, fields);
            var mode = NormalizeMode(mapping.Mode);
            var targets = new List<string>();
            var actualValues = new List<string>();

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

            if (mode is "Placeholder" or "Hybrid")
            {
                targets.AddRange(mapping.PlaceholderTokens.Select(item => $"{{{{{item}}}}}"));
            }

            if (actualValues.Count == 0 && mapping.Targets.Count > 0)
            {
                results.Add(new TemplateFieldTestResult(
                    mapping.FieldKey,
                    expectedValue,
                    mode,
                    targets,
                    actualValues,
                    false,
                    $"{mapping.DisplayName}没有找到任何写入结果。"));
                continue;
            }

            var failedValues = actualValues
                .Where(item => !string.Equals(item, expectedValue, StringComparison.Ordinal))
                .ToArray();
            results.Add(new TemplateFieldTestResult(
                mapping.FieldKey,
                expectedValue,
                mode,
                targets,
                actualValues,
                failedValues.Length == 0,
                failedValues.Length == 0
                    ? $"{mapping.DisplayName}写入成功。"
                    : $"{mapping.DisplayName}存在不一致值：{string.Join("、", failedValues)}"));
        }

        return results;
    }

    public static IReadOnlyDictionary<string, string> BuildCanonicalFields(IReadOnlyDictionary<string, string> fields)
    {
        var result = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase)
        {
            [TemplateAdaptationFields.ProjectName] = Pick(fields, TemplateAdaptationFields.ProjectName, "projectName", "工程名称"),
            [TemplateAdaptationFields.ConstructionUnit] = Pick(fields, TemplateAdaptationFields.ConstructionUnit, "constructorUnitName", "施工单位"),
            [TemplateAdaptationFields.SupervisionUnit] = Pick(fields, TemplateAdaptationFields.SupervisionUnit, "supervisorUnitName", "监理单位"),
            [TemplateAdaptationFields.PartName] = Pick(fields, TemplateAdaptationFields.PartName, "partName", "检验批部位", "施工部位", "部位名称"),
            [TemplateAdaptationFields.Capacity] = Pick(fields, TemplateAdaptationFields.Capacity, "capacity", "检验批容量"),
            [TemplateAdaptationFields.ConstructionDate] = Pick(fields, TemplateAdaptationFields.ConstructionDate, "constructionDate", "施工日期"),
            ["developerUnitName"] = Pick(fields, "developerUnitName", "建设单位"),
            ["designUnitName"] = Pick(fields, "designUnitName", "设计单位"),
            ["professionalSubcontractorUnitName"] = Pick(fields, "professionalSubcontractorUnitName", "专业分包单位"),
            ["thirdPartyInspectionUnitName"] = Pick(fields, "thirdPartyInspectionUnitName", "第三方检测单位", "检测单位"),
            ["acceptanceDate"] = Pick(fields, "acceptanceDate", "验收日期")
        };

        return result;
    }

    private static Dictionary<string, string> BuildPlaceholderReplacements(
        IReadOnlyList<TemplateFieldMappingInfo> mappings,
        IReadOnlyDictionary<string, string> fields)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in mappings)
        {
            var value = ResolveFieldValue(item, fields);
            foreach (var token in item.PlaceholderTokens)
            {
                replacements[token] = value;
            }
        }

        return replacements;
    }

    private static string ResolveFieldValue(TemplateFieldMappingInfo mapping, IReadOnlyDictionary<string, string> fields)
    {
        var canonicalFields = BuildCanonicalFields(fields);
        if (string.Equals(mapping.ValueSource, TemplateAdaptationFields.ValueSourceBusinessData, StringComparison.OrdinalIgnoreCase))
        {
            if (canonicalFields.TryGetValue(mapping.FieldKey, out var exactValue) && !string.IsNullOrWhiteSpace(exactValue))
            {
                return exactValue;
            }

            if (mapping.IsSystemField &&
                canonicalFields.TryGetValue(mapping.FieldKey, out var systemValue))
            {
                return systemValue ?? "";
            }
        }

        return mapping.DefaultValue ?? "";
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
