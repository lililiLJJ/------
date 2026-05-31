using System.Text.RegularExpressions;
using GeneratorService.Models;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateValidationService
{
    private static readonly Regex CellReferenceRegex = new(@"^[A-Z]{1,3}[1-9][0-9]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FieldKeyRegex = new(@"^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly TemplateService _templateService;
    private readonly TemplateAdaptationRepository _repository;

    public TemplateValidationService(TemplateService templateService, TemplateAdaptationRepository repository)
    {
        _templateService = templateService;
        _repository = repository;
    }

    public TemplateValidationResult Validate(string templateNodeId)
    {
        var template = _templateService.ResolveTemplate(templateNodeId);
        var result = Validate(template);
        _repository.RecordValidation(template, result);
        return result;
    }

    public TemplateValidationResult Validate(TemplateResolution template)
    {
        var detail = _repository.GetAdaptationDetail(template);
        var workbook = File.Exists(template.TemplatePath)
            ? TemplateWorkbookHelper.Inspect(template.TemplatePath)
            : throw new FileNotFoundException($"模板文件不存在：{template.TemplatePath}", template.TemplatePath);
        var mappingsByField = detail.Mappings.ToDictionary(item => item.FieldKey, StringComparer.OrdinalIgnoreCase);
        var issues = new List<TemplateValidationIssue>();
        var missingFields = new List<string>();

        foreach (var duplicateFieldKey in detail.Mappings
                     .GroupBy(item => item.FieldKey, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            issues.Add(CreateIssue("duplicate_field_key", "error", duplicateFieldKey, $"字段 Key 重复：{duplicateFieldKey}"));
        }

        foreach (var mapping in detail.Mappings.Where(item => !item.IsSystemField))
        {
            if (!FieldKeyRegex.IsMatch(mapping.FieldKey))
            {
                issues.Add(CreateIssue("invalid_field_key", "error", mapping.FieldKey, $"自定义字段 Key 无效：{mapping.FieldKey}"));
            }
        }

        foreach (var mapping in detail.Mappings)
        {
            if (mapping.ValueSource is not (TemplateAdaptationFields.ValueSourceBusinessData or TemplateAdaptationFields.ValueSourceDefaultValue))
            {
                issues.Add(CreateIssue("invalid_value_source", "error", mapping.FieldKey, $"字段值来源无效：{mapping.ValueSource}"));
            }
        }

        foreach (var mapping in detail.Mappings.Where(item => item.IsRequired))
        {
            if (!mappingsByField.TryGetValue(mapping.FieldKey, out var current))
            {
                missingFields.Add(mapping.FieldKey);
                issues.Add(CreateIssue("missing_mapping", "error", mapping.FieldKey, "缺少字段映射配置。"));
                continue;
            }

            if (!current.IsEnabled)
            {
                missingFields.Add(mapping.FieldKey);
                issues.Add(CreateIssue("disabled_mapping", "error", mapping.FieldKey, "字段映射已禁用。"));
                continue;
            }

            var mode = NormalizeMode(current.Mode);
            var hasPlaceholder = false;
            var hasCell = false;

            if (mode is "Placeholder" or "Hybrid")
            {
                if (current.PlaceholderTokens.Count == 0)
                {
                    issues.Add(CreateIssue("missing_placeholder_token", "error", mapping.FieldKey, "未配置占位符。"));
                }
                else
                {
                    foreach (var token in current.PlaceholderTokens)
                    {
                        if (workbook.Placeholders.Any(item => string.Equals(item, token, StringComparison.OrdinalIgnoreCase)))
                        {
                            hasPlaceholder = true;
                        }
                        else
                        {
                            issues.Add(CreateIssue("placeholder_not_found", "error", mapping.FieldKey, $"模板中不存在占位符：{{{{{token}}}}}。"));
                        }
                    }
                }
            }

            if (mode is "Cell" or "Hybrid")
            {
                if (current.Targets.Count == 0)
                {
                    issues.Add(CreateIssue("missing_target_cell", "error", mapping.FieldKey, "未配置目标单元格。"));
                }
                else
                {
                    foreach (var target in current.Targets)
                    {
                        if (!CellReferenceRegex.IsMatch(target.CellReference))
                        {
                            issues.Add(CreateIssue("invalid_cell_reference", "error", mapping.FieldKey, $"单元格地址无效：{target.CellReference}", target.WorksheetName, target.CellReference));
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(target.WorksheetName) &&
                            workbook.Worksheets.All(item => !string.Equals(item.Name, target.WorksheetName, StringComparison.OrdinalIgnoreCase)))
                        {
                            issues.Add(CreateIssue("worksheet_not_found", "error", mapping.FieldKey, $"工作表不存在：{target.WorksheetName}", target.WorksheetName, target.CellReference));
                            continue;
                        }

                        hasCell = true;
                    }
                }
            }

            if (mode == "Placeholder" && !hasPlaceholder)
            {
                missingFields.Add(mapping.FieldKey);
            }
            else if (mode == "Cell" && !hasCell)
            {
                missingFields.Add(mapping.FieldKey);
            }
            else if (mode == "Hybrid" && !hasPlaceholder && !hasCell)
            {
                missingFields.Add(mapping.FieldKey);
            }
        }

        var status = missingFields.Count == 0
            ? issues.Any(item => string.Equals(item.Level, "error", StringComparison.OrdinalIgnoreCase)) ? "error" : "ready"
            : "incomplete";
        return new TemplateValidationResult(
            true,
            template.TemplateNodeId,
            template.TemplateName,
            template.ModuleId,
            template.ModuleVersion,
            detail.Profile.MappingMode,
            status,
            missingFields.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            issues,
            missingFields.Count == 0 && issues.All(item => !string.Equals(item.Level, "error", StringComparison.OrdinalIgnoreCase)),
            DateTimeOffset.Now);
    }

    public void EnsureReadyForGeneration(TemplateResolution template)
    {
        var result = Validate(template);
        if (result.CanGenerate)
        {
            return;
        }

        _repository.RecordValidation(template, result);
        var detail = _repository.GetAdaptationDetail(template);
        var labelsByField = detail.Mappings.ToDictionary(
            item => item.FieldKey,
            item => string.IsNullOrWhiteSpace(item.DisplayName) ? TemplateAdaptationFields.GetDisplayName(item.FieldKey) : item.DisplayName,
            StringComparer.OrdinalIgnoreCase);
        var missingLabels = result.MissingFields
            .Select(fieldKey => labelsByField.TryGetValue(fieldKey, out var displayName) ? displayName : TemplateAdaptationFields.GetDisplayName(fieldKey))
            .ToArray();
        var message = missingLabels.Length > 0
            ? $"模板“{template.TemplateName}”缺少必填字段映射：{string.Join("、", missingLabels)}，无法生成资料。"
            : $"模板“{template.TemplateName}”未通过模板适配校验，无法生成资料。";
        throw new TemplateAdaptationException(
            message,
            template.TemplateNodeId,
            template.TemplateName,
            result.MissingFields,
            result.Status);
    }

    private static string NormalizeMode(string mode)
    {
        return mode switch
        {
            "Placeholder" or "Cell" or "Hybrid" => mode,
            _ => "Cell"
        };
    }

    private static TemplateValidationIssue CreateIssue(
        string code,
        string level,
        string fieldKey,
        string message,
        string? worksheetName = null,
        string? cellReference = null)
    {
        return new TemplateValidationIssue(code, level, fieldKey, message, worksheetName, cellReference);
    }
}
