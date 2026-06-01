using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Models;
using GeneratorService.Projects;

namespace GeneratorService.TemplateLibrary;

public sealed class GeneratedFormService
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(?<type>[^:}]+):(?<name>[^}]+)\}\}", RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, string[]> FieldAliases = new Dictionary<string, string[]>
    {
        ["projectName"] = ["工程名称", "项目名称"],
        ["developerUnitName"] = ["建设单位"],
        ["constructorUnitName"] = ["施工单位", "承包单位"],
        ["designUnitName"] = ["设计单位"],
        ["supervisorUnitName"] = ["监理单位"],
        ["professionalSubcontractorUnitName"] = ["专业分包单位"],
        ["thirdPartyInspectionUnitName"] = ["第三方检测单位", "检测单位"],
        ["partName"] = ["部位名称", "检验批部位", "施工部位"],
        ["capacity"] = ["检验批容量"],
        ["constructionDate"] = ["施工日期"],
        ["acceptanceDate"] = ["验收日期"]
    };

    private readonly TemplateTreeRepository _repository;
    private readonly TemplateService _templateService;
    private readonly ProjectManager _projectManager;
    private readonly UnitProjectService _unitProjectService;
    private readonly ProjectPathResolver _pathResolver;
    private readonly RowHeightBalanceService _rowHeightBalanceService;
    private readonly TemplateMappingService _templateMappingService;

    public GeneratedFormService(
        TemplateTreeRepository repository,
        TemplateService templateService,
        ProjectManager projectManager,
        UnitProjectService unitProjectService,
        ProjectPathResolver pathResolver,
        RowHeightBalanceService rowHeightBalanceService,
        TemplateMappingService templateMappingService)
    {
        _repository = repository;
        _templateService = templateService;
        _projectManager = projectManager;
        _unitProjectService = unitProjectService;
        _pathResolver = pathResolver;
        _rowHeightBalanceService = rowHeightBalanceService;
        _templateMappingService = templateMappingService;
    }

    public GeneratedFormInfo GetGeneratedForm(string nodeId)
    {
        return GetProjectDocument(nodeId, null, null);
    }

    public GeneratedFormInfo GetProjectDocument(string documentId, string? projectId, string? unitProjectId)
    {
        var document = _repository.GetProjectDocument(documentId);
        if (document is not null)
        {
            EnsureProjectDocumentAccess(document, projectId, unitProjectId);
            var documentPath = _repository.ResolveStoredPath(document.FilePath);
            if (!File.Exists(documentPath))
            {
                throw new FileNotFoundException($"资料表文件不存在：{documentPath}", documentPath);
            }

            return new GeneratedFormInfo(
                true,
                document.Id,
                document.DocumentName,
                document.TemplateItemId.ToString(),
                documentPath,
                true,
                BuildProjectDocumentParentId(document),
                document.Id,
                null,
                document.DocumentName);
        }

        var node = _repository.GetNode(documentId) ?? throw new FileNotFoundException("资料表节点不存在。", documentId);
        EnsureLegacyNodeAccess(node, projectId);
        if (node.NodeType != "document" || string.IsNullOrWhiteSpace(node.GeneratedFilePath))
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
            true,
            node.TemplateNodeId ?? node.ParentId,
            node.Id,
            node.TemplateName,
            node.FormName ?? node.Name);
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

        var project = _projectManager.ResolveProject(request.ProjectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, request.UnitProjectId);
        var template = _templateService.ResolveTemplate(request.TemplateNodeId);
        var templateCode = template.TemplateCode;
        var targetDirectory = project.IsDefault
            ? Path.Combine(
                _repository.GetProjectsRootPath(),
                SanitizePathSegment(project.ProjectId),
                "GeneratedForms",
                SanitizePathSegment(templateCode))
            : _pathResolver.ResolveGeneratedFormDirectory(project, request.TemplateNodeId, template.TemplateName);
        Directory.CreateDirectory(targetDirectory);

        var templateExtension = Path.GetExtension(template.TemplatePath);
        if (string.IsNullOrWhiteSpace(templateExtension))
        {
            templateExtension = ".xlsx";
        }

        var fields = request.Fields ?? new Dictionary<string, string>();
        var targetPath = ResolveUniquePath(targetDirectory, $"{SanitizePathSegment(request.FormName)}{templateExtension}");
        File.Copy(template.TemplatePath, targetPath);
        var rowHeightBaseline = _rowHeightBalanceService.CaptureBaseline(targetPath);
        if (_templateMappingService.ShouldUseAdaptation(template))
        {
            var layoutBaseline = TemplateWorkbookHelper.CaptureLayoutSnapshot(targetPath);
            _templateMappingService.Apply(targetPath, template, fields);
            _rowHeightBalanceService.ApplyLight(targetPath, request.FormName, fields, rowHeightBaseline);
            var layoutResult = TemplateWorkbookHelper.CompareLayout(layoutBaseline, targetPath);
            if (!layoutResult.Success)
            {
                throw new TemplateAdaptationException(
                    $"模板“{template.TemplateName}”写入后版式保护校验失败：{string.Join("；", layoutResult.Differences)}",
                    template.TemplateNodeId,
                    template.TemplateName,
                    adaptationStatus: "layout_changed");
            }
        }
        else
        {
            ApplyFields(targetPath, request.FormName, fields);
            _rowHeightBalanceService.ApplyLight(targetPath, request.FormName, fields, rowHeightBaseline);
        }

        var node = template.ModuleId == "legacy"
            ? _repository.InsertGeneratedForm(
                project.ProjectId,
                template.TemplateNodeId,
                request.FormName.Trim(),
                templateCode,
                targetPath)
            : _repository.InsertProjectDocument(
                project.ProjectId,
                unitProject.Id,
                template.ModuleId,
                template.TemplateItemId,
                template.TemplateNodeId,
                request.FormName.Trim(),
                request.FormName.Trim(),
                GetField(fields, "capacity", "检验批容量"),
                templateCode,
                targetPath);

        return new GeneratedFormCreateResult(true, node, targetPath, "资料表已创建。");
    }

    public DeleteGeneratedFormResult DeleteGeneratedForm(string nodeId)
    {
        return DeleteProjectDocument(nodeId, null, null);
    }

    public DeleteGeneratedFormResult DeleteProjectDocument(string documentId, string? projectId, string? unitProjectId)
    {
        return DeleteProjectDocumentCore(documentId, projectId, unitProjectId);
    }

    public BatchDeleteProjectDocumentsResult BatchDeleteProjectDocuments(
        BatchDeleteProjectDocumentsRequest request,
        string? projectId,
        string? unitProjectId)
    {
        var documentIds = request.DocumentIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (documentIds.Length == 0)
        {
            throw new InvalidOperationException("请至少选择一个资料表。");
        }

        var deletedIds = new List<string>();
        var failedItems = new List<BatchDeleteProjectDocumentsFailedItem>();
        foreach (var documentId in documentIds)
        {
            var result = DeleteProjectDocumentCore(documentId, projectId, unitProjectId);
            if (result.Success)
            {
                deletedIds.Add(result.DocumentId ?? documentId);
                continue;
            }

            failedItems.Add(new BatchDeleteProjectDocumentsFailedItem(
                result.DocumentId ?? documentId,
                result.Message,
                result.FormName));
        }

        var message = failedItems.Count == 0
            ? $"已删除 {deletedIds.Count} 个资料表"
            : $"成功删除 {deletedIds.Count} 个，失败 {failedItems.Count} 个";
        return new BatchDeleteProjectDocumentsResult(
            failedItems.Count == 0,
            deletedIds,
            failedItems,
            message);
    }

    public GeneratedFormBackupResult BackupGeneratedForm(string nodeId)
    {
        var info = GetGeneratedForm(nodeId);
        var sourcePath = Path.GetFullPath(info.GeneratedFilePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"资料表文件不存在：{sourcePath}", sourcePath);
        }

        var project = _projectManager.GetCurrentProject();
        var backupDirectory = Path.Combine(project.VersionsPath, "RowHeightFit");
        Directory.CreateDirectory(backupDirectory);

        var now = DateTimeOffset.Now;
        var backupId = $"row-height-{now:yyyyMMddHHmmssfff}";
        var extension = Path.GetExtension(sourcePath);
        var backupFileName = $"{Path.GetFileNameWithoutExtension(sourcePath)}.{backupId}{extension}";
        var backupPath = Path.Combine(backupDirectory, backupFileName);

        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var target = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(target);
        }

        return new GeneratedFormBackupResult(
            true,
            backupId,
            sourcePath,
            backupPath,
            now,
            "已备份当前资料表。");
    }

    internal static void ApplyFields(string filePath, string formName, IReadOnlyDictionary<string, string> fields)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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

        foreach (var alias in FieldAliases["partName"])
        {
            replacements[alias] = formName;
        }

        foreach (var item in FieldAliases)
        {
            var value = GetField(fields, item.Key, item.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var alias in item.Value)
            {
                replacements[alias] = value;
            }
        }

        using var document = SpreadsheetDocument.Open(filePath, true);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少 WorkbookPart。");

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

            ApplyAdjacentLabelFields(worksheetPart, workbookPart.SharedStringTablePart?.SharedStringTable, replacements);
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

    private static void ApplyAdjacentLabelFields(
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStringTable,
        IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var labelCell in worksheetPart.Worksheet.Descendants<Cell>().ToArray())
        {
            var labelText = ReadCellText(labelCell, sharedStringTable);
            if (!TryGetAdjacentFieldValue(labelText, replacements, out var value))
            {
                continue;
            }

            var targetReference = GetNextColumnReference(labelCell.CellReference?.Value);
            if (targetReference is null)
            {
                continue;
            }

            var targetCell = GetOrCreateCell(worksheetPart.Worksheet, targetReference);
            var targetText = ReadCellText(targetCell, sharedStringTable);
            if (!string.IsNullOrWhiteSpace(targetText) && !targetText.Contains("{{", StringComparison.Ordinal))
            {
                continue;
            }

            WriteCellText(targetCell, value);
        }
    }

    private static bool TryGetAdjacentFieldValue(
        string labelText,
        IReadOnlyDictionary<string, string> replacements,
        out string value)
    {
        var normalizedLabel = NormalizeLabel(labelText);
        foreach (var item in replacements)
        {
            if (string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            var normalizedKey = NormalizeLabel(item.Key);
            if (normalizedLabel == normalizedKey || normalizedLabel.Contains(normalizedKey, StringComparison.Ordinal))
            {
                value = item.Value;
                return true;
            }
        }

        value = "";
        return false;
    }

    private static string ReadCellText(Cell cell, SharedStringTable? sharedStringTable)
    {
        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(cell.CellValue?.Text, out var sharedStringIndex) &&
            sharedStringTable is not null)
        {
            return sharedStringTable.Elements<SharedStringItem>().ElementAtOrDefault(sharedStringIndex)?.InnerText ?? "";
        }

        if (cell.InlineString is not null)
        {
            return cell.InlineString.InnerText ?? "";
        }

        return cell.CellValue?.Text ?? "";
    }

    private static void WriteCellText(Cell cell, string value)
    {
        cell.DataType = CellValues.String;
        cell.CellValue = new CellValue(value);
        cell.InlineString = null;
    }

    private static Cell GetOrCreateCell(Worksheet worksheet, string cellReference)
    {
        var rowIndex = GetRowIndex(cellReference);
        var sheetData = worksheet.GetFirstChild<SheetData>() ?? worksheet.AppendChild(new SheetData());
        var row = sheetData.Elements<Row>().FirstOrDefault(item => item.RowIndex?.Value == rowIndex);
        if (row is null)
        {
            row = new Row { RowIndex = rowIndex };
            sheetData.Append(row);
        }

        var cell = row.Elements<Cell>().FirstOrDefault(item => item.CellReference?.Value == cellReference);
        if (cell is not null)
        {
            return cell;
        }

        cell = new Cell { CellReference = cellReference };
        var nextCell = row.Elements<Cell>()
            .FirstOrDefault(item => string.Compare(item.CellReference?.Value, cellReference, StringComparison.OrdinalIgnoreCase) > 0);
        if (nextCell is null)
        {
            row.Append(cell);
        }
        else
        {
            row.InsertBefore(cell, nextCell);
        }

        return cell;
    }

    private static string? GetNextColumnReference(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return null;
        }

        var columnName = new string(cellReference.TakeWhile(char.IsLetter).ToArray());
        var rowName = new string(cellReference.SkipWhile(char.IsLetter).ToArray());
        if (string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(rowName))
        {
            return null;
        }

        return $"{IncrementColumn(columnName)}{rowName}";
    }

    private static uint GetRowIndex(string cellReference)
    {
        var rowName = new string(cellReference.SkipWhile(char.IsLetter).ToArray());
        return uint.TryParse(rowName, out var rowIndex) ? rowIndex : 1;
    }

    private static string IncrementColumn(string columnName)
    {
        var chars = columnName.ToUpperInvariant().ToCharArray();
        for (var index = chars.Length - 1; index >= 0; index--)
        {
            if (chars[index] < 'Z')
            {
                chars[index]++;
                return new string(chars);
            }

            chars[index] = 'A';
        }

        return "A" + new string(chars);
    }

    private static string NormalizeLabel(string value)
    {
        return value
            .Replace(":", "", StringComparison.Ordinal)
            .Replace("：", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Trim();
    }

    private static string GetField(IReadOnlyDictionary<string, string> fields, string englishKey, string chineseKey)
    {
        if (fields.TryGetValue(englishKey, out var englishValue))
        {
            return englishValue;
        }

        return fields.TryGetValue(chineseKey, out var chineseValue) ? chineseValue : "";
    }

    private static string GetField(IReadOnlyDictionary<string, string> fields, string englishKey, IEnumerable<string> chineseKeys)
    {
        if (fields.TryGetValue(englishKey, out var englishValue))
        {
            return englishValue;
        }

        foreach (var chineseKey in chineseKeys)
        {
            if (fields.TryGetValue(chineseKey, out var chineseValue))
            {
                return chineseValue;
            }
        }

        return "";
    }

    private DeleteGeneratedFormResult DeleteProjectDocumentCore(string documentId, string? projectId, string? unitProjectId)
    {
        var document = _repository.GetProjectDocument(documentId);
        if (document is not null)
        {
            EnsureProjectDocumentAccess(document, projectId, unitProjectId);
            var parentId = BuildProjectDocumentParentId(document);
            var generatedFilePath = _repository.ResolveStoredPath(document.FilePath);
            try
            {
                var fileDeleted = DeleteGeneratedFile(generatedFilePath);
                var projectDocumentDeleted = _repository.DeleteProjectDocument(documentId);
                return new DeleteGeneratedFormResult(
                    projectDocumentDeleted,
                    documentId,
                    documentId,
                    parentId,
                    projectDocumentDeleted ? "资料表已删除。" : "资料表记录不存在。",
                    document.DocumentName,
                    generatedFilePath,
                    fileDeleted,
                    projectDocumentDeleted,
                    false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new DeleteGeneratedFormResult(
                    false,
                    documentId,
                    documentId,
                    parentId,
                    ex.Message,
                    document.DocumentName,
                    generatedFilePath);
            }
        }

        var legacyNode = _repository.GetNode(documentId);
        if (legacyNode is null)
        {
            return new DeleteGeneratedFormResult(false, documentId, documentId, null, "资料表节点不存在。");
        }

        EnsureLegacyNodeAccess(legacyNode, projectId);
        if (legacyNode.NodeType != "document")
        {
            return new DeleteGeneratedFormResult(false, documentId, documentId, legacyNode.ParentId, "当前节点不是已创建的资料表。");
        }

        var legacyFilePath = ResolveLegacyGeneratedFilePath(legacyNode);
        try
        {
            var fileDeleted = DeleteGeneratedFile(legacyFilePath);
            var legacyNodeDeleted = _repository.DeleteGeneratedForm(documentId);
            return new DeleteGeneratedFormResult(
                legacyNodeDeleted,
                documentId,
                documentId,
                legacyNode.ParentId,
                legacyNodeDeleted ? "资料表已删除。" : "资料表节点不存在。",
                legacyNode.FormName ?? legacyNode.Name,
                legacyFilePath,
                fileDeleted,
                false,
                legacyNodeDeleted);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DeleteGeneratedFormResult(
                false,
                documentId,
                documentId,
                legacyNode.ParentId,
                ex.Message,
                legacyNode.FormName ?? legacyNode.Name,
                legacyFilePath);
        }
    }

    private static bool DeleteGeneratedFile(string? generatedFilePath)
    {
        if (string.IsNullOrWhiteSpace(generatedFilePath) || !File.Exists(generatedFilePath))
        {
            return false;
        }

        File.Delete(generatedFilePath);
        return true;
    }

    private static void EnsureProjectDocumentAccess(ProjectDocumentInfo document, string? projectId, string? unitProjectId)
    {
        if (!string.IsNullOrWhiteSpace(projectId) &&
            !string.Equals(document.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("资料表不属于当前工程。");
        }

        if (!string.IsNullOrWhiteSpace(unitProjectId) &&
            !string.Equals(document.UnitProjectId, unitProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("资料表不属于当前单位工程。");
        }
    }

    private static void EnsureLegacyNodeAccess(TemplateTreeNodeDto node, string? projectId)
    {
        if (!string.IsNullOrWhiteSpace(projectId) &&
            !string.Equals(node.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("资料表不属于当前工程。");
        }
    }

    private static string BuildProjectDocumentParentId(ProjectDocumentInfo document)
    {
        return $"module:{document.ModuleId}:template:{document.TemplateItemId}";
    }

    private string? ResolveLegacyGeneratedFilePath(TemplateTreeNodeDto? legacyNode)
    {
        if (legacyNode is null || string.IsNullOrWhiteSpace(legacyNode.GeneratedFilePath))
        {
            return null;
        }

        return _repository.ResolveStoredPath(legacyNode.GeneratedFilePath);
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
