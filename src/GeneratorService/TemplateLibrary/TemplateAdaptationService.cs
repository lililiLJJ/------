using System.Text.RegularExpressions;
using GeneratorService.Models;
using GeneratorService.Modules;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateAdaptationService
{
    private static readonly Regex CellReferenceRegex = new(@"^[A-Z]{1,3}[1-9][0-9]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FieldKeyRegex = new(@"^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly TemplateService _templateService;
    private readonly TemplateAdaptationRepository _repository;
    private readonly TemplateValidationService _validationService;
    private readonly TemplateTestHarness _testHarness;
    private readonly ModuleManager _moduleManager;

    public TemplateAdaptationService(
        TemplateService templateService,
        TemplateAdaptationRepository repository,
        TemplateValidationService validationService,
        TemplateTestHarness testHarness,
        ModuleManager moduleManager)
    {
        _templateService = templateService;
        _repository = repository;
        _validationService = validationService;
        _testHarness = testHarness;
        _moduleManager = moduleManager;
    }

    public TemplateMappingTemplateListResult GetTemplates()
    {
        var templates = _repository.ListTemplateResolutions(_moduleManager)
            .Select(template =>
            {
                var detail = _repository.GetAdaptationDetail(template);
                var requiredMappings = detail.Mappings.Where(item => item.IsRequired).ToArray();
                return new TemplateMappingTemplateSummary(
                    template.TemplateNodeId,
                    template.ModuleId,
                    template.ModuleVersion,
                    template.TemplateItemId,
                    template.TemplateCode,
                    template.TemplateName,
                    template.TemplateFile,
                    template.TemplatePath,
                    detail.Mappings.Count(item => item.IsEnabled),
                    requiredMappings.Length,
                    requiredMappings.Length - detail.MissingRequiredFields.Count,
                    detail.MissingRequiredFields,
                    _repository.GetProfile(template.ModuleId, template.ModuleVersion, template.TemplateItemId)?.UpdatedAt,
                    detail.ModuleStatus,
                    detail.AdaptationStatus,
                    detail.CanEdit,
                    detail.CanTest);
            })
            .OrderBy(item => item.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TemplateName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new TemplateMappingTemplateListResult(true, templates.Length, templates);
    }

    public TemplateAdaptationDetailResult GetDetail(string templateNodeId)
    {
        return _repository.GetAdaptationDetail(_templateService.ResolveTemplate(templateNodeId));
    }

    public TemplateMappingFieldListResult GetFields(string templateNodeId)
    {
        var detail = GetDetail(templateNodeId);
        return new TemplateMappingFieldListResult(true, templateNodeId, detail.Profile.TemplateName, detail.Mappings);
    }

    public TemplateAdaptationDetailResult Save(string templateNodeId, TemplateAdaptationSaveRequest request)
    {
        var template = _templateService.ResolveTemplate(templateNodeId);
        var mappings = NormalizeMappings(request.Mappings);
        var mappingMode = string.IsNullOrWhiteSpace(request.MappingMode)
            ? TemplateAdaptationRepository.ResolveProfileMappingMode(mappings)
            : request.MappingMode.Trim();
        return _repository.SaveAdaptation(template, mappingMode, mappings);
    }

    public TemplateMappingFieldListResult SaveFields(string templateNodeId, TemplateAdaptationSaveRequest request)
    {
        var detail = Save(templateNodeId, request);
        return new TemplateMappingFieldListResult(true, templateNodeId, detail.Profile.TemplateName, detail.Mappings);
    }

    public TemplateFieldMappingInfo SaveField(string templateNodeId, string fieldKey, TemplateFieldMappingSaveItem request)
    {
        var detail = GetDetail(templateNodeId);
        var nextFieldKey = string.IsNullOrWhiteSpace(request.FieldKey) ? fieldKey : request.FieldKey.Trim();
        var normalized = NormalizeMappings(
        [
            .. detail.Mappings
                .Where(item => !string.Equals(item.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
                .Select(item => ToSaveItem(item)),
            request with { FieldKey = nextFieldKey }
        ]);
        var saved = _repository.SaveAdaptation(
            _templateService.ResolveTemplate(templateNodeId),
            TemplateAdaptationRepository.ResolveProfileMappingMode(normalized),
            normalized);
        return saved.Mappings.First(item => string.Equals(item.FieldKey, nextFieldKey, StringComparison.OrdinalIgnoreCase));
    }

    public TemplateMappingFieldDeleteResult DeleteField(string templateNodeId, string fieldKey)
    {
        if (TemplateAdaptationFields.IsSystemField(fieldKey))
        {
            throw new InvalidOperationException($"系统字段不允许删除：{fieldKey}");
        }

        var detail = GetDetail(templateNodeId);
        var remaining = detail.Mappings
            .Where(item => !string.Equals(item.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
            .Select(ToSaveItem)
            .ToArray();
        _repository.SaveAdaptation(
            _templateService.ResolveTemplate(templateNodeId),
            TemplateAdaptationRepository.ResolveProfileMappingMode(NormalizeMappings(remaining)),
            NormalizeMappings(remaining));
        return new TemplateMappingFieldDeleteResult(true, templateNodeId, fieldKey, "字段映射已删除。");
    }

    public TemplateValidationResult Validate(string templateNodeId)
    {
        return _validationService.Validate(templateNodeId);
    }

    public TemplateTestResult Test(string templateNodeId)
    {
        return _testHarness.RunTemplateTest(templateNodeId);
    }

    public TemplateBatchTestResult BatchTest(TemplateBatchTestRequest? request)
    {
        return _testHarness.RunBatchTest(request);
    }

    private static IReadOnlyList<TemplateFieldMappingInfo> NormalizeMappings(IReadOnlyList<TemplateFieldMappingSaveItem>? mappings)
    {
        var provided = mappings ?? [];
        var seenFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<TemplateFieldMappingInfo>();

        foreach (var item in provided)
        {
            var fieldKey = item.FieldKey?.Trim();
            if (string.IsNullOrWhiteSpace(fieldKey))
            {
                continue;
            }

            if (!seenFieldKeys.Add(fieldKey))
            {
                throw new InvalidOperationException($"字段 Key 重复：{fieldKey}");
            }

            ValidateFieldKey(fieldKey, item.IsSystemField);

            var placeholderTokens = (item.PlaceholderTokens ?? [])
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(token => token.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var targets = (item.Targets ?? [])
                .Where(target => !string.IsNullOrWhiteSpace(target.CellReference))
                .Select(target => new TemplateFieldTarget(
                    string.IsNullOrWhiteSpace(target.WorksheetName) ? null : target.WorksheetName.Trim(),
                    target.CellReference.Trim().ToUpperInvariant()))
                .ToArray();
            foreach (var target in targets)
            {
                if (!CellReferenceRegex.IsMatch(target.CellReference))
                {
                    throw new InvalidOperationException($"单元格地址格式无效：{target.CellReference}");
                }
            }

            var isSystemField = item.IsSystemField ?? TemplateAdaptationFields.IsSystemField(fieldKey);
            var displayName = string.IsNullOrWhiteSpace(item.DisplayName)
                ? TemplateAdaptationFields.GetDisplayName(fieldKey)
                : item.DisplayName.Trim();
            var valueSource = NormalizeValueSource(item.ValueSource, fieldKey);
            var sortOrder = item.SortOrder ?? TemplateAdaptationFields.GetDefaultSortOrder(fieldKey);
            var defaultValue = item.DefaultValue?.Trim() ?? "";
            var isEnabled = item.IsEnabled ?? (placeholderTokens.Length > 0 || targets.Length > 0);
            normalized.Add(new TemplateFieldMappingInfo(
                fieldKey,
                displayName,
                item.Mode?.Trim() ?? "Cell",
                placeholderTokens,
                targets,
                valueSource,
                defaultValue,
                sortOrder,
                isSystemField,
                string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
                item.IsRequired ?? true,
                isEnabled,
                targets.Length == 0
                    ? null
                    : string.IsNullOrWhiteSpace(targets[0].WorksheetName)
                        ? targets[0].CellReference
                        : $"{targets[0].WorksheetName}!{targets[0].CellReference}",
                targets.Length));
        }

        foreach (var requiredFieldKey in TemplateAdaptationFields.RequiredFieldKeys)
        {
            if (normalized.Any(item => string.Equals(item.FieldKey, requiredFieldKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalized.Add(TemplateAdaptationFields.CreateDefaultMapping(requiredFieldKey));
        }

        return normalized
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateFieldKey(string fieldKey, bool? isSystemField)
    {
        if (TemplateAdaptationFields.IsSystemField(fieldKey))
        {
            return;
        }

        if (isSystemField == true)
        {
            return;
        }

        if (!FieldKeyRegex.IsMatch(fieldKey))
        {
            throw new InvalidOperationException($"自定义字段 Key 只允许英文、数字和下划线，且必须以字母开头：{fieldKey}");
        }
    }

    private static string NormalizeValueSource(string? valueSource, string fieldKey)
    {
        if (string.Equals(valueSource, TemplateAdaptationFields.ValueSourceDefaultValue, StringComparison.OrdinalIgnoreCase))
        {
            return TemplateAdaptationFields.ValueSourceDefaultValue;
        }

        return TemplateAdaptationFields.ValueSourceBusinessData;
    }

    private static TemplateFieldMappingSaveItem ToSaveItem(TemplateFieldMappingInfo item)
    {
        return new TemplateFieldMappingSaveItem(
            item.FieldKey,
            item.DisplayName,
            item.Mode,
            item.PlaceholderTokens,
            item.Targets,
            item.ValueSource,
            item.DefaultValue,
            item.SortOrder,
            item.IsSystemField,
            item.Description,
            item.IsRequired,
            item.IsEnabled);
    }
}
