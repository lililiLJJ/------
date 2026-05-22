using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Ai;
using GeneratorService.Knowledge;
using GeneratorService.Licensing;
using GeneratorService.Models;
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

    public ExcelGenerationService(
        DirectoryInfo rootPath,
        AppConfig config,
        TemplateCatalog templateCatalog,
        KnowledgeRepository knowledgeRepository,
        AiTextService aiTextService,
        LicenseService licenseService)
    {
        _rootPath = rootPath;
        _config = config;
        _templateCatalog = templateCatalog;
        _knowledgeRepository = knowledgeRepository;
        _aiTextService = aiTextService;
        _licenseService = licenseService;
    }

    public async Task<GenerationResult> GenerateCurrentAsync(GenerateRequest request)
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
            var outputPath = Path.Combine(exportDirectory, fileName);
            File.Copy(templatePath, outputPath, overwrite: true);

            await ReplaceWorkbookTextAsync(outputPath, request);
            _licenseService.RecordGeneration();
            Log.Information("生成成功。Template={Template}, Output={Output}", request.TemplateName, outputPath);
            return new GenerationResult(true, [outputPath], "生成成功");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "生成失败。Project={ProjectName}, SubItem={SubItem}, Location={Location}", request.ProjectName, request.SubItem, request.Location);
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
                SubItem = string.IsNullOrWhiteSpace(item.SubItem) ? item.BaseRequest.SubItem : item.SubItem,
                Location = string.IsNullOrWhiteSpace(item.Location) ? item.BaseRequest.Location : item.Location,
                ConstructionDate = item.Date,
                AcceptanceDate = item.Date,
                TemplateName = string.IsNullOrWhiteSpace(item.TemplateName) ? item.BaseRequest.TemplateName : item.TemplateName,
                TemplateType = string.IsNullOrWhiteSpace(item.MaterialType) ? item.BaseRequest.TemplateType : item.MaterialType
            };

            var result = await GenerateCurrentAsync(current);
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
            "施工单位" => request.Constructor,
            "监理单位" => request.Supervisor,
            "施工部位" => request.Location,
            "分部工程" => request.Division,
            "分项名称" => request.SubItem,
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
        var itemType = name switch
        {
            "主控项目表" => "主控项目",
            "一般项目表" => "一般项目",
            "允许偏差表" => "允许偏差",
            _ => throw new InvalidOperationException($"未知规范字段：{name}")
        };

        var items = _knowledgeRepository.QueryItems(request.Division, request.SubItem, itemType);
        if (items.Count == 0)
        {
            throw new InvalidOperationException($"知识库缺少：{request.SubItem}-{name}");
        }

        return string.Join(Environment.NewLine, items.Select((item, index) =>
        {
            var deviation = string.IsNullOrWhiteSpace(item.AllowableDeviation) ? "" : $"；允许偏差：{item.AllowableDeviation}";
            return $"{index + 1}. {item.ItemName}：{item.QualifiedStandard}{deviation}；检查方法：{item.CheckMethod}；依据：{item.StandardCode}（{item.StandardVersion}）";
        }));
    }

    private string ResolveExportPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return requestedPath;
        }

        return _config.GetExportPath(_rootPath);
    }

    private static string BuildOutputFileName(GenerateRequest request)
    {
        var rawName = $"{request.ProjectName}-{request.SubItem}-{request.Location}-{request.TemplateType}.xlsx";
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            rawName = rawName.Replace(invalid, '_');
        }

        return rawName;
    }
}
