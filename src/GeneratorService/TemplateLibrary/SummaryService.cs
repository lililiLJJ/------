using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Models;
using GeneratorService.Projects;

namespace GeneratorService.TemplateLibrary;

public sealed class SummaryService
{
    private const string Division = "Division";
    private const string SubDivision = "SubDivision";
    private const string SubItem = "SubItem";
    private const string ConstructorResult = "符合要求";
    private const string SupervisorConclusion = "验收合格";

    private static readonly Regex SummaryNodeRegex = new(
        @"^summary:(?<summaryType>Division|SubDivision|SubItem):(?<moduleId>.+?):(?<categoryId>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly DirectoryInfo _rootPath;
    private readonly TemplateTreeRepository _repository;
    private readonly ProjectManager _projectManager;
    private readonly UnitProjectService _unitProjectService;

    public SummaryService(
        DirectoryInfo rootPath,
        TemplateTreeRepository repository,
        ProjectManager projectManager,
        UnitProjectService unitProjectService)
    {
        _rootPath = rootPath;
        _repository = repository;
        _projectManager = projectManager;
        _unitProjectService = unitProjectService;
    }

    public SummaryTreeResult GetTree(string? projectId, string? unitProjectId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, unitProjectId);
        var warnings = new List<string>();
        var documents = LoadInspectionBatchDocuments(project.ProjectId, unitProject.Id, warnings);
        var nodes = BuildTree(documents);

        return new SummaryTreeResult(
            true,
            project.ProjectId,
            project.ProjectName,
            unitProject.Id,
            unitProject.UnitProjectName,
            nodes,
            warnings);
    }

    public SummaryPreviewResult GetPreview(string? projectId, string? unitProjectId, string type, string categoryId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, unitProjectId);
        var summaryType = NormalizeSummaryType(type);
        var target = ParseSummaryNodeId(categoryId, summaryType);
        var warnings = new List<string>();
        var documents = LoadInspectionBatchDocuments(project.ProjectId, unitProject.Id, warnings)
            .Where(item => MatchesCategory(item, target))
            .ToArray();
        var rows = BuildPreviewRows(summaryType, documents);
        var title = BuildTitle(summaryType, documents);

