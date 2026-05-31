using System.Text.Json;
using GeneratorService.Models;
using GeneratorService.Modules;
using Microsoft.Data.Sqlite;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateAdaptationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly string[] BootstrapKeywords =
    [
        "火灾自动报警系统",
        "自动喷水灭火系统",
        "消火栓系统",
        "应急照明",
        "防排烟系统",
        "气体灭火系统",
        "消防电源监控系统",
        "电气火灾监控系统"
    ];

    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;

    public TemplateAdaptationRepository(DirectoryInfo rootPath, AppConfig config)
    {
        _rootPath = rootPath;
        _config = config;
    }

    public void EnsureCreated()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TemplateProfile (
              ModuleId TEXT NOT NULL,
              ModuleVersion TEXT NOT NULL,
              TemplateItemId INTEGER NOT NULL,
              TemplateCode TEXT NOT NULL DEFAULT '',
              TemplateName TEXT NOT NULL,
              TemplateFile TEXT NOT NULL,
              MappingMode TEXT NOT NULL,
              Status TEXT NOT NULL,
              LastValidatedAt TEXT NULL,
              LastTestedAt TEXT NULL,
              LastTestStatus TEXT NULL,
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL,
              PRIMARY KEY (ModuleId, ModuleVersion, TemplateItemId)
            );

            CREATE TABLE IF NOT EXISTS TemplateFieldMapping (
              ModuleId TEXT NOT NULL,
              ModuleVersion TEXT NOT NULL,
              TemplateItemId INTEGER NOT NULL,
              FieldKey TEXT NOT NULL,
              Mode TEXT NOT NULL,
              PlaceholderTokensJson TEXT NOT NULL DEFAULT '[]',
              TargetsJson TEXT NOT NULL DEFAULT '[]',
              IsRequired INTEGER NOT NULL DEFAULT 1,
              IsEnabled INTEGER NOT NULL DEFAULT 1,
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL,
              PRIMARY KEY (ModuleId, ModuleVersion, TemplateItemId, FieldKey)
            );

            CREATE TABLE IF NOT EXISTS TemplateTestRecord (
              Id TEXT PRIMARY KEY,
              ModuleId TEXT NOT NULL,
              ModuleVersion TEXT NOT NULL,
              TemplateItemId INTEGER NOT NULL,
              TemplateNodeId TEXT NOT NULL,
              TemplateName TEXT NOT NULL,
              ResultJson TEXT NOT NULL,
              ResultStatus TEXT NOT NULL,
              ReportPath TEXT NOT NULL DEFAULT '',
              CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_template_profile_status
              ON TemplateProfile(ModuleId, ModuleVersion, Status, UpdatedAt);
            CREATE INDEX IF NOT EXISTS idx_template_test_record_template
              ON TemplateTestRecord(ModuleId, ModuleVersion, TemplateItemId, CreatedAt);
            """;
        command.ExecuteNonQuery();
    }

    public void EnsureBootstrapProfiles(ModuleManager moduleManager)
    {
        foreach (var module in moduleManager.GetValidModules())
        {
            if (module.Manifest is null ||
                !string.Equals(module.Manifest.ModuleId, "gd_installation_2024", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(module.RulesDbPath) ||
                string.IsNullOrWhiteSpace(module.TemplateRootPath))
            {
                continue;
            }

            foreach (var item in ListModuleTemplates(module))
            {
                var existingProfile = GetProfile(item.ModuleId, item.ModuleVersion, item.TemplateItemId);
                if (!ShouldBootstrap(item.TemplateName, item.TemplateFile) ||
                    (existingProfile is not null && !CanAutoUpgrade(existingProfile)))
                {
                    continue;
                }

                var fullTemplatePath = Path.GetFullPath(Path.Combine(module.TemplateRootPath!, item.TemplateFile));
                if (!File.Exists(fullTemplatePath))
                {
                    continue;
                }

                var workbook = TemplateWorkbookHelper.Inspect(fullTemplatePath);
                var adjacentTargets = TemplateWorkbookHelper.FindAdjacentTargets(fullTemplatePath, TemplateAdaptationFields.LabelAliases);
                var mappings = EnsureUniqueTargets(TemplateAdaptationFields.RequiredFieldKeys
                    .Select(fieldKey =>
                    {
                        var placeholderTokens = workbook.Placeholders.Any(item => string.Equals(item, fieldKey, StringComparison.OrdinalIgnoreCase))
                            ? new[] { fieldKey }
                            : Array.Empty<string>();
                        adjacentTargets.TryGetValue(fieldKey, out var targets);
                        targets ??= [];
                        var mode = placeholderTokens.Length > 0 && targets.Count > 0
                            ? "Hybrid"
                            : placeholderTokens.Length > 0
                                ? "Placeholder"
                                : "Cell";
                        return new TemplateFieldMappingInfo(
                            fieldKey,
                            mode,
                            placeholderTokens,
                            targets,
                            true,
                            placeholderTokens.Length > 0 || targets.Count > 0);
                    })
                    .ToArray());

                var effectiveMappings = existingProfile is null
                    ? mappings
                    : MergeBootstrapMappings(
                        ListMappings(item.ModuleId, item.ModuleVersion, item.TemplateItemId),
                        mappings);
                SaveAdaptation(
                    item.ToResolution(fullTemplatePath),
                    ResolveProfileMappingMode(effectiveMappings),
                    effectiveMappings);
            }
        }
    }

    public TemplateProfileInfo? GetProfile(string moduleId, string moduleVersion, long templateItemId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ModuleId, ModuleVersion, TemplateItemId, TemplateCode, TemplateName, TemplateFile,
                   MappingMode, Status, LastValidatedAt, LastTestedAt, LastTestStatus, CreatedAt, UpdatedAt
            FROM TemplateProfile
            WHERE ModuleId = $moduleId AND ModuleVersion = $moduleVersion AND TemplateItemId = $templateItemId;
            """;
        command.Parameters.AddWithValue("$moduleId", moduleId);
        command.Parameters.AddWithValue("$moduleVersion", moduleVersion);
        command.Parameters.AddWithValue("$templateItemId", templateItemId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProfile(reader, BuildTemplateNodeId(moduleId, templateItemId)) : null;
    }

    public IReadOnlyList<TemplateFieldMappingInfo> ListMappings(string moduleId, string moduleVersion, long templateItemId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FieldKey, Mode, PlaceholderTokensJson, TargetsJson, IsRequired, IsEnabled
            FROM TemplateFieldMapping
            WHERE ModuleId = $moduleId AND ModuleVersion = $moduleVersion AND TemplateItemId = $templateItemId
            ORDER BY FieldKey;
            """;
        command.Parameters.AddWithValue("$moduleId", moduleId);
        command.Parameters.AddWithValue("$moduleVersion", moduleVersion);
        command.Parameters.AddWithValue("$templateItemId", templateItemId);
        using var reader = command.ExecuteReader();
        var mappings = new List<TemplateFieldMappingInfo>();
        while (reader.Read())
        {
            mappings.Add(ReadMapping(reader));
        }

        return mappings;
    }

    public TemplateAdaptationDetailResult GetAdaptationDetail(TemplateResolution template)
    {
        var profile = GetProfile(template.ModuleId, template.ModuleVersion, template.TemplateItemId)
            ?? BuildDefaultProfile(template);
        var mappings = ListMappings(template.ModuleId, template.ModuleVersion, template.TemplateItemId);
        if (mappings.Count == 0)
        {
            mappings = TemplateAdaptationFields.RequiredFieldKeys
                .Select(fieldKey => new TemplateFieldMappingInfo(fieldKey, "Cell", [], [], true, false))
                .ToArray();
        }

        return new TemplateAdaptationDetailResult(
            true,
            template.TemplateNodeId,
            profile,
            mappings,
            TemplateAdaptationFields.RequiredFieldKeys);
    }

    public TemplateAdaptationDetailResult SaveAdaptation(
        TemplateResolution template,
        string mappingMode,
        IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now;
        var currentProfile = GetProfile(template.ModuleId, template.ModuleVersion, template.TemplateItemId);
        var createdAt = currentProfile?.CreatedAt ?? now;
        var status = ResolveStatus(mappings);

        using (var upsertProfile = connection.CreateCommand())
        {
            upsertProfile.Transaction = transaction;
            upsertProfile.CommandText = """
                INSERT INTO TemplateProfile (
                    ModuleId, ModuleVersion, TemplateItemId, TemplateCode, TemplateName, TemplateFile,
                    MappingMode, Status, LastValidatedAt, LastTestedAt, LastTestStatus, CreatedAt, UpdatedAt)
                VALUES (
                    $moduleId, $moduleVersion, $templateItemId, $templateCode, $templateName, $templateFile,
                    $mappingMode, $status, NULL, NULL, NULL, $createdAt, $updatedAt)
                ON CONFLICT(ModuleId, ModuleVersion, TemplateItemId) DO UPDATE SET
                    TemplateCode = excluded.TemplateCode,
                    TemplateName = excluded.TemplateName,
                    TemplateFile = excluded.TemplateFile,
                    MappingMode = excluded.MappingMode,
                    Status = excluded.Status,
                    UpdatedAt = excluded.UpdatedAt;
                """;
            upsertProfile.Parameters.AddWithValue("$moduleId", template.ModuleId);
            upsertProfile.Parameters.AddWithValue("$moduleVersion", template.ModuleVersion);
            upsertProfile.Parameters.AddWithValue("$templateItemId", template.TemplateItemId);
            upsertProfile.Parameters.AddWithValue("$templateCode", template.TemplateCode);
            upsertProfile.Parameters.AddWithValue("$templateName", template.TemplateName);
            upsertProfile.Parameters.AddWithValue("$templateFile", template.TemplateFile);
            upsertProfile.Parameters.AddWithValue("$mappingMode", mappingMode);
            upsertProfile.Parameters.AddWithValue("$status", status);
            upsertProfile.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
            upsertProfile.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            upsertProfile.ExecuteNonQuery();
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM TemplateFieldMapping
                WHERE ModuleId = $moduleId AND ModuleVersion = $moduleVersion AND TemplateItemId = $templateItemId;
                """;
            delete.Parameters.AddWithValue("$moduleId", template.ModuleId);
            delete.Parameters.AddWithValue("$moduleVersion", template.ModuleVersion);
            delete.Parameters.AddWithValue("$templateItemId", template.TemplateItemId);
            delete.ExecuteNonQuery();
        }

        foreach (var mapping in mappings)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO TemplateFieldMapping (
                    ModuleId, ModuleVersion, TemplateItemId, FieldKey, Mode, PlaceholderTokensJson,
                    TargetsJson, IsRequired, IsEnabled, CreatedAt, UpdatedAt)
                VALUES (
                    $moduleId, $moduleVersion, $templateItemId, $fieldKey, $mode, $placeholderTokensJson,
                    $targetsJson, $isRequired, $isEnabled, $createdAt, $updatedAt);
                """;
            insert.Parameters.AddWithValue("$moduleId", template.ModuleId);
            insert.Parameters.AddWithValue("$moduleVersion", template.ModuleVersion);
            insert.Parameters.AddWithValue("$templateItemId", template.TemplateItemId);
            insert.Parameters.AddWithValue("$fieldKey", mapping.FieldKey);
            insert.Parameters.AddWithValue("$mode", NormalizeMode(mapping.Mode));
            insert.Parameters.AddWithValue("$placeholderTokensJson", JsonSerializer.Serialize(mapping.PlaceholderTokens, JsonOptions));
            insert.Parameters.AddWithValue("$targetsJson", JsonSerializer.Serialize(mapping.Targets, JsonOptions));
            insert.Parameters.AddWithValue("$isRequired", mapping.IsRequired ? 1 : 0);
            insert.Parameters.AddWithValue("$isEnabled", mapping.IsEnabled ? 1 : 0);
            insert.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            insert.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return GetAdaptationDetail(template);
    }

    public void RecordValidation(TemplateResolution template, TemplateValidationResult result)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TemplateProfile
            SET Status = $status,
                LastValidatedAt = $lastValidatedAt,
                UpdatedAt = $updatedAt
            WHERE ModuleId = $moduleId AND ModuleVersion = $moduleVersion AND TemplateItemId = $templateItemId;
            """;
        command.Parameters.AddWithValue("$status", result.Status);
        command.Parameters.AddWithValue("$lastValidatedAt", result.ValidatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue("$moduleId", template.ModuleId);
        command.Parameters.AddWithValue("$moduleVersion", template.ModuleVersion);
        command.Parameters.AddWithValue("$templateItemId", template.TemplateItemId);
        command.ExecuteNonQuery();
    }

    public void RecordTestResult(TemplateResolution template, TemplateTestResult result, string reportPath)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now;

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE TemplateProfile
                SET LastTestedAt = $lastTestedAt,
                    LastTestStatus = $lastTestStatus,
                    Status = $status,
                    UpdatedAt = $updatedAt
                WHERE ModuleId = $moduleId AND ModuleVersion = $moduleVersion AND TemplateItemId = $templateItemId;
                """;
            update.Parameters.AddWithValue("$lastTestedAt", result.TestedAt.ToString("O"));
            update.Parameters.AddWithValue("$lastTestStatus", result.Result);
            update.Parameters.AddWithValue("$status", result.Success ? "ready" : "error");
            update.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            update.Parameters.AddWithValue("$moduleId", template.ModuleId);
            update.Parameters.AddWithValue("$moduleVersion", template.ModuleVersion);
            update.Parameters.AddWithValue("$templateItemId", template.TemplateItemId);
            update.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO TemplateTestRecord (
                    Id, ModuleId, ModuleVersion, TemplateItemId, TemplateNodeId, TemplateName,
                    ResultJson, ResultStatus, ReportPath, CreatedAt)
                VALUES (
                    $id, $moduleId, $moduleVersion, $templateItemId, $templateNodeId, $templateName,
                    $resultJson, $resultStatus, $reportPath, $createdAt);
                """;
            insert.Parameters.AddWithValue("$id", $"template-test:{Guid.NewGuid():N}");
            insert.Parameters.AddWithValue("$moduleId", template.ModuleId);
            insert.Parameters.AddWithValue("$moduleVersion", template.ModuleVersion);
            insert.Parameters.AddWithValue("$templateItemId", template.TemplateItemId);
            insert.Parameters.AddWithValue("$templateNodeId", template.TemplateNodeId);
            insert.Parameters.AddWithValue("$templateName", template.TemplateName);
            insert.Parameters.AddWithValue("$resultJson", JsonSerializer.Serialize(result, JsonOptions));
            insert.Parameters.AddWithValue("$resultStatus", result.Result);
            insert.Parameters.AddWithValue("$reportPath", reportPath);
            insert.Parameters.AddWithValue("$createdAt", result.TestedAt.ToString("O"));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<TemplateProfileInfo> ListProfiles(string moduleId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ModuleId, ModuleVersion, TemplateItemId, TemplateCode, TemplateName, TemplateFile,
                   MappingMode, Status, LastValidatedAt, LastTestedAt, LastTestStatus, CreatedAt, UpdatedAt
            FROM TemplateProfile
            WHERE ModuleId = $moduleId
            ORDER BY TemplateName;
            """;
        command.Parameters.AddWithValue("$moduleId", moduleId);
        using var reader = command.ExecuteReader();
        var profiles = new List<TemplateProfileInfo>();
        while (reader.Read())
        {
            profiles.Add(ReadProfile(reader, BuildTemplateNodeId(moduleId, reader.GetInt64(2))));
        }

        return profiles;
    }

    private static string NormalizeMode(string mode)
    {
        return mode switch
        {
            "Placeholder" or "Cell" or "Hybrid" => mode,
            _ => "Cell"
        };
    }

    private static string ResolveProfileMappingMode(IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var hasPlaceholder = mappings.Any(item =>
            item.IsEnabled &&
            (string.Equals(item.Mode, "Placeholder", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Mode, "Hybrid", StringComparison.OrdinalIgnoreCase)) &&
            item.PlaceholderTokens.Count > 0);
        var hasCell = mappings.Any(item =>
            item.IsEnabled &&
            (string.Equals(item.Mode, "Cell", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Mode, "Hybrid", StringComparison.OrdinalIgnoreCase)) &&
            item.Targets.Count > 0);

        return hasPlaceholder && hasCell
            ? "Hybrid"
            : hasPlaceholder
                ? "Placeholder"
                : "Cell";
    }

    private static string ResolveStatus(IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var missing = mappings
            .Where(item => item.IsRequired)
            .Where(item =>
                !item.IsEnabled ||
                (string.Equals(item.Mode, "Placeholder", StringComparison.OrdinalIgnoreCase) && item.PlaceholderTokens.Count == 0) ||
                (string.Equals(item.Mode, "Cell", StringComparison.OrdinalIgnoreCase) && item.Targets.Count == 0) ||
                (string.Equals(item.Mode, "Hybrid", StringComparison.OrdinalIgnoreCase) && item.PlaceholderTokens.Count == 0 && item.Targets.Count == 0))
            .ToArray();
        return missing.Length == 0 ? "configured" : "incomplete";
    }

    private static bool CanAutoUpgrade(TemplateProfileInfo profile)
    {
        return string.Equals(profile.Status, "incomplete", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<TemplateFieldMappingInfo> MergeBootstrapMappings(
        IReadOnlyList<TemplateFieldMappingInfo> existingMappings,
        IReadOnlyList<TemplateFieldMappingInfo> generatedMappings)
    {
        var existingByField = existingMappings.ToDictionary(item => item.FieldKey, StringComparer.OrdinalIgnoreCase);
        return generatedMappings
            .Select(generated =>
            {
                if (!existingByField.TryGetValue(generated.FieldKey, out var existing))
                {
                    return generated;
                }

                var existingHasContent = existing.PlaceholderTokens.Count > 0 || existing.Targets.Count > 0;
                if (existing.IsEnabled && existingHasContent)
                {
                    return existing;
                }

                return generated;
            })
            .ToArray();
    }

    private static IReadOnlyList<TemplateFieldMappingInfo> EnsureUniqueTargets(
        IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TemplateFieldMappingInfo>(mappings.Count);

        foreach (var fieldKey in TemplateAdaptationFields.RequiredFieldKeys)
        {
            var mapping = mappings.FirstOrDefault(item => string.Equals(item.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));
            if (mapping is null)
            {
                continue;
            }

            var uniqueTargets = mapping.Targets
                .Where(target =>
                {
                    var key = BuildTargetKey(target);
                    if (usedTargets.Contains(key))
                    {
                        return false;
                    }

                    usedTargets.Add(key);
                    return true;
                })
                .ToArray();

            result.Add(mapping with
            {
                Targets = uniqueTargets,
                IsEnabled = mapping.PlaceholderTokens.Count > 0 || uniqueTargets.Length > 0
            });
        }

        return result;
    }

    private static bool ShouldBootstrap(string templateName, string templateFile)
    {
        return !string.IsNullOrWhiteSpace(templateName) || !string.IsNullOrWhiteSpace(templateFile);
    }

    private static string BuildTargetKey(TemplateFieldTarget target)
    {
        return $"{target.WorksheetName ?? string.Empty}!{target.CellReference}";
    }

    private static string BuildTemplateNodeId(string moduleId, long templateItemId)
    {
        return $"module:{moduleId}:template:{templateItemId}";
    }

    private TemplateProfileInfo BuildDefaultProfile(TemplateResolution template)
    {
        var now = DateTimeOffset.Now;
        return new TemplateProfileInfo(
            template.TemplateNodeId,
            template.ModuleId,
            template.ModuleVersion,
            template.TemplateItemId,
            template.TemplateCode,
            template.TemplateName,
            template.TemplateFile,
            "Cell",
            "unconfigured",
            null,
            null,
            null,
            now,
            now);
    }

    private static TemplateProfileInfo ReadProfile(SqliteDataReader reader, string templateNodeId)
    {
        return new TemplateProfileInfo(
            templateNodeId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
            reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }

    private static TemplateFieldMappingInfo ReadMapping(SqliteDataReader reader)
    {
        return new TemplateFieldMappingInfo(
            reader.GetString(0),
            reader.GetString(1),
            ParsePlaceholderTokens(reader.GetString(2)),
            ParseTargets(reader.GetString(3)),
            !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
            !reader.IsDBNull(5) && reader.GetInt32(5) != 0);
    }

    private static IReadOnlyList<string> ParsePlaceholderTokens(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<TemplateFieldTarget> ParseTargets(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<TemplateFieldTarget>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<ModuleTemplateSeedInfo> ListModuleTemplates(ModulePackageInfo module)
    {
        using var connection = new SqliteConnection($"Data Source={module.RulesDbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TemplateName, TemplateCode, TemplateFile, TemplateType
            FROM TemplateItem
            WHERE COALESCE(IsEnabled, 1) <> 0
            ORDER BY SortOrder, Id;
            """;
        using var reader = command.ExecuteReader();
        var items = new List<ModuleTemplateSeedInfo>();
        while (reader.Read())
        {
            items.Add(new ModuleTemplateSeedInfo(
                module.Manifest!.ModuleId,
                module.Manifest.Version,
                reader.GetInt64(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4)));
        }

        return items;
    }

    private SqliteConnection OpenConnection()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        return connection;
    }

    private sealed record ModuleTemplateSeedInfo(
        string ModuleId,
        string ModuleVersion,
        long TemplateItemId,
        string TemplateName,
        string TemplateCode,
        string TemplateFile,
        string TemplateType)
    {
        public TemplateResolution ToResolution(string templatePath)
        {
            return new TemplateResolution(
                $"module:{ModuleId}:template:{TemplateItemId}",
                ModuleId,
                ModuleVersion,
                TemplateItemId,
                TemplateName,
                string.IsNullOrWhiteSpace(TemplateCode) ? "template" : TemplateCode,
                TemplateType,
                TemplateFile,
                templatePath);
        }
    }
}
