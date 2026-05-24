using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GeneratorService.Projects;

public sealed class ProjectStorageService
{
    public const string ProjectInfoFileName = "ProjectInfo.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ProjectContext CreateProject(ProjectCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectName))
        {
            throw new InvalidOperationException("工程名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.ProjectRootPath))
        {
            throw new InvalidOperationException("工程目录不能为空。");
        }

        var rootPath = Path.GetFullPath(request.ProjectRootPath.Trim());
        Directory.CreateDirectory(rootPath);

        var now = DateTimeOffset.Now;
        var context = new ProjectContext
        {
            ProjectId = BuildProjectId(rootPath),
            ProjectName = request.ProjectName.Trim(),
            ProjectRootPath = rootPath,
            ModuleName = request.ModuleName?.Trim() ?? "",
            TemplateVersion = request.TemplateVersion?.Trim() ?? "",
            CreatedAt = now,
            UpdatedAt = now
        };

        PopulatePaths(context);
        EnsureProjectDirectories(context);
        SaveProjectInfo(context);
        return context;
    }

    public ProjectContext OpenProject(string projectRootPath)
    {
        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            throw new InvalidOperationException("工程目录不能为空。");
        }

        var rootPath = Path.GetFullPath(projectRootPath.Trim());
        var infoPath = Path.Combine(rootPath, ProjectInfoFileName);
        if (!File.Exists(infoPath))
        {
            throw new FileNotFoundException($"工程目录缺少 {ProjectInfoFileName}：{rootPath}", infoPath);
        }

        var context = JsonSerializer.Deserialize<ProjectContext>(File.ReadAllText(infoPath), JsonOptions)
            ?? throw new InvalidOperationException("ProjectInfo.json 内容无效。");
        context.ProjectRootPath = rootPath;
        if (string.IsNullOrWhiteSpace(context.ProjectId))
        {
            context.ProjectId = BuildProjectId(rootPath);
        }

        PopulatePaths(context);
        EnsureProjectDirectories(context);
        return context;
    }

    public void SaveProjectInfo(ProjectContext context)
    {
        context.UpdatedAt = DateTimeOffset.Now;
        PopulatePaths(context);
        EnsureProjectDirectories(context);
        var infoPath = Path.Combine(context.ProjectRootPath, ProjectInfoFileName);
        File.WriteAllText(infoPath, JsonSerializer.Serialize(context, JsonOptions), new UTF8Encoding(false));
    }

    public static void PopulatePaths(ProjectContext context)
    {
        context.ProjectRootPath = Path.GetFullPath(context.ProjectRootPath);
        context.GeneratedFormsPath = Path.Combine(context.ProjectRootPath, "GeneratedForms");
        context.ExportPath = Path.Combine(context.ProjectRootPath, "Export");
        context.CachePath = Path.Combine(context.ProjectRootPath, "Cache");
        context.LogsPath = Path.Combine(context.ProjectRootPath, "Logs");
        context.TempPath = Path.Combine(context.ProjectRootPath, "Temp");
        context.VersionsPath = Path.Combine(context.ProjectRootPath, "Versions");
    }

    public static void EnsureProjectDirectories(ProjectContext context)
    {
        Directory.CreateDirectory(context.ProjectRootPath);
        Directory.CreateDirectory(context.GeneratedFormsPath);
        Directory.CreateDirectory(context.ExportPath);
        Directory.CreateDirectory(context.CachePath);
        Directory.CreateDirectory(context.LogsPath);
        Directory.CreateDirectory(context.TempPath);
        Directory.CreateDirectory(context.VersionsPath);
    }

    public static string BuildProjectId(string projectRootPath)
    {
        var normalized = Path.GetFullPath(projectRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant()));
        return "project-" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
