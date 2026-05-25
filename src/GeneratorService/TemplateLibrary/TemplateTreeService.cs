using GeneratorService.Models;
using GeneratorService.Modules;
using GeneratorService.Projects;
using Microsoft.Data.Sqlite;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateTreeService
{
    private readonly TemplateTreeRepository _repository;
    private readonly ModuleManager _moduleManager;
    private readonly ProjectManager _projectManager;
    private readonly UnitProjectService _unitProjectService;

    public TemplateTreeService(
        TemplateTreeRepository repository,
        ModuleManager moduleManager,
        ProjectManager projectManager,
        UnitProjectService unitProjectService)
    {
        _repository = repository;
        _moduleManager = moduleManager;
        _projectManager = projectManager;
        _unitProjectService = unitProjectService;
    }

    public TemplateTreeResult GetTree(string? projectId, string? unitProjectId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, unitProjectId);

        _repository.EnsureProject(project.ProjectId, project.ProjectName);
        var moduleNodes = BuildModuleTree(project.ProjectId, unitProject.Id);
        return new TemplateTreeResult(
            true,
            project.ProjectId,
            _repository.GetProjectName(project.ProjectId),
            unitProject.Id,
            unitProject.UnitProjectName,
            moduleNodes.Count > 0 ? moduleNodes : _repository.GetTree(project.ProjectId));
    }

    private IReadOnlyList<TemplateTreeNodeDto> BuildModuleTree(string projectId, string unitProjectId)
    {
        var modules = _moduleManager.GetValidModules();
        if (modules.Count == 0)
        {
            return [];
        }

        var documents = _repository.ListProjectDocuments(projectId, unitProjectId)
            .GroupBy(document => $"{document.ModuleId}:{document.TemplateItemId}")
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.CreatedAt).ToArray());

        var result = new List<TemplateTreeNodeDto>();
        var moduleSortOrder = 10;
        foreach (var module in modules)
        {
            if (module.Manifest is null || module.RulesDbPath is null || module.TemplateRootPath is null)
            {
                continue;
            }

            var children = LoadModuleChildren(module, projectId, documents);
            result.Add(new TemplateTreeNodeDto(
                $"module:{module.Manifest.ModuleId}",
                null,
                null,
                module.Manifest.Name,
                "folder",
                "module",
                null,
                null,
                null,
                module.Manifest.Major,
                module.Manifest.ModuleId,
                module.Manifest.Name,
                module.Manifest.Province,
                module.Manifest.Major,
                module.Manifest.Year,
                null,
                moduleSortOrder,
                children));
            moduleSortOrder += 10;
        }

        return result;
    }

    private static IReadOnlyList<TemplateTreeNodeDto> LoadModuleChildren(
        ModulePackageInfo module,
        string projectId,
        IReadOnlyDictionary<string, ProjectDocumentInfo[]> documents)
    {
        using var connection = new SqliteConnection($"Data Source={module.RulesDbPath};Mode=ReadOnly");
        connection.Open();
        var categories = QueryCategories(connection, module);
        var templatesByCategory = QueryTemplates(connection, module, projectId, documents)
            .GroupBy(template => template.ParentId ?? "")
            .ToDictionary(group => group.Key, group => group.OrderBy(node => node.SortOrder).ThenBy(node => node.Name).ToArray());

        var categoriesByParent = categories
            .GroupBy(category => category.ParentId ?? $"module:{module.Manifest!.ModuleId}")
            .ToDictionary(group => group.Key, group => group.OrderBy(node => node.SortOrder).ThenBy(node => node.Name).ToArray());

        return BuildChildren($"module:{module.Manifest!.ModuleId}");

        IReadOnlyList<TemplateTreeNodeDto> BuildChildren(string parentId)
        {
            var children = new List<TemplateTreeNodeDto>();
            if (categoriesByParent.TryGetValue(parentId, out var categoryChildren))
            {
                children.AddRange(categoryChildren.Select(category =>
                    category with { Children = BuildChildren(category.Id) }));
            }

            if (templatesByCategory.TryGetValue(parentId, out var templateChildren))
            {
                children.AddRange(templateChildren);
            }

            return children
                .OrderBy(node => node.SortOrder)
                .ThenBy(node => node.Name)
                .ToArray();
        }
    }

    private static IReadOnlyList<TemplateTreeNodeDto> QueryCategories(SqliteConnection connection, ModulePackageInfo module)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ParentId, Name, Level, SortOrder, CategoryType
            FROM TemplateCategory
            ORDER BY SortOrder, Id;
            """;

        using var reader = command.ExecuteReader();
        var nodes = new List<TemplateTreeNodeDto>();
        while (reader.Read())
        {
            var categoryId = reader.GetInt64(0);
            var parentId = reader.IsDBNull(1)
                ? $"module:{module.Manifest!.ModuleId}"
                : $"module:{module.Manifest!.ModuleId}:category:{reader.GetInt64(1)}";

            nodes.Add(new TemplateTreeNodeDto(
                $"module:{module.Manifest!.ModuleId}:category:{categoryId}",
                parentId,
                null,
                reader.GetString(2),
                "folder",
                reader.IsDBNull(5) ? reader.GetInt32(3).ToString() : reader.GetString(5),
                null,
                null,
                null,
                module.Manifest.Major,
                module.Manifest.ModuleId,
                module.Manifest.Name,
                module.Manifest.Province,
                module.Manifest.Major,
                module.Manifest.Year,
                null,
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                []));
        }

        return nodes;
    }

    private static IReadOnlyList<TemplateTreeNodeDto> QueryTemplates(
        SqliteConnection connection,
        ModulePackageInfo module,
        string projectId,
        IReadOnlyDictionary<string, ProjectDocumentInfo[]> documents)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CategoryId, TemplateName, TemplateCode, TemplateFile, TemplateType, SortOrder
            FROM TemplateItem
            WHERE COALESCE(IsEnabled, 1) <> 0
            ORDER BY SortOrder, Id;
            """;

        using var reader = command.ExecuteReader();
        var nodes = new List<TemplateTreeNodeDto>();
        while (reader.Read())
        {
            var templateItemId = reader.GetInt64(0);
            var templateNodeId = $"module:{module.Manifest!.ModuleId}:template:{templateItemId}";
            var templateCode = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var templateFile = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var childDocuments = BuildDocumentNodes(module, projectId, templateNodeId, templateItemId, templateCode, documents);

            nodes.Add(new TemplateTreeNodeDto(
                templateNodeId,
                $"module:{module.Manifest.ModuleId}:category:{reader.GetInt64(1)}",
                null,
                reader.GetString(2),
                "template",
                reader.IsDBNull(5) ? "检验批" : reader.GetString(5),
                templateCode,
                templateFile,
                null,
                module.Manifest.Major,
                module.Manifest.ModuleId,
                module.Manifest.Name,
                module.Manifest.Province,
                module.Manifest.Major,
                module.Manifest.Year,
                templateItemId,
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                childDocuments));
        }

        return nodes;
    }

    private static IReadOnlyList<TemplateTreeNodeDto> BuildDocumentNodes(
        ModulePackageInfo module,
        string projectId,
        string templateNodeId,
        long templateItemId,
        string templateCode,
        IReadOnlyDictionary<string, ProjectDocumentInfo[]> documents)
    {
        if (!documents.TryGetValue($"{module.Manifest!.ModuleId}:{templateItemId}", out var items))
        {
            return [];
        }

        var sortOrder = 1000;
        return items.Select(item =>
        {
            sortOrder += 10;
            return new TemplateTreeNodeDto(
                item.Id,
                templateNodeId,
                projectId,
                item.DocumentName,
                "generated_form",
                null,
                templateCode,
                null,
                item.FilePath,
                module.Manifest.Major,
                module.Manifest.ModuleId,
                module.Manifest.Name,
                module.Manifest.Province,
                module.Manifest.Major,
                module.Manifest.Year,
                templateItemId,
                sortOrder,
                []);
        }).ToArray();
    }
}
