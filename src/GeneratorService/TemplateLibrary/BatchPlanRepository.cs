using System.Text.Json;
using GeneratorService.Models;
using Microsoft.Data.Sqlite;

namespace GeneratorService.TemplateLibrary;

public sealed class BatchPlanRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;

    public BatchPlanRepository(DirectoryInfo rootPath, AppConfig config)
    {
        _rootPath = rootPath;
        _config = config;
    }

    public void EnsureCreated()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS BatchPlan (
              Id TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NULL,
              Name TEXT NOT NULL,
              Remark TEXT NOT NULL DEFAULT '',
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL,
              Status TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_batch_plan_project
              ON BatchPlan(ProjectId, Status, UpdatedAt);
            CREATE TABLE IF NOT EXISTS BatchPlanItem (
              Id TEXT PRIMARY KEY,
              BatchPlanId TEXT NOT NULL,
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NULL,
              ModuleId TEXT NOT NULL,
              TemplateItemId INTEGER NOT NULL,
              TemplateName TEXT NOT NULL,
              PartName TEXT NOT NULL,
              Capacity TEXT NOT NULL DEFAULT '',
              QuantityUnit TEXT NOT NULL DEFAULT '',
              ConstructionDate TEXT NOT NULL DEFAULT '',
              DeviceQuantitiesJson TEXT NOT NULL DEFAULT '{}',
              GeneratedDocumentId TEXT NULL,
              Status TEXT NOT NULL,
              ErrorMessage TEXT NOT NULL DEFAULT '',
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL,
              FOREIGN KEY(BatchPlanId) REFERENCES BatchPlan(Id)
            );

            CREATE INDEX IF NOT EXISTS idx_batch_plan_item_plan
              ON BatchPlanItem(BatchPlanId, TemplateItemId);

            CREATE TABLE IF NOT EXISTS InspectionItemDeviceMapping (
              Id TEXT PRIMARY KEY,
              ModuleId TEXT NOT NULL,
              TemplateItemId INTEGER NOT NULL,
              RuleId INTEGER NULL,
              InspectionItemName TEXT NOT NULL,
              DeviceFieldKey TEXT NOT NULL,
              DeviceDisplayName TEXT NOT NULL,
              TargetCellsJson TEXT NOT NULL DEFAULT '{}',
              FillMode TEXT NOT NULL,
              IsEnabled INTEGER NOT NULL DEFAULT 1,
              SortOrder INTEGER NOT NULL DEFAULT 0,
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_device_mapping_template
              ON InspectionItemDeviceMapping(ModuleId, TemplateItemId, IsEnabled, SortOrder);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "BatchPlan", "UnitProjectId", "TEXT NULL");
        EnsureColumn(connection, "BatchPlanItem", "UnitProjectId", "TEXT NULL");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS idx_batch_plan_unit_project ON BatchPlan(ProjectId, UnitProjectId, Status, UpdatedAt);");
    }

    public IReadOnlyList<BatchPlanInfo> ListPlans(string projectId, string unitProjectId)
    {
        using var connection = OpenConnection();
        var plans = new List<BatchPlanInfo>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, UnitProjectId, Name, Remark, Status, CreatedAt, UpdatedAt
            FROM BatchPlan
            WHERE ProjectId = $projectId AND UnitProjectId = $unitProjectId AND Status <> 'deleted'
            ORDER BY UpdatedAt DESC, CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            plans.Add(ReadPlan(reader, []));
        }

        return plans.Select(plan => plan with { Items = ListItems(plan.Id) }).ToArray();
    }

    public BatchPlanInfo? GetPlan(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, UnitProjectId, Name, Remark, Status, CreatedAt, UpdatedAt
            FROM BatchPlan
            WHERE Id = $id AND Status <> 'deleted';
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var plan = ReadPlan(reader, []);
        return plan with { Items = ListItems(plan.Id) };
    }

    public BatchPlanInfo SavePlan(string? id, string projectId, string unitProjectId, BatchPlanSaveRequest request)
    {
        var planId = string.IsNullOrWhiteSpace(id) ? $"batch-plan:{Guid.NewGuid():N}" : id.Trim();
        var now = DateTimeOffset.Now;
        var planName = string.IsNullOrWhiteSpace(request.Name)
            ? $"检验批划分计划-{now:yyyyMMddHHmm}"
            : request.Name.Trim();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO BatchPlan (Id, ProjectId, UnitProjectId, Name, Remark, CreatedAt, UpdatedAt, Status)
                VALUES ($id, $projectId, $unitProjectId, $name, $remark, $createdAt, $updatedAt, 'active')
                ON CONFLICT(Id) DO UPDATE SET
                  ProjectId = excluded.ProjectId,
                  UnitProjectId = excluded.UnitProjectId,
                  Name = excluded.Name,
                  Remark = excluded.Remark,
                  UpdatedAt = excluded.UpdatedAt,
                  Status = 'active';
                """;
            command.Parameters.AddWithValue("$id", planId);
            command.Parameters.AddWithValue("$projectId", projectId);
            command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
            command.Parameters.AddWithValue("$name", planName);
            command.Parameters.AddWithValue("$remark", request.Remark?.Trim() ?? "");
            command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            command.ExecuteNonQuery();
        }

        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Items ?? Array.Empty<BatchPlanItemSaveRequest>())
        {
            if (IsBlankItem(item))
            {
                continue;
            }

            var itemId = string.IsNullOrWhiteSpace(item.Id) ? $"batch-item:{Guid.NewGuid():N}" : item.Id.Trim();
            keepIds.Add(itemId);
            UpsertItem(connection, transaction, planId, projectId, unitProjectId, itemId, item, now);
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = keepIds.Count == 0
                ? "DELETE FROM BatchPlanItem WHERE BatchPlanId = $planId;"
                : $"DELETE FROM BatchPlanItem WHERE BatchPlanId = $planId AND Id NOT IN ({string.Join(",", keepIds.Select((_, index) => $"$id{index}"))});";
            delete.Parameters.AddWithValue("$planId", planId);
            var index = 0;
            foreach (var keepId in keepIds)
            {
                delete.Parameters.AddWithValue($"$id{index++}", keepId);
            }

            delete.ExecuteNonQuery();
        }

        transaction.Commit();
        return GetPlan(planId) ?? throw new InvalidOperationException("批量计划保存失败。");
    }

    public void MarkItemGenerated(string itemId, string documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE BatchPlanItem
            SET GeneratedDocumentId = $documentId,
                Status = 'generated',
                ErrorMessage = '',
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", itemId);
        command.Parameters.AddWithValue("$documentId", documentId);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void MarkItemFailed(string itemId, string errorMessage)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE BatchPlanItem
            SET Status = 'failed',
                ErrorMessage = $errorMessage,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", itemId);
        command.Parameters.AddWithValue("$errorMessage", errorMessage);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<DeviceMappingInfo> ListMappings(string moduleId, long templateItemId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ModuleId, TemplateItemId, RuleId, InspectionItemName,
                   DeviceFieldKey, DeviceDisplayName, TargetCellsJson,
                   FillMode, IsEnabled, SortOrder, CreatedAt, UpdatedAt
            FROM InspectionItemDeviceMapping
            WHERE ModuleId = $moduleId AND TemplateItemId = $templateItemId
            ORDER BY SortOrder, InspectionItemName, DeviceFieldKey;
            """;
        command.Parameters.AddWithValue("$moduleId", moduleId);
        command.Parameters.AddWithValue("$templateItemId", templateItemId);
        using var reader = command.ExecuteReader();
        var mappings = new List<DeviceMappingInfo>();
        while (reader.Read())
        {
            mappings.Add(ReadMapping(reader));
        }

        return mappings;
    }

    public IReadOnlyList<DeviceMappingInfo> SaveMappings(string moduleId, long templateItemId, IReadOnlyList<DeviceMappingSaveItem> mappings)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM InspectionItemDeviceMapping WHERE ModuleId = $moduleId AND TemplateItemId = $templateItemId;";
            delete.Parameters.AddWithValue("$moduleId", moduleId);
            delete.Parameters.AddWithValue("$templateItemId", templateItemId);
            delete.ExecuteNonQuery();
        }

        var now = DateTimeOffset.Now.ToString("O");
        foreach (var item in mappings.Where(item => !string.IsNullOrWhiteSpace(item.DeviceFieldKey)))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO InspectionItemDeviceMapping (
                    Id, ModuleId, TemplateItemId, RuleId, InspectionItemName,
                    DeviceFieldKey, DeviceDisplayName, TargetCellsJson,
                    FillMode, IsEnabled, SortOrder, CreatedAt, UpdatedAt
                )
                VALUES (
                    $id, $moduleId, $templateItemId, $ruleId, $inspectionItemName,
                    $deviceFieldKey, $deviceDisplayName, $targetCellsJson,
                    $fillMode, $isEnabled, $sortOrder, $createdAt, $updatedAt
                );
                """;
            command.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(item.Id) ? $"device-map:{Guid.NewGuid():N}" : item.Id.Trim());
            command.Parameters.AddWithValue("$moduleId", moduleId);
            command.Parameters.AddWithValue("$templateItemId", templateItemId);
            command.Parameters.AddWithValue("$ruleId", item.RuleId is null ? DBNull.Value : item.RuleId);
            command.Parameters.AddWithValue("$inspectionItemName", item.InspectionItemName?.Trim() ?? "");
            command.Parameters.AddWithValue("$deviceFieldKey", item.DeviceFieldKey?.Trim() ?? "");
            command.Parameters.AddWithValue("$deviceDisplayName", item.DeviceDisplayName?.Trim() ?? item.DeviceFieldKey?.Trim() ?? "");
            command.Parameters.AddWithValue("$targetCellsJson", JsonSerializer.Serialize(item.TargetCells ?? new Dictionary<string, string>(), JsonOptions));
            command.Parameters.AddWithValue("$fillMode", string.IsNullOrWhiteSpace(item.FillMode) ? "same-as-device-count" : item.FillMode.Trim());
            command.Parameters.AddWithValue("$isEnabled", item.IsEnabled == false ? 0 : 1);
            command.Parameters.AddWithValue("$sortOrder", item.SortOrder ?? 0);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return ListMappings(moduleId, templateItemId);
    }

    private IReadOnlyList<BatchPlanItemInfo> ListItems(string planId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BatchPlanId, ProjectId, UnitProjectId, ModuleId, TemplateItemId, TemplateName,
                   PartName, Capacity, QuantityUnit, ConstructionDate, DeviceQuantitiesJson,
                   GeneratedDocumentId, Status, ErrorMessage, CreatedAt, UpdatedAt
            FROM BatchPlanItem
            WHERE BatchPlanId = $planId
            ORDER BY CreatedAt, Id;
            """;
        command.Parameters.AddWithValue("$planId", planId);
        using var reader = command.ExecuteReader();
        var items = new List<BatchPlanItemInfo>();
        while (reader.Read())
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    private static void UpsertItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string planId,
        string projectId,
        string unitProjectId,
        string itemId,
        BatchPlanItemSaveRequest item,
        DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO BatchPlanItem (
                Id, BatchPlanId, ProjectId, UnitProjectId, ModuleId, TemplateItemId, TemplateName,
                PartName, Capacity, QuantityUnit, ConstructionDate, DeviceQuantitiesJson,
                GeneratedDocumentId, Status, ErrorMessage, CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $batchPlanId, $projectId, $unitProjectId, $moduleId, $templateItemId, $templateName,
                $partName, $capacity, $quantityUnit, $constructionDate, $deviceQuantitiesJson,
                NULL, $status, $errorMessage, $createdAt, $updatedAt
            )
            ON CONFLICT(Id) DO UPDATE SET
                ModuleId = excluded.ModuleId,
                UnitProjectId = excluded.UnitProjectId,
                TemplateItemId = excluded.TemplateItemId,
                TemplateName = excluded.TemplateName,
                PartName = excluded.PartName,
                Capacity = excluded.Capacity,
                QuantityUnit = excluded.QuantityUnit,
                ConstructionDate = excluded.ConstructionDate,
                DeviceQuantitiesJson = excluded.DeviceQuantitiesJson,
                Status = CASE
                    WHEN BatchPlanItem.Status = 'generated' AND excluded.Status = 'planned' THEN BatchPlanItem.Status
                    ELSE excluded.Status
                END,
                ErrorMessage = excluded.ErrorMessage,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$id", itemId);
        command.Parameters.AddWithValue("$batchPlanId", planId);
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        command.Parameters.AddWithValue("$moduleId", item.ModuleId?.Trim() ?? "");
        command.Parameters.AddWithValue("$templateItemId", item.TemplateItemId ?? 0);
        command.Parameters.AddWithValue("$templateName", item.TemplateName?.Trim() ?? "");
        command.Parameters.AddWithValue("$partName", item.PartName?.Trim() ?? "");
        command.Parameters.AddWithValue("$capacity", item.Capacity?.Trim() ?? "");
        command.Parameters.AddWithValue("$quantityUnit", item.QuantityUnit?.Trim() ?? "");
        command.Parameters.AddWithValue("$constructionDate", item.ConstructionDate?.Trim() ?? "");
        command.Parameters.AddWithValue("$deviceQuantitiesJson", JsonSerializer.Serialize(NormalizeDeviceQuantities(item.DeviceQuantities), JsonOptions));
        command.Parameters.AddWithValue("$status", string.IsNullOrWhiteSpace(item.Status) ? "planned" : item.Status.Trim());
        command.Parameters.AddWithValue("$errorMessage", item.ErrorMessage?.Trim() ?? "");
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        return connection;
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static BatchPlanInfo ReadPlan(SqliteDataReader reader, IReadOnlyList<BatchPlanItemInfo> items)
    {
        return new BatchPlanInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7)),
            items);
    }

    private static BatchPlanItemInfo ReadItem(SqliteDataReader reader)
    {
        return new BatchPlanItemInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? "" : reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            ParseDeviceQuantities(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            DateTimeOffset.Parse(reader.GetString(15)),
            DateTimeOffset.Parse(reader.GetString(16)));
    }

    private static DeviceMappingInfo ReadMapping(SqliteDataReader reader)
    {
        return new DeviceMappingInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            ParseTargetCells(reader.GetString(7)),
            reader.GetString(8),
            reader.GetInt32(9) != 0,
            reader.GetInt32(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }

    private static IReadOnlyDictionary<string, decimal> NormalizeDeviceQuantities(IReadOnlyDictionary<string, decimal?>? values)
    {
        if (values is null)
        {
            return new Dictionary<string, decimal>();
        }

        return values
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value is not null)
            .ToDictionary(item => item.Key.Trim(), item => item.Value!.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, decimal> ParseDeviceQuantities(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, decimal>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json, JsonOptions)
                   ?? new Dictionary<string, decimal>();
        }
        catch
        {
            return new Dictionary<string, decimal>();
        }
    }

    private static IReadOnlyDictionary<string, string> ParseTargetCells(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static bool IsBlankItem(BatchPlanItemSaveRequest item)
    {
        return string.IsNullOrWhiteSpace(item.ModuleId) &&
               (item.TemplateItemId is null or 0) &&
               string.IsNullOrWhiteSpace(item.TemplateName) &&
               string.IsNullOrWhiteSpace(item.PartName) &&
               string.IsNullOrWhiteSpace(item.Capacity) &&
               string.IsNullOrWhiteSpace(item.ConstructionDate) &&
               (item.DeviceQuantities is null || item.DeviceQuantities.Count == 0);
    }
}
