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

        var templateFile = reader.IsDBNull(3) ? "" : reader.GetString(3);
        var templatePath = Path.GetFullPath(Path.Combine(module.TemplateRootPath, templateFile));
        if (!File.Exists(templatePath) && module.Manifest is not null)
        {
            var cacheRoot = _config.GetModuleCachePath(_rootPath);
            var cachePath = _reader.EnsureExtracted(module.PackagePath, cacheRoot, module.Manifest, forceRefresh: true);
            var templateRootPath = Path.Combine(cachePath, module.Manifest.TemplateRoot);
            templatePath = Path.GetFullPath(Path.Combine(templateRootPath, templateFile));
        }

        return new ModuleTemplateRef(
            moduleId,
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2),
            templateFile,
            reader.IsDBNull(4) ? "" : reader.GetString(4),
            templatePath);
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
}
