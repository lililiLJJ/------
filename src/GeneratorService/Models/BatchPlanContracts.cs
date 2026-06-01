namespace GeneratorService.Models;

public sealed record InspectionPlanListResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    string? CurrentPlanId,
    IReadOnlyList<InspectionBatchPlanDto> Plans);

public sealed record InspectionBatchPlanDto(
    string PlanId,
    string ProjectId,
    string UnitProjectId,
    string PlanName,
    string Remark,
    string Status,
    int RowCount,
    int GeneratedCount,
    DateTimeOffset CreatedTime,
    DateTimeOffset UpdatedTime,
    DateTimeOffset? DeletedTime,
    IReadOnlyList<InspectionBatchPlanRowDto> Rows);

public sealed record InspectionBatchPlanRowDto(
    string PlanRowId,
    string PlanId,
    string ProjectId,
    string UnitProjectId,
    string DivisionId,
    string DivisionName,
    string SubDivisionId,
    string SubDivisionName,
    string SubItemId,
    string SubItemName,
    string TemplateNodeId,
    long TemplateItemId,
    string TemplateName,
    string InspectionPart,
    string ConstructionDate,
    string CapacitySummary,
    string Remark,
    string Status,
    string GenerateStatus,
    string? ActiveDocumentId,
    DateTimeOffset CreatedTime,
    DateTimeOffset UpdatedTime,
    DateTimeOffset? DeletedTime,
    IReadOnlyList<PlanRowCapacityDto> Capacities);

public sealed record PlanRowCapacityDto(
    string CapacityId,
    string PlanRowId,
    string CapacityKey,
    string CapacityName,
    string Value,
    string Unit,
    int SortOrder,
    DateTimeOffset CreatedTime,
    DateTimeOffset UpdatedTime);

public sealed record InspectionPlanSaveRequest(
    string? UnitProjectId,
    string? PlanName,
    string? Remark,
    IReadOnlyList<InspectionPlanRowSaveRequest>? Rows);

public sealed record InspectionPlanRowSaveRequest(
    string? PlanRowId,
    string? DivisionId,
    string? DivisionName,
    string? SubDivisionId,
    string? SubDivisionName,
    string? SubItemId,
    string? SubItemName,
    string? TemplateNodeId,
    long? TemplateItemId,
    string? TemplateName,
    string? InspectionPart,
    string? ConstructionDate,
    string? Remark,
    string? Status,
    string? GenerateStatus,
    IReadOnlyList<PlanRowCapacitySaveItem>? Capacities);

public sealed record PlanRowCapacitySaveItem(
    string? CapacityId,
    string? CapacityKey,
    string? CapacityName,
    string? Value,
    string? Unit,
    int? SortOrder);

public sealed record InspectionPlanSaveResult(
    bool Success,
    InspectionBatchPlanDto Plan,
    string Message);

public sealed record InspectionPlanDeleteResult(
    bool Success,
    string PlanId,
    string Message);

public sealed record CapacityFieldConfigInfo(
    string ConfigId,
    string ProjectId,
    string UnitProjectId,
    string TemplateNodeId,
    string CapacityKey,
    string CapacityName,
    string DefaultUnit,
    bool Required,
    int SortOrder,
    bool Enabled,
    bool IsOverride);

public sealed record CapacityFieldConfigResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    string TemplateNodeId,
    IReadOnlyList<CapacityFieldConfigInfo> Items);

public sealed record SaveCapacityFieldConfigRequest(
    string? UnitProjectId,
    string TemplateNodeId,
    IReadOnlyList<SaveCapacityFieldConfigItem>? Items);

public sealed record SaveCapacityFieldConfigItem(
    string? ConfigId,
    string? CapacityKey,
    string? CapacityName,
    string? DefaultUnit,
    bool? Required,
    int? SortOrder,
    bool? Enabled);

public sealed record InspectionPlanPreviewResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    string PlanId,
    int TotalCount,
    int GeneratableCount,
    int BlockedCount,
    IReadOnlyList<InspectionPlanPreviewRow> Rows,
    IReadOnlyList<string> Warnings);

public sealed record InspectionPlanPreviewRow(
    int RowIndex,
    string PlanRowId,
    string TemplateNodeId,
    string TemplateName,
    string DivisionName,
    string SubDivisionName,
    string SubItemName,
    string InspectionPart,
    string ConstructionDate,
    string CapacitySummary,
    string Status,
    string GenerateStatus,
    string? ActiveDocumentId,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool CanGenerate);

public sealed record InspectionPlanGenerateRequest(
    IReadOnlyList<string>? SelectedRowIds,
    bool Overwrite,
    IReadOnlyDictionary<string, string>? Fields);

public sealed record InspectionPlanGenerateResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    string PlanId,
    int SuccessCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<InspectionPlanGenerateRowResult> Rows,
    string Message);

public sealed record InspectionPlanGenerateRowResult(
    int RowIndex,
    string PlanRowId,
    bool Success,
    bool Skipped,
    string? DocumentId,
    string? FilePath,
    string Message);

public sealed record FieldMappingOverrideResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    string TemplateNodeId,
    IReadOnlyDictionary<string, string> Data);

public sealed record SaveFieldMappingOverrideRequest(
    string? UnitProjectId,
    string TemplateNodeId,
    IReadOnlyDictionary<string, string>? Data);
