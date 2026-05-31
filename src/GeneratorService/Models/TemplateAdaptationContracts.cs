namespace GeneratorService.Models;

public sealed record TemplateFieldTarget(
    string? WorksheetName,
    string CellReference);

public sealed record TemplateProfileInfo(
    string TemplateNodeId,
    string ModuleId,
    string ModuleVersion,
    long TemplateItemId,
    string TemplateCode,
    string TemplateName,
    string TemplateFile,
    string MappingMode,
    string Status,
    DateTimeOffset? LastValidatedAt,
    DateTimeOffset? LastTestedAt,
    string? LastTestStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TemplateFieldMappingInfo(
    string FieldKey,
    string Mode,
    IReadOnlyList<string> PlaceholderTokens,
    IReadOnlyList<TemplateFieldTarget> Targets,
    bool IsRequired,
    bool IsEnabled);

public sealed record TemplateAdaptationDetailResult(
    bool Success,
    string TemplateNodeId,
    TemplateProfileInfo Profile,
    IReadOnlyList<TemplateFieldMappingInfo> Mappings,
    IReadOnlyList<string> RequiredFields);

public sealed record TemplateFieldMappingSaveItem(
    string? FieldKey,
    string? Mode,
    IReadOnlyList<string>? PlaceholderTokens,
    IReadOnlyList<TemplateFieldTarget>? Targets,
    bool? IsRequired,
    bool? IsEnabled);

public sealed record TemplateAdaptationSaveRequest(
    string? MappingMode,
    IReadOnlyList<TemplateFieldMappingSaveItem>? Mappings);

public sealed record TemplateValidationIssue(
    string Code,
    string Level,
    string FieldKey,
    string Message,
    string? WorksheetName,
    string? CellReference);

public sealed record TemplateValidationResult(
    bool Success,
    string TemplateNodeId,
    string TemplateName,
    string ModuleId,
    string ModuleVersion,
    string MappingMode,
    string Status,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<TemplateValidationIssue> Issues,
    bool CanGenerate,
    DateTimeOffset ValidatedAt);

public sealed record TemplateFieldTestResult(
    string FieldKey,
    string ExpectedValue,
    string Mode,
    IReadOnlyList<string> TargetLocations,
    IReadOnlyList<string> ActualValues,
    bool Success,
    string Message);

public sealed record TemplateLayoutComparisonResult(
    bool Success,
    int PrintPageCountBefore,
    int PrintPageCountAfter,
    IReadOnlyList<string> Differences);

public sealed record TemplateTestResult(
    bool Success,
    string TemplateNodeId,
    string TemplateName,
    string ModuleId,
    string ModuleVersion,
    string OutputPath,
    string MappingMode,
    IReadOnlyList<TemplateFieldTestResult> Fields,
    TemplateLayoutComparisonResult Layout,
    string Result,
    DateTimeOffset TestedAt,
    IReadOnlyList<string> Messages);

public sealed record TemplateBatchTestRequest(
    string? ModuleId,
    int? Count);

public sealed record TemplateBatchTestResult(
    bool Success,
    string ModuleId,
    string ModuleVersion,
    int RequestedCount,
    int TestedCount,
    int PassedCount,
    int FailedCount,
    string ReportPath,
    IReadOnlyList<TemplateTestResult> Results,
    DateTimeOffset TestedAt);

public sealed class TemplateAdaptationException : InvalidOperationException
{
    public TemplateAdaptationException(
        string message,
        string templateNodeId,
        string templateName,
        IReadOnlyList<string>? missingFields = null,
        string? adaptationStatus = null,
        bool canOpenAdaptationPanel = true)
        : base(message)
    {
        TemplateNodeId = templateNodeId;
        TemplateName = templateName;
        MissingFields = missingFields ?? [];
        AdaptationStatus = adaptationStatus ?? "error";
        CanOpenAdaptationPanel = canOpenAdaptationPanel;
    }

    public string TemplateNodeId { get; }

    public string TemplateName { get; }

    public IReadOnlyList<string> MissingFields { get; }

    public string AdaptationStatus { get; }

    public bool CanOpenAdaptationPanel { get; }
}
