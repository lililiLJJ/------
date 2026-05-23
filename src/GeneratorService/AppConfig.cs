using System.Text.Json;

namespace GeneratorService;

public sealed class AppConfig
{
    public ServiceConfig Service { get; set; } = new();
    public bool EnableAI { get; set; }
    public string ModulesPath { get; set; } = "Modules";
    public string ModuleCachePath { get; set; } = "Templates/ModuleCache";
    public string TemplatePath { get; set; } = "Templates";
    public string ExportPath { get; set; } = "Export";
    public string KnowledgeBasePath { get; set; } = "KnowledgeBase/quality.db";
    public string LogPath { get; set; } = "Logs";
    public DeepSeekConfig DeepSeek { get; set; } = new();

    public static AppConfig Load(DirectoryInfo rootPath)
    {
        var configPath = Path.Combine(rootPath.FullName, "config.json");
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(rootPath.FullName, "config.example.json");
        }

        if (!File.Exists(configPath))
        {
            return new AppConfig();
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppConfig();
    }

    public string GetTemplatePath(DirectoryInfo rootPath) => GetFullPath(rootPath, TemplatePath);

    public string GetModulesPath(DirectoryInfo rootPath) => GetFullPath(rootPath, ModulesPath);

    public string GetModuleCachePath(DirectoryInfo rootPath) => GetFullPath(rootPath, ModuleCachePath);

    public string GetExportPath(DirectoryInfo rootPath) => GetFullPath(rootPath, ExportPath);

    public string GetKnowledgeBasePath(DirectoryInfo rootPath) => GetFullPath(rootPath, KnowledgeBasePath);

    public string GetLogPath(DirectoryInfo rootPath) => GetFullPath(rootPath, LogPath);

    private static string GetFullPath(DirectoryInfo rootPath, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(rootPath.FullName, path));
    }
}

public sealed class ServiceConfig
{
    public int Port { get; set; } = 5188;
}

public sealed class DeepSeekConfig
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-v4-flash";
    public string ApiKey { get; set; } = "";
}
