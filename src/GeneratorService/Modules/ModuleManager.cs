using Microsoft.Data.Sqlite;
using Serilog;

namespace GeneratorService.Modules;

public sealed class ModuleManager
{
    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;
    private readonly ModulePackageReader _reader;
    private readonly ModuleValidator _validator;
    private readonly object _lock = new();
    private List<ModulePackageInfo> _modules = [];

    public ModuleManager(
        DirectoryInfo rootPath,
        AppConfig config,
        ModulePackageReader reader,
        ModuleValidator validator)
    {
        _rootPath = rootPath;
        _config = config;
        _reader = reader;
        _validator = validator;
    }

    public IReadOnlyList<ModulePackageInfo> Scan()
    {
        var modulesPath = _config.GetModulesPath(_rootPath);
        var cachePath = _config.GetModuleCachePath(_rootPath);
        Directory.CreateDirectory(modulesPath);
        Directory.CreateDirectory(cachePath);

        var modules = Directory.EnumerateFiles(modulesPath, "*.module", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => LoadPackage(path, cachePath))
            .ToList();

        lock (_lock)
        {
            _modules = modules;
        }

        Log.Information("模块扫描完成。Path={ModulesPath}, Count={Count}, Valid={ValidCount}",
            modulesPath,
            modules.Count,
            modules.Count(module => module.IsValid));
        return modules;
    }

    public IReadOnlyList<ModulePackageInfo> GetModules()
    {
        lock (_lock)
        {
            return _modules.ToArray();
        }
    }

    public IReadOnlyList<ModulePackageInfo> GetValidModules()
    {
        return GetModules().Where(module => module.IsValid && module.Manifest is not null).ToArray();
    }

    public IReadOnlyList<ModuleSummary> GetSummaries()
    {
        return GetModules()
            .Select(module => new ModuleSummary(
                module.PackagePath,
                module.Manifest?.ModuleId,
                module.Manifest?.Name,
                module.Manifest?.Version,
                module.Manifest?.Province,
                module.Manifest?.Major,
                module.Manifest?.Year,
                module.IsValid,
                module.Errors))
            .ToArray();
    }

