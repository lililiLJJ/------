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
    DateTimeOffset? UpdatedAt = null);

public sealed record TemplateTreeResult(
    bool Success,
    string ProjectId,
    string ProjectName,
    string UnitProjectId,
    string UnitProjectName,
    IReadOnlyList<TemplateTreeNodeDto> Nodes);

public sealed record CreateGeneratedFormRequest(
    string ProjectId,
    string? UnitProjectId,
    string TemplateNodeId,
    string FormName,
    IReadOnlyDictionary<string, string>? Fields);

public sealed record GeneratedFormInfo(
    bool Success,
    string Id,
    string Name,
    string TemplateCode,
    string GeneratedFilePath,
    bool CanEdit,
    string? TemplateNodeId = null,
    string? DocumentId = null,
    string? TemplateName = null,
    string? FormName = null);

public sealed record GeneratedFormCreateResult(
    bool Success,
    TemplateTreeNodeDto Node,
    string GeneratedFilePath,
    string Message,
    IReadOnlyList<string>? MissingFields = null,
    string? TemplateNodeId = null,
    string? AdaptationStatus = null);

public sealed record DeleteGeneratedFormResult(
    bool Success,
    string Id,
    string? DocumentId,
    string? ParentId,
    string Message,
    string? FormName = null,
    string? GeneratedFilePath = null,
    bool FileDeleted = false,
    bool ProjectDocumentDeleted = false,
    bool LegacyNodeDeleted = false);

public sealed record BatchDeleteProjectDocumentsRequest(
    IReadOnlyList<string>? DocumentIds);

public sealed record BatchDeleteProjectDocumentsFailedItem(
    string DocumentId,
    string Reason,
    string? FormName = null);

public sealed record BatchDeleteProjectDocumentsResult(
    bool Success,
    IReadOnlyList<string> DeletedIds,
    IReadOnlyList<BatchDeleteProjectDocumentsFailedItem> FailedItems,
    string Message);

public sealed record GeneratedFormBackupResult(
    bool Success,
    string BackupId,
    string SourcePath,
    string BackupPath,
    DateTimeOffset CreatedAt,
    string Message);

public sealed record ProjectDocumentInfo(
    string Id,
    string ProjectId,
    string? UnitProjectId,
    string ModuleId,
    long TemplateItemId,
    string DocumentName,
    string PartName,
    string? Capacity,
    string FilePath,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
    string DivisionName,
    string SubDivisionName,
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
    SummaryDocumentInfo? SummaryDocument,
    string FilePath,
    string Message,
    IReadOnlyList<string> Warnings);

public sealed record SummaryDocumentInfo(
    string Id,
    string ProjectId,
    string? UnitProjectId,
    string ModuleId,
    string SummaryType,
    string DivisionName,
    string SubDivisionName,
    string SubItemName,
    string DocumentName,
    string FilePath,
    int SourceDocumentCount,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
