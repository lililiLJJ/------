using GeneratorService.Modules;

namespace GeneratorService.Projects;

public sealed class ProjectPathResolver
{
    private readonly ModuleManager _moduleManager;

    public ProjectPathResolver(ModuleManager moduleManager)
    {
        _moduleManager = moduleManager;
    }

    public string ResolveGeneratedFormDirectory(ProjectContext project, string templateNodeId, string templateName)
    {
        var segments = _moduleManager.GetTemplateDirectorySegments(templateNodeId);
        if (segments.Count == 0)
        {
            segments = [templateName];
        }

        var safeSegments = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(SanitizePathSegment)
            .ToArray();

        var directory = safeSegments.Length == 0
            ? project.GeneratedFormsPath
            : Path.Combine([project.GeneratedFormsPath, ..safeSegments]);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string SanitizePathSegment(string value)
    {
        var sanitized = string.IsNullOrWhiteSpace(value) ? "未命名" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized;
    }
}