    public ModulePackageInfo? FindModule(string moduleId)
    {
        return GetModules().FirstOrDefault(module =>
            module.Manifest is not null &&
            string.Equals(module.Manifest.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryParseTemplateNodeId(string nodeId, out string moduleId, out long templateItemId)
    {
        moduleId = "";
        templateItemId = 0;
        const string marker = ":template:";
        if (!nodeId.StartsWith("module:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var markerIndex = nodeId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        moduleId = nodeId["module:".Length..markerIndex];
        return long.TryParse(nodeId[(markerIndex + marker.Length)..], out templateItemId);
    }

    public ModuleTemplateRef? ResolveTemplate(string templateNodeId)
    {
        if (!TryParseTemplateNodeId(templateNodeId, out var moduleId, out var templateItemId))
        {
            return null;
        }

        var module = FindModule(moduleId);
        if (module is not { IsValid: true, RulesDbPath: not null, TemplateRootPath: not null })
        {
            return null;
        }

        using var connection = new SqliteConnection($"Data Source={module.RulesDbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TemplateName, TemplateCode, TemplateFile, TemplateType
            FROM TemplateItem
            WHERE Id = $id AND COALESCE(IsEnabled, 1) <> 0;
            """;
        command.Parameters.AddWithValue("$id", templateItemId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var manifest = module.Manifest;
        if (manifest is null)
        {
            return null;
        }

        var templateFile = reader.IsDBNull(3) ? "" : reader.GetString(3);
        var templatePath = ResolveTemplatePath(module, manifest, templateFile, ensureExists: true);

        return new ModuleTemplateRef(
            moduleId,
            manifest.Version,
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2),
            templateFile,
            reader.IsDBNull(4) ? "" : reader.GetString(4),
            templatePath);
    }

    public IReadOnlyList<ModuleTemplateRef> ListTemplates()
    {
        var results = new List<ModuleTemplateRef>();
        foreach (var module in GetValidModules())
        {
            if (module is not { RulesDbPath: not null, TemplateRootPath: not null, Manifest: not null })
            {
                continue;
            }

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
            while (reader.Read())
            {
                var templateFile = reader.IsDBNull(3) ? "" : reader.GetString(3);
                results.Add(new ModuleTemplateRef(
                    module.Manifest.ModuleId,
                    module.Manifest.Version,
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    templateFile,
                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ResolveTemplatePath(module, module.Manifest, templateFile, ensureExists: false)));
            }
        }

        return results;
    }

    public IReadOnlyList<string> GetTemplateDirectorySegments(string templateNodeId)
    {
        if (!TryParseTemplateNodeId(templateNodeId, out var moduleId, out var templateItemId))
        {
            return [];
        }

        var module = FindModule(moduleId);
        if (module is not { IsValid: true, RulesDbPath: not null, Manifest: not null })
        {
            return [];
        }

        using var connection = new SqliteConnection($"Data Source={module.RulesDbPath};Mode=ReadOnly");
        connection.Open();
        using var templateCommand = connection.CreateCommand();
        templateCommand.CommandText = "SELECT CategoryId, TemplateName FROM TemplateItem WHERE Id = $id;";
        templateCommand.Parameters.AddWithValue("$id", templateItemId);
        using var reader = templateCommand.ExecuteReader();
        if (!reader.Read())
        {
            return [];
        }

        var categoryId = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
        var templateName = reader.IsDBNull(1) ? "" : reader.GetString(1);
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(module.Manifest.Name))
        {
            segments.Add(module.Manifest.Name);
        }

        segments.AddRange(GetCategorySegments(connection, categoryId));
        if (!string.IsNullOrWhiteSpace(templateName))
        {
            segments.Add(templateName);
        }

        return segments;
    }

    private static IReadOnlyList<string> GetCategorySegments(SqliteConnection connection, long categoryId)
    {
        var categories = new List<(long Id, long? ParentId, string Name)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, ParentId, Name FROM TemplateCategory;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                categories.Add((
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    reader.GetString(2)));
            }
        }

        var byId = categories.ToDictionary(item => item.Id);
        var stack = new Stack<string>();
        var currentId = categoryId;
        while (currentId != 0 && byId.TryGetValue(currentId, out var category))
        {
            stack.Push(category.Name);
            currentId = category.ParentId ?? 0;
        }

        return stack.ToArray();
    }

    private ModulePackageInfo LoadPackage(string packagePath, string cacheRoot)
    {
        ModuleManifest? manifest = null;
        var errors = new List<string>();
        var cachePath = "";
        string? rulesDbPath = null;
        string? templateRootPath = null;

        try
        {
            manifest = _reader.ReadManifest(packagePath);
            cachePath = _reader.EnsureExtracted(packagePath, cacheRoot, manifest);
            rulesDbPath = Path.Combine(cachePath, manifest.Database);
            templateRootPath = Path.Combine(cachePath, manifest.TemplateRoot);
            errors.AddRange(_validator.Validate(manifest, cachePath));
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        if (errors.Count > 0)
        {
            Log.Warning("模块加载失败。Package={PackagePath}, Errors={Errors}", packagePath, string.Join("；", errors));
        }

        return new ModulePackageInfo(
            packagePath,
            cachePath,
            rulesDbPath,
            templateRootPath,
            manifest,
            errors.Count == 0,
            errors);
    }

    private string ResolveTemplatePath(
        ModulePackageInfo module,
        ModuleManifest manifest,
        string templateFile,
        bool ensureExists)
    {
        var templatePath = Path.GetFullPath(Path.Combine(module.TemplateRootPath!, templateFile));
        if (!ensureExists || File.Exists(templatePath))
        {
            return templatePath;
        }

        var cacheRoot = _config.GetModuleCachePath(_rootPath);
        var cachePath = _reader.EnsureExtracted(module.PackagePath, cacheRoot, manifest, forceRefresh: true);
        var templateRootPath = Path.Combine(cachePath, manifest.TemplateRoot);
        return Path.GetFullPath(Path.Combine(templateRootPath, templateFile));
    }
}
