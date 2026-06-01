using Microsoft.Data.Sqlite;

namespace GeneratorService.Projects;

public sealed class UnitProjectRepository
{
    public const string DefaultUnitProjectName = "默认单位工程";
    public const string DefaultUnitProjectCode = "default";

    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;

    public UnitProjectRepository(DirectoryInfo rootPath, AppConfig config)
    {
        _rootPath = rootPath;
        _config = config;
    }

    public void EnsureCreated()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS UnitProject (
              Id TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              UnitProjectName TEXT NOT NULL,
              UnitProjectCode TEXT NOT NULL,
              ConstructionUnit TEXT NOT NULL DEFAULT '',
              SupervisionUnit TEXT NOT NULL DEFAULT '',
              DesignUnit TEXT NOT NULL DEFAULT '',
              SurveyUnit TEXT NOT NULL DEFAULT '',
              BuildingArea TEXT NOT NULL DEFAULT '',
              StructureType TEXT NOT NULL DEFAULT '',
              Floors TEXT NOT NULL DEFAULT '',
              StartDate TEXT NULL,
              CompletionDate TEXT NULL,
              DefaultModule TEXT NOT NULL DEFAULT '',
              TemplateVersion TEXT NOT NULL DEFAULT '',
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL,
              Status TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_unit_project_project
              ON UnitProject(ProjectId, Status, UpdatedAt);
            """;
        command.ExecuteNonQuery();

        EnsureColumnIfTableExists(connection, "MaterialEntry", "UnitProjectId", "TEXT NULL");
        CreateIndexIfTableExists(connection, "MaterialEntry", "idx_material_entry_unit_project",
            "CREATE INDEX IF NOT EXISTS idx_material_entry_unit_project ON MaterialEntry(ProjectId, UnitProjectId, EntryDate, MaterialName);");
        CreateIndexIfTableExists(connection, "GeneratedDocumentIndex", "idx_generated_document_unit_project",
            "CREATE INDEX IF NOT EXISTS idx_generated_document_unit_project ON GeneratedDocumentIndex(ProjectId, UnitProjectId, DocumentType, DocumentStatus, UpdatedTime);");
    }

    public UnitProjectInfo EnsureDefault(string projectId, string projectName, string defaultModule, string templateVersion)
    {
        using var connection = OpenConnection();
        var existing = List(connection, projectId, includeInactive: true).FirstOrDefault();
        var unit = existing ?? Insert(connection, projectId, new UnitProjectSaveRequest(
            null,
            projectId,
            string.IsNullOrWhiteSpace(projectName) ? DefaultUnitProjectName : DefaultUnitProjectName,
            DefaultUnitProjectCode,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            defaultModule,
            templateVersion,
            null));

        BackfillLegacyRows(connection, projectId, unit.Id);
        return Get(projectId, unit.Id) ?? unit;
    }

    public IReadOnlyList<UnitProjectInfo> List(string projectId, bool includeInactive = false)
    {
        using var connection = OpenConnection();
        return List(connection, projectId, includeInactive);
    }

    public UnitProjectInfo? Get(string projectId, string unitProjectId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, UnitProjectName, UnitProjectCode, ConstructionUnit,
                   SupervisionUnit, DesignUnit, SurveyUnit, BuildingArea, StructureType,
                   Floors, StartDate, CompletionDate, DefaultModule, TemplateVersion,
                   CreatedAt, UpdatedAt, Status
            FROM UnitProject
            WHERE ProjectId = $projectId AND Id = $id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$id", unitProjectId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var unitProject = ReadUnitProject(reader);
        reader.Dispose();
        return EnrichCounts(connection, unitProject);
    }

    public UnitProjectInfo Save(string projectId, UnitProjectSaveRequest request)
    {
        using var connection = OpenConnection();
        var id = string.IsNullOrWhiteSpace(request.Id) ? $"unit:{Guid.NewGuid():N}" : request.Id.Trim();
        var existing = Get(projectId, id);
        var copied = string.IsNullOrWhiteSpace(request.CopyFromUnitProjectId)
            ? null
            : Get(projectId, request.CopyFromUnitProjectId.Trim());

        var now = DateTimeOffset.Now;
        var name = CleanRequired(request.UnitProjectName, "单位工程名称不能为空。");
        var code = Clean(request.UnitProjectCode);
        if (string.IsNullOrWhiteSpace(code))
        {
            code = BuildCode(name);
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO UnitProject (
                Id, ProjectId, UnitProjectName, UnitProjectCode, ConstructionUnit,
                SupervisionUnit, DesignUnit, SurveyUnit, BuildingArea, StructureType,
                Floors, StartDate, CompletionDate, DefaultModule, TemplateVersion,
                CreatedAt, UpdatedAt, Status
            )
            VALUES (
                $id, $projectId, $name, $code, $constructionUnit,
                $supervisionUnit, $designUnit, $surveyUnit, $buildingArea, $structureType,
                $floors, $startDate, $completionDate, $defaultModule, $templateVersion,
                $createdAt, $updatedAt, 'active'
            )
            ON CONFLICT(Id) DO UPDATE SET
                UnitProjectName = excluded.UnitProjectName,
                UnitProjectCode = excluded.UnitProjectCode,
                ConstructionUnit = excluded.ConstructionUnit,
                SupervisionUnit = excluded.SupervisionUnit,
                DesignUnit = excluded.DesignUnit,
                SurveyUnit = excluded.SurveyUnit,
                BuildingArea = excluded.BuildingArea,
                StructureType = excluded.StructureType,
                Floors = excluded.Floors,
                StartDate = excluded.StartDate,
                CompletionDate = excluded.CompletionDate,
                DefaultModule = excluded.DefaultModule,
                TemplateVersion = excluded.TemplateVersion,
                UpdatedAt = excluded.UpdatedAt,
                Status = 'active';
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$constructionUnit", Pick(request.ConstructionUnit, existing?.ConstructionUnit, copied?.ConstructionUnit));
        command.Parameters.AddWithValue("$supervisionUnit", Pick(request.SupervisionUnit, existing?.SupervisionUnit, copied?.SupervisionUnit));
        command.Parameters.AddWithValue("$designUnit", Pick(request.DesignUnit, existing?.DesignUnit, copied?.DesignUnit));
        command.Parameters.AddWithValue("$surveyUnit", Pick(request.SurveyUnit, existing?.SurveyUnit, copied?.SurveyUnit));
        command.Parameters.AddWithValue("$buildingArea", Pick(request.BuildingArea, existing?.BuildingArea, copied?.BuildingArea));
        command.Parameters.AddWithValue("$structureType", Pick(request.StructureType, existing?.StructureType, copied?.StructureType));
        command.Parameters.AddWithValue("$floors", Pick(request.Floors, existing?.Floors, copied?.Floors));
        command.Parameters.AddWithValue("$startDate", ToDbDate(request.StartDate ?? existing?.StartDate ?? copied?.StartDate));
        command.Parameters.AddWithValue("$completionDate", ToDbDate(request.CompletionDate ?? existing?.CompletionDate ?? copied?.CompletionDate));
        command.Parameters.AddWithValue("$defaultModule", Pick(request.DefaultModule, existing?.DefaultModule, copied?.DefaultModule));
        command.Parameters.AddWithValue("$templateVersion", Pick(request.TemplateVersion, existing?.TemplateVersion, copied?.TemplateVersion));
        command.Parameters.AddWithValue("$createdAt", (existing?.CreatedAt ?? now).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.ExecuteNonQuery();

        return Get(projectId, id) ?? throw new InvalidOperationException("单位工程保存失败。");
    }

    public void Deactivate(string projectId, string unitProjectId)
    {
        using var connection = OpenConnection();
        var activeCount = List(connection, projectId, includeInactive: false).Count;
        if (activeCount <= 1)
        {
            throw new InvalidOperationException("至少需要保留一个启用的单位工程。");
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE UnitProject
            SET Status = 'inactive',
                UpdatedAt = $updatedAt
            WHERE ProjectId = $projectId AND Id = $id AND Status <> 'inactive';
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$id", unitProjectId);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("单位工程不存在或已停用。");
        }
    }

    private UnitProjectInfo Insert(SqliteConnection connection, string projectId, UnitProjectSaveRequest request)
    {
        var id = $"unit:{Guid.NewGuid():N}";
        using var command = connection.CreateCommand();
        var now = DateTimeOffset.Now;
        command.CommandText = """
            INSERT INTO UnitProject (
                Id, ProjectId, UnitProjectName, UnitProjectCode, ConstructionUnit,
                SupervisionUnit, DesignUnit, SurveyUnit, BuildingArea, StructureType,
                Floors, StartDate, CompletionDate, DefaultModule, TemplateVersion,
                CreatedAt, UpdatedAt, Status
            )
            VALUES (
                $id, $projectId, $name, $code, '', '', '', '', '', '',
                '', NULL, NULL, $defaultModule, $templateVersion,
                $createdAt, $updatedAt, 'active'
            );
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$name", CleanRequired(request.UnitProjectName, "单位工程名称不能为空。"));
        command.Parameters.AddWithValue("$code", Clean(request.UnitProjectCode) is { Length: > 0 } code ? code : DefaultUnitProjectCode);
        command.Parameters.AddWithValue("$defaultModule", Clean(request.DefaultModule));
        command.Parameters.AddWithValue("$templateVersion", Clean(request.TemplateVersion));
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.ExecuteNonQuery();
        return Get(projectId, id) ?? throw new InvalidOperationException("默认单位工程创建失败。");
    }

