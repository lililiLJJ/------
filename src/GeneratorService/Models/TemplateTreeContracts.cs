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
    IReadOnlyList<TemplateTreeNodeDto> Children);

public sealed record TemplateTreeResult(
    bool Success,
    string ProjectId,
    string ProjectName,
    IReadOnlyList<TemplateTreeNodeDto> Nodes);

public sealed record CreateGeneratedFormRequest(
    string ProjectId,
    string TemplateNodeId,
    string FormName,
    IReadOnlyDictionary<string, string>? Fields);

public sealed record GeneratedFormInfo(
    bool Success,
    string Id,
    string Name,
    string TemplateCode,
    string GeneratedFilePath,
    bool CanEdit);

public sealed record GeneratedFormCreateResult(
    bool Success,
    TemplateTreeNodeDto Node,
    string GeneratedFilePath,
    string Message);

public sealed record DeleteGeneratedFormResult(
    bool Success,
    string Id,
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
