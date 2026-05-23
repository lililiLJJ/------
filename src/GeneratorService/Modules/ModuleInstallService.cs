namespace GeneratorService.Modules;

public sealed class ModuleInstallService
{
    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;
    private readonly ModuleManager _moduleManager;

    public ModuleInstallService(DirectoryInfo rootPath, AppConfig config, ModuleManager moduleManager)
    {
        _rootPath = rootPath;
        _config = config;
        _moduleManager = moduleManager;
    }

    public ModuleSummary Install(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("模块包文件不存在。", sourcePath);
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".module", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能安装 .module 模块包。");
        }

        var modulesPath = _config.GetModulesPath(_rootPath);
        Directory.CreateDirectory(modulesPath);
        var targetPath = Path.Combine(modulesPath, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, targetPath, overwrite: true);

        return _moduleManager.Scan()
            .Where(module => string.Equals(Path.GetFullPath(module.PackagePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            .Select(module => _moduleManager.GetSummaries().First(summary => summary.PackagePath == module.PackagePath))
            .First();
    }
}
