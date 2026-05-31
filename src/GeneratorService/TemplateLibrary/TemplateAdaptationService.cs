using GeneratorService.Models;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateAdaptationService
{
    private readonly TemplateService _templateService;
    private readonly TemplateAdaptationRepository _repository;
    private readonly TemplateValidationService _validationService;
    private readonly TemplateTestHarness _testHarness;

    public TemplateAdaptationService(
        TemplateService templateService,
        TemplateAdaptationRepository repository,
        TemplateValidationService validationService,
        TemplateTestHarness testHarness)
    {
        _templateService = templateService;
        _repository = repository;
        _validationService = validationService;
        _testHarness = testHarness;
    }

    public TemplateAdaptationDetailResult GetDetail(string templateNodeId)
    {
        return _repository.GetAdaptationDetail(_templateService.ResolveTemplate(templateNodeId));
    }

    public TemplateAdaptationDetailResult Save(string templateNodeId, TemplateAdaptationSaveRequest request)
    {
        var template = _templateService.ResolveTemplate(templateNodeId);
        var mappings = NormalizeMappings(request.Mappings);
        var mappingMode = string.IsNullOrWhiteSpace(request.MappingMode)
            ? ResolveProfileMappingMode(mappings)
            : request.MappingMode.Trim();
        return _repository.SaveAdaptation(template, mappingMode, mappings);
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
        var byField = provided
            .Where(item => !string.IsNullOrWhiteSpace(item.FieldKey))
            .ToDictionary(item => item.FieldKey!.Trim(), StringComparer.OrdinalIgnoreCase);

        return TemplateAdaptationFields.RequiredFieldKeys
            .Select(fieldKey =>
            {
                if (!byField.TryGetValue(fieldKey, out var item))
                {
                    return new TemplateFieldMappingInfo(fieldKey, "Cell", [], [], true, false);
                }

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
                return new TemplateFieldMappingInfo(
                    fieldKey,
                    item.Mode?.Trim() ?? "Cell",
                    placeholderTokens,
                    targets,
                    item.IsRequired ?? true,
                    item.IsEnabled ?? (placeholderTokens.Length > 0 || targets.Length > 0));
            })
            .ToArray();
    }

    private static string ResolveProfileMappingMode(IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var hasPlaceholder = mappings.Any(item =>
            item.IsEnabled &&
            (string.Equals(item.Mode, "Placeholder", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Mode, "Hybrid", StringComparison.OrdinalIgnoreCase)) &&
            item.PlaceholderTokens.Count > 0);
        var hasCell = mappings.Any(item =>
            item.IsEnabled &&
            (string.Equals(item.Mode, "Cell", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Mode, "Hybrid", StringComparison.OrdinalIgnoreCase)) &&
            item.Targets.Count > 0);

        return hasPlaceholder && hasCell
            ? "Hybrid"
            : hasPlaceholder
                ? "Placeholder"
                : "Cell";
    }
}
