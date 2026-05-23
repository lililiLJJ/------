using GeneratorService;
using GeneratorService.Ai;
using GeneratorService.Generation;
using GeneratorService.Knowledge;
using GeneratorService.Licensing;
using GeneratorService.Models;
using GeneratorService.Modules;
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
builder.Services.AddSingleton<TemplateTreeRepository>();
builder.Services.AddSingleton<TemplateTreeService>();
builder.Services.AddSingleton<GeneratedFormService>();
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
knowledgeRepository.EnsureCreated();
templateCatalog.EnsureSampleTemplates();
moduleManager.Scan();
templateTreeRepository.EnsureCreated();

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

app.MapGet("/api/template-library/tree", (string? projectId, TemplateTreeService service) =>
{
    return Results.Ok(service.GetTree(projectId));
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
    LicenseService licenseService) =>
{
    var templatePath = currentConfig.GetTemplatePath(workspaceRoot);
    var exportPath = currentConfig.GetExportPath(workspaceRoot);
    var knowledgeBasePath = currentConfig.GetKnowledgeBasePath(workspaceRoot);
    var logPath = currentConfig.GetLogPath(workspaceRoot);
    var license = licenseService.GetStatus();
    var templateCount = catalog.ListTemplates().Count;

    var items = new List<EnvironmentCheckItem>
    {
        new("service", "本地服务", "ok", $"服务已启动，监听端口 {currentConfig.Service.Port}"),
        new("templates", "模板目录", Directory.Exists(templatePath) ? "ok" : "error",
            Directory.Exists(templatePath)
                ? $"目录存在，当前识别到 {templateCount} 个模板"
                : $"目录不存在：{templatePath}"),
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
