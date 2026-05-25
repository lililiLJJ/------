using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Models;
using GeneratorService.Projects;

namespace GeneratorService.TemplateLibrary;

public sealed class BatchPlanService
{
    private static readonly Regex SimplePlaceholderRegex = new(@"\{\{(?<name>[^:{}]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex CellReferenceRegex = new(@"^[A-Z]{1,3}[1-9][0-9]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyList<DeviceFieldInfo> DefaultDeviceFields =
    [
        new("SmokeDetectorCount", "感烟探测器数量", "个", 10),
        new("HeatDetectorCount", "感温探测器数量", "个", 20),
        new("ManualAlarmButtonCount", "手动报警按钮数量", "个", 30),
        new("FireHydrantCount", "消火栓数量", "个", 40),
        new("SprayHeadCount", "喷头数量", "个", 50),
        new("PipeLength", "管道长度", "m", 60),
        new("ValveCount", "阀门数量", "个", 70),
        new("CableLength", "电缆长度", "m", 80),
        new("DistributionBoxCount", "配电箱数量", "台", 90),
        new("DeviceTotalCount", "设备总数", "个", 100)
    ];

    private readonly BatchPlanRepository _repository;
    private readonly TemplateTreeRepository _templateTreeRepository;
    private readonly TemplateService _templateService;
    private readonly ProjectManager _projectManager;
    private readonly RowHeightBalanceService _rowHeightBalanceService;

    public BatchPlanService(
        BatchPlanRepository repository,
        TemplateTreeRepository templateTreeRepository,
        TemplateService templateService,
        ProjectManager projectManager,
        RowHeightBalanceService rowHeightBalanceService)
    {
        _repository = repository;
        _templateTreeRepository = templateTreeRepository;
        _templateService = templateService;
        _projectManager = projectManager;
        _rowHeightBalanceService = rowHeightBalanceService;
    }

    public BatchPlanListResult List(string? projectId)
    {
        var project = _projectManager.ResolveProject(projectId);
        return new BatchPlanListResult(true, project.ProjectId, _repository.ListPlans(project.ProjectId));
    }

    public BatchPlanSaveResult Create(BatchPlanSaveRequest request)
    {
        var project = _projectManager.ResolveProject(request.ProjectId);
        var plan = _repository.SavePlan(null, project.ProjectId, request);
        return new BatchPlanSaveResult(true, plan, "检验批划分计划已创建。");
    }

    public BatchPlanSaveResult Update(string id, BatchPlanSaveRequest request)
    {
        var existing = _repository.GetPlan(id) ?? throw new InvalidOperationException("检验批划分计划不存在。");
        var project = _projectManager.ResolveProject(request.ProjectId ?? existing.ProjectId);
        var plan = _repository.SavePlan(id, project.ProjectId, request);
        return new BatchPlanSaveResult(true, plan, "检验批划分计划已保存。");
    }

    public BatchPlanPreviewResult Preview(string id)
    {
        var plan = _repository.GetPlan(id) ?? throw new InvalidOperationException("检验批划分计划不存在。");
        var rows = BuildPreviewRows(plan).ToArray();
        var warnings = rows.SelectMany(row => row.Warnings.Select(warning => $"第 {row.RowIndex} 行：{warning}")).ToArray();
        return new BatchPlanPreviewResult(
            true,
            plan.ProjectId,
            plan.Id,
            rows.Length,
            rows.Count(row => row.CanGenerate),
            rows.Count(row => !row.CanGenerate),
            rows,
            warnings);
    }

    public BatchPlanGenerateResult Generate(string id, BatchPlanGenerateRequest? request)
    {
        var plan = _repository.GetPlan(id) ?? throw new InvalidOperationException("检验批划分计划不存在。");
        var previewRows = BuildPreviewRows(plan).ToArray();
        var project = _projectManager.ResolveProject(plan.ProjectId);
        var itemsById = plan.Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var results = new List<BatchPlanGenerateRowResult>();

        foreach (var row in previewRows)
        {
            if (!itemsById.TryGetValue(row.ItemId, out var item))
            {
                results.Add(new BatchPlanGenerateRowResult(row.RowIndex, row.ItemId, false, true, null, null, "计划行不存在，已跳过。"));
                continue;
            }

            if (!row.CanGenerate)
            {
                var message = string.Join("；", row.Errors);
                _repository.MarkItemFailed(item.Id, message);
                results.Add(new BatchPlanGenerateRowResult(row.RowIndex, item.Id, false, true, null, null, message));
                continue;
            }

            try
            {
                var result = GenerateOne(project, plan, item, row, request?.Fields ?? new Dictionary<string, string>());
                _repository.MarkItemGenerated(item.Id, result.Node.Id);
                results.Add(new BatchPlanGenerateRowResult(row.RowIndex, item.Id, true, false, result.Node.Id, result.GeneratedFilePath, "生成成功。"));
            }
            catch (Exception ex)
            {
                _repository.MarkItemFailed(item.Id, ex.Message);
                results.Add(new BatchPlanGenerateRowResult(row.RowIndex, item.Id, false, false, null, null, ex.Message));
            }
        }

        var successCount = results.Count(item => item.Success);
        var skippedCount = results.Count(item => item.Skipped);
        var failedCount = results.Count - successCount - skippedCount;
        return new BatchPlanGenerateResult(
            failedCount == 0 && successCount > 0,
            plan.ProjectId,
            plan.Id,
            successCount,
            failedCount,
            skippedCount,
            results,
            $"批量创建完成：成功 {successCount} 张，失败 {failedCount} 张，跳过 {skippedCount} 张。");
    }

    public DeviceFieldsResult GetDeviceFields(string? moduleId, long? templateItemId)
    {
        var normalizedModuleId = moduleId?.Trim() ?? "";
        var normalizedTemplateItemId = templateItemId ?? 0;
        var mappingFields = _repository
            .ListMappings(normalizedModuleId, normalizedTemplateItemId)
            .Select(item => new DeviceFieldInfo(item.DeviceFieldKey, item.DeviceDisplayName, "", item.SortOrder))
            .ToArray();

        var fields = DefaultDeviceFields
            .Concat(mappingFields)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var configured = group.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.DisplayName) && item.SortOrder != 0);
                return configured ?? group.First();
            })
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.DisplayName)
            .ToArray();

        return new DeviceFieldsResult(true, normalizedModuleId, normalizedTemplateItemId, fields);
    }

    public DeviceMappingsResult GetDeviceMappings(string? moduleId, long? templateItemId)
    {
        var normalizedModuleId = moduleId?.Trim() ?? "";
        var normalizedTemplateItemId = templateItemId ?? 0;
        return new DeviceMappingsResult(
            true,
            normalizedModuleId,
            normalizedTemplateItemId,
            _repository.ListMappings(normalizedModuleId, normalizedTemplateItemId));
    }

    public DeviceMappingsResult SaveDeviceMappings(DeviceMappingsSaveRequest request)
    {
        var moduleId = request.ModuleId?.Trim();
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new InvalidOperationException("ModuleId 不能为空。");
        }

        var templateItemId = request.TemplateItemId ?? 0;
        if (templateItemId <= 0)
        {
            throw new InvalidOperationException("TemplateItemId 不能为空。");
        }

        var mappings = _repository.SaveMappings(moduleId, templateItemId, request.Mappings ?? Array.Empty<DeviceMappingSaveItem>());
        return new DeviceMappingsResult(true, moduleId, templateItemId, mappings);
    }

    private IEnumerable<BatchPlanPreviewRow> BuildPreviewRows(BatchPlanInfo plan)
    {
        var usedOutputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < plan.Items.Count; index++)
        {
            var item = plan.Items[index];
            var warnings = new List<string>();
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(item.ModuleId))
            {
                errors.Add("未选择模块。");
            }

            if (item.TemplateItemId <= 0)
            {
                errors.Add("未选择检验批模板。");
            }

            if (string.IsNullOrWhiteSpace(item.TemplateName))
            {
                errors.Add("检验批名称为空。");
            }

            if (string.IsNullOrWhiteSpace(item.PartName))
            {
                errors.Add("检验批部位不能为空。");
            }

            if (string.IsNullOrWhiteSpace(item.ConstructionDate))
            {
                warnings.Add("施工日期为空，生成资料时对应字段留空。");
            }

            if (item.DeviceQuantities.Count == 0)
            {
                warnings.Add("未填写设备数量，验收项目抽样数量将不自动填写。");
            }

            var templateNodeId = BuildTemplateNodeId(item.ModuleId, item.TemplateItemId);
            try
            {
                _templateService.ResolveTemplate(templateNodeId);
            }
            catch (Exception ex)
            {
                errors.Add($"模板无法解析：{ex.Message}");
            }

            var outputName = BuildOutputName(item.TemplateName, item.PartName, item.ConstructionDate);
            if (!usedOutputNames.Add(outputName))
            {
                warnings.Add("存在重复文件名，生成时会自动追加序号。");
            }

            var mappings = BuildMappingPreview(item).ToArray();
            foreach (var quantity in item.DeviceQuantities.Where(q => q.Value != 0))
            {
                if (!mappings.Any(mapping => string.Equals(mapping.DeviceFieldKey, quantity.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    warnings.Add($"{quantity.Key} 未配置验收项目映射，仅会尝试模板占位符替换。");
                }
            }

            yield return new BatchPlanPreviewRow(
                index + 1,
                item.Id,
                item.ModuleId,
                item.TemplateItemId,
                item.TemplateName,
                item.PartName,
                item.Capacity,
                item.QuantityUnit,
                item.ConstructionDate,
                outputName,
                item.DeviceQuantities,
                mappings,
                warnings,
                errors,
                errors.Count == 0);
        }
    }

    private IEnumerable<DeviceMappingPreview> BuildMappingPreview(BatchPlanItemInfo item)
    {
        var enabledMappings = _repository
            .ListMappings(item.ModuleId, item.TemplateItemId)
            .Where(mapping => mapping.IsEnabled)
            .ToArray();

        foreach (var mapping in enabledMappings)
        {
            if (!item.DeviceQuantities.TryGetValue(mapping.DeviceFieldKey, out var quantity) || quantity == 0)
            {
                continue;
            }

            yield return new DeviceMappingPreview(
                mapping.DeviceFieldKey,
                mapping.DeviceDisplayName,
                quantity,
                mapping.InspectionItemName,
                mapping.FillMode,
                mapping.TargetCells.Count > 0,
                true,
                mapping.TargetCells.Count > 0 ? "InspectionItemDeviceMapping" : "placeholder");
        }
    }

    private GeneratedFormCreateResult GenerateOne(
        ProjectContext project,
        BatchPlanInfo plan,
        BatchPlanItemInfo item,
        BatchPlanPreviewRow preview,
        IReadOnlyDictionary<string, string> requestFields)
    {
        var templateNodeId = BuildTemplateNodeId(item.ModuleId, item.TemplateItemId);
        var template = _templateService.ResolveTemplate(templateNodeId);
        var targetDirectory = Path.Combine(project.GeneratedFormsPath, "BatchPlans", SanitizePathSegment(plan.Name));
        Directory.CreateDirectory(targetDirectory);

        var extension = Path.GetExtension(template.TemplatePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".xlsx";
        }

        var outputPath = ResolveUniquePath(targetDirectory, $"{SanitizePathSegment(preview.OutputName)}{extension}");
        File.Copy(template.TemplatePath, outputPath);

        var fields = BuildFields(project, item, requestFields);
        var baseline = _rowHeightBalanceService.CaptureBaseline(outputPath);
        GeneratedFormService.ApplyFields(outputPath, item.PartName, fields);
        var deviceMappings = _repository
            .ListMappings(item.ModuleId, item.TemplateItemId)
            .Where(mapping => mapping.IsEnabled)
            .ToArray();
        ApplyDeviceValues(outputPath, item, deviceMappings);
        _rowHeightBalanceService.ApplyLight(outputPath, item.PartName, fields, baseline);

        var documentName = preview.OutputName;
        var node = _templateTreeRepository.InsertProjectDocument(
            project.ProjectId,
            template.ModuleId,
            template.TemplateItemId,
            template.TemplateNodeId,
            documentName,
            item.PartName,
            BuildCapacityText(item),
            template.TemplateCode,
            outputPath);

        return new GeneratedFormCreateResult(true, node, outputPath, "资料表已创建。");
    }

    private static IReadOnlyDictionary<string, string> BuildFields(
        ProjectContext project,
        BatchPlanItemInfo item,
        IReadOnlyDictionary<string, string> requestFields)
    {
        var fields = new Dictionary<string, string>(requestFields, StringComparer.OrdinalIgnoreCase)
        {
            ["projectName"] = project.ProjectName,
            ["工程名称"] = project.ProjectName,
            ["partName"] = item.PartName,
            ["检验批部位"] = item.PartName,
            ["施工部位"] = item.PartName,
            ["capacity"] = BuildCapacityText(item),
            ["检验批容量"] = BuildCapacityText(item),
            ["constructionDate"] = item.ConstructionDate,
            ["施工日期"] = item.ConstructionDate
        };

        return fields;
    }

    private static void ApplyDeviceValues(
        string filePath,
        BatchPlanItemInfo item,
        IReadOnlyList<DeviceMappingInfo> mappings)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var replacements = BuildDeviceReplacements(item.DeviceQuantities);
        using var document = SpreadsheetDocument.Open(filePath, true);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少 WorkbookPart。");
        ReplaceSimplePlaceholders(workbookPart, replacements);

        WriteMappedDeviceCells(workbookPart, item.DeviceQuantities, mappings);
        workbookPart.Workbook.Save();
    }

    private static Dictionary<string, string> BuildDeviceReplacements(IReadOnlyDictionary<string, decimal> quantities)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in quantities)
        {
            var value = FormatQuantity(item.Value);
            replacements[item.Key] = value;
            replacements[$"{item.Key}SampleQuantity"] = value;
            replacements[$"{item.Key}ActualQuantity"] = value;
            replacements[$"{item.Key}CheckRecord"] = $"抽查 {value} 处";
            replacements[$"{item.Key}QualifiedCount"] = $"合格 {value} 处";
            replacements[$"{item.Key}Qualified"] = $"合格 {value} 处";
        }

        return replacements;
    }

    private static void ReplaceSimplePlaceholders(WorkbookPart workbookPart, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        if (workbookPart.SharedStringTablePart?.SharedStringTable is { } sharedStringTable)
        {
            foreach (var item in sharedStringTable.Elements<SharedStringItem>())
            {
                var replacement = ReplaceSimple(item.InnerText, replacements);
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
                    cell.InlineString.Text.Text = ReplaceSimple(inlineText, replacements);
                }

                if (cell.CellValue?.Text is { } cellText &&
                    cell.DataType?.Value == CellValues.String)
                {
                    cell.CellValue.Text = ReplaceSimple(cellText, replacements);
                }
            }

            worksheetPart.Worksheet.Save();
        }
    }

    private static void WriteMappedDeviceCells(
        WorkbookPart workbookPart,
        IReadOnlyDictionary<string, decimal> quantities,
        IReadOnlyList<DeviceMappingInfo> mappings)
    {
        if (quantities.Count == 0 || mappings.Count == 0)
        {
            return;
        }

        var worksheetPart = workbookPart.WorksheetParts.FirstOrDefault();
        if (worksheetPart is null)
        {
            return;
        }

        foreach (var mapping in mappings)
        {
            if (!mapping.IsEnabled ||
                !quantities.TryGetValue(mapping.DeviceFieldKey, out var quantity) ||
                quantity == 0)
            {
                continue;
            }

            var value = FormatQuantity(quantity);
            WriteMappedCell(worksheetPart, mapping.TargetCells, value, ["sampleQuantityCell", "sampleCell", "targetSampleCell"]);
            WriteMappedCell(worksheetPart, mapping.TargetCells, value, ["actualQuantityCell", "actualSampleCell", "targetActualSampleCell"]);
            WriteMappedCell(worksheetPart, mapping.TargetCells, $"抽查 {value} 处", ["checkRecordCell", "recordCell", "targetCheckRecordCell"]);
            WriteMappedCell(worksheetPart, mapping.TargetCells, $"合格 {value} 处", ["qualifiedCountCell", "qualifiedCell", "targetQualifiedCell"]);
        }

        worksheetPart.Worksheet.Save();
    }

    private static void WriteMappedCell(
        WorksheetPart worksheetPart,
        IReadOnlyDictionary<string, string> targetCells,
        string value,
        IReadOnlyList<string> keys)
    {
        var cellReference = keys
            .Select(key => targetCells.TryGetValue(key, out var cell) ? cell : "")
            .FirstOrDefault(cell => !string.IsNullOrWhiteSpace(cell));
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return;
        }

        cellReference = cellReference.Trim().ToUpperInvariant();
        if (!CellReferenceRegex.IsMatch(cellReference))
        {
            return;
        }

        WriteCellText(GetOrCreateCell(worksheetPart.Worksheet, cellReference), value);
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

        var cell = row.Elements<Cell>().FirstOrDefault(item => string.Equals(item.CellReference?.Value, cellReference, StringComparison.OrdinalIgnoreCase));
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

    private static uint GetRowIndex(string cellReference)
    {
        var rowName = new string(cellReference.SkipWhile(char.IsLetter).ToArray());
        return uint.TryParse(rowName, out var rowIndex) ? rowIndex : 1;
    }

    private static void WriteCellText(Cell cell, string value)
    {
        cell.DataType = CellValues.String;
        cell.CellValue = new CellValue(value);
        cell.InlineString = null;
    }

    private static string ReplaceSimple(string text, IReadOnlyDictionary<string, string> replacements)
    {
        if (!text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        return SimplePlaceholderRegex.Replace(text, match =>
        {
            var name = match.Groups["name"].Value.Trim();
            return replacements.TryGetValue(name, out var value) ? value : match.Value;
        });
    }

    private static string BuildTemplateNodeId(string moduleId, long templateItemId)
    {
        return $"module:{moduleId}:template:{templateItemId}";
    }

    private static string BuildOutputName(string templateName, string partName, string constructionDate)
    {
        var parts = new[] { templateName, partName, constructionDate }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();
        return parts.Length == 0 ? $"检验批资料-{DateTime.Now:yyyyMMddHHmmss}" : string.Join("-", parts);
    }

    private static string BuildCapacityText(BatchPlanItemInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.Capacity))
        {
            return "";
        }

        return string.IsNullOrWhiteSpace(item.QuantityUnit)
            ? item.Capacity.Trim()
            : $"{item.Capacity.Trim()}{item.QuantityUnit.Trim()}";
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

    private static string FormatQuantity(decimal value)
    {
        return value % 1 == 0
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
