using System.Text;
using System.Text.Json;

namespace GeneratorService.Projects;

public sealed class RecentProjectService
{
    private const int MaxRecentProjects = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly DirectoryInfo _rootPath;
    private readonly ProjectStorageService _storage;
    private readonly object _lock = new();

    public RecentProjectService(DirectoryInfo rootPath, ProjectStorageService storage)
    {
        _rootPath = rootPath;
        _storage = storage;
    }

    public RecentProjectsResult List(ProjectContext currentProject)
    {
        lock (_lock)
        {
            var items = ReadRecords()
                .Select(ToInfo)
                .OrderByDescending(item => item.LastOpenedAt)
                .ToArray();
            return new RecentProjectsResult(true, currentProject.ProjectId, items, "已读取最近工程。");
        }
    }

    public RecentProjectsResult Upsert(ProjectContext project)
    {
        lock (_lock)
        {
            var records = ReadRecords();
            var normalizedPath = NormalizePath(project.ProjectRootPath);
            records.RemoveAll(item =>
                string.Equals(item.ProjectId, project.ProjectId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizePath(item.ProjectPath), normalizedPath, StringComparison.OrdinalIgnoreCase));

            records.Add(new RecentProjectRecord(
                project.ProjectId,
                project.ProjectName,
                normalizedPath,
                DateTimeOffset.Now,
                project.CreatedAt,
                project.ModuleName,
                project.TemplateVersion));

            records = records
                .OrderByDescending(item => item.LastOpenedAt)
                .Take(MaxRecentProjects)
                .ToList();
            WriteRecords(records);
            var items = records.Select(ToInfo).ToArray();
            return new RecentProjectsResult(true, project.ProjectId, items, "最近工程已更新。");
        }
    }

    public RecentProjectsResult Remove(ProjectContext currentProject, string projectId)
    {
        lock (_lock)
        {
            var records = ReadRecords();
            records.RemoveAll(item => string.Equals(item.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
            WriteRecords(records);
            var items = records.Select(ToInfo).OrderByDescending(item => item.LastOpenedAt).ToArray();
            return new RecentProjectsResult(true, currentProject.ProjectId, items, "已移除最近工程记录。");
        }
    }

    public RecentProjectsResult Clear(ProjectContext currentProject)
    {
        lock (_lock)
        {
            WriteRecords([]);
            return new RecentProjectsResult(true, currentProject.ProjectId, [], "已清空最近工程记录。");
        }
    }

    public RecentProjectsResult Validate(ProjectContext currentProject)
    {
        return List(currentProject);
    }

    private RecentProjectInfo ToInfo(RecentProjectRecord record)
    {
        return new RecentProjectInfo(
            record.ProjectId,
            record.ProjectName,
            record.ProjectPath,
            record.LastOpenedAt,
            record.CreatedAt,
            record.DefaultModule,
            record.TemplateVersion,
            ResolveStatus(record.ProjectPath));
    }

    private string ResolveStatus(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return "路径不存在";
        }

        try
        {
            _storage.OpenProject(projectPath);
            return "正常";
        }
        catch (FileNotFoundException)
        {
            return "无效工程";
        }
        catch
        {
            return "无效工程";
        }
    }

    private List<RecentProjectRecord> ReadRecords()
    {
        var path = GetStoragePath();
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<RecentProjectRecord>>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteRecords(IReadOnlyList<RecentProjectRecord> records)
    {
        var path = GetStoragePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records, JsonOptions), new UTF8Encoding(false));
    }

    private string GetStoragePath()
    {
        return Path.Combine(_rootPath.FullName, "Config", "recent-projects.json");
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }

    private sealed record RecentProjectRecord(
        string ProjectId,
        string ProjectName,
        string ProjectPath,
        DateTimeOffset LastOpenedAt,
        DateTimeOffset CreatedAt,
        string DefaultModule,
        string TemplateVersion);
}
