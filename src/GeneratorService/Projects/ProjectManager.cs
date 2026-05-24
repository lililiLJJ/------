using System.Text.Json;

namespace GeneratorService.Projects;

public sealed class ProjectManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly DirectoryInfo _rootPath;
    private readonly ProjectStorageService _storage;
    private readonly object _lock = new();
    private ProjectContext? _currentProject;

    public ProjectManager(DirectoryInfo rootPath, ProjectStorageService storage)
    {
        _rootPath = rootPath;
        _storage = storage;
    }

    public ProjectContext GetCurrentProject()
    {
        lock (_lock)
        {
            if (_currentProject is not null)
            {
                return _currentProject;
            }

            _currentProject = LoadCurrentProject() ?? BuildDefaultProject();
            return _currentProject;
        }
    }

    public ProjectContext CreateProject(ProjectCreateRequest request)
    {
        var context = _storage.CreateProject(request);
        SetCurrentProject(context);
        return context;
    }

    public ProjectContext OpenProject(ProjectOpenRequest request)
    {
        var context = _storage.OpenProject(request.ProjectRootPath);
        SetCurrentProject(context);
        return context;
    }

    public bool IsCurrentProject(string projectId)
    {
        return string.Equals(GetCurrentProject().ProjectId, projectId, StringComparison.OrdinalIgnoreCase);
    }

    public ProjectContext ResolveProject(string? projectId)
    {
        var current = GetCurrentProject();
        if (string.IsNullOrWhiteSpace(projectId) ||
            string.Equals(projectId, current.ProjectId, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (string.Equals(projectId, TemplateLibrary.TemplateTreeRepository.DefaultProjectId, StringComparison.OrdinalIgnoreCase))
        {
            return BuildDefaultProject();
        }

        return current;
    }

    private void SetCurrentProject(ProjectContext context)
    {
        lock (_lock)
        {
            _currentProject = context;
            SaveCurrentProjectPointer(context);
        }
    }

    private ProjectContext? LoadCurrentProject()
    {
        var pointerPath = GetPointerPath();
        if (!File.Exists(pointerPath))
        {
            return null;
        }

        try
        {
            var pointer = JsonSerializer.Deserialize<CurrentProjectPointer>(File.ReadAllText(pointerPath), JsonOptions);
            if (string.IsNullOrWhiteSpace(pointer?.ProjectRootPath))
            {
                return null;
            }

            return _storage.OpenProject(pointer.ProjectRootPath);
        }
        catch
        {
            return null;
        }
    }

    private void SaveCurrentProjectPointer(ProjectContext context)
    {
        var pointerPath = GetPointerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(pointerPath)!);
        var pointer = new CurrentProjectPointer(context.ProjectRootPath, DateTimeOffset.Now);
        File.WriteAllText(pointerPath, JsonSerializer.Serialize(pointer, JsonOptions));
    }

    private string GetPointerPath()
    {
        return Path.Combine(_rootPath.FullName, "Config", "CurrentProject.json");
    }

    private ProjectContext BuildDefaultProject()
    {
        var root = Path.Combine(_rootPath.FullName, "Projects", TemplateLibrary.TemplateTreeRepository.DefaultProjectId);
        var context = new ProjectContext
        {
            ProjectId = TemplateLibrary.TemplateTreeRepository.DefaultProjectId,
            ProjectName = "默认工程",
            ProjectRootPath = root,
            ModuleName = "",
            TemplateVersion = "",
            IsDefault = true
        };
        ProjectStorageService.PopulatePaths(context);
        ProjectStorageService.EnsureProjectDirectories(context);
        return context;
    }

    private sealed record CurrentProjectPointer(string ProjectRootPath, DateTimeOffset UpdatedAt);
}
