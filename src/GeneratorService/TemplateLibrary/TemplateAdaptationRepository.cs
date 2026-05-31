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
              DisplayName TEXT NULL,
              Mode TEXT NOT NULL,
              PlaceholderTokensJson TEXT NOT NULL DEFAULT '[]',
              TargetsJson TEXT NOT NULL DEFAULT '[]',
              ValueSource TEXT NOT NULL DEFAULT 'BusinessData',
              DefaultValue TEXT NOT NULL DEFAULT '',
              SortOrder INTEGER NOT NULL DEFAULT 0,
              IsSystemField INTEGER NOT NULL DEFAULT 0,
              Description TEXT NULL,
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

        EnsureColumnExists(connection, "TemplateFieldMapping", "DisplayName TEXT NULL");
        EnsureColumnExists(connection, "TemplateFieldMapping", "ValueSource TEXT NOT NULL DEFAULT 'BusinessData'");
        EnsureColumnExists(connection, "TemplateFieldMapping", "DefaultValue TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, "TemplateFieldMapping", "SortOrder INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "TemplateFieldMapping", "IsSystemField INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "TemplateFieldMapping", "Description TEXT NULL");

        using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_template_mapping_sort
              ON TemplateFieldMapping(ModuleId, ModuleVersion, TemplateItemId, SortOrder, FieldKey);
            """;
        indexCommand.ExecuteNonQuery();

        BackfillMappingMetadata(connection);
    }

    public IReadOnlyList<TemplateResolution> ListTemplateResolutions(ModuleManager moduleManager)
    {
        return moduleManager.ListTemplates()
            .Select(template => new TemplateResolution(
                BuildTemplateNodeId(template.ModuleId, template.TemplateItemId),
                template.ModuleId,
                template.ModuleVersion,
                template.TemplateItemId,
                template.TemplateName,
                string.IsNullOrWhiteSpace(template.TemplateCode) ? "template" : template.TemplateCode,
                template.TemplateType,
                template.TemplateFile,
                template.TemplatePath))
            .OrderBy(item => item.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TemplateName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void EnsureBootstrapProfiles(ModuleManager moduleManager)
    {
        foreach (var module in moduleManager.GetValidModules())
        {
            if (module.Manifest is null ||
                !TemplateAdaptationFields.IsManagedModule(module.Manifest.ModuleId) ||
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
                var generatedMappings = EnsureUniqueTargets(TemplateAdaptationFields.RequiredFieldKeys
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
                        return CreateMapping(
                            fieldKey,
                            TemplateAdaptationFields.GetDisplayName(fieldKey),
                            mode,
                            placeholderTokens,
                            targets,
                            TemplateAdaptationFields.ValueSourceBusinessData,
                            "",
                            TemplateAdaptationFields.GetDefaultSortOrder(fieldKey),
                            true,
                            null,
                            true,
                            placeholderTokens.Length > 0 || targets.Count > 0);
                    })
                    .ToArray());

                var effectiveMappings = existingProfile is null
                    ? generatedMappings
                    : MergeBootstrapMappings(
                        ListMappings(item.ModuleId, item.ModuleVersion, item.TemplateItemId),
                        generatedMappings);
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
            SELECT FieldKey, DisplayName, Mode, PlaceholderTokensJson, TargetsJson,
                   ValueSource, DefaultValue, SortOrder, IsSystemField, Description,
                   IsRequired, IsEnabled
            FROM TemplateFieldMapping
            WHERE ModuleId = $moduleId AND ModuleVersion = $moduleVersion AND TemplateItemId = $templateItemId
            ORDER BY SortOrder, FieldKey;
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
        return BuildDetail(template, profile, mappings);
    }

    public TemplateAdaptationDetailResult SaveAdaptation(
        TemplateResolution template,
        string mappingMode,
        IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var normalizedMappings = EnsureSystemMappings(mappings)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now;
        var currentProfile = GetProfile(template.ModuleId, template.ModuleVersion, template.TemplateItemId);
        var createdAt = currentProfile?.CreatedAt ?? now;
        var status = ResolveStoredStatus(normalizedMappings);

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

        foreach (var mapping in normalizedMappings)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO TemplateFieldMapping (
                    ModuleId, ModuleVersion, TemplateItemId, FieldKey, DisplayName, Mode,
                    PlaceholderTokensJson, TargetsJson, ValueSource, DefaultValue, SortOrder,
                    IsSystemField, Description, IsRequired, IsEnabled, CreatedAt, UpdatedAt)
                VALUES (
                    $moduleId, $moduleVersion, $templateItemId, $fieldKey, $displayName, $mode,
                    $placeholderTokensJson, $targetsJson, $valueSource, $defaultValue, $sortOrder,
                    $isSystemField, $description, $isRequired, $isEnabled, $createdAt, $updatedAt);
                """;
            insert.Parameters.AddWithValue("$moduleId", template.ModuleId);
            insert.Parameters.AddWithValue("$moduleVersion", template.ModuleVersion);
            insert.Parameters.AddWithValue("$templateItemId", template.TemplateItemId);
            insert.Parameters.AddWithValue("$fieldKey", mapping.FieldKey);
            insert.Parameters.AddWithValue("$displayName", mapping.DisplayName);
            insert.Parameters.AddWithValue("$mode", NormalizeMode(mapping.Mode));
            insert.Parameters.AddWithValue("$placeholderTokensJson", JsonSerializer.Serialize(mapping.PlaceholderTokens, JsonOptions));
            insert.Parameters.AddWithValue("$targetsJson", JsonSerializer.Serialize(mapping.Targets, JsonOptions));
            insert.Parameters.AddWithValue("$valueSource", NormalizeValueSource(mapping.ValueSource));
            insert.Parameters.AddWithValue("$defaultValue", mapping.DefaultValue ?? "");
            insert.Parameters.AddWithValue("$sortOrder", mapping.SortOrder);
            insert.Parameters.AddWithValue("$isSystemField", mapping.IsSystemField ? 1 : 0);
            insert.Parameters.AddWithValue("$description", string.IsNullOrWhiteSpace(mapping.Description) ? DBNull.Value : mapping.Description);
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

    private TemplateAdaptationDetailResult BuildDetail(
        TemplateResolution template,
        TemplateProfileInfo profile,
        IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var effectiveMappings = EnsureSystemMappings(mappings)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingRequiredFields = GetMissingRequiredFieldKeys(effectiveMappings);
        var moduleStatus = TemplateAdaptationFields.ResolveModuleStatus(template.ModuleId);
        var adaptationStatus = ResolveAdaptationStatus(profile.Status, effectiveMappings, missingRequiredFields, profile.LastTestStatus, moduleStatus);
        var canEdit = string.Equals(moduleStatus, TemplateAdaptationFields.ModuleStatusManaged, StringComparison.OrdinalIgnoreCase);

        return new TemplateAdaptationDetailResult(
            true,
            template.TemplateNodeId,
            profile,
            effectiveMappings,
            TemplateAdaptationFields.RequiredFieldKeys,
            missingRequiredFields,
            moduleStatus,
            adaptationStatus,
            canEdit,
            canEdit);
    }

    private static string NormalizeMode(string mode)
    {
        return mode switch
        {
            "Placeholder" or "Cell" or "Hybrid" => mode,
            _ => "Cell"
        };
    }

    private static string NormalizeValueSource(string? valueSource)
    {
        return valueSource switch
        {
            TemplateAdaptationFields.ValueSourceDefaultValue => TemplateAdaptationFields.ValueSourceDefaultValue,
            _ => TemplateAdaptationFields.ValueSourceBusinessData
        };
    }

    public static string ResolveProfileMappingMode(IReadOnlyList<TemplateFieldMappingInfo> mappings)
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

    private static string ResolveStoredStatus(IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var enabledMappings = mappings.Where(item => item.IsEnabled).ToArray();
        if (enabledMappings.Length == 0)
        {
            return "unconfigured";
        }

        return GetMissingRequiredFieldKeys(mappings).Count == 0 ? "configured" : "incomplete";
    }

    public static IReadOnlyList<string> GetMissingRequiredFieldKeys(IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        return mappings
            .Where(item => item.IsRequired)
            .Where(item =>
                !item.IsEnabled ||
                (string.Equals(item.Mode, "Placeholder", StringComparison.OrdinalIgnoreCase) && item.PlaceholderTokens.Count == 0) ||
                (string.Equals(item.Mode, "Cell", StringComparison.OrdinalIgnoreCase) && item.Targets.Count == 0) ||
                (string.Equals(item.Mode, "Hybrid", StringComparison.OrdinalIgnoreCase) && item.PlaceholderTokens.Count == 0 && item.Targets.Count == 0))
            .Select(item => item.FieldKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveAdaptationStatus(
        string rawStatus,
        IReadOnlyList<TemplateFieldMappingInfo> mappings,
        IReadOnlyList<string> missingRequiredFields,
        string? lastTestStatus,
        string moduleStatus)
    {
        if (string.Equals(rawStatus, "error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(lastTestStatus, "FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return TemplateAdaptationFields.AdaptationStatusError;
        }

        var enabledMappings = mappings.Count(item => item.IsEnabled);
        if (enabledMappings == 0)
        {
            return TemplateAdaptationFields.AdaptationStatusUnconfigured;
        }

        if (missingRequiredFields.Count > 0)
        {
            return TemplateAdaptationFields.AdaptationStatusPartial;
        }

        return string.Equals(moduleStatus, TemplateAdaptationFields.ModuleStatusManaged, StringComparison.OrdinalIgnoreCase)
            ? TemplateAdaptationFields.AdaptationStatusCompleted
            : TemplateAdaptationFields.AdaptationStatusCompleted;
    }

    private static bool CanAutoUpgrade(TemplateProfileInfo profile)
    {
        return string.Equals(profile.Status, "incomplete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(profile.Status, "unconfigured", StringComparison.OrdinalIgnoreCase);
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

                return generated with
                {
                    DefaultValue = existing.DefaultValue,
                    Description = existing.Description
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<TemplateFieldMappingInfo> EnsureUniqueTargets(
        IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TemplateFieldMappingInfo>(mappings.Count);

        foreach (var mapping in mappings.OrderBy(item => item.SortOrder).ThenBy(item => item.FieldKey, StringComparer.OrdinalIgnoreCase))
        {
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
                IsEnabled = mapping.PlaceholderTokens.Count > 0 || uniqueTargets.Length > 0 || mapping.IsEnabled,
                PrimaryCellReference = BuildPrimaryCellReference(uniqueTargets),
                TargetCount = uniqueTargets.Length
            });
        }

        return result;
    }

    private static IReadOnlyList<TemplateFieldMappingInfo> EnsureSystemMappings(IReadOnlyList<TemplateFieldMappingInfo> mappings)
    {
        var normalized = mappings
            .Select(mapping => mapping with
            {
                DisplayName = string.IsNullOrWhiteSpace(mapping.DisplayName)
                    ? TemplateAdaptationFields.GetDisplayName(mapping.FieldKey)
                    : mapping.DisplayName,
                ValueSource = NormalizeValueSource(mapping.ValueSource),
                SortOrder = mapping.SortOrder == 0
                    ? TemplateAdaptationFields.GetDefaultSortOrder(mapping.FieldKey)
                    : mapping.SortOrder,
                IsSystemField = mapping.IsSystemField || TemplateAdaptationFields.IsSystemField(mapping.FieldKey),
                PrimaryCellReference = BuildPrimaryCellReference(mapping.Targets),
                TargetCount = mapping.Targets.Count
            })
            .ToDictionary(item => item.FieldKey, StringComparer.OrdinalIgnoreCase);

        foreach (var fieldKey in TemplateAdaptationFields.RequiredFieldKeys)
        {
            if (!normalized.ContainsKey(fieldKey))
            {
                normalized[fieldKey] = TemplateAdaptationFields.CreateDefaultMapping(fieldKey);
            }
        }

        return normalized.Values.ToArray();
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

    private static string? BuildPrimaryCellReference(IReadOnlyList<TemplateFieldTarget> targets)
    {
        var target = targets.FirstOrDefault();
        if (target is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(target.WorksheetName)
            ? target.CellReference
            : $"{target.WorksheetName}!{target.CellReference}";
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
        var fieldKey = reader.GetString(0);
        var displayName = reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1))
            ? TemplateAdaptationFields.GetDisplayName(fieldKey)
            : reader.GetString(1);
        var targets = ParseTargets(reader.GetString(4));

        return CreateMapping(
            fieldKey,
            displayName,
            reader.GetString(2),
            ParsePlaceholderTokens(reader.GetString(3)),
            targets,
            reader.IsDBNull(5) ? TemplateAdaptationFields.GetDefaultValueSource(fieldKey) : NormalizeValueSource(reader.GetString(5)),
            reader.IsDBNull(6) ? "" : reader.GetString(6),
            reader.IsDBNull(7) ? TemplateAdaptationFields.GetDefaultSortOrder(fieldKey) : reader.GetInt32(7),
            !reader.IsDBNull(8) && reader.GetInt32(8) != 0 || TemplateAdaptationFields.IsSystemField(fieldKey),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            !reader.IsDBNull(10) && reader.GetInt32(10) != 0,
            !reader.IsDBNull(11) && reader.GetInt32(11) != 0);
    }

    private static TemplateFieldMappingInfo CreateMapping(
        string fieldKey,
        string displayName,
        string mode,
        IReadOnlyList<string> placeholderTokens,
        IReadOnlyList<TemplateFieldTarget> targets,
        string valueSource,
        string defaultValue,
        int sortOrder,
        bool isSystemField,
        string? description,
        bool isRequired,
        bool isEnabled)
    {
        var normalizedTargets = targets
            .Where(target => !string.IsNullOrWhiteSpace(target.CellReference))
            .Select(target => new TemplateFieldTarget(
                string.IsNullOrWhiteSpace(target.WorksheetName) ? null : target.WorksheetName.Trim(),
                target.CellReference.Trim().ToUpperInvariant()))
            .ToArray();

        return new TemplateFieldMappingInfo(
            fieldKey,
            displayName,
            NormalizeMode(mode),
            placeholderTokens
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            normalizedTargets,
            NormalizeValueSource(valueSource),
            defaultValue ?? "",
            sortOrder,
            isSystemField,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            isRequired,
            isEnabled,
            BuildPrimaryCellReference(normalizedTargets),
            normalizedTargets.Length);
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

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnDefinition)
    {
        var columnName = columnDefinition.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        if (HasColumn(connection, tableName, columnName))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnDefinition};";
        command.ExecuteNonQuery();
    }

    private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
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

    private static void BackfillMappingMetadata(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE TemplateFieldMapping
                SET DisplayName = CASE
                        WHEN DisplayName IS NULL OR trim(DisplayName) = '' THEN FieldKey
                        ELSE DisplayName
                    END,
                    DefaultValue = COALESCE(DefaultValue, ''),
                    ValueSource = CASE
                        WHEN ValueSource IN ('BusinessData', 'DefaultValue') THEN ValueSource
                        ELSE 'BusinessData'
                    END,
                    SortOrder = CASE
                        WHEN FieldKey = 'ProjectName' AND SortOrder = 0 THEN 10
                        WHEN FieldKey = 'ConstructionUnit' AND SortOrder = 0 THEN 20
                        WHEN FieldKey = 'SupervisionUnit' AND SortOrder = 0 THEN 30
                        WHEN FieldKey = 'PartName' AND SortOrder = 0 THEN 40
                        WHEN FieldKey = 'Capacity' AND SortOrder = 0 THEN 50
                        WHEN FieldKey = 'ConstructionDate' AND SortOrder = 0 THEN 60
                        WHEN SortOrder IS NULL THEN 1000
                        ELSE SortOrder
                    END,
                    IsSystemField = CASE
                        WHEN FieldKey IN ('ProjectName', 'ConstructionUnit', 'SupervisionUnit', 'PartName', 'Capacity', 'ConstructionDate') THEN 1
                        WHEN IsSystemField IS NULL THEN 0
                        ELSE IsSystemField
                    END;
                """;
            command.ExecuteNonQuery();
        }

        foreach (var fieldKey in TemplateAdaptationFields.RequiredFieldKeys)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE TemplateFieldMapping
                SET DisplayName = $displayName,
                    ValueSource = 'BusinessData',
                    IsSystemField = 1,
                    SortOrder = $sortOrder
                WHERE FieldKey = $fieldKey
                  AND (DisplayName IS NULL OR trim(DisplayName) = '' OR DisplayName = FieldKey OR IsSystemField = 0 OR SortOrder = 0);
                """;
            command.Parameters.AddWithValue("$displayName", TemplateAdaptationFields.GetDisplayName(fieldKey));
            command.Parameters.AddWithValue("$sortOrder", TemplateAdaptationFields.GetDefaultSortOrder(fieldKey));
            command.Parameters.AddWithValue("$fieldKey", fieldKey);
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
