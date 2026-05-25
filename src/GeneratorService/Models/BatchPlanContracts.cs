namespace GeneratorService.Models;

public sealed record BatchPlanListResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    IReadOnlyList<BatchPlanInfo> Plans);

public sealed record BatchPlanInfo(
    string Id,
    string ProjectId,
    string UnitProjectId,
    string Name,
    string Remark,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<BatchPlanItemInfo> Items);

public sealed record BatchPlanItemInfo(
    string Id,
    string BatchPlanId,
    string ProjectId,
    string UnitProjectId,
    string ModuleId,
    long TemplateItemId,
    string TemplateName,
    string PartName,
    string Capacity,
    string QuantityUnit,
    string ConstructionDate,
    IReadOnlyDictionary<string, decimal> DeviceQuantities,
    string? GeneratedDocumentId,
    string Status,
    string ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record BatchPlanSaveRequest(
    string? ProjectId,
    string? UnitProjectId,
    string? Name,
    string? Remark,
    IReadOnlyList<BatchPlanItemSaveRequest>? Items);

public sealed record BatchPlanItemSaveRequest(
    string? Id,
    string? ModuleId,
    long? TemplateItemId,
    string? TemplateName,
    string? PartName,
    string? Capacity,
    string? QuantityUnit,
    string? ConstructionDate,
    IReadOnlyDictionary<string, decimal?>? DeviceQuantities,
    string? Status,
    string? ErrorMessage);

public sealed record BatchPlanSaveResult(
    bool Success,
    BatchPlanInfo Plan,
    string Message);

public sealed record BatchPlanPreviewResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    string BatchPlanId,
    int TotalCount,
    int GeneratableCount,
    int BlockedCount,
    IReadOnlyList<BatchPlanPreviewRow> Rows,
    IReadOnlyList<string> Warnings);

public sealed record BatchPlanPreviewRow(
    int RowIndex,
    string ItemId,
    string ModuleId,
    long TemplateItemId,
    string TemplateName,
    string PartName,
    string Capacity,
    string QuantityUnit,
    string ConstructionDate,
    string OutputName,
    IReadOnlyDictionary<string, decimal> DeviceQuantities,
    IReadOnlyList<DeviceMappingPreview> Mappings,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool CanGenerate);

public sealed record DeviceMappingPreview(
    string DeviceFieldKey,
    string DeviceDisplayName,
    decimal Quantity,
    string InspectionItemName,
    string FillMode,
    bool HasTargetCells,
    bool HasPlaceholderFallback,
    string Source);

public sealed record BatchPlanGenerateResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    string BatchPlanId,
    int SuccessCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<BatchPlanGenerateRowResult> Rows,
    string Message);

public sealed record BatchPlanGenerateRequest(
    IReadOnlyDictionary<string, string>? Fields);

public sealed record BatchPlanGenerateRowResult(
    int RowIndex,
    string ItemId,
    bool Success,
    bool Skipped,
    string? DocumentId,
    string? FilePath,
    string Message);

public sealed record DeviceFieldInfo(
    string Key,
    string DisplayName,
    string Unit,
    int SortOrder);

public sealed record DeviceFieldsResult(
    bool Success,
    string ModuleId,
    long TemplateItemId,
    IReadOnlyList<DeviceFieldInfo> Fields);

public sealed record DeviceMappingInfo(
    string Id,
    string ModuleId,
    long TemplateItemId,
    long? RuleId,
    string InspectionItemName,
    string DeviceFieldKey,
    string DeviceDisplayName,
    IReadOnlyDictionary<string, string> TargetCells,
    string FillMode,
    bool IsEnabled,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DeviceMappingsResult(
    bool Success,
    string ModuleId,
    long TemplateItemId,
    IReadOnlyList<DeviceMappingInfo> Mappings);

public sealed record DeviceMappingsSaveRequest(
    string? ModuleId,
    long? TemplateItemId,
    IReadOnlyList<DeviceMappingSaveItem>? Mappings);

public sealed record DeviceMappingSaveItem(
    string? Id,
    long? RuleId,
    string? InspectionItemName,
    string? DeviceFieldKey,
    string? DeviceDisplayName,
    IReadOnlyDictionary<string, string>? TargetCells,
    string? FillMode,
    bool? IsEnabled,
    int? SortOrder);
