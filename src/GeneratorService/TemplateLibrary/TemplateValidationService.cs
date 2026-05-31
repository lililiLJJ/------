using System.Text.RegularExpressions;
using GeneratorService.Models;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateValidationService
{
    private static readonly Regex CellReferenceRegex = new(@"^[A-Z]{1,3}[1-9][0-9]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        foreach (var fieldKey in TemplateAdaptationFields.RequiredFieldKeys)
        {
            if (!mappingsByField.TryGetValue(fieldKey, out var mapping))
            {
                missingFields.Add(fieldKey);
                issues.Add(CreateIssue("missing_mapping", "error", fieldKey, "缺少字段映射配置。"));
                continue;
            }

            if (!mapping.IsEnabled)
            {
                missingFields.Add(fieldKey);
                issues.Add(CreateIssue("disabled_mapping", "error", fieldKey, "字段映射已禁用。"));
                continue;
            }

            var mode = NormalizeMode(mapping.Mode);
            var hasPlaceholder = false;
            var hasCell = false;

            if (mode is "Placeholder" or "Hybrid")
            {
                if (mapping.PlaceholderTokens.Count == 0)
                {
                    issues.Add(CreateIssue("missing_placeholder_token", "error", fieldKey, "未配置占位符。"));
                }
                else
                {
                    foreach (var token in mapping.PlaceholderTokens)
                    {
                        if (workbook.Placeholders.Any(item => string.Equals(item, token, StringComparison.OrdinalIgnoreCase)))
                        {
                            hasPlaceholder = true;
                        }
                        else
                        {
                            issues.Add(CreateIssue("placeholder_not_found", "error", fieldKey, $"模板中不存在占位符：{{{{{token}}}}}。"));
                        }
                    }
                }
            }

            if (mode is "Cell" or "Hybrid")
            {
                if (mapping.Targets.Count == 0)
                {
                    issues.Add(CreateIssue("missing_target_cell", "error", fieldKey, "未配置目标单元格。"));
                }
                else
                {
                    foreach (var target in mapping.Targets)
                    {
                        if (!CellReferenceRegex.IsMatch(target.CellReference ?? ""))
                        {
                            issues.Add(CreateIssue("invalid_cell_reference", "error", fieldKey, $"单元格地址无效：{target.CellReference}", target.WorksheetName, target.CellReference));
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(target.WorksheetName) &&
                            workbook.Worksheets.All(item => !string.Equals(item.Name, target.WorksheetName, StringComparison.OrdinalIgnoreCase)))
                        {
                            issues.Add(CreateIssue("worksheet_not_found", "error", fieldKey, $"工作表不存在：{target.WorksheetName}", target.WorksheetName, target.CellReference));
                            continue;
                        }

                        hasCell = true;
                    }
                }
            }

            if (mode == "Placeholder" && !hasPlaceholder)
            {
                missingFields.Add(fieldKey);
            }
            else if (mode == "Cell" && !hasCell)
            {
                missingFields.Add(fieldKey);
            }
            else if (mode == "Hybrid" && !hasPlaceholder && !hasCell)
            {
                missingFields.Add(fieldKey);
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
        var missingLabels = result.MissingFields
            .Select(TemplateAdaptationFields.GetDisplayName)
            .ToArray();
        var message = missingLabels.Length > 0
            ? $"模板“{template.TemplateName}”缺少必要字段映射：{string.Join("、", missingLabels)}，无法生成资料。"
            : $"模板“{template.TemplateName}”未通过适配校验，无法生成资料。";
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
