using Microsoft.Data.Sqlite;

namespace GeneratorService.Modules;

public sealed class ModuleValidator
{
    private static readonly string[] RequiredTables =
    [
        "ModuleInfo",
        "TemplateCategory",
        "TemplateItem",
        "InspectionRule",
        "TemplateField"
    ];

    public IReadOnlyList<string> Validate(ModuleManifest? manifest, string cachePath)
    {
        var errors = new List<string>();
        if (manifest is null)
        {
            errors.Add("manifest.json 读取失败。");
            return errors;
        }

        Require(manifest.ModuleId, "moduleId", errors);
        Require(manifest.Name, "name", errors);
        Require(manifest.Version, "version", errors);
        Require(manifest.Province, "province", errors);
        Require(manifest.Major, "major", errors);
        Require(manifest.Year, "year", errors);
        Require(manifest.Database, "database", errors);
        Require(manifest.TemplateRoot, "templateRoot", errors);
        Require(manifest.CreatedAt, "createdAt", errors);

        var rulesDbPath = Path.Combine(cachePath, manifest.Database);
        var templateRootPath = Path.Combine(cachePath, manifest.TemplateRoot);
        if (!File.Exists(rulesDbPath))
        {
            errors.Add($"缺少规则数据库：{manifest.Database}");
        }

        if (!Directory.Exists(templateRootPath))
        {
            errors.Add($"缺少模板目录：{manifest.TemplateRoot}");
        }

        if (File.Exists(rulesDbPath))
        {
            ValidateDatabase(rulesDbPath, templateRootPath, errors);
        }

        return errors;
    }

    private static void ValidateDatabase(string rulesDbPath, string templateRootPath, List<string> errors)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={rulesDbPath};Mode=ReadOnly");
            connection.Open();
            foreach (var table in RequiredTables)
            {
                if (!TableExists(connection, table))
                {
                    errors.Add($"rules.db 缺少数据表：{table}");
                }
            }

            if (TableExists(connection, "TemplateItem") && Directory.Exists(templateRootPath))
            {
                ValidateTemplateFiles(connection, templateRootPath, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"rules.db 无法打开：{ex.Message}");
        }
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return (long)command.ExecuteScalar()! > 0;
    }

    private static void ValidateTemplateFiles(SqliteConnection connection, string templateRootPath, List<string> errors)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TemplateFile FROM TemplateItem WHERE COALESCE(IsEnabled, 1) <> 0;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                errors.Add("TemplateItem 存在空 TemplateFile。");
                continue;
            }

            var templateFile = reader.GetString(0);
            var templatePath = Path.GetFullPath(Path.Combine(templateRootPath, templateFile));
            if (!IsWithinRoot(templatePath, templateRootPath))
            {
                errors.Add($"模板文件路径越界：{templateFile}");
                continue;
            }

            if (!File.Exists(templatePath))
            {
                errors.Add($"模板文件不存在：{templateFile}");
            }
        }
    }

    private static void Require(string value, string fieldName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"manifest.json 缺少必填字段：{fieldName}");
        }
    }

    private static bool IsWithinRoot(string fullPath, string rootPath)
    {
        var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
