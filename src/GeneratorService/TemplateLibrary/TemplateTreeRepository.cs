using GeneratorService.Models;
using Microsoft.Data.Sqlite;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateTreeRepository
{
    public const string DefaultProjectId = "project-default";
    private const string DefaultProjectName = "默认工程";

    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;

    public TemplateTreeRepository(DirectoryInfo rootPath, AppConfig config)
    {
        _rootPath = rootPath;
        _config = config;
    }

    public void EnsureCreated()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS projects (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS template_tree_nodes (
              id TEXT PRIMARY KEY,
              parent_id TEXT NULL,
              project_id TEXT NULL,
              name TEXT NOT NULL,
              node_type TEXT NOT NULL CHECK (node_type IN ('folder', 'template', 'generated_form')),
              folder_level TEXT NULL,
              template_code TEXT NULL,
              template_file_path TEXT NULL,
              generated_file_path TEXT NULL,
              discipline TEXT NULL,
              sort_order INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              FOREIGN KEY(parent_id) REFERENCES template_tree_nodes(id)
            );

            CREATE INDEX IF NOT EXISTS idx_template_nodes_parent
              ON template_tree_nodes(parent_id, sort_order);
            CREATE INDEX IF NOT EXISTS idx_template_nodes_project
              ON template_tree_nodes(project_id, node_type);
            CREATE INDEX IF NOT EXISTS idx_template_nodes_template_code
              ON template_tree_nodes(template_code);
            CREATE INDEX IF NOT EXISTS idx_template_nodes_discipline
              ON template_tree_nodes(discipline);

            CREATE TABLE IF NOT EXISTS ProjectDocument (
              Id TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              ModuleId TEXT NOT NULL,
              TemplateItemId INTEGER NOT NULL,
              DocumentName TEXT NOT NULL,
              PartName TEXT NOT NULL,
              FilePath TEXT NOT NULL,
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL,
              Status TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_project_document_project
              ON ProjectDocument(ProjectId, ModuleId, TemplateItemId);
            """;
        command.ExecuteNonQuery();

        EnsureProject(DefaultProjectId, DefaultProjectName);
        if (CountCatalogNodes(connection) == 0)
        {
            SeedCatalog(connection);
        }

        RepairMissingTemplatePaths(connection);
    }

    public string GetProjectName(string projectId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", projectId);
        return command.ExecuteScalar() as string ?? DefaultProjectName;
    }

    public void EnsureProject(string projectId, string? projectName)
    {
        using var connection = OpenConnection();
        EnsureProject(connection, projectId, projectName);
    }

    public IReadOnlyList<TemplateTreeNodeDto> GetTree(string projectId)
    {
        using var connection = OpenConnection();
        var nodes = QueryNodes(connection, projectId);
        return BuildTree(nodes);
    }

    public TemplateTreeNodeDto? GetNode(string nodeId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, parent_id, project_id, name, node_type, folder_level,
                   template_code, template_file_path, generated_file_path,
                   discipline, sort_order
            FROM template_tree_nodes
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", nodeId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadNode(reader, []) : null;
    }

    public TemplateTreeNodeDto InsertGeneratedForm(
        string projectId,
        string templateNodeId,
        string name,
        string templateCode,
        string generatedFilePath)
    {
        EnsureProject(projectId, DefaultProjectName);
        using var connection = OpenConnection();
        var now = DateTimeOffset.Now.ToString("O");
        var nodeId = $"form-{Guid.NewGuid():N}";
        var sortOrder = GetNextChildSortOrder(connection, templateNodeId);
        var relativeGeneratedPath = ToStoredPath(generatedFilePath);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO template_tree_nodes (
                id, parent_id, project_id, name, node_type, folder_level,
                template_code, template_file_path, generated_file_path,
                discipline, sort_order, created_at, updated_at
            )
            VALUES (
                $id, $parentId, $projectId, $name, 'generated_form', NULL,
                $templateCode, NULL, $generatedFilePath,
                NULL, $sortOrder, $createdAt, $updatedAt
            );
            """;
        command.Parameters.AddWithValue("$id", nodeId);
        command.Parameters.AddWithValue("$parentId", templateNodeId);
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$templateCode", templateCode);
        command.Parameters.AddWithValue("$generatedFilePath", relativeGeneratedPath);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.ExecuteNonQuery();

        return GetNode(nodeId) ?? throw new InvalidOperationException("生成资料节点写入失败。");
    }

    public TemplateTreeNodeDto InsertProjectDocument(
        string projectId,
        string moduleId,
        long templateItemId,
        string templateNodeId,
        string documentName,
        string partName,
        string templateCode,
        string generatedFilePath)
    {
        EnsureProject(projectId, DefaultProjectName);
        using var connection = OpenConnection();
        var now = DateTimeOffset.Now;
        var documentId = $"document:{Guid.NewGuid():N}";
        var storedPath = ToStoredPath(generatedFilePath);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProjectDocument (
                Id, ProjectId, ModuleId, TemplateItemId, DocumentName,
                PartName, FilePath, CreatedAt, UpdatedAt, Status
            )
            VALUES (
                $id, $projectId, $moduleId, $templateItemId, $documentName,
                $partName, $filePath, $createdAt, $updatedAt, 'active'
            );
            """;
        command.Parameters.AddWithValue("$id", documentId);
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$moduleId", moduleId);
        command.Parameters.AddWithValue("$templateItemId", templateItemId);
        command.Parameters.AddWithValue("$documentName", documentName);
        command.Parameters.AddWithValue("$partName", partName);
        command.Parameters.AddWithValue("$filePath", storedPath);
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.ExecuteNonQuery();

        return new TemplateTreeNodeDto(
            documentId,
            templateNodeId,
            projectId,
            documentName,
            "generated_form",
            null,
            templateCode,
            null,
            storedPath,
            null,
            moduleId,
            null,
            null,
            null,
            null,
            templateItemId,
            GetNextProjectDocumentSortOrder(connection, projectId, moduleId, templateItemId),
            []);
    }

    public ProjectDocumentInfo? GetProjectDocument(string documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, ModuleId, TemplateItemId, DocumentName,
                   PartName, FilePath, Status, CreatedAt, UpdatedAt
            FROM ProjectDocument
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", documentId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProjectDocument(reader) : null;
    }

    public IReadOnlyList<ProjectDocumentInfo> ListProjectDocuments(string projectId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, ModuleId, TemplateItemId, DocumentName,
                   PartName, FilePath, Status, CreatedAt, UpdatedAt
            FROM ProjectDocument
            WHERE ProjectId = $projectId
            ORDER BY CreatedAt, DocumentName;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        using var reader = command.ExecuteReader();
        var documents = new List<ProjectDocumentInfo>();
        while (reader.Read())
        {
            documents.Add(ReadProjectDocument(reader));
        }

        return documents;
    }

    public bool DeleteGeneratedForm(string nodeId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM template_tree_nodes WHERE id = $id AND node_type = 'generated_form';";
        command.Parameters.AddWithValue("$id", nodeId);
        return command.ExecuteNonQuery() > 0;
    }

    public bool DeleteProjectDocument(string documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProjectDocument WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", documentId);
        return command.ExecuteNonQuery() > 0;
    }

    public string ResolveStoredPath(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(_rootPath.FullName, path));
    }

    public string GetProjectsRootPath()
    {
        var path = Path.Combine(_rootPath.FullName, "Projects");
        Directory.CreateDirectory(path);
        return path;
    }

    private SqliteConnection OpenConnection()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        return connection;
    }

    private static long CountCatalogNodes(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM template_tree_nodes WHERE node_type <> 'generated_form';";
        return (long)command.ExecuteScalar()!;
    }

    private void SeedCatalog(SqliteConnection connection)
    {
        var templatePath = ResolveSeedTemplatePath();
        var rows = new[]
        {
            new SeedNode("folder-architecture-electrical", null, "建筑电气", "folder", "discipline", null, null, "建筑电气", 10),
            new SeedNode("folder-electrical-lighting", "folder-architecture-electrical", "电气照明", "folder", "division", null, null, "建筑电气", 10),
            new SeedNode("folder-lighting-distribution", "folder-electrical-lighting", "照明配电箱安装", "folder", "sub_division", null, null, "建筑电气", 10),
            new SeedNode("folder-complete-cabinet", "folder-lighting-distribution", "成套配电柜、控制柜（台、箱）和配电箱（盘）安装", "folder", "sub_item", null, null, "建筑电气", 10),
            new SeedNode("template-gd-c3-5182", "folder-complete-cabinet", "GD-C3-5182 成套配电柜、控制柜（台、箱）和配电箱（盘）安装检验批质量验收记录", "template", null, "GD-C3-5182", templatePath, "建筑电气", 10)
        };

        foreach (var row in rows)
        {
            InsertSeedNode(connection, row);
        }
    }

    private string ResolveSeedTemplatePath()
    {
        var templateRoot = _config.GetTemplatePath(_rootPath);
        Directory.CreateDirectory(templateRoot);
        var firstTemplate = Directory.EnumerateFiles(templateRoot, "*.xlsx", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return ToStoredPath(firstTemplate ?? Path.Combine(templateRoot, "钢筋安装检验批.xlsx"));
    }

    private void RepairMissingTemplatePaths(SqliteConnection connection)
    {
        var fallbackTemplatePath = ResolveSeedTemplatePath();
        var missingIds = new List<string>();
        {
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT id, template_file_path FROM template_tree_nodes WHERE node_type = 'template';";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var storedPath = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(storedPath) || !File.Exists(ResolveStoredPath(storedPath)))
                {
                    missingIds.Add(id);
                }
            }
        }

        foreach (var id in missingIds)
        {
            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE template_tree_nodes
                SET template_file_path = $templateFilePath,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$templateFilePath", fallbackTemplatePath);
            update.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }
    }

    private static void InsertSeedNode(SqliteConnection connection, SeedNode row)
    {
        var now = DateTimeOffset.Now.ToString("O");
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO template_tree_nodes (
                id, parent_id, project_id, name, node_type, folder_level,
                template_code, template_file_path, generated_file_path,
                discipline, sort_order, created_at, updated_at
            )
            VALUES (
                $id, $parentId, NULL, $name, $nodeType, $folderLevel,
                $templateCode, $templateFilePath, NULL,
                $discipline, $sortOrder, $createdAt, $updatedAt
            );
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$parentId", (object?)row.ParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", row.Name);
        command.Parameters.AddWithValue("$nodeType", row.NodeType);
        command.Parameters.AddWithValue("$folderLevel", (object?)row.FolderLevel ?? DBNull.Value);
        command.Parameters.AddWithValue("$templateCode", (object?)row.TemplateCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$templateFilePath", (object?)row.TemplateFilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$discipline", (object?)row.Discipline ?? DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", row.SortOrder);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.ExecuteNonQuery();
    }

    private static void EnsureProject(SqliteConnection connection, string projectId, string? projectName)
    {
        var now = DateTimeOffset.Now.ToString("O");
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (id, name, created_at, updated_at)
            VALUES ($id, $name, $createdAt, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                name = COALESCE(NULLIF($name, ''), projects.name),
                updated_at = $updatedAt;
            """;
        command.Parameters.AddWithValue("$id", projectId);
        command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(projectName) ? DefaultProjectName : projectName);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<TemplateTreeNodeDto> QueryNodes(SqliteConnection connection, string projectId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, parent_id, project_id, name, node_type, folder_level,
                   template_code, template_file_path, generated_file_path,
                   discipline, sort_order
            FROM template_tree_nodes
            WHERE node_type <> 'generated_form'
               OR project_id = $projectId
            ORDER BY parent_id, sort_order, name;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        using var reader = command.ExecuteReader();
        var nodes = new List<TemplateTreeNodeDto>();
        while (reader.Read())
        {
            nodes.Add(ReadNode(reader, []));
        }

        return nodes;
    }

    private static IReadOnlyList<TemplateTreeNodeDto> BuildTree(IReadOnlyList<TemplateTreeNodeDto> nodes)
    {
        var childrenByParent = nodes
            .GroupBy(node => node.ParentId ?? "")
            .ToDictionary(group => group.Key, group => group.OrderBy(node => node.SortOrder).ThenBy(node => node.Name).ToArray());

        return BuildChildren("");

        IReadOnlyList<TemplateTreeNodeDto> BuildChildren(string parentId)
        {
            if (!childrenByParent.TryGetValue(parentId, out var children))
            {
                return [];
            }

            return children
                .Select(child => child with { Children = BuildChildren(child.Id) })
                .ToArray();
        }
    }

    private static TemplateTreeNodeDto ReadNode(SqliteDataReader reader, IReadOnlyList<TemplateTreeNodeDto> children)
    {
        return new TemplateTreeNodeDto(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            null,
            null,
            null,
            null,
            null,
            null,
            reader.GetInt32(10),
            children);
    }

    private static ProjectDocumentInfo ReadProjectDocument(SqliteDataReader reader)
    {
        return new ProjectDocumentInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            DateTimeOffset.Parse(reader.GetString(8)),
            DateTimeOffset.Parse(reader.GetString(9)));
    }

    private static int GetNextChildSortOrder(SqliteConnection connection, string parentId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(sort_order), 0) + 10 FROM template_tree_nodes WHERE parent_id = $parentId;";
        command.Parameters.AddWithValue("$parentId", parentId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int GetNextProjectDocumentSortOrder(
        SqliteConnection connection,
        string projectId,
        string moduleId,
        long templateItemId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM ProjectDocument
            WHERE ProjectId = $projectId
              AND ModuleId = $moduleId
              AND TemplateItemId = $templateItemId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$moduleId", moduleId);
        command.Parameters.AddWithValue("$templateItemId", templateItemId);
        return Convert.ToInt32(command.ExecuteScalar()) * 10;
    }

    private string ToStoredPath(string path)
    {
        var fullRoot = Path.GetFullPath(_rootPath.FullName);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(fullRoot, fullPath)
            : fullPath;
    }

    private sealed record SeedNode(
        string Id,
        string? ParentId,
        string Name,
        string NodeType,
        string? FolderLevel,
        string? TemplateCode,
        string? TemplateFilePath,
        string? Discipline,
        int SortOrder);
}
