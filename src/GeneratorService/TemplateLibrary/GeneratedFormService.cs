using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Models;

namespace GeneratorService.TemplateLibrary;

public sealed class GeneratedFormService
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(?<type>[^:}]+):(?<name>[^}]+)\}\}", RegexOptions.Compiled);

    private readonly TemplateTreeRepository _repository;

    public GeneratedFormService(TemplateTreeRepository repository)
    {
        _repository = repository;
    }

    public GeneratedFormInfo GetGeneratedForm(string nodeId)
    {
        var node = _repository.GetNode(nodeId) ?? throw new FileNotFoundException("资料表节点不存在。", nodeId);
        if (node.NodeType != "generated_form" || string.IsNullOrWhiteSpace(node.GeneratedFilePath))
        {
            throw new InvalidOperationException("当前节点不是已创建的资料表。");
        }

        var filePath = _repository.ResolveStoredPath(node.GeneratedFilePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"资料表文件不存在：{filePath}", filePath);
        }

        return new GeneratedFormInfo(
            true,
            node.Id,
            node.Name,
            node.TemplateCode ?? "",
            filePath,
            true);
    }

    public GeneratedFormCreateResult CreateGeneratedForm(CreateGeneratedFormRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new InvalidOperationException("项目ID不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.FormName))
        {
            throw new InvalidOperationException("部位名称不能为空。");
        }

        var templateNode = _repository.GetNode(request.TemplateNodeId)
            ?? throw new FileNotFoundException("模板节点不存在。", request.TemplateNodeId);
        if (templateNode.NodeType != "template" || string.IsNullOrWhiteSpace(templateNode.TemplateFilePath))
        {
            throw new InvalidOperationException("请选择检验批模板节点后再新建资料。");
        }

        var templatePath = _repository.ResolveStoredPath(templateNode.TemplateFilePath);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"模板文件不存在：{templatePath}", templatePath);
        }

        var templateCode = string.IsNullOrWhiteSpace(templateNode.TemplateCode)
            ? "template"
            : templateNode.TemplateCode;
        var targetDirectory = Path.Combine(
            _repository.GetProjectsRootPath(),
            SanitizePathSegment(request.ProjectId),
            "GeneratedForms",
            SanitizePathSegment(templateCode));
        Directory.CreateDirectory(targetDirectory);

        var targetPath = ResolveUniquePath(targetDirectory, $"{SanitizePathSegment(request.FormName)}.xlsx");
        File.Copy(templatePath, targetPath);
        ApplyFields(targetPath, request.FormName, request.Fields ?? new Dictionary<string, string>());

        var node = _repository.InsertGeneratedForm(
            request.ProjectId.Trim(),
            templateNode.Id,
            request.FormName.Trim(),
            templateCode,
            targetPath);

        return new GeneratedFormCreateResult(true, node, targetPath, "资料表已创建。");
    }

    public DeleteGeneratedFormResult DeleteGeneratedForm(string nodeId)
    {
        var info = GetGeneratedForm(nodeId);
        if (File.Exists(info.GeneratedFilePath))
        {
            File.Delete(info.GeneratedFilePath);
        }

        var deleted = _repository.DeleteGeneratedForm(nodeId);
        return new DeleteGeneratedFormResult(deleted, nodeId, deleted ? "资料表已删除。" : "资料表节点不存在。");
    }

    private static void ApplyFields(string filePath, string formName, IReadOnlyDictionary<string, string> fields)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["部位名称"] = formName,
            ["检验批部位"] = formName,
            ["施工部位"] = formName,
            ["检验批容量"] = GetField(fields, "capacity", "检验批容量"),
            ["施工日期"] = GetField(fields, "constructionDate", "施工日期"),
            ["验收日期"] = GetField(fields, "acceptanceDate", "验收日期")
        };

        foreach (var item in fields)
        {
            replacements[item.Key] = item.Value;
        }

        using var document = SpreadsheetDocument.Open(filePath, true);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少WorkbookPart。");

        if (workbookPart.SharedStringTablePart?.SharedStringTable is { } sharedStringTable)
        {
            foreach (var item in sharedStringTable.Elements<SharedStringItem>())
            {
                var replacement = ReplacePlaceholders(item.InnerText, replacements);
                if (replacement == item.InnerText)
                {
                    continue;
                }

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
                if (cell.InlineString?.Text?.Text is { } inlineText)
                {
                    cell.InlineString.Text.Text = ReplacePlaceholders(inlineText, replacements);
                }

                if (cell.CellValue?.Text is { } cellText &&
                    cell.DataType?.Value == CellValues.String)
                {
                    cell.CellValue.Text = ReplacePlaceholders(cellText, replacements);
                }
            }

            worksheetPart.Worksheet.Save();
        }
    }

    private static string ReplacePlaceholders(string text, IReadOnlyDictionary<string, string> replacements)
    {
        if (!text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        return PlaceholderRegex.Replace(text, match =>
        {
            var type = match.Groups["type"].Value.Trim();
            var name = match.Groups["name"].Value.Trim();
            if (type == "系统" && name == "当前日期")
            {
                return DateTime.Today.ToString("yyyy-MM-dd");
            }

            if (type == "系统" && name == "编号")
            {
                return $"GD-{DateTime.Now:yyyyMMddHHmmss}";
            }

            return type == "静态" && replacements.TryGetValue(name, out var value)
                ? value
                : match.Value;
        });
    }

    private static string GetField(IReadOnlyDictionary<string, string> fields, string englishKey, string chineseKey)
    {
        if (fields.TryGetValue(englishKey, out var englishValue))
        {
            return englishValue;
        }

        return fields.TryGetValue(chineseKey, out var chineseValue) ? chineseValue : "";
    }

    private static string ResolveUniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            path = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }
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
