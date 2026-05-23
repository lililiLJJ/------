namespace GeneratorService.Models;

public sealed record UnitInfo(
    string Name,
    string ProjectManager,
    string TechnicalManager,
    string UnitTechnicalManager);

public sealed record SupervisorUnitInfo(
    string Name,
    string ProjectManager,
    string TechnicalManager,
    string UnitTechnicalManager,
    string ProfessionalSupervisorEngineer,
    string ChiefSupervisorEngineer);

public sealed record GenerateRequest(
    string ProjectName,
    UnitInfo DeveloperUnit,
    UnitInfo ConstructorUnit,
    UnitInfo DesignUnit,
    SupervisorUnitInfo SupervisorUnit,
    UnitInfo ProfessionalSubcontractorUnit,
    UnitInfo ThirdPartyInspectionUnit,
    string Capacity,
    DateOnly ConstructionDate,
    DateOnly AcceptanceDate,
    string TemplateType,
    string TemplateName,
    string? ExportPath);

public sealed record BatchGenerateRequest(IReadOnlyList<BatchGenerateItem> Items);

public sealed record BatchGenerateItem(
    bool Enabled,
    string MaterialType,
    DateOnly Date,
    string TemplateName,
    GenerateRequest BaseRequest);

public sealed record ActivateLicenseRequest(string ActivationCode);

public sealed record AiPreviewRequest(string FieldName, GenerateRequest Context);

public sealed record AiPreviewResult(bool Success, string FieldName, string Text, string Message);

public sealed record SettingsInfo(
    int ServicePort,
    bool EnableAI,
    string ModulesPath,
    string ModuleCachePath,
    string TemplatePath,
    string ExportPath,
    string KnowledgeBasePath,
    string LogPath,
    string DeepSeekBaseUrl,
    string DeepSeekModel,
    bool HasDeepSeekApiKey);

public sealed record EnvironmentCheckItem(
    string Key,
    string Label,
    string Status,
    string Detail);

public sealed record EnvironmentCheckResult(
    string OverallStatus,
    int OkCount,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<EnvironmentCheckItem> Items);

public sealed record GenerationResult(
    bool Success,
    IReadOnlyList<string> Files,
    string Message);

public sealed record BatchGenerationResult(
    int Total,
    int Success,
    int Failed,
    IReadOnlyList<string> Files,
    IReadOnlyList<BatchFailure> FailedItems);

public sealed record BatchFailure(int Row, string Reason);

public sealed record TemplateInfo(string Name, string FullPath);

public sealed record TemplateLibraryNode(
    string Name,
    string RelativePath,
    string FullPath,
    string Type,
    IReadOnlyList<TemplateLibraryNode> Children);

public sealed record LicenseStatus(
    bool Activated,
    string LicenseType,
    string MachineCodeHash,
    DateOnly? ExpireDate,
    IReadOnlyList<string> Modules,
    int TrialLimit,
    int TrialUsed,
    bool CanGenerate,
    string Message);

public sealed record ActivationResult(bool Success, string Message, LicenseStatus Status);

public sealed record KnowledgeItem(
    string Profession,
    string Division,
    string SubItem,
    string ItemType,
    string ItemName,
    string QualifiedStandard,
    string? AllowableDeviation,
    string CheckMethod,
    string StandardCode,
    string StandardVersion,
    string SourceNote);

public sealed record ModuleFileRequest(string SourcePath);
