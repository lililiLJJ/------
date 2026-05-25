namespace GeneratorService.Projects;

public sealed record UnitProjectInfo(
    string Id,
    string ProjectId,
    string UnitProjectName,
    string UnitProjectCode,
    string ConstructionUnit,
    string SupervisionUnit,
    string DesignUnit,
    string SurveyUnit,
    string BuildingArea,
    string StructureType,
    string Floors,
    DateOnly? StartDate,
    DateOnly? CompletionDate,
    string DefaultModule,
    string TemplateVersion,
    int DocumentCount,
    int MaterialCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Status);

public sealed record UnitProjectListResult(
    bool Success,
    string ProjectId,
    string CurrentUnitProjectId,
    IReadOnlyList<UnitProjectInfo> Items);

public sealed record UnitProjectSaveRequest(
    string? Id,
    string? ProjectId,
    string UnitProjectName,
    string? UnitProjectCode,
    string? ConstructionUnit,
    string? SupervisionUnit,
    string? DesignUnit,
    string? SurveyUnit,
    string? BuildingArea,
    string? StructureType,
    string? Floors,
    DateOnly? StartDate,
    DateOnly? CompletionDate,
    string? DefaultModule,
    string? TemplateVersion,
    string? CopyFromUnitProjectId);

public sealed record UnitProjectSaveResult(
    bool Success,
    UnitProjectInfo UnitProject,
    string Message);

public sealed record UnitProjectDeleteResult(
    bool Success,
    string Id,
    string CurrentUnitProjectId,
    string Message);

public sealed record UnitProjectCurrentRequest(
    string? ProjectId,
    string UnitProjectId);

public sealed record UnitProjectCurrentResult(
    bool Success,
    string ProjectId,
    UnitProjectInfo UnitProject,
    string Message);
