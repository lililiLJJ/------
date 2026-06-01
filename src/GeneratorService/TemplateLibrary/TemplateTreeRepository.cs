using System.Text.Json;
using GeneratorService.Models;
using GeneratorService.Modules;
using Microsoft.Data.Sqlite;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateTreeRepository
{
    public const string DefaultProjectId = "project-default";
    private const string DefaultProjectName = "默认工程";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

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

            CREATE TABLE IF NOT EXISTS ProjectModuleReference (
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NOT NULL,
              ModuleId TEXT NOT NULL,
              ModuleName TEXT NOT NULL,
              ModuleVersion TEXT NOT NULL,
              CreatedTime TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL,
              PRIMARY KEY (ProjectId, UnitProjectId)
            );

            CREATE TABLE IF NOT EXISTS TemplateTreeSnapshot (
              SnapshotId TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NOT NULL,
              ModuleId TEXT NOT NULL,
              ModuleName TEXT NOT NULL,
              ModuleVersion TEXT NOT NULL,
              CreatedTime TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TemplateTreeSnapshotNode (
              NodeId TEXT NOT NULL,
              SnapshotId TEXT NOT NULL,
              ParentId TEXT NULL,
              NodeName TEXT NOT NULL,
              NodeType TEXT NOT NULL,
              FolderLevel TEXT NULL,
              TemplateNodeId TEXT NULL,
              TemplateItemId INTEGER NULL,
              TemplateCode TEXT NULL,
              SortOrder INTEGER NOT NULL DEFAULT 0,
              PathIdsJson TEXT NOT NULL DEFAULT '[]',
              FullPath TEXT NOT NULL DEFAULT '',
              DivisionId TEXT NOT NULL DEFAULT '',
              DivisionName TEXT NOT NULL DEFAULT '',
              SubDivisionId TEXT NOT NULL DEFAULT '',
              SubDivisionName TEXT NOT NULL DEFAULT '',
              SubItemId TEXT NOT NULL DEFAULT '',
              SubItemName TEXT NOT NULL DEFAULT '',
              PRIMARY KEY (SnapshotId, NodeId)
            );

            CREATE TABLE IF NOT EXISTS TemplateNodeState (
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NOT NULL,
              NodeId TEXT NOT NULL,
              Status TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL,
              PRIMARY KEY (ProjectId, UnitProjectId, NodeId)
            );

            CREATE TABLE IF NOT EXISTS GeneratedDocumentIndex (
              DocumentId TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NOT NULL,
              DocumentType TEXT NOT NULL,
              DocumentName TEXT NOT NULL,
              SourceType TEXT NOT NULL,
              SourceId TEXT NOT NULL,
              TemplateNodeId TEXT NOT NULL,
              FilePath TEXT NOT NULL,
              DocumentStatus TEXT NOT NULL,
              SyncStatus TEXT NOT NULL,
              SyncErrorMessage TEXT NOT NULL DEFAULT '',
              LastSyncTime TEXT NULL,
              CreatedTime TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL,
              DeleteTime TEXT NULL,
              RestoreToken TEXT NULL,
              ReplacedByDocumentId TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS InspectionBatchDocumentDetail (
              DocumentId TEXT PRIMARY KEY,
              PlanId TEXT NULL,
              PlanRowId TEXT NULL,
              InspectionPart TEXT NOT NULL DEFAULT '',
              ConstructionDate TEXT NOT NULL DEFAULT '',
              CapacitySummary TEXT NOT NULL DEFAULT '',
              TemplateNodeId TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SummaryDocumentDetail (
              DocumentId TEXT PRIMARY KEY,
              SummaryType TEXT NOT NULL,
              CategoryNodeId TEXT NOT NULL,
              ParentDocumentIdsJson TEXT NOT NULL DEFAULT '[]',
              TotalCount INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_snapshot_project
              ON TemplateTreeSnapshot(ProjectId, UnitProjectId);
            CREATE INDEX IF NOT EXISTS idx_snapshot_node_template
              ON TemplateTreeSnapshotNode(SnapshotId, TemplateNodeId, SortOrder);
            CREATE INDEX IF NOT EXISTS idx_doc_project
              ON GeneratedDocumentIndex(ProjectId, UnitProjectId, DocumentType, DocumentStatus, UpdatedTime);
            CREATE INDEX IF NOT EXISTS idx_doc_template
              ON GeneratedDocumentIndex(ProjectId, UnitProjectId, TemplateNodeId, DocumentStatus, UpdatedTime);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_doc_active_plan_row
              ON GeneratedDocumentIndex(SourceId, DocumentStatus)
              WHERE SourceType = 'InspectionBatchPlanRow' AND DocumentStatus = 'Active';
            CREATE INDEX IF NOT EXISTS idx_doc_source
              ON GeneratedDocumentIndex(SourceType, SourceId, DocumentStatus);
            """;
        command.ExecuteNonQuery();

        EnsureProject(DefaultProjectId, DefaultProjectName);
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
        var now = DateTimeOffset.Now.ToString("O");
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (id, name, created_at, updated_at)
            VALUES ($id, $name, $createdAt, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
              name = excluded.name,
              updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", projectId);
        command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(projectName) ? DefaultProjectName : projectName.Trim());
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.ExecuteNonQuery();
    }

    public TemplateSnapshotMetadata? GetSnapshot(string projectId, string unitProjectId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SnapshotId, ProjectId, UnitProjectId, ModuleId, ModuleName, ModuleVersion, CreatedTime, UpdatedTime
            FROM TemplateTreeSnapshot
            WHERE ProjectId = $projectId AND UnitProjectId = $unitProjectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSnapshot(reader) : null;
    }

    public void SaveSnapshot(
        string projectId,
        string unitProjectId,
        ModulePackageInfo module,
        IReadOnlyList<TemplateSnapshotSaveNode> nodes)
    {
        if (module.Manifest is null)
        {
            throw new InvalidOperationException("模块清单不存在。");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now;
        var existing = GetSnapshot(projectId, unitProjectId);
        var snapshotId = existing?.SnapshotId ?? $"snapshot:{Guid.NewGuid():N}";

        using (var upsertSnapshot = connection.CreateCommand())
        {
            upsertSnapshot.Transaction = transaction;
            upsertSnapshot.CommandText = """
                INSERT INTO TemplateTreeSnapshot (
                    SnapshotId, ProjectId, UnitProjectId, ModuleId, ModuleName, ModuleVersion, CreatedTime, UpdatedTime
                )
                VALUES (
                    $snapshotId, $projectId, $unitProjectId, $moduleId, $moduleName, $moduleVersion, $createdTime, $updatedTime
                )
                ON CONFLICT(SnapshotId) DO UPDATE SET
                    ModuleId = excluded.ModuleId,
                    ModuleName = excluded.ModuleName,
                    ModuleVersion = excluded.ModuleVersion,
                    UpdatedTime = excluded.UpdatedTime;
                """;
            upsertSnapshot.Parameters.AddWithValue("$snapshotId", snapshotId);
            upsertSnapshot.Parameters.AddWithValue("$projectId", projectId);
            upsertSnapshot.Parameters.AddWithValue("$unitProjectId", unitProjectId);
            upsertSnapshot.Parameters.AddWithValue("$moduleId", module.Manifest.ModuleId);
            upsertSnapshot.Parameters.AddWithValue("$moduleName", module.Manifest.Name);
            upsertSnapshot.Parameters.AddWithValue("$moduleVersion", module.Manifest.Version);
            upsertSnapshot.Parameters.AddWithValue("$createdTime", (existing?.CreatedTime ?? now).ToString("O"));
            upsertSnapshot.Parameters.AddWithValue("$updatedTime", now.ToString("O"));
            upsertSnapshot.ExecuteNonQuery();
        }

        using (var upsertReference = connection.CreateCommand())
        {
            upsertReference.Transaction = transaction;
            upsertReference.CommandText = """
                INSERT INTO ProjectModuleReference (
                    ProjectId, UnitProjectId, ModuleId, ModuleName, ModuleVersion, CreatedTime, UpdatedTime
                )
                VALUES (
                    $projectId, $unitProjectId, $moduleId, $moduleName, $moduleVersion, $createdTime, $updatedTime
                )
                ON CONFLICT(ProjectId, UnitProjectId) DO UPDATE SET
                    ModuleId = excluded.ModuleId,
                    ModuleName = excluded.ModuleName,
                    ModuleVersion = excluded.ModuleVersion,
                    UpdatedTime = excluded.UpdatedTime;
                """;
            upsertReference.Parameters.AddWithValue("$projectId", projectId);
            upsertReference.Parameters.AddWithValue("$unitProjectId", unitProjectId);
            upsertReference.Parameters.AddWithValue("$moduleId", module.Manifest.ModuleId);
            upsertReference.Parameters.AddWithValue("$moduleName", module.Manifest.Name);
            upsertReference.Parameters.AddWithValue("$moduleVersion", module.Manifest.Version);
            upsertReference.Parameters.AddWithValue("$createdTime", (existing?.CreatedTime ?? now).ToString("O"));
            upsertReference.Parameters.AddWithValue("$updatedTime", now.ToString("O"));
            upsertReference.ExecuteNonQuery();
        }

        using (var deleteNodes = connection.CreateCommand())
        {
            deleteNodes.Transaction = transaction;
            deleteNodes.CommandText = "DELETE FROM TemplateTreeSnapshotNode WHERE SnapshotId = $snapshotId;";
            deleteNodes.Parameters.AddWithValue("$snapshotId", snapshotId);
            deleteNodes.ExecuteNonQuery();
        }

        foreach (var node in nodes)
        {
            using var insertNode = connection.CreateCommand();
            insertNode.Transaction = transaction;
            insertNode.CommandText = """
                INSERT INTO TemplateTreeSnapshotNode (
                    NodeId, SnapshotId, ParentId, NodeName, NodeType, FolderLevel, TemplateNodeId, TemplateItemId,
                    TemplateCode, SortOrder, PathIdsJson, FullPath, DivisionId, DivisionName, SubDivisionId,
                    SubDivisionName, SubItemId, SubItemName
                )
                VALUES (
                    $nodeId, $snapshotId, $parentId, $nodeName, $nodeType, $folderLevel, $templateNodeId, $templateItemId,
                    $templateCode, $sortOrder, $pathIdsJson, $fullPath, $divisionId, $divisionName, $subDivisionId,
                    $subDivisionName, $subItemId, $subItemName
                );
                """;
            insertNode.Parameters.AddWithValue("$nodeId", node.NodeId);
            insertNode.Parameters.AddWithValue("$snapshotId", snapshotId);
            insertNode.Parameters.AddWithValue("$parentId", (object?)node.ParentId ?? DBNull.Value);
            insertNode.Parameters.AddWithValue("$nodeName", node.NodeName);
            insertNode.Parameters.AddWithValue("$nodeType", node.NodeType);
            insertNode.Parameters.AddWithValue("$folderLevel", (object?)node.FolderLevel ?? DBNull.Value);
            insertNode.Parameters.AddWithValue("$templateNodeId", (object?)node.TemplateNodeId ?? DBNull.Value);
            insertNode.Parameters.AddWithValue("$templateItemId", node.TemplateItemId is null ? DBNull.Value : node.TemplateItemId.Value);
            insertNode.Parameters.AddWithValue("$templateCode", (object?)node.TemplateCode ?? DBNull.Value);
            insertNode.Parameters.AddWithValue("$sortOrder", node.SortOrder);
            insertNode.Parameters.AddWithValue("$pathIdsJson", JsonSerializer.Serialize(node.PathIds, JsonOptions));
            insertNode.Parameters.AddWithValue("$fullPath", node.FullPath ?? "");
            insertNode.Parameters.AddWithValue("$divisionId", node.DivisionId ?? "");
            insertNode.Parameters.AddWithValue("$divisionName", node.DivisionName ?? "");
            insertNode.Parameters.AddWithValue("$subDivisionId", node.SubDivisionId ?? "");
            insertNode.Parameters.AddWithValue("$subDivisionName", node.SubDivisionName ?? "");
            insertNode.Parameters.AddWithValue("$subItemId", node.SubItemId ?? "");
            insertNode.Parameters.AddWithValue("$subItemName", node.SubItemName ?? "");
            insertNode.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyDictionary<string, TemplateNodeContext> LoadTemplateNodeContexts(string projectId, string unitProjectId)
    {
        var snapshot = GetSnapshot(projectId, unitProjectId);
        if (snapshot is null)
        {
            return new Dictionary<string, TemplateNodeContext>(StringComparer.OrdinalIgnoreCase);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT NodeId, TemplateNodeId, TemplateItemId, TemplateCode, PathIdsJson, FullPath,
                   DivisionId, DivisionName, SubDivisionId, SubDivisionName, SubItemId, SubItemName, NodeName
            FROM TemplateTreeSnapshotNode
            WHERE SnapshotId = $snapshotId AND NodeType = 'Template';
            """;
        command.Parameters.AddWithValue("$snapshotId", snapshot.SnapshotId);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, TemplateNodeContext>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var templateNodeId = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1);
            result[templateNodeId] = new TemplateNodeContext(
                reader.GetString(0),
                templateNodeId,
                reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                DeserializeStringList(reader.IsDBNull(4) ? "[]" : reader.GetString(4)),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6),
                reader.IsDBNull(7) ? "" : reader.GetString(7),
                reader.IsDBNull(8) ? "" : reader.GetString(8),
                reader.IsDBNull(9) ? "" : reader.GetString(9),
                reader.IsDBNull(10) ? "" : reader.GetString(10),
                reader.IsDBNull(11) ? "" : reader.GetString(11),
                reader.IsDBNull(12) ? "" : reader.GetString(12));
        }

        return result;
    }

    public GeneratedDocumentIndexInfo InsertGeneratedDocument(GeneratedDocumentIndexInfo document)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GeneratedDocumentIndex (
                DocumentId, ProjectId, UnitProjectId, DocumentType, DocumentName, SourceType, SourceId, TemplateNodeId,
                FilePath, DocumentStatus, SyncStatus, SyncErrorMessage, LastSyncTime, CreatedTime, UpdatedTime,
                DeleteTime, RestoreToken, ReplacedByDocumentId
            )
            VALUES (
                $documentId, $projectId, $unitProjectId, $documentType, $documentName, $sourceType, $sourceId, $templateNodeId,
                $filePath, $documentStatus, $syncStatus, $syncErrorMessage, $lastSyncTime, $createdTime, $updatedTime,
                $deleteTime, $restoreToken, $replacedByDocumentId
            );
            """;
        BindDocument(command, document);
        command.ExecuteNonQuery();
        return document;
    }

    public void UpsertInspectionBatchDetail(InspectionBatchDocumentDetailInfo detail)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO InspectionBatchDocumentDetail (
                DocumentId, PlanId, PlanRowId, InspectionPart, ConstructionDate, CapacitySummary, TemplateNodeId
            )
            VALUES (
                $documentId, $planId, $planRowId, $inspectionPart, $constructionDate, $capacitySummary, $templateNodeId
            )
            ON CONFLICT(DocumentId) DO UPDATE SET
                PlanId = excluded.PlanId,
                PlanRowId = excluded.PlanRowId,
                InspectionPart = excluded.InspectionPart,
                ConstructionDate = excluded.ConstructionDate,
                CapacitySummary = excluded.CapacitySummary,
                TemplateNodeId = excluded.TemplateNodeId;
            """;
        command.Parameters.AddWithValue("$documentId", detail.DocumentId);
        command.Parameters.AddWithValue("$planId", (object?)detail.PlanId ?? DBNull.Value);
        command.Parameters.AddWithValue("$planRowId", (object?)detail.PlanRowId ?? DBNull.Value);
        command.Parameters.AddWithValue("$inspectionPart", detail.InspectionPart);
        command.Parameters.AddWithValue("$constructionDate", detail.ConstructionDate);
        command.Parameters.AddWithValue("$capacitySummary", detail.CapacitySummary);
        command.Parameters.AddWithValue("$templateNodeId", detail.TemplateNodeId);
        command.ExecuteNonQuery();
    }

    public void UpsertSummaryDetail(string documentId, string summaryType, string categoryNodeId, IReadOnlyList<string> parentDocumentIds, int totalCount)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SummaryDocumentDetail (
                DocumentId, SummaryType, CategoryNodeId, ParentDocumentIdsJson, TotalCount
            )
            VALUES (
                $documentId, $summaryType, $categoryNodeId, $parentDocumentIdsJson, $totalCount
            )
            ON CONFLICT(DocumentId) DO UPDATE SET
                SummaryType = excluded.SummaryType,
                CategoryNodeId = excluded.CategoryNodeId,
                ParentDocumentIdsJson = excluded.ParentDocumentIdsJson,
                TotalCount = excluded.TotalCount;
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        command.Parameters.AddWithValue("$summaryType", summaryType);
        command.Parameters.AddWithValue("$categoryNodeId", categoryNodeId);
        command.Parameters.AddWithValue("$parentDocumentIdsJson", JsonSerializer.Serialize(parentDocumentIds, JsonOptions));
        command.Parameters.AddWithValue("$totalCount", totalCount);
        command.ExecuteNonQuery();
    }

    public GeneratedDocumentIndexInfo? GetGeneratedDocument(string documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DocumentId, ProjectId, UnitProjectId, DocumentType, DocumentName, SourceType, SourceId,
                   TemplateNodeId, FilePath, DocumentStatus, SyncStatus, SyncErrorMessage, LastSyncTime,
                   CreatedTime, UpdatedTime, DeleteTime, RestoreToken, ReplacedByDocumentId
            FROM GeneratedDocumentIndex
            WHERE DocumentId = $documentId;
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDocument(reader) : null;
    }

    public InspectionBatchDocumentDetailInfo? GetInspectionBatchDetail(string documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DocumentId, PlanId, PlanRowId, InspectionPart, ConstructionDate, CapacitySummary, TemplateNodeId
            FROM InspectionBatchDocumentDetail
            WHERE DocumentId = $documentId;
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new InspectionBatchDocumentDetailInfo(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6))
            : null;
    }

    public SummaryDocumentDetailInfo? GetSummaryDetail(string documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DocumentId, SummaryType, CategoryNodeId, ParentDocumentIdsJson, TotalCount
            FROM SummaryDocumentDetail
            WHERE DocumentId = $documentId;
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new SummaryDocumentDetailInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DeserializeStringList(reader.IsDBNull(3) ? "[]" : reader.GetString(3)),
                reader.GetInt32(4))
            : null;
    }

    public IReadOnlyList<GeneratedDocumentIndexInfo> ListGeneratedDocuments(
        string projectId,
        string unitProjectId,
        string? documentType = null,
        string? documentStatus = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DocumentId, ProjectId, UnitProjectId, DocumentType, DocumentName, SourceType, SourceId,
                   TemplateNodeId, FilePath, DocumentStatus, SyncStatus, SyncErrorMessage, LastSyncTime,
                   CreatedTime, UpdatedTime, DeleteTime, RestoreToken, ReplacedByDocumentId
            FROM GeneratedDocumentIndex
            WHERE ProjectId = $projectId
              AND UnitProjectId = $unitProjectId
              AND ($documentType = '' OR DocumentType = $documentType)
              AND ($documentStatus = '' OR DocumentStatus = $documentStatus)
            ORDER BY UpdatedTime DESC, CreatedTime DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        command.Parameters.AddWithValue("$documentType", documentType ?? "");
        command.Parameters.AddWithValue("$documentStatus", documentStatus ?? "");
        using var reader = command.ExecuteReader();
        var results = new List<GeneratedDocumentIndexInfo>();
        while (reader.Read())
        {
            results.Add(ReadDocument(reader));
        }

        return results;
    }

    public GeneratedDocumentIndexInfo? GetActiveDocumentByPlanRowId(string planRowId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DocumentId, ProjectId, UnitProjectId, DocumentType, DocumentName, SourceType, SourceId,
                   TemplateNodeId, FilePath, DocumentStatus, SyncStatus, SyncErrorMessage, LastSyncTime,
                   CreatedTime, UpdatedTime, DeleteTime, RestoreToken, ReplacedByDocumentId
            FROM GeneratedDocumentIndex
            WHERE SourceType = 'InspectionBatchPlanRow'
              AND SourceId = $sourceId
              AND DocumentStatus = 'Active'
            ORDER BY UpdatedTime DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sourceId", planRowId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDocument(reader) : null;
    }

    public IReadOnlyDictionary<string, GeneratedDocumentIndexInfo> ListActiveDocumentsByTemplateNode(string projectId, string unitProjectId)
    {
        return ListGeneratedDocuments(projectId, unitProjectId, documentStatus: "Active")
            .Where(item => !string.IsNullOrWhiteSpace(item.TemplateNodeId))
            .GroupBy(item => item.DocumentId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(item => item.DocumentId, item => item, StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateDocument(GeneratedDocumentIndexInfo document)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE GeneratedDocumentIndex
            SET DocumentType = $documentType,
                DocumentName = $documentName,
                SourceType = $sourceType,
                SourceId = $sourceId,
                TemplateNodeId = $templateNodeId,
                FilePath = $filePath,
                DocumentStatus = $documentStatus,
                SyncStatus = $syncStatus,
                SyncErrorMessage = $syncErrorMessage,
                LastSyncTime = $lastSyncTime,
                UpdatedTime = $updatedTime,
                DeleteTime = $deleteTime,
                RestoreToken = $restoreToken,
                ReplacedByDocumentId = $replacedByDocumentId
            WHERE DocumentId = $documentId;
            """;
        BindDocument(command, document);
        command.ExecuteNonQuery();
    }

    public void DeleteSummaryDetail(string documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SummaryDocumentDetail WHERE DocumentId = $documentId;";
        command.Parameters.AddWithValue("$documentId", documentId);
        command.ExecuteNonQuery();
    }

    public void DeleteInspectionBatchDetail(string documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM InspectionBatchDocumentDetail WHERE DocumentId = $documentId;";
        command.Parameters.AddWithValue("$documentId", documentId);
        command.ExecuteNonQuery();
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

    public string ToStoredPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetFullPath(_rootPath.FullName);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, fullPath)
            : fullPath;
    }

    private SqliteConnection OpenConnection()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        return connection;
    }

    private static void BindDocument(SqliteCommand command, GeneratedDocumentIndexInfo document)
    {
        command.Parameters.AddWithValue("$documentId", document.DocumentId);
        command.Parameters.AddWithValue("$projectId", document.ProjectId);
        command.Parameters.AddWithValue("$unitProjectId", document.UnitProjectId);
        command.Parameters.AddWithValue("$documentType", document.DocumentType);
        command.Parameters.AddWithValue("$documentName", document.DocumentName);
        command.Parameters.AddWithValue("$sourceType", document.SourceType);
        command.Parameters.AddWithValue("$sourceId", document.SourceId);
        command.Parameters.AddWithValue("$templateNodeId", document.TemplateNodeId);
        command.Parameters.AddWithValue("$filePath", document.FilePath);
        command.Parameters.AddWithValue("$documentStatus", document.DocumentStatus);
        command.Parameters.AddWithValue("$syncStatus", document.SyncStatus);
        command.Parameters.AddWithValue("$syncErrorMessage", document.SyncErrorMessage ?? "");
        command.Parameters.AddWithValue("$lastSyncTime", document.LastSyncTime?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdTime", document.CreatedTime.ToString("O"));
        command.Parameters.AddWithValue("$updatedTime", document.UpdatedTime.ToString("O"));
        command.Parameters.AddWithValue("$deleteTime", document.DeleteTime?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$restoreToken", document.RestoreToken ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$replacedByDocumentId", document.ReplacedByDocumentId ?? (object)DBNull.Value);
    }

    private static TemplateSnapshotMetadata ReadSnapshot(SqliteDataReader reader)
    {
        return new TemplateSnapshotMetadata(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7)));
    }

    private static GeneratedDocumentIndexInfo ReadDocument(SqliteDataReader reader)
    {
        return new GeneratedDocumentIndexInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? "" : reader.GetString(11),
            reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)),
            DateTimeOffset.Parse(reader.GetString(13)),
            DateTimeOffset.Parse(reader.GetString(14)),
            reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15)),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17));
    }

    private static IReadOnlyList<string> DeserializeStringList(string json)
    {
        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
    }
}

public sealed record TemplateSnapshotMetadata(
    string SnapshotId,
    string ProjectId,
    string UnitProjectId,
    string ModuleId,
    string ModuleName,
    string ModuleVersion,
    DateTimeOffset CreatedTime,
    DateTimeOffset UpdatedTime);

public sealed record TemplateSnapshotSaveNode(
    string NodeId,
    string? ParentId,
    string NodeName,
    string NodeType,
    string? FolderLevel,
    string? TemplateNodeId,
    long? TemplateItemId,
    string? TemplateCode,
    int SortOrder,
    IReadOnlyList<string> PathIds,
    string FullPath,
    string? DivisionId,
    string? DivisionName,
    string? SubDivisionId,
    string? SubDivisionName,
    string? SubItemId,
    string? SubItemName);

public sealed record TemplateNodeContext(
    string NodeId,
    string TemplateNodeId,
    long TemplateItemId,
    string TemplateCode,
    IReadOnlyList<string> PathIds,
    string FullPath,
    string DivisionId,
    string DivisionName,
    string SubDivisionId,
    string SubDivisionName,
    string SubItemId,
    string SubItemName,
    string TemplateName);

public sealed record SummaryDocumentDetailInfo(
    string DocumentId,
    string SummaryType,
    string CategoryNodeId,
    IReadOnlyList<string> ParentDocumentIds,
    int TotalCount);
