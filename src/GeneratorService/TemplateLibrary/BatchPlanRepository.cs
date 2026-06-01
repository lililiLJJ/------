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
            CREATE TABLE IF NOT EXISTS InspectionBatchPlan (
              PlanId TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NOT NULL,
              PlanName TEXT NOT NULL,
              Remark TEXT NOT NULL DEFAULT '',
              Status TEXT NOT NULL DEFAULT 'Active',
              CreatedTime TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL,
              DeletedTime TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS InspectionBatchPlanRow (
              PlanRowId TEXT PRIMARY KEY,
              PlanId TEXT NOT NULL,
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NOT NULL,
              DivisionId TEXT NOT NULL DEFAULT '',
              DivisionName TEXT NOT NULL DEFAULT '',
              SubDivisionId TEXT NOT NULL DEFAULT '',
              SubDivisionName TEXT NOT NULL DEFAULT '',
              SubItemId TEXT NOT NULL DEFAULT '',
              SubItemName TEXT NOT NULL DEFAULT '',
              TemplateNodeId TEXT NOT NULL DEFAULT '',
              TemplateItemId INTEGER NOT NULL DEFAULT 0,
              TemplateName TEXT NOT NULL DEFAULT '',
              InspectionPart TEXT NOT NULL DEFAULT '',
              ConstructionDate TEXT NOT NULL DEFAULT '',
              CapacitySummary TEXT NOT NULL DEFAULT '',
              Remark TEXT NOT NULL DEFAULT '',
              Status TEXT NOT NULL DEFAULT 'Active',
              GenerateStatus TEXT NOT NULL DEFAULT 'None',
              SortOrder INTEGER NOT NULL DEFAULT 0,
              CreatedTime TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL,
              DeletedTime TEXT NULL,
              FOREIGN KEY(PlanId) REFERENCES InspectionBatchPlan(PlanId)
            );

            CREATE TABLE IF NOT EXISTS PlanRowCapacity (
              CapacityId TEXT PRIMARY KEY,
              PlanRowId TEXT NOT NULL,
              CapacityKey TEXT NOT NULL,
              CapacityName TEXT NOT NULL,
              Value TEXT NOT NULL DEFAULT '',
              Unit TEXT NOT NULL DEFAULT '',
              SortOrder INTEGER NOT NULL DEFAULT 0,
              CreatedTime TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL,
              FOREIGN KEY(PlanRowId) REFERENCES InspectionBatchPlanRow(PlanRowId)
            );

            CREATE TABLE IF NOT EXISTS CapacityFieldConfigOverride (
              ConfigId TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NOT NULL,
              TemplateNodeId TEXT NOT NULL,
              CapacityKey TEXT NOT NULL,
              CapacityName TEXT NOT NULL,
              DefaultUnit TEXT NOT NULL DEFAULT '',
              Required INTEGER NOT NULL DEFAULT 0,
              SortOrder INTEGER NOT NULL DEFAULT 0,
              Enabled INTEGER NOT NULL DEFAULT 1,
              CreatedTime TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ProjectFieldMappingOverride (
              OverrideId TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              UnitProjectId TEXT NOT NULL,
              TemplateNodeId TEXT NOT NULL,
              DataJson TEXT NOT NULL DEFAULT '{}',
              CreatedTime TEXT NOT NULL,
              UpdatedTime TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_inspection_plan_project
              ON InspectionBatchPlan(ProjectId, UnitProjectId, Status, UpdatedTime);
            CREATE INDEX IF NOT EXISTS idx_inspection_plan_row_plan
              ON InspectionBatchPlanRow(PlanId, Status, SortOrder, CreatedTime);
            CREATE INDEX IF NOT EXISTS idx_inspection_plan_row_lookup
              ON InspectionBatchPlanRow(ProjectId, UnitProjectId, Status, GenerateStatus, UpdatedTime);
            CREATE INDEX IF NOT EXISTS idx_plan_row_capacity_row
              ON PlanRowCapacity(PlanRowId, SortOrder, CapacityKey);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_capacity_config_override_unique
              ON CapacityFieldConfigOverride(ProjectId, UnitProjectId, TemplateNodeId, CapacityKey);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_field_mapping_override_unique
              ON ProjectFieldMappingOverride(ProjectId, UnitProjectId, TemplateNodeId);
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<InspectionBatchPlanDto> ListPlans(string projectId, string unitProjectId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PlanId, ProjectId, UnitProjectId, PlanName, Remark, Status, CreatedTime, UpdatedTime, DeletedTime
            FROM InspectionBatchPlan
            WHERE ProjectId = $projectId
              AND UnitProjectId = $unitProjectId
              AND Status <> 'Deleted'
            ORDER BY UpdatedTime DESC, CreatedTime DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        using var reader = command.ExecuteReader();
        var plans = new List<InspectionBatchPlanDto>();
        while (reader.Read())
        {
            var planId = reader.GetString(0);
            var rows = ListRows(connection, planId, includeDeleted: false);
            plans.Add(BuildPlan(reader, rows));
        }

        return plans;
    }

    public InspectionBatchPlanDto? GetPlan(string planId, bool includeDeletedRows = false)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PlanId, ProjectId, UnitProjectId, PlanName, Remark, Status, CreatedTime, UpdatedTime, DeletedTime
            FROM InspectionBatchPlan
            WHERE PlanId = $planId;
            """;
        command.Parameters.AddWithValue("$planId", planId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var rows = ListRows(connection, planId, includeDeletedRows);
        return BuildPlan(reader, rows);
    }

    public InspectionBatchPlanRowDto? GetPlanRow(string planRowId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PlanRowId, PlanId, ProjectId, UnitProjectId, DivisionId, DivisionName, SubDivisionId, SubDivisionName,
                   SubItemId, SubItemName, TemplateNodeId, TemplateItemId, TemplateName, InspectionPart, ConstructionDate,
                   CapacitySummary, Remark, Status, GenerateStatus, CreatedTime, UpdatedTime, DeletedTime
            FROM InspectionBatchPlanRow
            WHERE PlanRowId = $planRowId;
            """;
        command.Parameters.AddWithValue("$planRowId", planRowId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return ReadRow(connection, reader);
    }

    public IReadOnlyList<InspectionBatchPlanRowDto> ListPlanRows(string planId, bool includeDeleted = false)
    {
        using var connection = OpenConnection();
        return ListRows(connection, planId, includeDeleted);
    }

    public InspectionBatchPlanDto SavePlan(string? planId, string projectId, string unitProjectId, InspectionPlanSaveRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now;
        var normalizedPlanId = string.IsNullOrWhiteSpace(planId) ? $"inspection-plan:{Guid.NewGuid():N}" : planId.Trim();
        var existing = GetPlan(normalizedPlanId, includeDeletedRows: true);
        var planName = string.IsNullOrWhiteSpace(request.PlanName)
            ? $"检验批计划-{now:yyyyMMddHHmm}"
            : request.PlanName.Trim();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO InspectionBatchPlan (
                    PlanId, ProjectId, UnitProjectId, PlanName, Remark, Status, CreatedTime, UpdatedTime, DeletedTime
                )
                VALUES (
                    $planId, $projectId, $unitProjectId, $planName, $remark, 'Active', $createdTime, $updatedTime, NULL
                )
                ON CONFLICT(PlanId) DO UPDATE SET
                    ProjectId = excluded.ProjectId,
                    UnitProjectId = excluded.UnitProjectId,
                    PlanName = excluded.PlanName,
                    Remark = excluded.Remark,
                    Status = 'Active',
                    UpdatedTime = excluded.UpdatedTime,
                    DeletedTime = NULL;
                """;
            command.Parameters.AddWithValue("$planId", normalizedPlanId);
            command.Parameters.AddWithValue("$projectId", projectId);
            command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
            command.Parameters.AddWithValue("$planName", planName);
            command.Parameters.AddWithValue("$remark", request.Remark?.Trim() ?? "");
            command.Parameters.AddWithValue("$createdTime", (existing?.CreatedTime ?? now).ToString("O"));
            command.Parameters.AddWithValue("$updatedTime", now.ToString("O"));
            command.ExecuteNonQuery();
        }

        var requestedRows = request.Rows ?? [];
        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < requestedRows.Count; index++)
        {
            var row = requestedRows[index];
            if (IsBlankRow(row))
            {
                continue;
            }

            var rowId = string.IsNullOrWhiteSpace(row.PlanRowId) ? $"inspection-plan-row:{Guid.NewGuid():N}" : row.PlanRowId.Trim();
            keepIds.Add(rowId);
            UpsertRow(connection, transaction, normalizedPlanId, projectId, unitProjectId, rowId, row, index, now);
        }

        var existingRowIds = GetRowIds(connection, normalizedPlanId);
        foreach (var existingRowId in existingRowIds.Where(id => !keepIds.Contains(id)))
        {
            SoftDeleteRow(connection, transaction, existingRowId, now);
        }

        transaction.Commit();
        return GetPlan(normalizedPlanId) ?? throw new InvalidOperationException("检验批计划保存失败。");
    }

    public InspectionPlanDeleteResult DeletePlan(string planId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now.ToString("O");

        using (var rowCommand = connection.CreateCommand())
        {
            rowCommand.Transaction = transaction;
            rowCommand.CommandText = """
                UPDATE InspectionBatchPlanRow
                SET Status = 'Deleted',
                    DeletedTime = $deletedTime,
                    UpdatedTime = $updatedTime
                WHERE PlanId = $planId AND Status <> 'Deleted';
                """;
            rowCommand.Parameters.AddWithValue("$planId", planId);
            rowCommand.Parameters.AddWithValue("$deletedTime", now);
            rowCommand.Parameters.AddWithValue("$updatedTime", now);
            rowCommand.ExecuteNonQuery();
        }

        using (var planCommand = connection.CreateCommand())
        {
            planCommand.Transaction = transaction;
            planCommand.CommandText = """
                UPDATE InspectionBatchPlan
                SET Status = 'Deleted',
                    DeletedTime = $deletedTime,
                    UpdatedTime = $updatedTime
                WHERE PlanId = $planId;
                """;
            planCommand.Parameters.AddWithValue("$planId", planId);
            planCommand.Parameters.AddWithValue("$deletedTime", now);
            planCommand.Parameters.AddWithValue("$updatedTime", now);
            if (planCommand.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException("检验批计划不存在。");
            }
        }

        transaction.Commit();
        return new InspectionPlanDeleteResult(true, planId, "检验批计划已删除。");
    }

    public void UpdatePlanRowLifecycleStatus(string planRowId, string status, DateTimeOffset? deletedTime = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE InspectionBatchPlanRow
            SET Status = $status,
                DeletedTime = $deletedTime,
                UpdatedTime = $updatedTime
            WHERE PlanRowId = $planRowId;
            """;
        command.Parameters.AddWithValue("$planRowId", planRowId);
        command.Parameters.AddWithValue("$status", NormalizeLifecycleStatus(status));
        command.Parameters.AddWithValue("$deletedTime", deletedTime?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedTime", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void UpdatePlanRowGenerateStatus(string planRowId, string generateStatus)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE InspectionBatchPlanRow
            SET GenerateStatus = $generateStatus,
                UpdatedTime = $updatedTime
            WHERE PlanRowId = $planRowId;
            """;
        command.Parameters.AddWithValue("$planRowId", planRowId);
        command.Parameters.AddWithValue("$generateStatus", NormalizeGenerateStatus(generateStatus));
        command.Parameters.AddWithValue("$updatedTime", DateTimeOffset.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<CapacityFieldConfigInfo> ListCapacityFieldOverrides(string projectId, string unitProjectId, string templateNodeId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ConfigId, ProjectId, UnitProjectId, TemplateNodeId, CapacityKey, CapacityName,
                   DefaultUnit, Required, SortOrder, Enabled
            FROM CapacityFieldConfigOverride
            WHERE ProjectId = $projectId
              AND UnitProjectId = $unitProjectId
              AND TemplateNodeId = $templateNodeId
            ORDER BY SortOrder, CapacityKey;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        command.Parameters.AddWithValue("$templateNodeId", templateNodeId);
        using var reader = command.ExecuteReader();
        var items = new List<CapacityFieldConfigInfo>();
        while (reader.Read())
        {
            items.Add(new CapacityFieldConfigInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6),
                !reader.IsDBNull(7) && reader.GetInt32(7) != 0,
                reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                reader.IsDBNull(9) || reader.GetInt32(9) != 0,
                true));
        }

        return items;
    }

    public IReadOnlyList<CapacityFieldConfigInfo> SaveCapacityFieldOverrides(
        string projectId,
        string unitProjectId,
        string templateNodeId,
        IReadOnlyList<SaveCapacityFieldConfigItem> items)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now.ToString("O");

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM CapacityFieldConfigOverride
                WHERE ProjectId = $projectId
                  AND UnitProjectId = $unitProjectId
                  AND TemplateNodeId = $templateNodeId;
                """;
            delete.Parameters.AddWithValue("$projectId", projectId);
            delete.Parameters.AddWithValue("$unitProjectId", unitProjectId);
            delete.Parameters.AddWithValue("$templateNodeId", templateNodeId);
            delete.ExecuteNonQuery();
        }

        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.CapacityKey)))
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO CapacityFieldConfigOverride (
                    ConfigId, ProjectId, UnitProjectId, TemplateNodeId, CapacityKey, CapacityName,
                    DefaultUnit, Required, SortOrder, Enabled, CreatedTime, UpdatedTime
                )
                VALUES (
                    $configId, $projectId, $unitProjectId, $templateNodeId, $capacityKey, $capacityName,
                    $defaultUnit, $required, $sortOrder, $enabled, $createdTime, $updatedTime
                );
                """;
            insert.Parameters.AddWithValue("$configId", string.IsNullOrWhiteSpace(item.ConfigId) ? $"capacity-config:{Guid.NewGuid():N}" : item.ConfigId.Trim());
            insert.Parameters.AddWithValue("$projectId", projectId);
            insert.Parameters.AddWithValue("$unitProjectId", unitProjectId);
            insert.Parameters.AddWithValue("$templateNodeId", templateNodeId);
            insert.Parameters.AddWithValue("$capacityKey", item.CapacityKey!.Trim());
            insert.Parameters.AddWithValue("$capacityName", string.IsNullOrWhiteSpace(item.CapacityName) ? item.CapacityKey.Trim() : item.CapacityName.Trim());
            insert.Parameters.AddWithValue("$defaultUnit", item.DefaultUnit?.Trim() ?? "");
            insert.Parameters.AddWithValue("$required", item.Required == true ? 1 : 0);
            insert.Parameters.AddWithValue("$sortOrder", item.SortOrder ?? 0);
            insert.Parameters.AddWithValue("$enabled", item.Enabled == false ? 0 : 1);
            insert.Parameters.AddWithValue("$createdTime", now);
            insert.Parameters.AddWithValue("$updatedTime", now);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return ListCapacityFieldOverrides(projectId, unitProjectId, templateNodeId);
    }

    public IReadOnlyDictionary<string, string> GetFieldMappingOverride(string projectId, string unitProjectId, string templateNodeId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DataJson
            FROM ProjectFieldMappingOverride
            WHERE ProjectId = $projectId
              AND UnitProjectId = $unitProjectId
              AND TemplateNodeId = $templateNodeId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        command.Parameters.AddWithValue("$templateNodeId", templateNodeId);
        var json = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyDictionary<string, string> SaveFieldMappingOverride(
        string projectId,
        string unitProjectId,
        string templateNodeId,
        IReadOnlyDictionary<string, string> data)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var now = DateTimeOffset.Now.ToString("O");
        command.CommandText = """
            INSERT INTO ProjectFieldMappingOverride (
                OverrideId, ProjectId, UnitProjectId, TemplateNodeId, DataJson, CreatedTime, UpdatedTime
            )
            VALUES (
                $overrideId, $projectId, $unitProjectId, $templateNodeId, $dataJson, $createdTime, $updatedTime
            )
            ON CONFLICT(ProjectId, UnitProjectId, TemplateNodeId) DO UPDATE SET
                DataJson = excluded.DataJson,
                UpdatedTime = excluded.UpdatedTime;
            """;
        command.Parameters.AddWithValue("$overrideId", $"field-mapping-override:{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
        command.Parameters.AddWithValue("$templateNodeId", templateNodeId);
        command.Parameters.AddWithValue("$dataJson", JsonSerializer.Serialize(data, JsonOptions));
        command.Parameters.AddWithValue("$createdTime", now);
        command.Parameters.AddWithValue("$updatedTime", now);
        command.ExecuteNonQuery();
        return GetFieldMappingOverride(projectId, unitProjectId, templateNodeId);
    }

    private static bool IsBlankRow(InspectionPlanRowSaveRequest row)
    {
        return string.IsNullOrWhiteSpace(row.TemplateNodeId) &&
               (row.TemplateItemId is null or 0) &&
               string.IsNullOrWhiteSpace(row.TemplateName) &&
               string.IsNullOrWhiteSpace(row.InspectionPart) &&
               string.IsNullOrWhiteSpace(row.ConstructionDate) &&
               string.IsNullOrWhiteSpace(row.Remark) &&
               (row.Capacities is null || row.Capacities.Count == 0);
    }

    private static void UpsertRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string planId,
        string projectId,
        string unitProjectId,
        string rowId,
        InspectionPlanRowSaveRequest row,
        int sortOrder,
        DateTimeOffset now)
    {
        var existingRow = GetExistingRow(connection, rowId);
        var capacities = NormalizeCapacities(rowId, row.Capacities, now);
        var capacitySummary = BuildCapacitySummary(capacities);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO InspectionBatchPlanRow (
                    PlanRowId, PlanId, ProjectId, UnitProjectId, DivisionId, DivisionName, SubDivisionId, SubDivisionName,
                    SubItemId, SubItemName, TemplateNodeId, TemplateItemId, TemplateName, InspectionPart, ConstructionDate,
                    CapacitySummary, Remark, Status, GenerateStatus, SortOrder, CreatedTime, UpdatedTime, DeletedTime
                )
                VALUES (
                    $planRowId, $planId, $projectId, $unitProjectId, $divisionId, $divisionName, $subDivisionId, $subDivisionName,
                    $subItemId, $subItemName, $templateNodeId, $templateItemId, $templateName, $inspectionPart, $constructionDate,
                    $capacitySummary, $remark, $status, $generateStatus, $sortOrder, $createdTime, $updatedTime, $deletedTime
                )
                ON CONFLICT(PlanRowId) DO UPDATE SET
                    PlanId = excluded.PlanId,
                    ProjectId = excluded.ProjectId,
                    UnitProjectId = excluded.UnitProjectId,
                    DivisionId = excluded.DivisionId,
                    DivisionName = excluded.DivisionName,
                    SubDivisionId = excluded.SubDivisionId,
                    SubDivisionName = excluded.SubDivisionName,
                    SubItemId = excluded.SubItemId,
                    SubItemName = excluded.SubItemName,
                    TemplateNodeId = excluded.TemplateNodeId,
                    TemplateItemId = excluded.TemplateItemId,
                    TemplateName = excluded.TemplateName,
                    InspectionPart = excluded.InspectionPart,
                    ConstructionDate = excluded.ConstructionDate,
                    CapacitySummary = excluded.CapacitySummary,
                    Remark = excluded.Remark,
                    Status = excluded.Status,
                    GenerateStatus = excluded.GenerateStatus,
                    SortOrder = excluded.SortOrder,
                    UpdatedTime = excluded.UpdatedTime,
                    DeletedTime = excluded.DeletedTime;
                """;
            command.Parameters.AddWithValue("$planRowId", rowId);
            command.Parameters.AddWithValue("$planId", planId);
            command.Parameters.AddWithValue("$projectId", projectId);
            command.Parameters.AddWithValue("$unitProjectId", unitProjectId);
            command.Parameters.AddWithValue("$divisionId", row.DivisionId?.Trim() ?? "");
            command.Parameters.AddWithValue("$divisionName", row.DivisionName?.Trim() ?? "");
            command.Parameters.AddWithValue("$subDivisionId", row.SubDivisionId?.Trim() ?? "");
            command.Parameters.AddWithValue("$subDivisionName", row.SubDivisionName?.Trim() ?? "");
            command.Parameters.AddWithValue("$subItemId", row.SubItemId?.Trim() ?? "");
            command.Parameters.AddWithValue("$subItemName", row.SubItemName?.Trim() ?? "");
            command.Parameters.AddWithValue("$templateNodeId", row.TemplateNodeId?.Trim() ?? "");
            command.Parameters.AddWithValue("$templateItemId", row.TemplateItemId ?? 0);
            command.Parameters.AddWithValue("$templateName", row.TemplateName?.Trim() ?? "");
            command.Parameters.AddWithValue("$inspectionPart", row.InspectionPart?.Trim() ?? "");
            command.Parameters.AddWithValue("$constructionDate", row.ConstructionDate?.Trim() ?? "");
            command.Parameters.AddWithValue("$capacitySummary", capacitySummary);
            command.Parameters.AddWithValue("$remark", row.Remark?.Trim() ?? "");
            command.Parameters.AddWithValue("$status", NormalizeLifecycleStatus(row.Status));
            command.Parameters.AddWithValue("$generateStatus", NormalizeGenerateStatus(row.GenerateStatus, existingRow?.GenerateStatus));
            command.Parameters.AddWithValue("$sortOrder", sortOrder);
            command.Parameters.AddWithValue("$createdTime", (existingRow?.CreatedTime ?? now).ToString("O"));
            command.Parameters.AddWithValue("$updatedTime", now.ToString("O"));
            command.Parameters.AddWithValue("$deletedTime",
                string.Equals(NormalizeLifecycleStatus(row.Status), "Deleted", StringComparison.OrdinalIgnoreCase)
                    ? now.ToString("O")
                    : (object)DBNull.Value);
            command.ExecuteNonQuery();
        }

        using (var deleteCapacities = connection.CreateCommand())
        {
            deleteCapacities.Transaction = transaction;
            deleteCapacities.CommandText = "DELETE FROM PlanRowCapacity WHERE PlanRowId = $planRowId;";
            deleteCapacities.Parameters.AddWithValue("$planRowId", rowId);
            deleteCapacities.ExecuteNonQuery();
        }

        foreach (var capacity in capacities)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO PlanRowCapacity (
                    CapacityId, PlanRowId, CapacityKey, CapacityName, Value, Unit, SortOrder, CreatedTime, UpdatedTime
                )
                VALUES (
                    $capacityId, $planRowId, $capacityKey, $capacityName, $value, $unit, $sortOrder, $createdTime, $updatedTime
                );
                """;
            insert.Parameters.AddWithValue("$capacityId", capacity.CapacityId);
            insert.Parameters.AddWithValue("$planRowId", rowId);
            insert.Parameters.AddWithValue("$capacityKey", capacity.CapacityKey);
            insert.Parameters.AddWithValue("$capacityName", capacity.CapacityName);
            insert.Parameters.AddWithValue("$value", capacity.Value);
            insert.Parameters.AddWithValue("$unit", capacity.Unit);
            insert.Parameters.AddWithValue("$sortOrder", capacity.SortOrder);
            insert.Parameters.AddWithValue("$createdTime", capacity.CreatedTime.ToString("O"));
            insert.Parameters.AddWithValue("$updatedTime", capacity.UpdatedTime.ToString("O"));
            insert.ExecuteNonQuery();
        }
    }

    private static void SoftDeleteRow(SqliteConnection connection, SqliteTransaction transaction, string rowId, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE InspectionBatchPlanRow
            SET Status = 'Deleted',
                DeletedTime = $deletedTime,
                UpdatedTime = $updatedTime
            WHERE PlanRowId = $planRowId;
            """;
        command.Parameters.AddWithValue("$planRowId", rowId);
        command.Parameters.AddWithValue("$deletedTime", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedTime", now.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static InspectionBatchPlanRowDto? GetExistingRow(SqliteConnection connection, string rowId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PlanRowId, PlanId, ProjectId, UnitProjectId, DivisionId, DivisionName, SubDivisionId, SubDivisionName,
                   SubItemId, SubItemName, TemplateNodeId, TemplateItemId, TemplateName, InspectionPart, ConstructionDate,
                   CapacitySummary, Remark, Status, GenerateStatus, CreatedTime, UpdatedTime, DeletedTime
            FROM InspectionBatchPlanRow
            WHERE PlanRowId = $planRowId;
            """;
        command.Parameters.AddWithValue("$planRowId", rowId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return ReadRow(connection, reader);
    }

    private static IReadOnlyList<string> GetRowIds(SqliteConnection connection, string planId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PlanRowId FROM InspectionBatchPlanRow WHERE PlanId = $planId;";
        command.Parameters.AddWithValue("$planId", planId);
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static IReadOnlyList<InspectionBatchPlanRowDto> ListRows(SqliteConnection connection, string planId, bool includeDeleted)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PlanRowId, PlanId, ProjectId, UnitProjectId, DivisionId, DivisionName, SubDivisionId, SubDivisionName,
                   SubItemId, SubItemName, TemplateNodeId, TemplateItemId, TemplateName, InspectionPart, ConstructionDate,
                   CapacitySummary, Remark, Status, GenerateStatus, CreatedTime, UpdatedTime, DeletedTime
            FROM InspectionBatchPlanRow
            WHERE PlanId = $planId
              AND ($includeDeleted = 1 OR Status <> 'Deleted')
            ORDER BY SortOrder, CreatedTime, PlanRowId;
            """;
        command.Parameters.AddWithValue("$planId", planId);
        command.Parameters.AddWithValue("$includeDeleted", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        var rows = new List<InspectionBatchPlanRowDto>();
        while (reader.Read())
        {
            rows.Add(ReadRow(connection, reader));
        }

        return rows;
    }

    private static InspectionBatchPlanRowDto ReadRow(SqliteConnection connection, SqliteDataReader reader)
    {
        var planRowId = reader.GetString(0);
        var capacities = ListCapacities(connection, planRowId);
        return new InspectionBatchPlanRowDto(
            planRowId,
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? "" : reader.GetString(4),
            reader.IsDBNull(5) ? "" : reader.GetString(5),
            reader.IsDBNull(6) ? "" : reader.GetString(6),
            reader.IsDBNull(7) ? "" : reader.GetString(7),
            reader.IsDBNull(8) ? "" : reader.GetString(8),
            reader.IsDBNull(9) ? "" : reader.GetString(9),
            reader.IsDBNull(10) ? "" : reader.GetString(10),
            reader.IsDBNull(11) ? 0 : reader.GetInt64(11),
            reader.IsDBNull(12) ? "" : reader.GetString(12),
            reader.IsDBNull(13) ? "" : reader.GetString(13),
            reader.IsDBNull(14) ? "" : reader.GetString(14),
            reader.IsDBNull(15) ? "" : reader.GetString(15),
            reader.IsDBNull(16) ? "" : reader.GetString(16),
            reader.IsDBNull(17) ? "Active" : reader.GetString(17),
            reader.IsDBNull(18) ? "None" : reader.GetString(18),
            null,
            DateTimeOffset.Parse(reader.GetString(19)),
            DateTimeOffset.Parse(reader.GetString(20)),
            reader.IsDBNull(21) ? null : DateTimeOffset.Parse(reader.GetString(21)),
            capacities);
    }

    private static IReadOnlyList<PlanRowCapacityDto> ListCapacities(SqliteConnection connection, string planRowId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CapacityId, PlanRowId, CapacityKey, CapacityName, Value, Unit, SortOrder, CreatedTime, UpdatedTime
            FROM PlanRowCapacity
            WHERE PlanRowId = $planRowId
            ORDER BY SortOrder, CapacityKey;
            """;
        command.Parameters.AddWithValue("$planRowId", planRowId);
        using var reader = command.ExecuteReader();
        var items = new List<PlanRowCapacityDto>();
        while (reader.Read())
        {
            items.Add(new PlanRowCapacityDto(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                DateTimeOffset.Parse(reader.GetString(7)),
                DateTimeOffset.Parse(reader.GetString(8))));
        }

        return items;
    }

    private static InspectionBatchPlanDto BuildPlan(SqliteDataReader reader, IReadOnlyList<InspectionBatchPlanRowDto> rows)
    {
        return new InspectionBatchPlanDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? "" : reader.GetString(4),
            reader.IsDBNull(5) ? "Active" : reader.GetString(5),
            rows.Count(row => !string.Equals(row.Status, "Deleted", StringComparison.OrdinalIgnoreCase)),
            rows.Count(row =>
                !string.Equals(row.Status, "Deleted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.GenerateStatus, "Generated", StringComparison.OrdinalIgnoreCase)),
            DateTimeOffset.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
            rows);
    }

    private static IReadOnlyList<PlanRowCapacityDto> NormalizeCapacities(string rowId, IReadOnlyList<PlanRowCapacitySaveItem>? items, DateTimeOffset now)
    {
        if (items is null)
        {
            return [];
        }

        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.CapacityKey) || !string.IsNullOrWhiteSpace(item.CapacityName))
            .Select((item, index) => new PlanRowCapacityDto(
                string.IsNullOrWhiteSpace(item.CapacityId) ? $"plan-row-capacity:{Guid.NewGuid():N}" : item.CapacityId.Trim(),
                rowId,
                item.CapacityKey?.Trim() ?? $"capacity_{index + 1}",
                string.IsNullOrWhiteSpace(item.CapacityName) ? item.CapacityKey?.Trim() ?? $"容量{index + 1}" : item.CapacityName.Trim(),
                item.Value?.Trim() ?? "",
                item.Unit?.Trim() ?? "",
                item.SortOrder ?? ((index + 1) * 10),
                now,
                now))
            .ToArray();
    }

    private static string BuildCapacitySummary(IReadOnlyList<PlanRowCapacityDto> capacities)
    {
        var parts = capacities
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CapacityName, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var name = string.IsNullOrWhiteSpace(item.CapacityName) ? item.CapacityKey : item.CapacityName;
                return $"{name}{item.Value}{item.Unit}";
            })
            .ToArray();
        return string.Join("；", parts);
    }

    private static string NormalizeLifecycleStatus(string? status)
    {
        return string.Equals(status, "Deleted", StringComparison.OrdinalIgnoreCase) ? "Deleted" : "Active";
    }

    private static string NormalizeGenerateStatus(string? requestedStatus, string? fallbackStatus = null)
    {
        var value = string.IsNullOrWhiteSpace(requestedStatus) ? fallbackStatus : requestedStatus;
        return value switch
        {
            "Generated" => "Generated",
            "Failed" => "Failed",
            "NeedSync" => "NeedSync",
            _ => "None"
        };
    }

    private SqliteConnection OpenConnection()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        return connection;
    }
}
