using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Models;
using GeneratorService.Modules;
using GeneratorService.Projects;
using Microsoft.Data.Sqlite;
using Serilog;

namespace GeneratorService.TemplateLibrary;

public sealed class SummaryService
{
    private const string Division = "Division";
    private const string SubDivision = "SubDivision";
    private const string SubItem = "SubItem";
    private const string ConstructorResult = "符合要求";
    private const string SupervisorConclusion = "验收合格";

    private static readonly Regex CategoryNodeRegex = new(
        @"^module:(?<moduleId>.+):category:(?<categoryId>\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly DirectoryInfo _rootPath;
    private readonly TemplateTreeRepository _repository;
    private readonly ModuleManager _moduleManager;
    private readonly ProjectManager _projectManager;

    public SummaryService(
        DirectoryInfo rootPath,
        TemplateTreeRepository repository,
        ModuleManager moduleManager,
        ProjectManager projectManager)
    {
        _rootPath = rootPath;
        _repository = repository;
        _moduleManager = moduleManager;
        _projectManager = projectManager;
    }

    public SummaryTreeResult GetTree(string? projectId)
    {
        var effectiveProjectId = ResolveProjectId(projectId);
        var warnings = new List<string>();
        var documents = LoadDocuments(effectiveProjectId, warnings);
        var nodes = BuildTree(documents);
        return new SummaryTreeResult(
            true,
            effectiveProjectId,
            _repository.GetProjectName(effectiveProjectId),
            nodes,
            warnings);
    }

    public SummaryPreviewResult GetPreview(string? projectId, string type, string categoryId)
    {
        var effectiveProjectId = ResolveProjectId(projectId);
        var summaryType = NormalizeSummaryType(type);
        var target = ParseCategoryNodeId(categoryId);
        var warnings = new List<string>();
        var documents = LoadDocuments(effectiveProjectId, warnings)
            .Where(item => MatchesCategory(item.Path, summaryType, target.ModuleId, target.CategoryId))
            .ToArray();

        foreach (var item in documents)
        {
            if (string.IsNullOrWhiteSpace(item.Document.Capacity))
            {
                warnings.Add($"资料“{item.Document.DocumentName}”缺少检验批容量，预览中按空值显示。");
            }

            if (string.IsNullOrWhiteSpace(item.Document.PartName))
            {
                warnings.Add($"资料“{item.Document.DocumentName}”缺少检验批部位，预览中按空值显示。");
            }
        }

        var rows = BuildPreviewRows(summaryType, documents);
        var first = documents.FirstOrDefault()?.Path;
        var title = first is null
            ? summaryType
            : summaryType switch
            {
                SubItem => $"{first.SubItemName}分项工程质量验收记录",
                SubDivision => $"{first.SubDivisionName}子分部工程质量验收记录",
                Division => $"{first.DivisionName}分部工程质量验收记录",
                _ => summaryType
            };

        return new SummaryPreviewResult(
            true,
            effectiveProjectId,
            summaryType,
            categoryId,
            title,
            first?.DivisionName ?? "",
            summaryType == Division ? "" : first?.SubDivisionName ?? "",
            summaryType == SubItem ? first?.SubItemName ?? "" : "",
            rows,
            BuildTotals(documents),
            warnings);
    }

    public GenerateSummaryResult Generate(GenerateSummaryRequest request)
    {
        var preview = GetPreview(request.ProjectId, request.Type, request.CategoryId);
        if (preview.Rows.Count == 0)
        {
            throw new InvalidOperationException("当前汇总对象没有可生成的有效资料。");
        }

        var templatePath = ResolveSummaryTemplate(preview.SummaryType);
        var project = _projectManager.ResolveProject(preview.ProjectId);
        var outputDirectory = Path.Combine(project.GeneratedFormsPath, "Summary", preview.SummaryType);
        Directory.CreateDirectory(outputDirectory);

        var outputName = SanitizeFileName($"{preview.Title}-{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        var outputPath = ResolveUniquePath(outputDirectory, outputName);
        File.Copy(templatePath, outputPath);
        FillWorkbook(outputPath, project.ProjectName, preview);

        var target = ParseCategoryNodeId(preview.CategoryId);
        var summaryDocument = _repository.InsertSummaryDocument(
            preview.ProjectId,
            target.ModuleId,
            preview.SummaryType,
            preview.DivisionName,
            preview.SubDivisionName,
            preview.SubItemName,
            preview.Title,
            outputPath,
            preview.Totals.SourceDocumentCount);

        return new GenerateSummaryResult(
            true,
            summaryDocument,
            outputPath,
            "汇总表已生成。",
            preview.Warnings);
    }

    private string ResolveProjectId(string? projectId)
    {
        return string.IsNullOrWhiteSpace(projectId)
            ? _projectManager.GetCurrentProject().ProjectId
            : projectId.Trim();
    }

    private IReadOnlyList<SummarySourceDocument> LoadDocuments(string projectId, List<string> warnings)
    {
        var documents = _repository.ListProjectDocuments(projectId)
            .Where(document =>
                !string.Equals(document.Status, "Deleted", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(document.Status, "Invalid", StringComparison.OrdinalIgnoreCase))
            .OrderBy(document => document.CreatedAt)
            .ThenBy(document => document.DocumentName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<SummarySourceDocument>();
        foreach (var moduleGroup in documents.GroupBy(document => document.ModuleId))
        {
            var module = _moduleManager.FindModule(moduleGroup.Key);
            if (module is not { IsValid: true, RulesDbPath: not null })
            {
                foreach (var document in moduleGroup)
                {
                    WarnSkip(warnings, document, $"模块“{document.ModuleId}”不可用，无法反查分部分项层级。");
                }

                continue;
            }

            var paths = LoadCategoryPaths(module.RulesDbPath, module.Manifest!.ModuleId);
            foreach (var document in moduleGroup)
            {
                if (!paths.TryGetValue(document.TemplateItemId, out var path))
                {
                    WarnSkip(warnings, document, "无法通过 TemplateItemId 反查完整分部、子分部、分项层级。");
                    continue;
                }

                result.Add(new SummarySourceDocument(document, path));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<long, SummaryCategoryPath> LoadCategoryPaths(string rulesDbPath, string moduleId)
    {
        using var connection = new SqliteConnection($"Data Source={rulesDbPath};Mode=ReadOnly");
        connection.Open();

        var categories = new List<CategoryInfo>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, ParentId, Name, Level, SortOrder, CategoryType FROM TemplateCategory;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                categories.Add(new CategoryInfo(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    reader.IsDBNull(5) ? "" : reader.GetString(5)));
            }
        }

        var categoryById = categories.ToDictionary(category => category.Id);
        var result = new Dictionary<long, SummaryCategoryPath>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, CategoryId FROM TemplateItem WHERE COALESCE(IsEnabled, 1) <> 0;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var templateItemId = reader.GetInt64(0);
                var categoryId = reader.GetInt64(1);
                var chain = BuildCategoryChain(categoryById, categoryId);
                if (TryResolvePath(moduleId, chain, out var path))
                {
                    result[templateItemId] = path;
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<CategoryInfo> BuildCategoryChain(
        IReadOnlyDictionary<long, CategoryInfo> categoryById,
        long categoryId)
    {
        var stack = new Stack<CategoryInfo>();
        var currentId = categoryId;
        while (categoryById.TryGetValue(currentId, out var current))
        {
            stack.Push(current);
            currentId = current.ParentId ?? 0;
        }

        return stack.ToArray();
    }

    private static bool TryResolvePath(
        string moduleId,
        IReadOnlyList<CategoryInfo> chain,
        out SummaryCategoryPath path)
    {
        path = default!;
        if (chain.Count < 3)
        {
            return false;
        }

        var division = FindCategory(chain, category =>
            category.CategoryType.Contains("分部", StringComparison.Ordinal) &&
            !category.CategoryType.Contains("子分部", StringComparison.Ordinal), 1) ?? chain[0];
        var subDivision = FindCategory(chain, category =>
            category.CategoryType.Contains("子分部", StringComparison.Ordinal), 2) ?? chain.Skip(1).FirstOrDefault();
        var subItem = FindCategory(chain, category =>
            category.CategoryType.Contains("分项", StringComparison.Ordinal), 3) ?? chain.Skip(2).FirstOrDefault();

        if (subDivision is null || subItem is null)
        {
            return false;
        }

        path = new SummaryCategoryPath(
            moduleId,
            division.Id,
            division.Name,
            subDivision.Id,
            subDivision.Name,
            subItem.Id,
            subItem.Name,
            division.SortOrder,
            subDivision.SortOrder,
            subItem.SortOrder);
        return true;
    }

    private static CategoryInfo? FindCategory(
        IReadOnlyList<CategoryInfo> chain,
        Func<CategoryInfo, bool> typePredicate,
        int fallbackLevel)
    {
        return chain.FirstOrDefault(typePredicate)
               ?? chain.FirstOrDefault(category => category.Level == fallbackLevel);
    }

    private static IReadOnlyList<SummaryTreeNodeDto> BuildTree(IReadOnlyList<SummarySourceDocument> documents)
    {
        return documents
            .GroupBy(item => new { item.Path.ModuleId, item.Path.DivisionId })
            .OrderBy(group => group.Min(item => item.Path.DivisionSortOrder))
            .ThenBy(group => group.First().Path.DivisionName, StringComparer.OrdinalIgnoreCase)
            .Select(divisionGroup =>
            {
                var firstDivision = divisionGroup.First().Path;
                var subDivisionNodes = divisionGroup
                    .GroupBy(item => item.Path.SubDivisionId)
                    .OrderBy(group => group.Min(item => item.Path.SubDivisionSortOrder))
                    .ThenBy(group => group.First().Path.SubDivisionName, StringComparer.OrdinalIgnoreCase)
                    .Select(subDivisionGroup =>
                    {
                        var firstSubDivision = subDivisionGroup.First().Path;
                        var subItemNodes = subDivisionGroup
                            .GroupBy(item => item.Path.SubItemId)
                            .OrderBy(group => group.Min(item => item.Path.SubItemSortOrder))
                            .ThenBy(group => group.First().Path.SubItemName, StringComparer.OrdinalIgnoreCase)
                            .Select(subItemGroup =>
                            {
                                var firstSubItem = subItemGroup.First().Path;
                                return CreateNode(SubItem, firstSubItem, subItemGroup, []);
                            })
                            .ToArray();

                        return CreateNode(SubDivision, firstSubDivision, subDivisionGroup, subItemNodes);
                    })
                    .ToArray();

                return CreateNode(Division, firstDivision, divisionGroup, subDivisionNodes);
            })
            .ToArray();
    }

    private static SummaryTreeNodeDto CreateNode(
        string summaryType,
        SummaryCategoryPath path,
        IEnumerable<SummarySourceDocument> documents,
        IReadOnlyList<SummaryTreeNodeDto> children)
    {
        var items = documents.ToArray();
        var categoryId = CategoryNodeId(path.ModuleId, summaryType switch
        {
            Division => path.DivisionId,
            SubDivision => path.SubDivisionId,
            SubItem => path.SubItemId,
            _ => path.SubItemId
        });
        var name = summaryType switch
        {
            Division => path.DivisionName,
            SubDivision => path.SubDivisionName,
            SubItem => path.SubItemName,
            _ => path.SubItemName
        };

        return new SummaryTreeNodeDto(
            $"summary:{summaryType}:{categoryId}",
            items.FirstOrDefault()?.Document.ProjectId ?? "",
            path.ModuleId,
            categoryId,
            summaryType,
            name,
            path.DivisionName,
            path.SubDivisionName,
            path.SubItemName,
            items.Length,
            items.Length,
            items.Select(item => item.Path.SubItemId).Distinct().Count(),
            items.Select(item => item.Path.SubDivisionId).Distinct().Count(),
            children);
    }

    private static IReadOnlyList<SummaryPreviewRow> BuildPreviewRows(
        string summaryType,
        IReadOnlyList<SummarySourceDocument> documents)
    {
        return summaryType switch
        {
            SubItem => documents
                .Select((item, index) => new SummaryPreviewRow(
                    index + 1,
                    item.Document.DocumentName,
                    item.Document.Capacity ?? "",
                    item.Document.PartName,
                    1,
                    ConstructorResult,
                    SupervisorConclusion))
                .ToArray(),
            SubDivision => documents
                .GroupBy(item => item.Path.SubItemId)
                .OrderBy(group => group.Min(item => item.Path.SubItemSortOrder))
                .ThenBy(group => group.First().Path.SubItemName, StringComparer.OrdinalIgnoreCase)
                .Select((group, index) => new SummaryPreviewRow(
                    index + 1,
                    group.First().Path.SubItemName,
                    "",
                    "",
                    group.Count(),
                    ConstructorResult,
                    SupervisorConclusion))
                .ToArray(),
            Division => documents
                .GroupBy(item => item.Path.SubDivisionId)
                .OrderBy(group => group.Min(item => item.Path.SubDivisionSortOrder))
                .ThenBy(group => group.First().Path.SubDivisionName, StringComparer.OrdinalIgnoreCase)
                .Select((group, index) => new SummaryPreviewRow(
                    index + 1,
                    group.First().Path.SubDivisionName,
                    "",
                    "",
                    group.Select(item => item.Path.SubItemId).Distinct().Count(),
                    ConstructorResult,
                    SupervisorConclusion))
                .ToArray(),
            _ => throw new InvalidOperationException($"未知汇总类型：{summaryType}")
        };
    }

    private static SummaryPreviewTotals BuildTotals(IReadOnlyList<SummarySourceDocument> documents)
    {
        return new SummaryPreviewTotals(
            documents.Count,
            documents.Count,
            documents.Select(item => item.Path.SubItemId).Distinct().Count(),
            documents.Select(item => item.Path.SubDivisionId).Distinct().Count());
    }

    private static bool MatchesCategory(
        SummaryCategoryPath path,
        string summaryType,
        string moduleId,
        long categoryId)
    {
        if (!string.Equals(path.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return summaryType switch
        {
            Division => path.DivisionId == categoryId,
            SubDivision => path.SubDivisionId == categoryId,
            SubItem => path.SubItemId == categoryId,
            _ => false
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
            : throw new FileNotFoundException($"未找到 {prefix} 汇总模板，请确认 Templates/Summary 中存在 .xlsx 工作模板。", directory);
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

    private static void EnsureDataRows(
        WorksheetPart worksheetPart,
        uint dataStart,
        uint dataEnd,
        int requiredRows,
        int lastColumnIndex)
    {
        var reservedRows = (int)(dataEnd - dataStart + 1);
        if (requiredRows <= reservedRows)
        {
            return;
        }

        InsertRows(worksheetPart.Worksheet, dataEnd + 1, requiredRows - reservedRows, dataEnd, lastColumnIndex);
    }

    private static void InsertRows(
        Worksheet worksheet,
        uint insertAt,
        int count,
        uint styleRowIndex,
        int lastColumnIndex)
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

    private static bool TryParseRange(
        string reference,
        out string firstColumn,
        out uint firstRow,
        out string lastColumn,
        out uint lastRow)
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

    private static (string ModuleId, long CategoryId) ParseCategoryNodeId(string categoryId)
    {
        var match = CategoryNodeRegex.Match(categoryId);
        if (!match.Success || !long.TryParse(match.Groups["categoryId"].Value, out var id))
        {
            throw new InvalidOperationException($"汇总分类节点无效：{categoryId}");
        }

        return (match.Groups["moduleId"].Value, id);
    }

    private static string CategoryNodeId(string moduleId, long categoryId)
    {
        return $"module:{moduleId}:category:{categoryId}";
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

        return sanitized.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? sanitized
            : $"{sanitized}.xlsx";
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

    private static void WarnSkip(List<string> warnings, ProjectDocumentInfo document, string reason)
    {
        var message = $"资料“{document.DocumentName}”已跳过：{reason}";
        warnings.Add(message);
        Log.Warning("分部分项汇总跳过资料。DocumentId={DocumentId}, Reason={Reason}", document.Id, reason);
    }

    private sealed record CategoryInfo(
        long Id,
        long? ParentId,
        string Name,
        int Level,
        int SortOrder,
        string CategoryType);

    private sealed record SummaryCategoryPath(
        string ModuleId,
        long DivisionId,
        string DivisionName,
        long SubDivisionId,
        string SubDivisionName,
        long SubItemId,
        string SubItemName,
        int DivisionSortOrder,
        int SubDivisionSortOrder,
        int SubItemSortOrder);

    private sealed record SummarySourceDocument(
        ProjectDocumentInfo Document,
        SummaryCategoryPath Path);
}
