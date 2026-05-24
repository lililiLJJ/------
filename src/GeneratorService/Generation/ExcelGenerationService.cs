using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Ai;
using GeneratorService.Knowledge;
using GeneratorService.Licensing;
using GeneratorService.Models;
using GeneratorService.Projects;
using GeneratorService.Templates;
using Serilog;

namespace GeneratorService.Generation;

public sealed class ExcelGenerationService
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(?<type>[^:}]+):(?<name>[^}]+)\}\}", RegexOptions.Compiled);

    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;
    private readonly TemplateCatalog _templateCatalog;
    private readonly KnowledgeRepository _knowledgeRepository;
    private readonly AiTextService _aiTextService;
    private readonly LicenseService _licenseService;
    private readonly ProjectManager _projectManager;

    public ExcelGenerationService(
        DirectoryInfo rootPath,
        AppConfig config,
        TemplateCatalog templateCatalog,
        KnowledgeRepository knowledgeRepository,
        AiTextService aiTextService,
        LicenseService licenseService,
        ProjectManager projectManager)
    {
        _rootPath = rootPath;
        _config = config;
        _templateCatalog = templateCatalog;
        _knowledgeRepository = knowledgeRepository;
        _aiTextService = aiTextService;
        _licenseService = licenseService;
        _projectManager = projectManager;
    }

    public async Task<GenerationResult> GenerateCurrentAsync(GenerateRequest request)
    {
        return await GenerateAsync(request, avoidOverwrite: false);
    }

    private async Task<GenerationResult> GenerateAsync(GenerateRequest request, bool avoidOverwrite)
    {
        var license = _licenseService.GetStatus();
        if (!license.CanGenerate)
        {
            return new GenerationResult(false, [], license.Message);
        }

        try
        {
            var templatePath = _templateCatalog.ResolveTemplate(request.TemplateName);
            var exportDirectory = ResolveExportPath(request.ExportPath);
            Directory.CreateDirectory(exportDirectory);

            var fileName = BuildOutputFileName(request);
            var outputPath = avoidOverwrite
                ? ResolveUniqueOutputPath(exportDirectory, fileName)
                : Path.Combine(exportDirectory, fileName);
            File.Copy(templatePath, outputPath, overwrite: !avoidOverwrite);

            await ReplaceWorkbookTextAsync(outputPath, request);
            _licenseService.RecordGeneration();
            Log.Information("生成成功。Template={Template}, Output={Output}", request.TemplateName, outputPath);
            return new GenerationResult(true, [outputPath], "生成成功");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "生成失败。Project={ProjectName}, TemplateType={TemplateType}", request.ProjectName, request.TemplateType);
            return new GenerationResult(false, [], ex.Message);
        }
    }

    public async Task<BatchGenerationResult> GenerateBatchAsync(BatchGenerateRequest request)
    {
        var files = new List<string>();
        var failures = new List<BatchFailure>();
        var enabledItems = request.Items.Where(item => item.Enabled).ToArray();

        for (var index = 0; index < enabledItems.Length; index++)
        {
            var item = enabledItems[index];
            var current = item.BaseRequest with
            {
                ConstructionDate = item.Date,
                AcceptanceDate = item.Date,
                TemplateName = string.IsNullOrWhiteSpace(item.TemplateName) ? item.BaseRequest.TemplateName : item.TemplateName,
                TemplateType = string.IsNullOrWhiteSpace(item.MaterialType) ? item.BaseRequest.TemplateType : item.MaterialType
            };

            var result = await GenerateAsync(current, avoidOverwrite: true);
            if (result.Success)
            {
                files.AddRange(result.Files);
            }
            else
            {
                failures.Add(new BatchFailure(index + 1, result.Message));
            }
        }

        return new BatchGenerationResult(enabledItems.Length, files.Count, failures.Count, files, failures);
    }

    private async Task<string> ReplacePlaceholdersAsync(string text, GenerateRequest request)
    {
        var result = text;
        foreach (Match match in PlaceholderRegex.Matches(text))
        {
            var placeholder = match.Value;
            var type = match.Groups["type"].Value.Trim();
            var name = match.Groups["name"].Value.Trim();
            var value = type switch
            {
                "静态" => ResolveStaticValue(name, request),
                "系统" => ResolveSystemValue(name),
                "规范" => ResolveKnowledgeValue(name, request),
                "AI" => await _aiTextService.GenerateTextAsync(name, request),
                _ => throw new InvalidOperationException($"未知占位符类型：{type}")
            };

            result = result.Replace(placeholder, value);
        }

        return result;
    }

    private async Task ReplaceWorkbookTextAsync(string outputPath, GenerateRequest request)
    {
        using var document = SpreadsheetDocument.Open(outputPath, true);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少WorkbookPart。");

        if (workbookPart.SharedStringTablePart?.SharedStringTable is { } sharedStringTable)
        {
            foreach (var item in sharedStringTable.Elements<SharedStringItem>())
            {
                var text = item.InnerText;
                if (!text.Contains("{{", StringComparison.Ordinal))
                {
                    continue;
                }

                var replacement = await ReplacePlaceholdersAsync(text, request);
                item.RemoveAllChildren();
                item.AppendChild(new Text(replacement)
                {
                    Space = SpaceProcessingModeValues.Preserve
                });
            }

            sharedStringTable.Save();
        }

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            foreach (var cell in worksheetPart.Worksheet.Descendants<Cell>())
            {
                if (cell.InlineString?.Text?.Text is { } inlineText &&
                    inlineText.Contains("{{", StringComparison.Ordinal))
                {
                    cell.InlineString.Text.Text = await ReplacePlaceholdersAsync(inlineText, request);
                }

                if (cell.CellValue?.Text is { } cellText &&
                    cellText.Contains("{{", StringComparison.Ordinal) &&
                    cell.DataType?.Value == CellValues.String)
                {
                    cell.CellValue.Text = await ReplacePlaceholdersAsync(cellText, request);
                }
            }

            worksheetPart.Worksheet.Save();
        }
    }

    private static string ResolveStaticValue(string name, GenerateRequest request)
    {
        return name switch
        {
            "工程名称" => request.ProjectName,
            "建设单位" or "建设单位名称" => request.DeveloperUnit.Name,
            "施工单位" or "施工单位名称" => request.ConstructorUnit.Name,
            "设计单位" or "设计单位名称" => request.DesignUnit.Name,
            "监理单位" or "监理单位名称" => request.SupervisorUnit.Name,
            "专业分包单位" or "专业分包单位名称" => request.ProfessionalSubcontractorUnit.Name,
            "第三方检测单位" or "第三方检测单位名称" => request.ThirdPartyInspectionUnit.Name,
            "建设单位项目负责人" => request.DeveloperUnit.ProjectManager,
            "建设单位技术负责人" => request.DeveloperUnit.TechnicalManager,
            "建设单位单位技术负责人" => request.DeveloperUnit.UnitTechnicalManager,
            "施工单位项目负责人" => request.ConstructorUnit.ProjectManager,
            "施工单位技术负责人" => request.ConstructorUnit.TechnicalManager,
            "施工单位单位技术负责人" => request.ConstructorUnit.UnitTechnicalManager,
            "设计单位项目负责人" => request.DesignUnit.ProjectManager,
            "设计单位技术负责人" => request.DesignUnit.TechnicalManager,
            "设计单位单位技术负责人" => request.DesignUnit.UnitTechnicalManager,
            "监理单位项目负责人" => request.SupervisorUnit.ProjectManager,
            "监理单位技术负责人" => request.SupervisorUnit.TechnicalManager,
            "监理单位单位技术负责人" => request.SupervisorUnit.UnitTechnicalManager,
            "监理单位专业监理工程师" => request.SupervisorUnit.ProfessionalSupervisorEngineer,
            "监理单位总监理工程师" => request.SupervisorUnit.ChiefSupervisorEngineer,
            "专业分包单位项目负责人" => request.ProfessionalSubcontractorUnit.ProjectManager,
            "专业分包单位技术负责人" => request.ProfessionalSubcontractorUnit.TechnicalManager,
            "专业分包单位单位技术负责人" => request.ProfessionalSubcontractorUnit.UnitTechnicalManager,
            "第三方检测单位项目负责人" => request.ThirdPartyInspectionUnit.ProjectManager,
            "第三方检测单位技术负责人" => request.ThirdPartyInspectionUnit.TechnicalManager,
            "第三方检测单位单位技术负责人" => request.ThirdPartyInspectionUnit.UnitTechnicalManager,
            "检验批容量" => request.Capacity,
            "施工日期" => request.ConstructionDate.ToString("yyyy-MM-dd"),
            "验收日期" => request.AcceptanceDate.ToString("yyyy-MM-dd"),
            "模板类型" => request.TemplateType,
            _ => throw new InvalidOperationException($"未知静态字段：{name}")
        };
    }

    private static string ResolveSystemValue(string name)
    {
        return name switch
        {
            "当前日期" => DateTime.Today.ToString("yyyy-MM-dd"),
            "编号" => $"GD-{DateTime.Now:yyyyMMddHHmmss}",
            "页码" => "1",
            _ => throw new InvalidOperationException($"未知系统字段：{name}")
        };
    }

    private string ResolveKnowledgeValue(string name, GenerateRequest request)
    {
        _ = name switch
        {
            "主控项目表" => "主控项目",
            "一般项目表" => "一般项目",
            "允许偏差表" => "允许偏差",
            _ => throw new InvalidOperationException($"未知规范字段：{name}")
        };

        throw new InvalidOperationException($"资料生成已取消分部工程和分项名称，无法解析规范字段：{name}");
    }

    private string ResolveExportPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return requestedPath;
        }

        return _projectManager.GetCurrentProject().ExportPath;
    }

    private static string BuildOutputFileName(GenerateRequest request)
    {
        var rawName = $"{request.ProjectName}-{request.TemplateType}-{request.ConstructionDate:yyyyMMdd}.xlsx";
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            rawName = rawName.Replace(invalid, '_');
        }

        return rawName;
    }

    private static string ResolveUniqueOutputPath(string directory, string fileName)
    {
        var outputPath = Path.Combine(directory, fileName);
        if (!File.Exists(outputPath))
        {
            return outputPath;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            outputPath = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(outputPath))
            {
                return outputPath;
            }
        }
    }
}
