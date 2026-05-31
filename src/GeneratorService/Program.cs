using GeneratorService;
using GeneratorService.Ai;
using GeneratorService.Generation;
using GeneratorService.Knowledge;
using GeneratorService.Licensing;
using GeneratorService.Materials;
using GeneratorService.Models;
using GeneratorService.Modules;
using GeneratorService.Projects;
using GeneratorService.Templates;
using GeneratorService.TemplateLibrary;
using Serilog;
using System.Diagnostics;
using System.Net.Sockets;

var rootPath = WorkspacePaths.FindRoot(AppContext.BaseDirectory);
var config = AppConfig.Load(rootPath);

Directory.CreateDirectory(config.GetLogPath(rootPath));

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(config.GetLogPath(rootPath), "startup.log"),
        rollingInterval: RollingInterval.Day)
    .WriteTo.File(
        Path.Combine(config.GetLogPath(rootPath), "generate.log"),
        rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://127.0.0.1:{config.Service.Port}");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true);
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(rootPath);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<KnowledgeRepository>();
builder.Services.AddSingleton<TemplateCatalog>();
builder.Services.AddSingleton<ModulePackageReader>();
builder.Services.AddSingleton<ModuleValidator>();
builder.Services.AddSingleton<ModuleManager>();
builder.Services.AddSingleton<ModuleInstallService>();
builder.Services.AddSingleton<ModuleUpdateService>();
builder.Services.AddSingleton<ProjectStorageService>();
builder.Services.AddSingleton<ProjectManager>();
builder.Services.AddSingleton<RecentProjectService>();
builder.Services.AddSingleton<UnitProjectRepository>();
builder.Services.AddSingleton<UnitProjectService>();
builder.Services.AddSingleton<ProjectPathResolver>();
builder.Services.AddSingleton<ProjectFolderDialogService>();
builder.Services.AddSingleton<MaterialRepository>();
builder.Services.AddSingleton<MaterialService>();
builder.Services.AddSingleton<MaterialAttachmentService>();
builder.Services.AddSingleton<MaterialApprovalService>();
builder.Services.AddSingleton<MaterialLedgerService>();
builder.Services.AddSingleton<TemplateTreeRepository>();
builder.Services.AddSingleton<BatchPlanRepository>();
builder.Services.AddSingleton<TemplateAdaptationRepository>();
builder.Services.AddSingleton<TemplateTreeService>();
builder.Services.AddSingleton<TemplateService>();
builder.Services.AddSingleton<RuleService>();
builder.Services.AddSingleton<TemplateValidationService>();
builder.Services.AddSingleton<TemplateMappingService>();
builder.Services.AddSingleton<TemplateTestHarness>();
builder.Services.AddSingleton<TemplateAdaptationService>();
builder.Services.AddSingleton<RowHeightBalanceService>();
builder.Services.AddSingleton<GeneratedFormService>();
builder.Services.AddSingleton<SummaryService>();
builder.Services.AddSingleton<BatchPlanService>();
builder.Services.AddSingleton<SpreadsheetOpenService>();
builder.Services.AddSingleton<AiTextService>();
builder.Services.AddSingleton<LicenseService>();
builder.Services.AddSingleton<ExcelGenerationService>();

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

var knowledgeRepository = app.Services.GetRequiredService<KnowledgeRepository>();
var templateCatalog = app.Services.GetRequiredService<TemplateCatalog>();
var moduleManager = app.Services.GetRequiredService<ModuleManager>();
var templateTreeRepository = app.Services.GetRequiredService<TemplateTreeRepository>();
var projectManager = app.Services.GetRequiredService<ProjectManager>();
var unitProjectRepository = app.Services.GetRequiredService<UnitProjectRepository>();
var unitProjectService = app.Services.GetRequiredService<UnitProjectService>();
var materialRepository = app.Services.GetRequiredService<MaterialRepository>();
var batchPlanRepository = app.Services.GetRequiredService<BatchPlanRepository>();
var templateAdaptationRepository = app.Services.GetRequiredService<TemplateAdaptationRepository>();
knowledgeRepository.EnsureCreated();
templateCatalog.EnsureSampleTemplates();
moduleManager.Scan();
templateTreeRepository.EnsureCreated();
materialRepository.EnsureCreated();
batchPlanRepository.EnsureCreated();
templateAdaptationRepository.EnsureCreated();
templateAdaptationRepository.EnsureBootstrapProfiles(moduleManager);
unitProjectRepository.EnsureCreated();
unitProjectService.EnsureDefault(projectManager.GetCurrentProject());

Log.Information("工程资料生成服务已启动。Root={RootPath}, Port={Port}", rootPath.FullName, config.Service.Port);

app.MapGet("/api/health", (AppConfig currentConfig, LicenseService licenseService) =>
{
    return Results.Ok(new
    {
        success = true,
        service = "工程资料智能生成服务",
        version = "3.0-mvp",
        time = DateTimeOffset.Now,
        aiEnabled = currentConfig.EnableAI,
        license = licenseService.GetStatus()
    });
});

app.MapGet("/api/templates", (TemplateCatalog catalog) =>
{
    return Results.Ok(new
    {
        success = true,
        templates = catalog.ListTemplates()
    });
});

app.MapGet("/api/templates/tree", (TemplateCatalog catalog) =>
{
    var tree = catalog.GetTemplateLibraryTree();
    return Results.Ok(new
    {
        success = true,
        rootPath = catalog.GetTemplateRootPath(),
        tree,
        totalTemplates = CountTemplateNodes(tree)
    });
});

app.MapPost("/api/templates/open-folder", (TemplateCatalog catalog) =>
{
    var templatePath = catalog.GetTemplateRootPath();
    Process.Start(new ProcessStartInfo
    {
        FileName = "explorer.exe",
        Arguments = $"\"{templatePath}\"",
        UseShellExecute = true
    });

    return Results.Ok(new
    {
        success = true,
        path = templatePath,
        message = "已打开模板库文件夹。"
    });
});

app.MapGet("/api/modules", (ModuleManager manager) =>
{
    return Results.Ok(new
    {
        success = true,
        modules = manager.GetSummaries()
    });
});

app.MapPost("/api/modules/rescan", (ModuleManager manager) =>
{
    var modules = manager.Scan();
    return Results.Ok(new
    {
        success = true,
        modules = manager.GetSummaries(),
        total = modules.Count,
        valid = modules.Count(module => module.IsValid)
    });
});

app.MapPost("/api/modules/install", (ModuleFileRequest request, ModuleInstallService service) =>
{
    try
    {
        return Results.Ok(new
        {
            success = true,
            module = service.Install(request.SourcePath)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/modules/update", (ModuleFileRequest request, ModuleUpdateService service) =>
{
    try
    {
        return Results.Ok(new
        {
            success = true,
            module = service.Update(request.SourcePath)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapDelete("/api/modules/{moduleId}", (string moduleId, ModuleUpdateService service) =>
{
    try
    {
        return Results.Ok(new
        {
            success = true,
            modules = service.Uninstall(moduleId)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/projects/current", (ProjectManager manager) =>
{
    return Results.Ok(new
    {
        success = true,
        project = manager.GetCurrentProject()
    });
});

app.MapPost("/api/projects/current", (ProjectUpdateRequest request, ProjectManager manager) =>
{
    try
    {
        return Results.Ok(new
        {
            success = true,
            project = manager.UpdateCurrentProject(request)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/projects/create", (ProjectCreateRequest request, ProjectManager manager, RecentProjectService recentProjects, UnitProjectService unitProjects) =>
{
    try
    {
        var project = manager.CreateProject(request);
        unitProjects.EnsureDefault(project);
        recentProjects.Upsert(project);
        return Results.Ok(new
        {
            success = true,
            project
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/projects/open", (ProjectOpenRequest request, ProjectManager manager, RecentProjectService recentProjects, UnitProjectService unitProjects) =>
{
    try
    {
        var project = manager.OpenProject(request);
        unitProjects.EnsureDefault(project);
        recentProjects.Upsert(project);
        return Results.Ok(new
        {
            success = true,
            project
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/projects/select-folder", (ProjectFolderSelectRequest? request, ProjectFolderDialogService service) =>
{
    var result = service.SelectFolder(request?.Description, request?.InitialDirectory);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/api/projects/recent", (ProjectManager manager, RecentProjectService recentProjects) =>
{
    return Results.Ok(recentProjects.List(manager.GetCurrentProject()));
});

app.MapPost("/api/projects/recent/validate", (ProjectManager manager, RecentProjectService recentProjects) =>
{
    return Results.Ok(recentProjects.Validate(manager.GetCurrentProject()));
});

app.MapDelete("/api/projects/recent", (ProjectManager manager, RecentProjectService recentProjects) =>
{
    return Results.Ok(recentProjects.Clear(manager.GetCurrentProject()));
});

app.MapDelete("/api/projects/recent/{projectId}", (string projectId, ProjectManager manager, RecentProjectService recentProjects) =>
{
    return Results.Ok(recentProjects.Remove(manager.GetCurrentProject(), projectId));
});

app.MapGet("/api/unit-projects", (string? projectId, UnitProjectService service) =>
{
    try
    {
        return Results.Ok(service.List(projectId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/unit-projects", (UnitProjectSaveRequest request, UnitProjectService service) =>
{
    try
    {
        return Results.Ok(service.Create(request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPut("/api/unit-projects/{id}", (string id, UnitProjectSaveRequest request, UnitProjectService service) =>
{
    try
    {
        return Results.Ok(service.Update(id, request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapDelete("/api/unit-projects/{id}", (string id, string? projectId, UnitProjectService service) =>
{
    try
    {
        return Results.Ok(service.Deactivate(projectId ?? "", id));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/unit-projects/current", (string? projectId, UnitProjectService service) =>
{
    try
    {
        return Results.Ok(service.GetCurrent(projectId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/unit-projects/current", (UnitProjectCurrentRequest request, UnitProjectService service) =>
{
    try
    {
        return Results.Ok(service.SetCurrent(request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/materials", (
    string? projectId,
    string? unitProjectId,
    string? materialScope,
    string? materialName,
    DateOnly? entryDateFrom,
    DateOnly? entryDateTo,
    string? usePart,
    string? supplier,
    string? testStatus,
    string? approvalStatus,
    string? status,
    ProjectManager manager,
    MaterialService service,
    UnitProjectService unitProjects) =>
{
    try
    {
        var project = manager.ResolveProject(projectId);
        var unitProject = unitProjects.ResolveUnitProject(project.ProjectId, unitProjectId);
        var query = new MaterialQuery(
            project.ProjectId,
            unitProject.Id,
            string.IsNullOrWhiteSpace(materialScope) ? "currentAndPublic" : materialScope,
            materialName,
            entryDateFrom,
            entryDateTo,
            usePart,
            supplier,
            testStatus,
            approvalStatus,
            status);
        return Results.Ok(service.List(query));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/materials", (MaterialEntryCreateRequest request, MaterialService service) =>
{
    try
    {
        return Results.Ok(new
        {
            success = true,
            item = service.Create(request)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPut("/api/materials/{id}", (string id, MaterialEntryUpdateRequest request, MaterialService service) =>
{
    try
    {
        return Results.Ok(new
        {
            success = true,
            item = service.Update(id, request)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/materials/batch-save", (MaterialBatchSaveRequest request, MaterialService service) =>
{
    try
    {
        return Results.Ok(service.BatchSave(request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapDelete("/api/materials/{id}", (string id, string? projectId, MaterialService service) =>
{
    try
    {
        return Results.Ok(service.Delete(id, projectId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/materials/{id}/attachments", async (
    string id,
    HttpRequest request,
    MaterialAttachmentService service) =>
{
    try
    {
        return Results.Ok(await service.UploadAsync(id, request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/materials/{id}/test", (string id, MaterialTestUpsertRequest request, MaterialService service) =>
{
    try
    {
        return Results.Ok(new
        {
            success = true,
            test = service.SaveTest(id, request)
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/materials/{id}/approval/generate", (
    string id,
    string? projectId,
    MaterialApprovalService service) =>
{
    try
    {
        return Results.Ok(service.Generate(id, projectId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/materials/approval/generate", (
    MaterialApprovalBatchGenerateRequest request,
    MaterialApprovalService service) =>
{
    try
    {
        return Results.Ok(service.GenerateBatch(request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/materials/ledger", (
    string? projectId,
    string? unitProjectId,
    string? materialScope,
    string? materialName,
    DateOnly? entryDateFrom,
    DateOnly? entryDateTo,
    string? usePart,
    string? supplier,
    string? testStatus,
    string? approvalStatus,
    string? status,
    bool? export,
    ProjectManager manager,
    MaterialLedgerService service,
    UnitProjectService unitProjects) =>
{
    try
    {
        var project = manager.ResolveProject(projectId);
        var unitProject = unitProjects.ResolveUnitProject(project.ProjectId, unitProjectId);
        var query = new MaterialQuery(
            project.ProjectId,
            unitProject.Id,
            string.IsNullOrWhiteSpace(materialScope) ? "currentAndPublic" : materialScope,
            materialName,
            entryDateFrom,
            entryDateTo,
            usePart,
            supplier,
            testStatus,
            approvalStatus,
            status);
        return Results.Ok(service.GetLedger(query, export == true));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/materials/ledger/export", (
    MaterialLedgerExportRequest request,
    ProjectManager manager,
    MaterialLedgerService service,
    UnitProjectService unitProjects) =>
{
    try
    {
        var project = manager.ResolveProject(request.ProjectId);
        var unitProject = unitProjects.ResolveUnitProject(project.ProjectId, request.UnitProjectId);
        var query = new MaterialQuery(
            project.ProjectId,
            unitProject.Id,
            string.IsNullOrWhiteSpace(request.MaterialScope) ? "currentAndPublic" : request.MaterialScope,
            request.MaterialName,
            request.EntryDateFrom,
            request.EntryDateTo,
            request.UsePart,
            request.Supplier,
            request.TestStatus,
            request.ApprovalStatus,
            request.Status);
        return Results.Ok(service.GetLedger(query, true));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/files/open", (OpenSpreadsheetRequest request, SpreadsheetOpenService openService) =>
{
    try
    {
        openService.OpenSpreadsheet(request.FilePath);
        return Results.Ok(new
        {
            success = true,
            path = request.FilePath,
            message = "已打开表格文件。"
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/files/open-url", (OpenUrlRequest request) =>
{
    try
    {
        var url = request.Url?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("打开地址不能为空。");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("打开地址格式无效。");
        }

        var isLoopbackHttp = (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback;
        if (!uri.IsFile && !isLoopbackHttp)
        {
            throw new InvalidOperationException("只允许打开本地文件或本机服务地址。");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });

        return Results.Ok(new
        {
            success = true,
            url,
            message = "已打开独立窗口。"
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/template-library/tree", (string? projectId, string? unitProjectId, TemplateTreeService service) =>
{
    return Results.Ok(service.GetTree(projectId, unitProjectId));
});

app.MapGet("/api/templates/{templateNodeId}/rules", (string templateNodeId, RuleService service) =>
{
    try
    {
        return Results.Ok(service.GetRules(templateNodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/template-adaptations/{templateNodeId}", (string templateNodeId, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.GetDetail(templateNodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPut("/api/template-adaptations/{templateNodeId}", (string templateNodeId, TemplateAdaptationSaveRequest request, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.Save(templateNodeId, request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/template-mappings/templates", (TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.GetTemplates());
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/template-mappings/{templateNodeId}", (string templateNodeId, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.GetDetail(templateNodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/template-mappings/{templateNodeId}/fields", (string templateNodeId, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.GetFields(templateNodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/template-mappings/{templateNodeId}/fields", (string templateNodeId, TemplateAdaptationSaveRequest request, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.SaveFields(templateNodeId, request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPut("/api/template-mappings/{templateNodeId}/fields/{fieldKey}", (string templateNodeId, string fieldKey, TemplateFieldMappingSaveItem request, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.SaveField(templateNodeId, fieldKey, request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapDelete("/api/template-mappings/{templateNodeId}/fields/{fieldKey}", (string templateNodeId, string fieldKey, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.DeleteField(templateNodeId, fieldKey));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/template-mappings/{templateNodeId}/validate", (string templateNodeId, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.Validate(templateNodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/template-mappings/{templateNodeId}/test", (string templateNodeId, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.Test(templateNodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/template-adaptations/{templateNodeId}/validate", (string templateNodeId, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.Validate(templateNodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/template-adaptations/{templateNodeId}/test", (string templateNodeId, TemplateAdaptationService service) =>
{
    try
    {
        return Results.Ok(service.Test(templateNodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/template-adaptations/batch-test", async (HttpRequest httpRequest, TemplateAdaptationService service) =>
{
    try
    {
        TemplateBatchTestRequest? request = null;
        if (httpRequest.ContentLength is > 0)
        {
            request = await httpRequest.ReadFromJsonAsync<TemplateBatchTestRequest>();
        }

        return Results.Ok(service.BatchTest(request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/generated-forms/{nodeId}", (string nodeId, GeneratedFormService service) =>
{
    try
    {
        return Results.Ok(service.GetGeneratedForm(nodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/generated-forms", (CreateGeneratedFormRequest request, GeneratedFormService service) =>
{
    try
    {
        return Results.Ok(service.CreateGeneratedForm(request));
    }
    catch (Exception ex)
    {
        if (ex is TemplateAdaptationException adaptationEx)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = adaptationEx.Message,
                missingFields = adaptationEx.MissingFields,
                templateNodeId = adaptationEx.TemplateNodeId,
                adaptationStatus = adaptationEx.AdaptationStatus,
                canOpenAdaptationPanel = adaptationEx.CanOpenAdaptationPanel
            });
        }

        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/generated-forms/{nodeId}/open", (
    string nodeId,
    GeneratedFormService formService,
    SpreadsheetOpenService openService) =>
{
    try
    {
        var form = formService.GetGeneratedForm(nodeId);
        openService.OpenSpreadsheet(form.GeneratedFilePath);
        return Results.Ok(new
        {
            success = true,
            path = form.GeneratedFilePath,
            message = "已打开资料表。"
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/generated-forms/{nodeId}/backups", (string nodeId, GeneratedFormService service) =>
{
    try
    {
        return Results.Ok(service.BackupGeneratedForm(nodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapDelete("/api/generated-forms/{nodeId}", (string nodeId, GeneratedFormService service) =>
{
    try
    {
        return Results.Ok(service.DeleteGeneratedForm(nodeId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/summary/tree", (string? projectId, string? unitProjectId, SummaryService service) =>
{
    try
    {
        return Results.Ok(service.GetTree(projectId, unitProjectId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/summary/preview", (string? projectId, string? unitProjectId, string type, string categoryId, SummaryService service) =>
{
    try
    {
        return Results.Ok(service.GetPreview(projectId, unitProjectId, type, categoryId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/summary/generate", (GenerateSummaryRequest request, SummaryService service) =>
{
    try
    {
        return Results.Ok(service.Generate(request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/batch-plans", (string? projectId, string? unitProjectId, BatchPlanService service) =>
{
    try
    {
        return Results.Ok(service.List(projectId, unitProjectId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/batch-plans", (BatchPlanSaveRequest request, BatchPlanService service) =>
{
    try
    {
        return Results.Ok(service.Create(request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPut("/api/batch-plans/{id}", (string id, BatchPlanSaveRequest request, BatchPlanService service) =>
{
    try
    {
        return Results.Ok(service.Update(id, request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/batch-plans/{id}/preview", (string id, BatchPlanService service) =>
{
    try
    {
        return Results.Ok(service.Preview(id));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPost("/api/batch-plans/{id}/generate", async (string id, HttpRequest httpRequest, BatchPlanService service) =>
{
    try
    {
        BatchPlanGenerateRequest? request = null;
        if (httpRequest.ContentLength is > 0)
        {
            request = await httpRequest.ReadFromJsonAsync<BatchPlanGenerateRequest>();
        }

        return Results.Ok(service.Generate(id, request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/device-fields", (string? moduleId, long? templateItemId, BatchPlanService service) =>
{
    try
    {
        return Results.Ok(service.GetDeviceFields(moduleId, templateItemId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/device-mappings", (string? moduleId, long? templateItemId, BatchPlanService service) =>
{
    try
    {
        return Results.Ok(service.GetDeviceMappings(moduleId, templateItemId));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapPut("/api/device-mappings", (DeviceMappingsSaveRequest request, BatchPlanService service) =>
{
    try
    {
        return Results.Ok(service.SaveDeviceMappings(request));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = ex.Message
        });
    }
});

app.MapGet("/api/knowledge/items", (
    string? division,
    string? subItem,
    string? itemType,
    KnowledgeRepository repository) =>
{
    var items = repository.ListItems(division, subItem, itemType);
    return Results.Ok(new
    {
        success = true,
        total = items.Count,
        items
    });
});

app.MapGet("/api/settings", (AppConfig currentConfig, DirectoryInfo workspaceRoot) =>
{
    var settings = new SettingsInfo(
        currentConfig.Service.Port,
        currentConfig.EnableAI,
        currentConfig.GetModulesPath(workspaceRoot),
        currentConfig.GetModuleCachePath(workspaceRoot),
        currentConfig.GetTemplatePath(workspaceRoot),
        currentConfig.GetExportPath(workspaceRoot),
        currentConfig.GetKnowledgeBasePath(workspaceRoot),
        currentConfig.GetLogPath(workspaceRoot),
        currentConfig.DeepSeek.BaseUrl,
        currentConfig.DeepSeek.Model,
        !string.IsNullOrWhiteSpace(currentConfig.DeepSeek.ApiKey));

    return Results.Ok(new
    {
        success = true,
        settings
    });
});

app.MapGet("/api/environment/check", (
    AppConfig currentConfig,
    DirectoryInfo workspaceRoot,
    TemplateCatalog catalog,
    ModuleManager moduleManager,
    LicenseService licenseService) =>
{
    var templatePath = currentConfig.GetTemplatePath(workspaceRoot);
    var exportPath = currentConfig.GetExportPath(workspaceRoot);
    var knowledgeBasePath = currentConfig.GetKnowledgeBasePath(workspaceRoot);
    var logPath = currentConfig.GetLogPath(workspaceRoot);
    var license = licenseService.GetStatus();
    var templateCount = catalog.ListTemplates().Count;
    var modules = moduleManager.GetModules();
    var validModules = modules.Count(module => module.IsValid);

    var items = new List<EnvironmentCheckItem>
    {
        new("service", "本地服务", "ok", $"服务已启动，监听端口 {currentConfig.Service.Port}"),
        new("templates", "模板目录", Directory.Exists(templatePath) ? "ok" : "error",
            Directory.Exists(templatePath)
                ? $"目录存在，当前识别到 {templateCount} 个模板"
                : $"目录不存在：{templatePath}"),
        new("modules", "模块库",
            validModules > 0 ? "ok" : (modules.Count > 0 ? "error" : "warning"),
            modules.Count == 0
                ? $"尚未安装 .module 模块，目录：{currentConfig.GetModulesPath(workspaceRoot)}"
                : $"已发现 {modules.Count} 个模块，有效 {validModules} 个。"),
        new("knowledge", "知识库文件", File.Exists(knowledgeBasePath) ? "ok" : "error",
            File.Exists(knowledgeBasePath)
                ? $"知识库已就绪：{knowledgeBasePath}"
                : $"知识库文件不存在：{knowledgeBasePath}"),
        new("logs", "日志目录", Directory.Exists(logPath) ? "ok" : "warning",
            Directory.Exists(logPath)
                ? $"日志目录可用：{logPath}"
                : $"日志目录不存在：{logPath}"),
        new("export", "输出目录", Directory.Exists(exportPath) ? "ok" : "warning",
            Directory.Exists(exportPath)
                ? $"输出目录可用：{exportPath}"
                : $"输出目录尚未创建：{exportPath}"),
        new("ai", "AI 配置",
            currentConfig.EnableAI
                ? (!string.IsNullOrWhiteSpace(currentConfig.DeepSeek.ApiKey) ? "ok" : "warning")
                : "warning",
            currentConfig.EnableAI
                ? (!string.IsNullOrWhiteSpace(currentConfig.DeepSeek.ApiKey)
                    ? $"AI 已启用，模型：{currentConfig.DeepSeek.Model}"
                    : $"AI 已启用，但未配置 API Key，当前将使用兜底文本。模型：{currentConfig.DeepSeek.Model}")
                : "AI 未启用，当前将使用兜底文本。"),
        new("license", "授权状态",
            license.Activated ? "ok" : (license.CanGenerate ? "warning" : "error"),
            license.Activated
                ? $"授权有效，类型：{license.LicenseType}"
                : $"{license.Message}，试用次数：{license.TrialUsed}/{license.TrialLimit}")
    };

    var okCount = items.Count(item => item.Status == "ok");
    var warningCount = items.Count(item => item.Status == "warning");
    var errorCount = items.Count(item => item.Status == "error");
    var overallStatus = errorCount > 0 ? "error" : (warningCount > 0 ? "warning" : "ok");

    return Results.Ok(new EnvironmentCheckResult(
        overallStatus,
        okCount,
        warningCount,
        errorCount,
        items));
});

app.MapPost("/api/generate/current", async (
    GenerateRequest request,
    ExcelGenerationService generationService) =>
{
    var result = await generationService.GenerateCurrentAsync(request);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/generate/batch", async (
    BatchGenerateRequest request,
    ExcelGenerationService generationService) =>
{
    var result = await generationService.GenerateBatchAsync(request);
    return Results.Ok(result);
});

app.MapPost("/api/ai/preview", async (
    AiPreviewRequest request,
    AiTextService aiTextService) =>
{
    var allowedFields = new[] { "申请语", "验收意见", "备注说明", "试验过程" };
    if (!allowedFields.Contains(request.FieldName))
    {
        return Results.BadRequest(new AiPreviewResult(
            false,
            request.FieldName,
            "",
            "AI只能生成申请语、验收意见、备注说明、试验过程等自由文本。"));
    }

    var text = await aiTextService.GenerateTextAsync(request.FieldName, request.Context);
    return Results.Ok(new AiPreviewResult(true, request.FieldName, text, "AI文本生成成功"));
});

app.MapPost("/api/license/activate", (
    ActivateLicenseRequest request,
    LicenseService licenseService) =>
{
    var result = licenseService.Activate(request.ActivationCode);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/api/license/status", (LicenseService licenseService) =>
{
    return Results.Ok(licenseService.GetStatus());
});

app.MapGet("/api/logs/latest", (AppConfig currentConfig, DirectoryInfo workspaceRoot) =>
{
    var logPath = currentConfig.GetLogPath(workspaceRoot);
    var files = Directory.Exists(logPath)
        ? Directory.GetFiles(logPath, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(5)
            .Select(file => new
            {
                file.Name,
                file.LastWriteTime,
                Lines = ReadLastLines(file.FullName, 60)
            })
            .ToArray()
        : [];

    return Results.Ok(new
    {
        success = true,
        logs = files
    });
});

try
{
    app.Run();
}
catch (IOException ex) when (ex.InnerException is SocketException or not null &&
                             ex.Message.Contains("5188", StringComparison.OrdinalIgnoreCase))
{
    Log.Error(ex, "服务启动失败：端口 5188 已被占用。");
    Console.WriteLine("工程资料生成服务已经在运行，或端口 5188 被其他程序占用。");
    Console.WriteLine("请不要重复双击 GeneratorService.exe；如需重启，请先结束已有 GeneratorService 进程。");
    Environment.ExitCode = 2;
}
catch (Exception ex)
{
    Log.Fatal(ex, "服务启动失败。");
    Console.WriteLine("工程资料生成服务启动失败，详情请查看 Logs/startup 日志。");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

static string[] ReadLastLines(string path, int count)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    var lines = new Queue<string>();
    while (reader.ReadLine() is { } line)
    {
        lines.Enqueue(line);
        while (lines.Count > count)
        {
            lines.Dequeue();
        }
    }

    return lines.ToArray();
}

static int CountTemplateNodes(TemplateLibraryNode node)
{
    var self = node.Type == "template" ? 1 : 0;
    return self + node.Children.Sum(CountTemplateNodes);
}

public sealed record OpenSpreadsheetRequest(string FilePath);

public sealed record OpenUrlRequest(string Url);
