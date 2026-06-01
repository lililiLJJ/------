using System.Text;
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
    private readonly BatchPlanRepository _batchPlanRepository;
    private readonly TemplateService _templateService;
    private readonly ProjectManager _projectManager;
    private readonly UnitProjectService _unitProjectService;
    private readonly ProjectPathResolver _pathResolver;
    private readonly RowHeightBalanceService _rowHeightBalanceService;
    private readonly TemplateMappingService _templateMappingService;

    public GeneratedFormService(
        TemplateTreeRepository repository,
        BatchPlanRepository batchPlanRepository,
        TemplateService templateService,
        ProjectManager projectManager,
        UnitProjectService unitProjectService,
        ProjectPathResolver pathResolver,
        RowHeightBalanceService rowHeightBalanceService,
        TemplateMappingService templateMappingService)
    {
        _repository = repository;
        _batchPlanRepository = batchPlanRepository;
        _templateService = templateService;
        _projectManager = projectManager;
        _unitProjectService = unitProjectService;
        _pathResolver = pathResolver;
        _rowHeightBalanceService = rowHeightBalanceService;
        _templateMappingService = templateMappingService;
    }

    public GeneratedDocumentInfo GetDocument(string documentId, string? projectId, string? unitProjectId)
    {
        var document = _repository.GetGeneratedDocument(documentId)
            ?? throw new FileNotFoundException("资料索引不存在。", documentId);
        EnsureDocumentAccess(document, projectId, unitProjectId);

        var template = TryResolveTemplate(document.TemplateNodeId);
        var detail = string.Equals(document.DocumentType, "InspectionBatch", StringComparison.OrdinalIgnoreCase)
            ? _repository.GetInspectionBatchDetail(document.DocumentId)
            : null;

        return new GeneratedDocumentInfo(
            true,
            document.DocumentId,
            document.ProjectId,
            document.UnitProjectId,
            document.DocumentName,
            document.DocumentType,
            document.TemplateNodeId,
            template?.TemplateCode ?? "",
            _repository.ResolveStoredPath(document.FilePath),
            true,
            document.DocumentStatus,
            document.SyncStatus,
            string.IsNullOrWhiteSpace(document.SyncErrorMessage) ? null : document.SyncErrorMessage,
            template?.TemplateName,
            document.DocumentName,
            document.SourceType,
            document.SourceId,
            detail?.PlanId,
            detail?.PlanRowId,
            detail?.CapacitySummary,
            detail?.InspectionPart,
            detail?.ConstructionDate,
            document.LastSyncTime);
    }

    public IReadOnlyList<GeneratedDocumentIndexInfo> ListDocuments(
        string? projectId,
        string? unitProjectId,
        string? documentType = null,
        string? documentStatus = null)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, unitProjectId);
        return _repository.ListGeneratedDocuments(project.ProjectId, unitProject.Id, documentType, documentStatus);
    }

    public GeneratedDocumentCreateResult CreateDocument(CreateGeneratedDocumentRequest request, string? projectId = null)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, request.UnitProjectId);
        var template = _templateService.ResolveTemplate(request.TemplateNodeId);
        var targetDirectory = _pathResolver.ResolveGeneratedFormDirectory(project, request.TemplateNodeId, template.TemplateName);
        var fields = request.Fields ?? new Dictionary<string, string>();
        var targetPath = CreateSpreadsheetFile(template, request.DocumentName, targetDirectory, fields);

        var now = DateTimeOffset.Now;
        var documentId = $"generated-document:{Guid.NewGuid():N}";
        var document = new GeneratedDocumentIndexInfo(
            documentId,
            project.ProjectId,
            unitProject.Id,
            string.IsNullOrWhiteSpace(request.DocumentType) ? "InspectionBatch" : request.DocumentType.Trim(),
            request.DocumentName.Trim(),
            string.IsNullOrWhiteSpace(request.SourceType) ? "Manual" : request.SourceType.Trim(),
            string.IsNullOrWhiteSpace(request.SourceId) ? documentId : request.SourceId.Trim(),
            request.TemplateNodeId,
            _repository.ToStoredPath(targetPath),
            "Active",
            "Normal",
            "",
            now,
            now,
            now,
            null,
            null,
            null);
        _repository.InsertGeneratedDocument(document);

        if (string.Equals(document.SourceType, "InspectionBatchPlanRow", StringComparison.OrdinalIgnoreCase))
        {
            _repository.UpsertInspectionBatchDetail(new InspectionBatchDocumentDetailInfo(
                document.DocumentId,
                request.PlanId,
                request.PlanRowId ?? document.SourceId,
                GetField(fields, "partName", "检验批部位"),
                GetField(fields, "constructionDate", "施工日期"),
                GetField(fields, "capacity", "检验批容量"),
                request.TemplateNodeId));
        }

        return new GeneratedDocumentCreateResult(
            true,
            ToTreeNode(document, template.TemplateName, template.TemplateItemId, template.TemplateCode),
            targetPath,
            "资料表已创建。",
            documentId);
    }

    public GeneratedDocumentCreateResult CreateInspectionBatchDocument(
        ProjectContext project,
        UnitProjectInfo unitProject,
        InspectionBatchPlanDto plan,
        InspectionBatchPlanRowDto row,
        IReadOnlyDictionary<string, string>? requestFields,
        bool overwrite)
    {
        var activeDocument = _repository.GetActiveDocumentByPlanRowId(row.PlanRowId);
        if (activeDocument is not null && !overwrite)
        {
            throw new InvalidOperationException("该行已生成表格，请先删除或选择覆盖生成。");
        }

        if (activeDocument is not null)
        {
            UpdateDocumentStatus(activeDocument, "Replaced", moveFileToTrash: true, propagatePlanRowDeletion: false);
        }

        var template = _templateService.ResolveTemplate(row.TemplateNodeId);
        var targetDirectory = Path.Combine(
            project.GeneratedFormsPath,
            SanitizePathSegment(unitProject.UnitProjectName),
            "InspectionPlans",
            SanitizePathSegment(plan.PlanName));
        Directory.CreateDirectory(targetDirectory);

        var fields = BuildInspectionBatchFields(project, unitProject, row, requestFields);
        var documentName = BuildDocumentName(row);
        var targetPath = CreateSpreadsheetFile(template, documentName, targetDirectory, fields);
        var now = DateTimeOffset.Now;
        var document = new GeneratedDocumentIndexInfo(
            $"generated-document:{Guid.NewGuid():N}",
            project.ProjectId,
            unitProject.Id,
            "InspectionBatch",
            documentName,
            "InspectionBatchPlanRow",
            row.PlanRowId,
            row.TemplateNodeId,
            _repository.ToStoredPath(targetPath),
            "Active",
            "Normal",
            "",
            now,
            now,
            now,
            null,
            null,
            null);
        _repository.InsertGeneratedDocument(document);
        _repository.UpsertInspectionBatchDetail(new InspectionBatchDocumentDetailInfo(
            document.DocumentId,
            plan.PlanId,
            row.PlanRowId,
            row.InspectionPart,
            row.ConstructionDate,
            row.CapacitySummary,
            row.TemplateNodeId));
        _batchPlanRepository.UpdatePlanRowGenerateStatus(row.PlanRowId, "Generated");

        return new GeneratedDocumentCreateResult(
            true,
            ToTreeNode(document, row.TemplateName, row.TemplateItemId, template.TemplateCode),
            targetPath,
            "资料表已创建。",
            document.DocumentId);
    }

    public void SyncInspectionBatchDocument(
        string documentId,
        ProjectContext project,
        UnitProjectInfo unitProject,
        InspectionBatchPlanRowDto row,
        IReadOnlyDictionary<string, string>? requestFields = null)
    {
        var document = _repository.GetGeneratedDocument(documentId)
            ?? throw new InvalidOperationException("资料索引不存在。");
        if (!string.Equals(document.DocumentStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只有活动资料才能同步。");
        }

        try
        {
            var template = _templateService.ResolveTemplate(row.TemplateNodeId);
            var fields = BuildInspectionBatchFields(project, unitProject, row, requestFields);
            var path = _repository.ResolveStoredPath(document.FilePath);
            ApplyTemplateFields(path, template, row.InspectionPart, fields);

            _repository.UpdateDocument(document with
            {
                DocumentName = BuildDocumentName(row),
                SyncStatus = "Normal",
                SyncErrorMessage = "",
                LastSyncTime = DateTimeOffset.Now,
                UpdatedTime = DateTimeOffset.Now
            });
            var detail = _repository.GetInspectionBatchDetail(documentId);
            _repository.UpsertInspectionBatchDetail(new InspectionBatchDocumentDetailInfo(
                documentId,
                detail?.PlanId,
                row.PlanRowId,
                row.InspectionPart,
                row.ConstructionDate,
                row.CapacitySummary,
                row.TemplateNodeId));
            _batchPlanRepository.UpdatePlanRowGenerateStatus(row.PlanRowId, "Generated");
        }
        catch (Exception ex)
        {
            _repository.UpdateDocument(document with
            {
                SyncStatus = "WriteFailed",
                SyncErrorMessage = ex.Message,
                LastSyncTime = DateTimeOffset.Now,
                UpdatedTime = DateTimeOffset.Now
            });
            _batchPlanRepository.UpdatePlanRowGenerateStatus(row.PlanRowId, "NeedSync");
            throw;
        }
    }

    public DeleteGeneratedDocumentResult DeleteDocument(string documentId, string? projectId, string? unitProjectId)
    {
        var document = _repository.GetGeneratedDocument(documentId)
            ?? throw new InvalidOperationException("资料索引不存在。");
        EnsureDocumentAccess(document, projectId, unitProjectId);
        return UpdateDocumentStatus(document, "Deleted", moveFileToTrash: true, propagatePlanRowDeletion: true);
    }

    public BatchDeleteGeneratedDocumentsResult BatchDeleteDocuments(
        BatchDeleteGeneratedDocumentsRequest request,
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
        var failedItems = new List<BatchDeleteGeneratedDocumentsFailedItem>();
        foreach (var documentId in documentIds)
        {
            try
            {
                var result = DeleteDocument(documentId, projectId, unitProjectId);
                if (result.Success)
                {
                    deletedIds.Add(result.DocumentId);
                }
                else
                {
                    failedItems.Add(new BatchDeleteGeneratedDocumentsFailedItem(documentId, result.Message, result.DocumentName));
                }
            }
            catch (Exception ex)
            {
                failedItems.Add(new BatchDeleteGeneratedDocumentsFailedItem(documentId, ex.Message));
            }
        }

        return new BatchDeleteGeneratedDocumentsResult(
            failedItems.Count == 0,
            deletedIds,
            failedItems,
            failedItems.Count == 0
                ? $"已删除 {deletedIds.Count} 个资料表。"
                : $"成功删除 {deletedIds.Count} 个，失败 {failedItems.Count} 个。");
    }

    public GeneratedDocumentBackupResult BackupDocument(string documentId, string? projectId, string? unitProjectId)
    {
        var info = GetDocument(documentId, projectId, unitProjectId);
        var sourcePath = Path.GetFullPath(info.GeneratedFilePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"资料表文件不存在：{sourcePath}", sourcePath);
        }

        var project = _projectManager.ResolveProject(projectId ?? info.ProjectId);
        var backupDirectory = Path.Combine(project.VersionsPath, "GeneratedDocumentBackups");
        Directory.CreateDirectory(backupDirectory);

        var now = DateTimeOffset.Now;
        var backupId = $"generated-document-{now:yyyyMMddHHmmssfff}";
        var extension = Path.GetExtension(sourcePath);
        var backupFileName = $"{Path.GetFileNameWithoutExtension(sourcePath)}.{backupId}{extension}";
        var backupPath = Path.Combine(backupDirectory, backupFileName);

        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var target = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(target);
        }

        return new GeneratedDocumentBackupResult(true, backupId, sourcePath, backupPath, now, "资料表已备份。");
    }

    public GeneratedDocumentInfo RestoreDocument(string documentId, string? projectId, string? unitProjectId)
    {
        var document = _repository.GetGeneratedDocument(documentId)
            ?? throw new InvalidOperationException("资料索引不存在。");
        EnsureDocumentAccess(document, projectId, unitProjectId);
        if (string.IsNullOrWhiteSpace(document.RestoreToken))
        {
            throw new InvalidOperationException("当前资料没有可恢复的删除记录。");
        }

        var restoreInfo = ParseRestoreToken(document.RestoreToken);
        var originalPath = _repository.ResolveStoredPath(restoreInfo.OriginalStoredPath);
        var trashPath = _repository.ResolveStoredPath(restoreInfo.TrashStoredPath);
        Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);

        var syncStatus = "Normal";
        var syncErrorMessage = "";
        if (File.Exists(trashPath))
        {
            File.Move(trashPath, originalPath, overwrite: true);
        }
        else
        {
            syncStatus = "NeedSync";
            syncErrorMessage = "回收站文件不存在，请重新生成或重新同步。";
        }

        _repository.UpdateDocument(document with
        {
            FilePath = restoreInfo.OriginalStoredPath,
            DocumentStatus = "Active",
            SyncStatus = syncStatus,
            SyncErrorMessage = syncErrorMessage,
            DeleteTime = null,
            RestoreToken = null,
            LastSyncTime = DateTimeOffset.Now,
            UpdatedTime = DateTimeOffset.Now
        });

        if (string.Equals(document.SourceType, "InspectionBatchPlanRow", StringComparison.OrdinalIgnoreCase))
        {
            _batchPlanRepository.UpdatePlanRowLifecycleStatus(document.SourceId, "Active");
            _batchPlanRepository.UpdatePlanRowGenerateStatus(document.SourceId, syncStatus == "Normal" ? "Generated" : "NeedSync");
        }

        return GetDocument(documentId, projectId, unitProjectId);
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
                item.AppendChild(new Text(replacement) { Space = SpaceProcessingModeValues.Preserve });
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

                if (cell.CellValue?.Text is { } cellText && cell.DataType?.Value == CellValues.String)
                {
                    cell.CellValue.Text = ReplacePlaceholders(cellText, replacements);
                }
            }

            ApplyAdjacentLabelFields(worksheetPart, workbookPart.SharedStringTablePart?.SharedStringTable, replacements);
            worksheetPart.Worksheet.Save();
        }
    }

    private TemplateTreeNodeDto ToTreeNode(GeneratedDocumentIndexInfo document, string? templateName, long? templateItemId, string? templateCode)
    {
        return new TemplateTreeNodeDto(
            document.DocumentId,
            document.TemplateNodeId,
            document.ProjectId,
            document.DocumentName,
            "document",
            null,
            templateCode,
            null,
            _repository.ResolveStoredPath(document.FilePath),
            null,
            null,
            null,
            null,
            null,
            null,
            templateItemId,
            100000,
            [],
            document.TemplateNodeId,
            document.DocumentId,
            templateName,
            document.DocumentName,
            document.CreatedTime,
            document.UpdatedTime,
            null,
            null,
            null,
            document.DocumentStatus,
            document.SyncStatus);
    }

    private string CreateSpreadsheetFile(
        TemplateResolution template,
        string documentName,
        string targetDirectory,
        IReadOnlyDictionary<string, string> fields)
    {
        Directory.CreateDirectory(targetDirectory);
        var extension = Path.GetExtension(template.TemplatePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".xlsx";
        }

        var targetPath = ResolveUniquePath(targetDirectory, $"{SanitizePathSegment(documentName)}{extension}");
        File.Copy(template.TemplatePath, targetPath);
        ApplyTemplateFields(targetPath, template, documentName, fields);
        return targetPath;
    }

    private void ApplyTemplateFields(string targetPath, TemplateResolution template, string formName, IReadOnlyDictionary<string, string> fields)
    {
        var rowHeightBaseline = _rowHeightBalanceService.CaptureBaseline(targetPath);
        if (_templateMappingService.ShouldUseAdaptation(template))
        {
            var layoutBaseline = TemplateWorkbookHelper.CaptureLayoutSnapshot(targetPath);
            _templateMappingService.Apply(targetPath, template, fields);
            _rowHeightBalanceService.ApplyLight(targetPath, formName, fields, rowHeightBaseline);
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
            ApplyFields(targetPath, formName, fields);
            _rowHeightBalanceService.ApplyLight(targetPath, formName, fields, rowHeightBaseline);
        }
    }

    private DeleteGeneratedDocumentResult UpdateDocumentStatus(
        GeneratedDocumentIndexInfo document,
        string targetStatus,
        bool moveFileToTrash,
        bool propagatePlanRowDeletion)
    {
        var storedPath = document.FilePath;
        var restoreToken = document.RestoreToken;
        var fileMoved = false;

        if (moveFileToTrash && !string.IsNullOrWhiteSpace(document.FilePath))
        {
            var moved = MoveFileToTrash(document.ProjectId, document.FilePath);
            fileMoved = moved.Moved;
            if (moved.Moved)
            {
                storedPath = moved.TrashStoredPath;
                restoreToken = moved.RestoreToken;
            }
        }

        var now = DateTimeOffset.Now;
        _repository.UpdateDocument(document with
        {
            FilePath = storedPath,
            DocumentStatus = targetStatus,
            SyncStatus = string.Equals(targetStatus, "Active", StringComparison.OrdinalIgnoreCase) ? document.SyncStatus : "NeedSync",
            SyncErrorMessage = string.Equals(targetStatus, "Active", StringComparison.OrdinalIgnoreCase) ? document.SyncErrorMessage : "",
            DeleteTime = string.Equals(targetStatus, "Active", StringComparison.OrdinalIgnoreCase) ? null : now,
            RestoreToken = string.Equals(targetStatus, "Active", StringComparison.OrdinalIgnoreCase) ? null : restoreToken,
            UpdatedTime = now
        });

        if (propagatePlanRowDeletion &&
            string.Equals(document.SourceType, "InspectionBatchPlanRow", StringComparison.OrdinalIgnoreCase))
        {
            _batchPlanRepository.UpdatePlanRowLifecycleStatus(document.SourceId, "Deleted", now);
            _batchPlanRepository.UpdatePlanRowGenerateStatus(document.SourceId, "None");
        }

        return new DeleteGeneratedDocumentResult(
            true,
            document.DocumentId,
            document.TemplateNodeId,
            string.Equals(targetStatus, "Deleted", StringComparison.OrdinalIgnoreCase) ? "资料表已删除。" : "资料表状态已更新。",
            document.DocumentName,
            _repository.ResolveStoredPath(storedPath),
            fileMoved,
            restoreToken);
    }

    private (bool Moved, string TrashStoredPath, string RestoreToken) MoveFileToTrash(string projectId, string originalStoredPath)
    {
        var project = _projectManager.ResolveProject(projectId);
        var originalPath = _repository.ResolveStoredPath(originalStoredPath);
        if (!File.Exists(originalPath))
        {
            return (false, originalStoredPath, "");
        }

        var trashDirectory = Path.Combine(project.ProjectRootPath, ".trash", "forms");
        Directory.CreateDirectory(trashDirectory);
        var trashFileName = $"{DateTimeOffset.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{Path.GetExtension(originalPath)}";
        var trashPath = Path.Combine(trashDirectory, trashFileName);
        File.Move(originalPath, trashPath, overwrite: true);

        var trashStoredPath = _repository.ToStoredPath(trashPath);
        var payload = $"{originalStoredPath}|{trashStoredPath}|{DateTimeOffset.Now:O}";
        var restoreToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return (true, trashStoredPath, restoreToken);
    }

    private static (string OriginalStoredPath, string TrashStoredPath) ParseRestoreToken(string restoreToken)
    {
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(restoreToken));
        var parts = payload.Split('|');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("恢复令牌无效。");
        }

        return (parts[0], parts[1]);
    }

    private static IReadOnlyDictionary<string, string> BuildInspectionBatchFields(
        ProjectContext project,
        UnitProjectInfo unitProject,
        InspectionBatchPlanRowDto row,
        IReadOnlyDictionary<string, string>? requestFields)
    {
        var fields = new Dictionary<string, string>(requestFields ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["projectName"] = project.ProjectName,
            ["工程名称"] = project.ProjectName,
            ["constructorUnitName"] = unitProject.ConstructionUnit,
            ["施工单位"] = unitProject.ConstructionUnit,
            ["supervisorUnitName"] = unitProject.SupervisionUnit,
            ["监理单位"] = unitProject.SupervisionUnit,
            ["partName"] = row.InspectionPart,
            ["检验批部位"] = row.InspectionPart,
            ["施工部位"] = row.InspectionPart,
            ["capacity"] = row.CapacitySummary,
            ["检验批容量"] = row.CapacitySummary,
            ["constructionDate"] = row.ConstructionDate,
            ["施工日期"] = row.ConstructionDate,
            ["remark"] = row.Remark,
            ["备注"] = row.Remark
        };
        return fields;
    }

    private static string BuildDocumentName(InspectionBatchPlanRowDto row)
    {
        var parts = new[] { row.TemplateName, row.InspectionPart, row.ConstructionDate }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();
        return parts.Length == 0 ? $"检验批资料-{DateTime.Now:yyyyMMddHHmmss}" : string.Join("-", parts);
    }

    private TemplateResolution? TryResolveTemplate(string templateNodeId)
    {
        try
        {
            return _templateService.ResolveTemplate(templateNodeId);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureDocumentAccess(GeneratedDocumentIndexInfo document, string? projectId, string? unitProjectId)
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