        return new SummaryPreviewResult(
            true,
            project.ProjectId,
            unitProject.Id,
            summaryType,
            target.RawId,
            title,
            documents.FirstOrDefault()?.Context.DivisionName ?? "",
            summaryType == Division ? "" : documents.FirstOrDefault()?.Context.SubDivisionName ?? "",
            summaryType == SubItem ? documents.FirstOrDefault()?.Context.SubItemName ?? "" : "",
            rows,
            BuildTotals(documents),
            warnings);
    }

    public GenerateSummaryResult Generate(GenerateSummaryRequest request)
    {
        var preview = GetPreview(request.ProjectId, request.UnitProjectId, request.Type, request.CategoryId);
        if (preview.Rows.Count == 0)
        {
            throw new InvalidOperationException("当前汇总对象没有可生成的有效资料。");
        }

        var project = _projectManager.ResolveProject(preview.ProjectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, preview.UnitProjectId);
        var outputDirectory = Path.Combine(project.GeneratedFormsPath, "Summary", SanitizePathSegment(unitProject.UnitProjectName), preview.SummaryType);
        Directory.CreateDirectory(outputDirectory);

        var templatePath = ResolveSummaryTemplate(preview.SummaryType);
        var outputName = SanitizeFileName($"{preview.Title}-{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        var outputPath = ResolveUniquePath(outputDirectory, outputName);
        File.Copy(templatePath, outputPath);
        FillWorkbook(outputPath, project.ProjectName, preview);

        var sourceDocuments = LoadInspectionBatchDocuments(preview.ProjectId, preview.UnitProjectId, [])
            .Where(item => MatchesCategory(item, ParseSummaryNodeId(preview.CategoryId, preview.SummaryType)))
            .ToArray();
        var now = DateTimeOffset.Now;
        var document = new GeneratedDocumentIndexInfo(
            $"generated-document:{Guid.NewGuid():N}",
            preview.ProjectId,
            preview.UnitProjectId,
            "Summary",
            preview.Title,
            "SummaryData",
            preview.CategoryId,
            $"summary:{preview.SummaryType}:{preview.CategoryId}",
            _repository.ToStoredPath(outputPath),
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
        _repository.UpsertSummaryDetail(
            document.DocumentId,
            preview.SummaryType,
            preview.CategoryId,
            sourceDocuments.Select(item => item.Document.DocumentId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            preview.Totals.SourceDocumentCount);

        return new GenerateSummaryResult(
            true,
            document,
            outputPath,
            "汇总表已生成。",
            preview.Warnings);
    }

    private IReadOnlyList<InspectionBatchSummarySource> LoadInspectionBatchDocuments(string projectId, string unitProjectId, List<string> warnings)
    {
        var contexts = _repository.LoadTemplateNodeContexts(projectId, unitProjectId);
        var documents = _repository.ListGeneratedDocuments(projectId, unitProjectId, "InspectionBatch", "Active");
        var results = new List<InspectionBatchSummarySource>();
        foreach (var document in documents)
        {
            var detail = _repository.GetInspectionBatchDetail(document.DocumentId);
            if (detail is null)
            {
                warnings.Add($"资料“{document.DocumentName}”缺少检验批详情，已跳过。");
                continue;
            }

            if (!contexts.TryGetValue(detail.TemplateNodeId, out var context))
            {
                warnings.Add($"资料“{document.DocumentName}”未找到模板树路径缓存，已跳过。");
                continue;
            }

            results.Add(new InspectionBatchSummarySource(document, detail, context));
        }

        return results
            .OrderBy(item => item.Context.DivisionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Context.SubDivisionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Context.SubItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Detail.InspectionPart, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Document.DocumentName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SummaryTreeNodeDto> BuildTree(IReadOnlyList<InspectionBatchSummarySource> documents)
    {
        return documents
            .GroupBy(item => new { item.Context.DivisionId, item.Context.DivisionName })
            .OrderBy(group => group.Key.DivisionName, StringComparer.OrdinalIgnoreCase)
            .Select(divisionGroup =>
            {
                var subDivisionNodes = divisionGroup
                    .GroupBy(item => new { item.Context.SubDivisionId, item.Context.SubDivisionName })
                    .OrderBy(group => group.Key.SubDivisionName, StringComparer.OrdinalIgnoreCase)
                    .Select(subDivisionGroup =>
                    {
                        var subItemNodes = subDivisionGroup
                            .GroupBy(item => new { item.Context.SubItemId, item.Context.SubItemName })
                            .OrderBy(group => group.Key.SubItemName, StringComparer.OrdinalIgnoreCase)
                            .Select(subItemGroup => BuildNode(SubItem, subItemGroup.ToArray()))
                            .ToArray();
                        return BuildNode(SubDivision, subDivisionGroup.ToArray(), subItemNodes);
                    })
                    .ToArray();
                return BuildNode(Division, divisionGroup.ToArray(), subDivisionNodes);
            })
            .ToArray();
    }

    private static SummaryTreeNodeDto BuildNode(
        string summaryType,
        IReadOnlyList<InspectionBatchSummarySource> items,
        IReadOnlyList<SummaryTreeNodeDto>? children = null)
    {
        var first = items[0];
        var categoryId = summaryType switch
        {
            Division => first.Context.DivisionId,
            SubDivision => first.Context.SubDivisionId,
            _ => first.Context.SubItemId
        };
        var name = summaryType switch
        {
            Division => first.Context.DivisionName,
            SubDivision => first.Context.SubDivisionName,
            _ => first.Context.SubItemName
        };
        var nodeId = $"summary:{summaryType}:{ExtractModuleId(first.Context.TemplateNodeId)}:{categoryId}";
        return new SummaryTreeNodeDto(
            nodeId,
            first.Document.ProjectId,
            ExtractModuleId(first.Context.TemplateNodeId),
            categoryId,
            summaryType,
            name,
            first.Context.DivisionId,
            first.Context.DivisionName,
            first.Context.SubDivisionId,
            first.Context.SubDivisionName,
            first.Context.SubItemId,
            first.Context.SubItemName,
            items.Select(item => item.Document.DocumentId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            items.Count,
            items.Select(item => item.Context.SubItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            items.Select(item => item.Context.SubDivisionId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            children ?? []);
    }

    private static IReadOnlyList<SummaryPreviewRow> BuildPreviewRows(string summaryType, IReadOnlyList<InspectionBatchSummarySource> documents)
    {
        return summaryType switch
        {
            SubItem => documents
                .Select((item, index) => new SummaryPreviewRow(
                    index + 1,
                    item.Document.DocumentName,
                    item.Detail.CapacitySummary,
                    item.Detail.InspectionPart,
                    1,
                    ConstructorResult,
                    SupervisorConclusion))
                .ToArray(),
            SubDivision => documents
                .GroupBy(item => item.Context.SubItemId)
                .OrderBy(group => group.First().Context.SubItemName, StringComparer.OrdinalIgnoreCase)
                .Select((group, index) => new SummaryPreviewRow(
                    index + 1,
                    group.First().Context.SubItemName,
                    "",
                    "",
                    group.Count(),
                    ConstructorResult,
                    SupervisorConclusion))
                .ToArray(),
            Division => documents
                .GroupBy(item => item.Context.SubDivisionId)
                .OrderBy(group => group.First().Context.SubDivisionName, StringComparer.OrdinalIgnoreCase)
                .Select((group, index) => new SummaryPreviewRow(
                    index + 1,
                    group.First().Context.SubDivisionName,
                    "",
                    "",
                    group.Select(item => item.Context.SubItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    ConstructorResult,
                    SupervisorConclusion))
                .ToArray(),
            _ => throw new InvalidOperationException($"未知汇总类型：{summaryType}")
        };
    }

    private static SummaryPreviewTotals BuildTotals(IReadOnlyList<InspectionBatchSummarySource> documents)
    {
        return new SummaryPreviewTotals(
            documents.Select(item => item.Document.DocumentId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            documents.Count,
            documents.Select(item => item.Context.SubItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            documents.Select(item => item.Context.SubDivisionId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static string BuildTitle(string summaryType, IReadOnlyList<InspectionBatchSummarySource> documents)
    {
        var first = documents.FirstOrDefault();
        if (first is null)
        {
            return summaryType;
        }

        return summaryType switch
        {
            SubItem => $"{first.Context.SubItemName}分项工程质量验收记录",
            SubDivision => $"{first.Context.SubDivisionName}子分部工程质量验收记录",
            Division => $"{first.Context.DivisionName}分部工程质量验收记录",
            _ => summaryType
        };
    }

    private static bool MatchesCategory(InspectionBatchSummarySource source, SummaryNodeRef target)
    {
        if (!string.Equals(ExtractModuleId(source.Context.TemplateNodeId), target.ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return target.SummaryType switch
        {
            Division => string.Equals(source.Context.DivisionId, target.CategoryId, StringComparison.OrdinalIgnoreCase),
            SubDivision => string.Equals(source.Context.SubDivisionId, target.CategoryId, StringComparison.OrdinalIgnoreCase),
            SubItem => string.Equals(source.Context.SubItemId, target.CategoryId, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static SummaryNodeRef ParseSummaryNodeId(string categoryId, string summaryType)
    {
        if (SummaryNodeRegex.Match(categoryId) is { Success: true } match)
        {
            return new SummaryNodeRef(
                match.Groups["summaryType"].Value,
                match.Groups["moduleId"].Value,
                match.Groups["categoryId"].Value,
                categoryId);
        }

        var parts = categoryId.Split(':');
        if (parts.Length >= 3 && string.Equals(parts[0], "module", StringComparison.OrdinalIgnoreCase))
        {
            return new SummaryNodeRef(summaryType, parts[1], parts[^1], categoryId);
        }

        throw new InvalidOperationException($"汇总分类节点无效：{categoryId}");
    }

    private static string NormalizeSummaryType(string value)
    {
        return value.Trim() switch
        {
            Division => Division,
            SubDivision => SubDivision,
            SubItem => SubItem,
            var item when item.Equals("division", StringComparison.OrdinalIgnoreCase) => Division,
            var item when item.Equals("subdivision", StringComparison.OrdinalIgnoreCase) => SubDivision,
            var item when item.Equals("sub_division", StringComparison.OrdinalIgnoreCase) => SubDivision,
            var item when item.Equals("subitem", StringComparison.OrdinalIgnoreCase) => SubItem,
            var item when item.Equals("sub_item", StringComparison.OrdinalIgnoreCase) => SubItem,
            _ => throw new InvalidOperationException($"未知汇总类型：{value}")
        };
    }

    private string ResolveSummaryTemplate(string summaryType)
    {
        var prefix = summaryType switch
        {
            SubItem => "GD-C3-521",
            SubDivision => "GD-C3-5311",
            Division => "GD-C3-5312",
            _ => throw new InvalidOperationException($"未知汇总类型：{summaryType}")
        };
        var directory = Path.Combine(_rootPath.FullName, "Templates", "Summary");
        var path = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"{prefix}*.xlsx", SearchOption.TopDirectoryOnly)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;

        return path is not null && File.Exists(path)
            ? path
            : throw new FileNotFoundException($"未找到 {prefix} 汇总模板，请确认 Templates/Summary 中存在对应 .xlsx 工作模板。", directory);
    }

    private static void FillWorkbook(string filePath, string projectName, SummaryPreviewResult preview)
    {
        using var document = SpreadsheetDocument.Open(filePath, true);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("汇总模板缺少 WorkbookPart。");
        var worksheetPart = workbookPart.WorksheetParts.First();

        switch (preview.SummaryType)
        {
            case SubItem:
                EnsureDataRows(worksheetPart, 11, 17, preview.Rows.Count, 27);
                WriteCellText(worksheetPart, "E6", projectName);
                WriteCellText(worksheetPart, "E7", JoinNames(preview.DivisionName, preview.SubDivisionName));
                WriteCellText(worksheetPart, "N7", preview.Totals.SubItemCount.ToString());
                WriteRows(worksheetPart, 11, preview.Rows, row =>
                {
                    WriteCellText(worksheetPart, $"B{row.RowIndex}", row.Item.Sequence.ToString());
                    WriteCellText(worksheetPart, $"C{row.RowIndex}", row.Item.Name);
                    WriteCellText(worksheetPart, $"H{row.RowIndex}", row.Item.Capacity);
                    WriteCellText(worksheetPart, $"J{row.RowIndex}", row.Item.PartName);
                    WriteCellText(worksheetPart, $"N{row.RowIndex}", row.Item.ConstructorResult);
                    WriteCellText(worksheetPart, $"S{row.RowIndex}", row.Item.SupervisorConclusion);
                });
                break;
            case SubDivision:
                EnsureDataRows(worksheetPart, 9, 17, preview.Rows.Count, 31);
                WriteCellText(worksheetPart, "H5", projectName);
                WriteRows(worksheetPart, 9, preview.Rows, row =>
                {
                    WriteCellText(worksheetPart, $"B{row.RowIndex}", row.Item.Sequence.ToString());
                    WriteCellText(worksheetPart, $"C{row.RowIndex}", row.Item.Name);
                    WriteCellText(worksheetPart, $"K{row.RowIndex}", row.Item.Count.ToString());
                    WriteCellText(worksheetPart, $"N{row.RowIndex}", row.Item.ConstructorResult);
                    WriteCellText(worksheetPart, $"U{row.RowIndex}", row.Item.SupervisorConclusion);
                });
                WriteCellText(worksheetPart, $"H{18 + ExtraRows(9, 17, preview.Rows.Count)}", preview.Totals.SubItemCount.ToString());
                WriteCellText(worksheetPart, $"L{18 + ExtraRows(9, 17, preview.Rows.Count)}", preview.Totals.InspectionBatchCount.ToString());
                break;
            case Division:
                EnsureDataRows(worksheetPart, 9, 18, preview.Rows.Count, 31);
                WriteCellText(worksheetPart, "H5", projectName);
                WriteRows(worksheetPart, 9, preview.Rows, row =>
                {
                    WriteCellText(worksheetPart, $"B{row.RowIndex}", row.Item.Sequence.ToString());
                    WriteCellText(worksheetPart, $"C{row.RowIndex}", row.Item.Name);
                    WriteCellText(worksheetPart, $"K{row.RowIndex}", row.Item.Count.ToString());
                    WriteCellText(worksheetPart, $"N{row.RowIndex}", row.Item.ConstructorResult);
                    WriteCellText(worksheetPart, $"U{row.RowIndex}", row.Item.SupervisorConclusion);
                });
                WriteCellText(worksheetPart, $"K{19 + ExtraRows(9, 18, preview.Rows.Count)}", preview.Totals.SubDivisionCount.ToString());
                WriteCellText(worksheetPart, $"E{20 + ExtraRows(9, 18, preview.Rows.Count)}", preview.Totals.SubItemCount.ToString());
                break;
        }

        worksheetPart.Worksheet.Save();
    }

    private static void WriteRows(
        WorksheetPart worksheetPart,
        uint startRow,
        IReadOnlyList<SummaryPreviewRow> rows,
        Action<(uint RowIndex, SummaryPreviewRow Item)> write)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            write((startRow + (uint)index, rows[index]));
        }
    }

    private static void EnsureDataRows(WorksheetPart worksheetPart, uint dataStart, uint dataEnd, int requiredRows, int lastColumnIndex)
    {
        var reservedRows = (int)(dataEnd - dataStart + 1);
        if (requiredRows <= reservedRows)
        {
            return;
        }

        InsertRows(worksheetPart.Worksheet, dataEnd + 1, requiredRows - reservedRows, dataEnd, lastColumnIndex);
    }

    private static void InsertRows(Worksheet worksheet, uint insertAt, int count, uint styleRowIndex, int lastColumnIndex)
    {
        var sheetData = worksheet.GetFirstChild<SheetData>() ?? worksheet.AppendChild(new SheetData());
        var styleRow = sheetData.Elements<Row>().FirstOrDefault(row => row.RowIndex?.Value == styleRowIndex)
                       ?? throw new InvalidOperationException($"模板缺少样式行：{styleRowIndex}");
        var mergeCells = worksheet.Elements<MergeCells>().FirstOrDefault();
        var styleMerges = mergeCells?.Elements<MergeCell>()
            .Select(item => item.Reference?.Value ?? "")
            .Where(reference => TryParseRange(reference, out _, out var firstRow, out _, out var lastRow) &&
                                firstRow == styleRowIndex &&
                                lastRow == styleRowIndex)
            .ToArray() ?? [];

        foreach (var row in sheetData.Elements<Row>()
                     .Where(row => row.RowIndex?.Value >= insertAt)
                     .OrderByDescending(row => row.RowIndex!.Value)
                     .ToArray())
        {
            var newRowIndex = row.RowIndex!.Value + (uint)count;
            row.RowIndex = newRowIndex;
            foreach (var cell in row.Elements<Cell>())
            {
                if (cell.CellReference?.Value is { } reference)
                {
                    cell.CellReference = ShiftCellReference(reference, count);
                }
            }
        }

        if (mergeCells is not null)
        {
            foreach (var mergeCell in mergeCells.Elements<MergeCell>().ToArray())
            {
                if (mergeCell.Reference?.Value is { } reference)
                {
                    mergeCell.Reference = ShiftRangeReference(reference, insertAt, count);
                }
            }
        }

        for (var index = 0; index < count; index++)
        {
            var newRowIndex = insertAt + (uint)index;
            var newRow = (Row)styleRow.CloneNode(true);
            newRow.RowIndex = newRowIndex;
            foreach (var cell in newRow.Elements<Cell>())
            {
                var columnName = cell.CellReference?.Value is { } reference
                    ? GetColumnName(reference)
                    : ColumnNameFromIndex(lastColumnIndex);
                cell.CellReference = $"{columnName}{newRowIndex}";
                cell.CellValue = null;
                cell.InlineString = null;
                cell.DataType = null;
            }

            InsertRowSorted(sheetData, newRow);

            if (mergeCells is not null)
            {
                foreach (var reference in styleMerges)
                {
                    mergeCells.AppendChild(new MergeCell
                    {
                        Reference = MoveSingleRowRange(reference, newRowIndex)
                    });
                }
            }
        }
    }

    private static void InsertRowSorted(SheetData sheetData, Row newRow)
    {
        var nextRow = sheetData.Elements<Row>().FirstOrDefault(row => row.RowIndex?.Value > newRow.RowIndex?.Value);
        if (nextRow is null)
        {
            sheetData.Append(newRow);
        }
        else
        {
            sheetData.InsertBefore(newRow, nextRow);
        }
    }

    private static void WriteCellText(WorksheetPart worksheetPart, string cellReference, string value)
    {
        var cell = GetOrCreateCell(worksheetPart.Worksheet, cellReference);
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
            InsertRowSorted(sheetData, row);
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

    private static uint GetRowIndex(string cellReference)
    {
        var rowName = new string(cellReference.SkipWhile(char.IsLetter).ToArray());
        return uint.TryParse(rowName, out var rowIndex) ? rowIndex : 1;
    }

    private static string GetColumnName(string cellReference)
    {
        return new string(cellReference.TakeWhile(char.IsLetter).ToArray());
    }

    private static string ShiftCellReference(string cellReference, int offset)
    {
        var columnName = GetColumnName(cellReference);
        var rowIndex = GetRowIndex(cellReference);
        return $"{columnName}{rowIndex + (uint)offset}";
    }

    private static string ShiftRangeReference(string reference, uint insertAt, int offset)
    {
        if (!TryParseRange(reference, out var firstColumn, out var firstRow, out var lastColumn, out var lastRow))
        {
            return reference;
        }

        if (firstRow >= insertAt)
        {
            firstRow += (uint)offset;
            lastRow += (uint)offset;
        }
        else if (lastRow >= insertAt)
        {
            lastRow += (uint)offset;
        }

        return $"{firstColumn}{firstRow}:{lastColumn}{lastRow}";
    }

    private static string MoveSingleRowRange(string reference, uint rowIndex)
    {
        return TryParseRange(reference, out var firstColumn, out _, out var lastColumn, out _)
            ? $"{firstColumn}{rowIndex}:{lastColumn}{rowIndex}"
            : reference;
    }

    private static bool TryParseRange(string reference, out string firstColumn, out uint firstRow, out string lastColumn, out uint lastRow)
    {
        firstColumn = "";
        firstRow = 0;
        lastColumn = "";
        lastRow = 0;
        var parts = reference.Split(':', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        firstColumn = GetColumnName(parts[0]);
        lastColumn = GetColumnName(parts[1]);
        firstRow = GetRowIndex(parts[0]);
        lastRow = GetRowIndex(parts[1]);
        return !string.IsNullOrWhiteSpace(firstColumn) &&
               !string.IsNullOrWhiteSpace(lastColumn) &&
               firstRow > 0 &&
               lastRow > 0;
    }

    private static int ExtraRows(uint dataStart, uint dataEnd, int requiredRows)
    {
        return Math.Max(0, requiredRows - (int)(dataEnd - dataStart + 1));
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

    private static string SanitizeFileName(string value)
    {
        var sanitized = string.IsNullOrWhiteSpace(value) ? "汇总表.xlsx" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? sanitized : $"{sanitized}.xlsx";
    }

    private static string SanitizePathSegment(string value)
    {
        var sanitized = string.IsNullOrWhiteSpace(value) ? "默认单位工程" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized;
    }

    private static string JoinNames(params string[] values)
    {
        return string.Join(" / ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string ColumnNameFromIndex(int index)
    {
        var name = "";
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }

        return name;
    }

    private static string ExtractModuleId(string templateNodeId)
    {
        const string prefix = "module:";
        const string marker = ":template:";
        if (!templateNodeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var markerIndex = templateNodeId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0 ? "" : templateNodeId[prefix.Length..markerIndex];
    }

    private sealed record InspectionBatchSummarySource(
        GeneratedDocumentIndexInfo Document,
        InspectionBatchDocumentDetailInfo Detail,
        TemplateNodeContext Context);

    private sealed record SummaryNodeRef(
        string SummaryType,
        string ModuleId,
        string CategoryId,
        string RawId);
}
