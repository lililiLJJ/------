using System.Text.Json;

namespace GeneratorService.Projects;

public sealed class UnitProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly DirectoryInfo _rootPath;
    private readonly ProjectManager _projectManager;
    private readonly UnitProjectRepository _repository;
    private readonly object _lock = new();

    public UnitProjectService(
        DirectoryInfo rootPath,
        ProjectManager projectManager,
        UnitProjectRepository repository)
    {
        _rootPath = rootPath;
        _projectManager = projectManager;
        _repository = repository;
    }

    public UnitProjectListResult List(string? projectId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var current = EnsureCurrent(project.ProjectId);
        var items = _repository.List(project.ProjectId);
        return new UnitProjectListResult(true, project.ProjectId, current.Id, items);
    }

    public UnitProjectCurrentResult GetCurrent(string? projectId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var current = EnsureCurrent(project.ProjectId);
        return new UnitProjectCurrentResult(true, project.ProjectId, current, "当前单位工程已加载。");
    }

    public UnitProjectCurrentResult SetCurrent(UnitProjectCurrentRequest request)
    {
        var project = _projectManager.ResolveProject(request.ProjectId);
        EnsureDefault(project);
        var unit = _repository.Get(project.ProjectId, request.UnitProjectId)
                   ?? throw new InvalidOperationException("单位工程不存在。");
        if (!string.Equals(unit.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("停用的单位工程不能切换为当前单位工程。");
        }

        SavePointer(project.ProjectId, unit.Id);
        return new UnitProjectCurrentResult(true, project.ProjectId, unit, "当前单位工程已切换。");
    }

    public UnitProjectSaveResult Create(UnitProjectSaveRequest request)
    {
        var project = _projectManager.ResolveProject(request.ProjectId);
        EnsureDefault(project);
        var unit = _repository.Save(project.ProjectId, request);
        SavePointer(project.ProjectId, unit.Id);
        return new UnitProjectSaveResult(true, unit, "单位工程已创建。");
    }

    public UnitProjectSaveResult Update(string id, UnitProjectSaveRequest request)
    {
        var project = _projectManager.ResolveProject(request.ProjectId);
        EnsureDefault(project);
        var unit = _repository.Save(project.ProjectId, request with { Id = id });
        return new UnitProjectSaveResult(true, unit, "单位工程已保存。");
    }

    public UnitProjectDeleteResult Deactivate(string projectId, string id)
    {
        var project = _projectManager.ResolveProject(projectId);
        EnsureDefault(project);
        _repository.Deactivate(project.ProjectId, id);

        var current = EnsureCurrent(project.ProjectId, forceRefresh: true);
        return new UnitProjectDeleteResult(true, id, current.Id, "单位工程已停用。");
    }

    public UnitProjectInfo ResolveUnitProject(string? projectId, string? unitProjectId)
    {
        var project = _projectManager.ResolveProject(projectId);
        EnsureDefault(project);
        if (!string.IsNullOrWhiteSpace(unitProjectId))
        {
            var requested = _repository.Get(project.ProjectId, unitProjectId.Trim());
            if (requested is not null && string.Equals(requested.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                return requested;
            }
        }

        return EnsureCurrent(project.ProjectId);
    }

    public void EnsureDefault(ProjectContext project)
    {
        _repository.EnsureDefault(project.ProjectId, project.ProjectName, project.ModuleName, project.TemplateVersion);
    }

    private UnitProjectInfo EnsureCurrent(string projectId, bool forceRefresh = false)
    {
        lock (_lock)
        {
            var project = _projectManager.ResolveProject(projectId);
            var fallback = _repository.EnsureDefault(project.ProjectId, project.ProjectName, project.ModuleName, project.TemplateVersion);
            var pointer = forceRefresh ? null : LoadPointer().GetValueOrDefault(project.ProjectId);
            if (!string.IsNullOrWhiteSpace(pointer))
            {
                var existing = _repository.Get(project.ProjectId, pointer);
                if (existing is not null && string.Equals(existing.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }

            var active = _repository.List(project.ProjectId).FirstOrDefault() ?? fallback;
            SavePointer(project.ProjectId, active.Id);
            return active;
        }
    }

    private IReadOnlyDictionary<string, string> LoadPointer()
    {
        var path = GetPointerPath();
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOptions)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void SavePointer(string projectId, string unitProjectId)
    {
        var values = new Dictionary<string, string>(LoadPointer(), StringComparer.OrdinalIgnoreCase)
        {
            [projectId] = unitProjectId
        };
        var path = GetPointerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(values, JsonOptions));
    }

    private string GetPointerPath()
    {
        return Path.Combine(_rootPath.FullName, "Config", "CurrentUnitProjects.json");
    }
}
