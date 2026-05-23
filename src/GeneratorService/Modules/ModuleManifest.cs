namespace GeneratorService.Modules;

public sealed class ModuleManifest
{
    public string ModuleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Province { get; set; } = "";
    public string Major { get; set; } = "";
    public string Year { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public int TemplateCount { get; set; }
    public string Database { get; set; } = "rules.db";
    public string TemplateRoot { get; set; } = "templates";
    public string CreatedAt { get; set; } = "";
}

public sealed record ModulePackageInfo(
    string PackagePath,
    string CachePath,
    string? RulesDbPath,
    string? TemplateRootPath,
    ModuleManifest? Manifest,
    bool IsValid,
    IReadOnlyList<string> Errors);

public sealed record ModuleSummary(
    string PackagePath,
    string? ModuleId,
    string? Name,
    string? Version,
    string? Province,
    string? Major,
    string? Year,
    bool IsValid,
    IReadOnlyList<string> Errors);

public sealed record ModuleTemplateRef(
    string ModuleId,
    long TemplateItemId,
    string TemplateName,
    string TemplateCode,
    string TemplateFile,
    string TemplateType,
    string TemplatePath);

public sealed record TemplateRuleInfo(
    long Id,
    string RuleType,
    string ItemName,
    string Requirement,
    string CheckMethod,
    string? AllowedDeviation,
    int SortOrder,
    string? Source);

public sealed record TemplateRulesResult(
    bool Success,
    string TemplateNodeId,
    string ModuleId,
    string ModuleName,
    string TemplateName,
    string TemplateCode,
    IReadOnlyList<TemplateRuleInfo> Rules);
