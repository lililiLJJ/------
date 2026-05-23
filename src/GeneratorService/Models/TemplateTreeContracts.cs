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
