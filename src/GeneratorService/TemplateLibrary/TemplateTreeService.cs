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

        var module = ResolveModule(unitProject);
        var templateNodes = BuildModuleTree(project.ProjectId, unitProject.Id, module);
        return new TemplateTreeResult(
            true,
            project.ProjectId,
            _repository.GetProjectName(project.ProjectId),
            unitProject.Id,
            unitProject.UnitProjectName,
            module.Manifest!.ModuleId,
            module.Manifest.Name,
            module.Manifest.Version,
            templateNodes);
    }

    private ModulePackageInfo ResolveModule(UnitProjectInfo unitProject)
    {
        var validModules = _moduleManager.GetValidModules();
        var selected = !string.IsNullOrWhiteSpace(unitProject.DefaultModule)
            ? validModules.FirstOrDefault(item => string.Equals(item.Manifest?.ModuleId, unitProject.DefaultModule, StringComparison.OrdinalIgnoreCase))
            : null;
        selected ??= validModules.FirstOrDefault();
        return selected ?? throw new InvalidOperationException("当前没有可用模板模块。");
    }

    private IReadOnlyList<TemplateTreeNodeDto> BuildModuleTree(string projectId, string unitProjectId, ModulePackageInfo module)
    {
        if (module.Manifest is null || string.IsNullOrWhiteSpace(module.RulesDbPath))
        {
            return [];
        }

        var structureNodes = QueryStructureNodes(module);
        _repository.SaveSnapshot(projectId, unitProjectId, module, structureNodes.Select(ToSnapshotSaveNode).ToArray());
        var contexts = _repository.LoadTemplateNodeContexts(projectId, unitProjectId);
        var documents = _repository
            .ListGeneratedDocuments(projectId, unitProjectId, documentStatus: "Active")
            .Where(item => !string.IsNullOrWhiteSpace(item.TemplateNodeId))
            .GroupBy(item => item.TemplateNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var nodesById = structureNodes.ToDictionary(node => node.Id, node => node);
        foreach (var node in structureNodes.Where(node => string.Equals(node.NodeType, "template", StringComparison.OrdinalIgnoreCase)))
        {
            if (documents.TryGetValue(node.Id, out var nodeDocuments))
            {
                var children = nodeDocuments
                    .Select(document => ToDocumentNode(document, module, contexts))
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                nodesById[node.Id] = node with { Children = children };
            }
        }

        var childrenByParent = nodesById.Values
            .GroupBy(node => node.ParentId ?? "")
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());

        return BuildChildren($"module:{module.Manifest.ModuleId}");

        IReadOnlyList<TemplateTreeNodeDto> BuildChildren(string parentId)
        {
            if (!childrenByParent.TryGetValue(parentId, out var children))
            {
                return [];
            }

            return children
                .Select(child => child.NodeType == "template"
                    ? child
                    : child with { Children = BuildChildren(child.Id) })
                .ToArray();
        }
    }

    private IReadOnlyList<TemplateTreeNodeDto> QueryStructureNodes(ModulePackageInfo module)
    {
        using var connection = new SqliteConnection($"Data Source={module.RulesDbPath};Mode=ReadOnly");
        connection.Open();

        var categories = QueryCategories(connection, module);
        var categoriesByParent = categories
            .GroupBy(category => category.ParentId ?? $"module:{module.Manifest!.ModuleId}")
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());

        var result = new List<TemplateTreeNodeDto>();
        result.AddRange(categories);
        result.AddRange(QueryTemplates(connection, module, categoriesByParent));
        return result;
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
            var categoryType = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var fullPath = BuildCategoryPath(connection, categoryId);
            var parentId = reader.IsDBNull(1)
                ? $"module:{module.Manifest!.ModuleId}"
                : $"module:{module.Manifest!.ModuleId}:category:{reader.GetInt64(1)}";

            var folderLevel = ResolveFolderLevel(categoryType, reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
            var pathIds = BuildPathIds(folderLevel, module.Manifest!.ModuleId, categoryId, parentId);
            nodes.Add(new TemplateTreeNodeDto(
                $"module:{module.Manifest.ModuleId}:category:{categoryId}",
                parentId,
                null,
                reader.GetString(2),
                "folder",
                folderLevel,
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
                [],
                null,
                null,
                null,
                null,
                null,
                null,
                pathIds,
                fullPath,
                fullPath));
        }

        return nodes;
    }

    private static IReadOnlyList<TemplateTreeNodeDto> QueryTemplates(
        SqliteConnection connection,
        ModulePackageInfo module,
        IReadOnlyDictionary<string, TemplateTreeNodeDto[]> categoriesByParent)
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
            var categoryId = reader.GetInt64(1);
            var parentId = $"module:{module.Manifest!.ModuleId}:category:{categoryId}";
            categoriesByParent.TryGetValue(parentId, out var siblings);
            var categoryNode = siblings?.FirstOrDefault();
            var templateNodeId = $"module:{module.Manifest.ModuleId}:template:{templateItemId}";
            var fullPath = BuildCategoryPath(connection, categoryId);

            nodes.Add(new TemplateTreeNodeDto(
                templateNodeId,
                parentId,
                null,
                reader.GetString(2),
                "template",
                "inspection_batch",
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                null,
                module.Manifest.Major,
                module.Manifest.ModuleId,
                module.Manifest.Name,
                module.Manifest.Province,
                module.Manifest.Major,
                module.Manifest.Year,
                templateItemId,
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                [],
                templateNodeId,
                null,
                reader.GetString(2),
                null,
                null,
                null,
                BuildTemplatePathIds(parentId, categoryNode),
                fullPath,
                fullPath));
        }

        return nodes;
    }

    private static string ResolveFolderLevel(string categoryType, int level)
    {
        if (categoryType.Contains("子分部", StringComparison.Ordinal))
        {
            return "sub_division";
        }

        if (categoryType.Contains("分项", StringComparison.Ordinal))
        {
            return "sub_item";
        }

        if (categoryType.Contains("分部", StringComparison.Ordinal))
        {
            return "division";
        }

        return level switch
        {
            1 => "division",
            2 => "sub_division",
            _ => "sub_item"
        };
    }

    private static IReadOnlyList<string> BuildPathIds(string folderLevel, string moduleId, long categoryId, string parentId)
    {
        var currentId = $"module:{moduleId}:category:{categoryId}";
        return folderLevel switch
        {
            "division" => [currentId],
            "sub_division" => [parentId, currentId],
            _ => [parentId, currentId]
        };
    }

    private static IReadOnlyList<string> BuildTemplatePathIds(string parentId, TemplateTreeNodeDto? categoryNode)
    {
        if (categoryNode?.PathIds is { Count: > 0 } pathIds)
        {
            return pathIds;
        }

        return [parentId];
    }

    private static string BuildCategoryPath(SqliteConnection connection, long categoryId)
    {
        var items = new Dictionary<long, (long? ParentId, string Name)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, ParentId, Name FROM TemplateCategory;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items[reader.GetInt64(0)] = (reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetString(2));
            }
        }

        var segments = new Stack<string>();
        var currentId = categoryId;
        while (items.TryGetValue(currentId, out var current))
        {
            segments.Push(current.Name);
            currentId = current.ParentId ?? 0;
            if (currentId == 0)
            {
                break;
            }
        }

        return string.Join("/", segments);
    }

    private static TemplateTreeNodeDto ToDocumentNode(
        GeneratedDocumentIndexInfo document,
        ModulePackageInfo module,
        IReadOnlyDictionary<string, TemplateNodeContext> contexts)
    {
        contexts.TryGetValue(document.TemplateNodeId, out var context);
        return new TemplateTreeNodeDto(
            document.DocumentId,
            document.TemplateNodeId,
            document.ProjectId,
            document.DocumentName,
            "document",
            null,
            context?.TemplateCode,
            null,
            document.FilePath,
            module.Manifest?.Major,
            module.Manifest?.ModuleId,
            module.Manifest?.Name,
            module.Manifest?.Province,
            module.Manifest?.Major,
            module.Manifest?.Year,
            context?.TemplateItemId,
            100000,
            [],
            document.TemplateNodeId,
            document.DocumentId,
            context?.TemplateName,
            document.DocumentName,
            document.CreatedTime,
            document.UpdatedTime,
            context?.PathIds,
            context?.FullPath,
            context?.FullPath,
            document.DocumentStatus,
            document.SyncStatus);
    }

    private static TemplateSnapshotSaveNode ToSnapshotSaveNode(TemplateTreeNodeDto node)
    {
        return new TemplateSnapshotSaveNode(
            node.Id,
            node.ParentId,
            node.Name,
            node.NodeType == "template" ? "Template" : (node.FolderLevel ?? "SubItem"),
            node.FolderLevel,
            node.NodeType == "template" ? node.Id : node.TemplateNodeId,
            node.TemplateItemId,
            node.TemplateCode,
            node.SortOrder,
            node.PathIds ?? [],
            node.FullPath ?? "",
            FirstOrEmpty(node.PathIds, 0),
            ResolveSegment(node.FullPath, 0),
            FirstOrEmpty(node.PathIds, 1),
            ResolveSegment(node.FullPath, 1),
            FirstOrEmpty(node.PathIds, 2),
            ResolveLastSegment(node.FullPath));
    }

    private static string FirstOrEmpty(IReadOnlyList<string>? items, int index)
    {
        return items is { Count: > 0 } && index < items.Count ? items[index] : "";
    }

    private static string ResolveSegment(string? fullPath, int index)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return "";
        }

        var parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return index < parts.Length ? parts[index] : "";
    }

    private static string ResolveLastSegment(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return "";
        }

        var parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? "" : parts[^1];
    }
}
