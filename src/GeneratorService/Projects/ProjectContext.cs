using System.Text.Json.Serialization;

namespace GeneratorService.Projects;

public sealed class ProjectContext
{
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ProjectRootPath { get; set; } = "";
    public string GeneratedFormsPath { get; set; } = "";
    public string ExportPath { get; set; } = "";
    public string CachePath { get; set; } = "";
    public string LogsPath { get; set; } = "";
    public string TempPath { get; set; } = "";
    public string VersionsPath { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string TemplateVersion { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string? LastSnapshotId { get; set; }
    public string? SyncProvider { get; set; }
    public string? RemoteUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool IsDefault { get; set; }
}

public sealed record ProjectCreateRequest(
    string ProjectName,
    string ProjectRootPath,
    string? ModuleName,
    string? TemplateVersion);

public sealed record ProjectOpenRequest(string ProjectRootPath);

public sealed record ProjectUpdateRequest(
    string ProjectName,
    string ProjectRootPath,
    string? ModuleName,
    string? TemplateVersion);

public sealed record ProjectFolderSelectResult(
    bool Success,
    string? ProjectRootPath,
    string Message);

public sealed record ProjectFolderSelectRequest(
    string? Description,
    string? InitialDirectory);

public sealed record RecentProjectInfo(
    string ProjectId,
    string ProjectName,
    string ProjectPath,
    DateTimeOffset LastOpenedAt,
    DateTimeOffset CreatedAt,
    string DefaultModule,
    string TemplateVersion,
    string Status);

public sealed record RecentProjectsResult(
    bool Success,
    string CurrentProjectId,
    IReadOnlyList<RecentProjectInfo> Items,
    string Message);