    private IReadOnlyList<UnitProjectInfo> List(SqliteConnection connection, string projectId, bool includeInactive)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, UnitProjectName, UnitProjectCode, ConstructionUnit,
                   SupervisionUnit, DesignUnit, SurveyUnit, BuildingArea, StructureType,
                   Floors, StartDate, CompletionDate, DefaultModule, TemplateVersion,
                   CreatedAt, UpdatedAt, Status
            FROM UnitProject
            WHERE ProjectId = $projectId
              AND ($includeInactive = 1 OR Status = 'active')
            ORDER BY CASE UnitProjectCode WHEN 'default' THEN 0 ELSE 1 END, CreatedAt, UnitProjectName;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$includeInactive", includeInactive ? 1 : 0);
        using var reader = command.ExecuteReader();
        var items = new List<UnitProjectInfo>();
        while (reader.Read())
        {
            items.Add(ReadUnitProject(reader));
        }

        reader.Dispose();
        return items.Select(item => EnrichCounts(connection, item)).ToArray();
    }

    private static UnitProjectInfo ReadUnitProject(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var projectId = reader.GetString(1);
        return new UnitProjectInfo(
            id,
            projectId,
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : DateOnly.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : DateOnly.Parse(reader.GetString(12)),
            reader.GetString(13),
            reader.GetString(14),
            0,
            0,
            DateTimeOffset.Parse(reader.GetString(15)),
            DateTimeOffset.Parse(reader.GetString(16)),
            reader.GetString(17));
    }

    private UnitProjectInfo EnrichCounts(SqliteConnection connection, UnitProjectInfo item)
    {
        return item with
        {
            DocumentCount = CountRows(connection, "GeneratedDocumentIndex", item.ProjectId, item.Id),
            MaterialCount = CountRows(connection, "MaterialEntry", item.ProjectId, item.Id)
        };
    }

    private int CountRows(SqliteConnection connection, string tableName, string projectId, string unitProjectId)
    {
        if (!TableExists(connection, tableName))
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        var statusFilter = tableName == "GeneratedDocumentIndex"
            ? " AND DocumentStatus = 'Active'"
            : tableName == "MaterialEntry"
                ? " AND DeletedAt IS NULL"
                : "";
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM {tableName}
            WHERE ProjectId = $projectId
              AND UnitProjectId = $unitProjectId
              {statusFilter};
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void BackfillLegacyRows(SqliteConnection connection, string projectId, string unitProjectId)
    {
        foreach (var tableName in new[] { "GeneratedDocumentIndex", "InspectionBatchPlan", "InspectionBatchPlanRow" })
        {
            if (!TableExists(connection, tableName) || !ColumnExists(connection, tableName, "UnitProjectId"))
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE {tableName}
                SET UnitProjectId = $unitProjectId
                WHERE ProjectId = $projectId
                  AND (UnitProjectId IS NULL OR TRIM(UnitProjectId) = '');
                """;
            command.Parameters.AddWithValue("$projectId", projectId);
            command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        return connection;
    }

    private static void EnsureColumnIfTableExists(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        if (!TableExists(connection, tableName) || ColumnExists(connection, tableName, columnName))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        command.ExecuteNonQuery();
    }

    private static void CreateIndexIfTableExists(SqliteConnection connection, string tableName, string indexName, string sql)
    {
        if (!TableExists(connection, tableName))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Clean(string? value) => value?.Trim() ?? "";

    private static string Pick(string? primary, string? existing, string? copied)
    {
        return !string.IsNullOrWhiteSpace(primary)
            ? primary.Trim()
            : !string.IsNullOrWhiteSpace(existing)
                ? existing.Trim()
                : copied?.Trim() ?? "";
    }

    private static string CleanRequired(string? value, string message)
    {
        var cleaned = Clean(value);
        return string.IsNullOrWhiteSpace(cleaned) ? throw new InvalidOperationException(message) : cleaned;
    }

    private static string BuildCode(string name)
    {
        var code = new string(name.Where(char.IsLetterOrDigit).Take(32).ToArray());
        return string.IsNullOrWhiteSpace(code) ? $"unit-{DateTime.Now:yyyyMMddHHmmss}" : code;
    }

    private static object ToDbDate(DateOnly? value) => value is null ? DBNull.Value : value.Value.ToString("yyyy-MM-dd");
}
