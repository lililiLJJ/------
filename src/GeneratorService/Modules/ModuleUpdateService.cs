namespace GeneratorService.Modules;

public sealed class ModuleUpdateService
{
    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;
    private readonly ModuleManager _moduleManager;
    private readonly ModulePackageReader _reader;

    public ModuleUpdateService(
        DirectoryInfo rootPath,
        AppConfig config,
        ModuleManager moduleManager,
        ModulePackageReader reader)
    {
        _rootPath = rootPath;
        _config = config;
        _moduleManager = moduleManager;
        _reader = reader;
    }

    public IReadOnlyList<ModuleSummary> Uninstall(string moduleId)
    {
        var module = _moduleManager.FindModule(moduleId)
            ?? throw new FileNotFoundException("模块不存在。", moduleId);

        File.Delete(module.PackagePath);
        return _moduleManager.Scan()
            .Select(item => new ModuleSummary(
                item.PackagePath,
                item.Manifest?.ModuleId,
                item.Manifest?.Name,
                item.Manifest?.Version,
                item.Manifest?.Province,
                item.Manifest?.Major,
                item.Manifest?.Year,
                item.IsValid,
                item.Errors))
            .ToArray();
    }

    public ModuleSummary Update(string sourcePath)
    {
        var manifest = _reader.ReadManifest(sourcePath);
        var current = _moduleManager.FindModule(manifest.ModuleId);
        var modulesPath = _config.GetModulesPath(_rootPath);
        Directory.CreateDirectory(modulesPath);

        var targetPath = current?.PackagePath ?? Path.Combine(modulesPath, Path.GetFileName(sourcePath));
        var backupPath = File.Exists(targetPath) ? $"{targetPath}.bak-{DateTime.Now:yyyyMMddHHmmss}" : null;
        if (backupPath is not null)
        {
            File.Copy(targetPath, backupPath);
        }

        try
        {
            File.Copy(sourcePath, targetPath, overwrite: true);
            var summaries = _moduleManager.Scan();
            var updated = summaries.FirstOrDefault(module =>
                module.Manifest is not null &&
                string.Equals(module.Manifest.ModuleId, manifest.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (updated is not { IsValid: true })
            {
                throw new InvalidOperationException("更新后的模块校验失败。");
            }

            if (backupPath is not null && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            return _moduleManager.GetSummaries().First(summary =>
                string.Equals(summary.ModuleId, manifest.ModuleId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            if (backupPath is not null && File.Exists(backupPath))
            {
                File.Copy(backupPath, targetPath, overwrite: true);
                File.Delete(backupPath);
                _moduleManager.Scan();
            }

            throw;
        }
    }
}
