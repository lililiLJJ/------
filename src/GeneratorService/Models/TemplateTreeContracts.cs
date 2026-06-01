namespace GeneratorService.Models;

public sealed record TemplateTreeNodeDto(
    string Id,
    string? ParentId,
    string? ProjectId,
    string Name,
    string NodeType,
    string? FolderLevel,
    string? TemplateCode,
    string? TemplateFilePath,
    string? GeneratedFilePath,
    string? Discipline,
    string? ModuleId,
    string? ModuleName,
    string? Province,
    string? Major,
    string? Year,
    long? TemplateItemId,
    int SortOrder,
    IReadOnlyList<TemplateTreeNodeDto> Children,
    string? TemplateNodeId = null,
    string? DocumentId = null,
    string? TemplateName = null,
    string? FormName = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null,
    IReadOnlyList<string>? PathIds = null,
    string? FullPath = null,
    string? Breadcrumb = null,
    string? Status = null,
    string? SyncStatus = null);

public sealed record TemplateTreeResult(
    bool Success,
    string ProjectId,
    string ProjectName,
    string UnitProjectId,
    string UnitProjectName,
    string ModuleId,
    string ModuleName,
    string ModuleVersion,
    IReadOnlyList<TemplateTreeNodeDto> Nodes);

public sealed record CreateGeneratedDocumentRequest(
    string? UnitProjectId,
    string TemplateNodeId,
    string DocumentName,
    string? DocumentType,
    string? SourceType,
    string? SourceId,
    string? PlanId,
    string? PlanRowId,
    IReadOnlyDictionary<string, string>? Fields);

public sealed record GeneratedDocumentInfo(
    bool Success,
    string DocumentId,
    string ProjectId,
    string UnitProjectId,
    string DocumentName,
    string DocumentType,
    string TemplateNodeId,
    string TemplateCode,
    string GeneratedFilePath,
    bool CanEdit,
    string DocumentStatus,
    string SyncStatus,
    string? SyncErrorMessage,
    string? TemplateName = null,
    string? FormName = null,
    string? SourceType = null,
    string? SourceId = null,
    string? PlanId = null,
    string? PlanRowId = null,
    string? CapacitySummary = null,
    string? InspectionPart = null,
    string? ConstructionDate = null,
    DateTimeOffset? LastSyncTime = null);

public sealed record GeneratedDocumentCreateResult(
    bool Success,
    TemplateTreeNodeDto Node,
    string GeneratedFilePath,
    string Message,
    string? DocumentId = null);

public sealed record DeleteGeneratedDocumentResult(
    bool Success,
    string DocumentId,
    string? ParentId,
    string Message,
    string? DocumentName = null,
    string? GeneratedFilePath = null,
    bool FileMoved = false,
    string? RestoreToken = null);

public sealed record BatchDeleteGeneratedDocumentsRequest(
    IReadOnlyList<string>? DocumentIds);

public sealed record BatchDeleteGeneratedDocumentsFailedItem(
    string DocumentId,
    string Reason,
    string? DocumentName = null);

public sealed record BatchDeleteGeneratedDocumentsResult(
    bool Success,
    IReadOnlyList<string> DeletedIds,
    IReadOnlyList<BatchDeleteGeneratedDocumentsFailedItem> FailedItems,
    string Message);

public sealed record GeneratedDocumentBackupResult(
    bool Success,
    string BackupId,
    string SourcePath,
    string BackupPath,
    DateTimeOffset CreatedAt,
    string Message);

public sealed record GeneratedDocumentIndexInfo(
    string DocumentId,
    string ProjectId,
    string UnitProjectId,
    string DocumentType,
    string DocumentName,
    string SourceType,
    string SourceId,
    string TemplateNodeId,
    string FilePath,
    string DocumentStatus,
    string SyncStatus,
    string SyncErrorMessage,
    DateTimeOffset? LastSyncTime,
    DateTimeOffset CreatedTime,
    DateTimeOffset UpdatedTime,
    DateTimeOffset? DeleteTime,
    string? RestoreToken,
    string? ReplacedByDocumentId);

public sealed record InspectionBatchDocumentDetailInfo(
    string DocumentId,
    string? PlanId,
    string? PlanRowId,
    string InspectionPart,
    string ConstructionDate,
    string CapacitySummary,
    string TemplateNodeId);

public sealed record SummaryTreeResult(
    bool Success,
    string ProjectId,
    string ProjectName,
    string UnitProjectId,
    string UnitProjectName,
    IReadOnlyList<SummaryTreeNodeDto> Nodes,
    IReadOnlyList<string> Warnings);

public sealed record SummaryTreeNodeDto(
    string Id,
    string ProjectId,
    string ModuleId,
    string CategoryId,
    string SummaryType,
    string Name,
    string DivisionId,
    string DivisionName,
    string SubDivisionId,
    string SubDivisionName,
    string SubItemId,
    string SubItemName,
    int SourceDocumentCount,
    int InspectionBatchCount,
    int SubItemCount,
    int SubDivisionCount,
    IReadOnlyList<SummaryTreeNodeDto> Children);

public sealed record SummaryPreviewResult(
    bool Success,
    string ProjectId,
    string UnitProjectId,
    string SummaryType,
    string CategoryId,
    string Title,
    string DivisionName,
    string SubDivisionName,
    string SubItemName,
    IReadOnlyList<SummaryPreviewRow> Rows,
    SummaryPreviewTotals Totals,
    IReadOnlyList<string> Warnings);

public sealed record SummaryPreviewRow(
    int Sequence,
    string Name,
    string Capacity,
    string PartName,
    int Count,
    string ConstructorResult,
    string SupervisorConclusion);

public sealed record SummaryPreviewTotals(
    int SourceDocumentCount,
    int InspectionBatchCount,
    int SubItemCount,
    int SubDivisionCount);

public sealed record GenerateSummaryRequest(
    string ProjectId,
    string? UnitProjectId,
    string Type,
    string CategoryId);

public sealed record GenerateSummaryResult(
    bool Success,
    GeneratedDocumentIndexInfo? SummaryDocument,
    string FilePath,
    string Message,
    IReadOnlyList<string> Warnings);
