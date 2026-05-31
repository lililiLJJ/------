using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Models;

namespace GeneratorService.TemplateLibrary;

internal static class TemplateWorkbookHelper
{
    private static readonly Regex SimplePlaceholderRegex = new(@"\{\{(?<name>[^:{}]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex CellReferenceRegex = new(@"^[A-Z]{1,3}[1-9][0-9]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static TemplateWorkbookInfo Inspect(string filePath)
    {
        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少 WorkbookPart。");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToArray() ?? [];
        var worksheetInfos = new List<TemplateWorksheetInfo>();
        var placeholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is null)
            {
                continue;
            }

            if (workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            var cells = new List<TemplateCellTextInfo>();
            foreach (var cell in worksheetPart.Worksheet.Descendants<Cell>())
            {
                var cellReference = cell.CellReference?.Value;
                if (string.IsNullOrWhiteSpace(cellReference))
                {
                    continue;
                }

                var text = ReadCellText(cell, sharedStrings).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                cells.Add(new TemplateCellTextInfo(cellReference, text));
                foreach (Match match in SimplePlaceholderRegex.Matches(text))
                {
                    var name = match.Groups["name"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        placeholders.Add(name);
                    }
                }
            }

            worksheetInfos.Add(new TemplateWorksheetInfo(sheet.Name?.Value ?? "Sheet", worksheetPart, cells));
        }

        return new TemplateWorkbookInfo(worksheetInfos, placeholders);
    }

    public static Dictionary<string, List<TemplateFieldTarget>> FindAdjacentTargets(
        string filePath,
        IReadOnlyDictionary<string, string[]> aliases)
    {
        var result = new Dictionary<string, List<TemplateFieldTarget>>(StringComparer.OrdinalIgnoreCase);
        var workbook = Inspect(filePath);
        foreach (var field in aliases)
        {
            var targets = new List<TemplateFieldTarget>();
            foreach (var worksheet in workbook.Worksheets)
            {
                if (string.Equals(field.Key, TemplateAdaptationFields.ConstructionDate, StringComparison.OrdinalIgnoreCase))
                {
                    targets.AddRange(FindDateTargets(worksheet));
                    if (targets.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                foreach (var cell in worksheet.Cells)
                {
                    if (!field.Value.Any(alias => MatchesAlias(cell.Text, alias)))
                    {
                        continue;
                    }

                    var target = GetHorizontalTarget(
                        worksheet,
                        cell.CellReference,
                        preferFarthestRight: string.Equals(field.Key, TemplateAdaptationFields.SupervisionUnit, StringComparison.OrdinalIgnoreCase));
                    if (target is null)
                    {
                        continue;
                    }

                    AddTarget(targets, worksheet.Name, target);
                    break;
                }

                if (targets.Count > 0)
                {
                    break;
                }
            }

            result[field.Key] = targets;
        }

        return result;
    }

    public static TemplateLayoutSnapshot CaptureLayoutSnapshot(string filePath)
    {
        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("模板缺少 WorkbookPart。");
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToArray() ?? [];
        var worksheetSnapshots = new List<TemplateWorksheetLayoutSnapshot>();
        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is null || workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            var worksheet = worksheetPart.Worksheet;
            var columns = worksheet.Elements<Columns>().FirstOrDefault();
            var mergeCells = worksheet.Elements<MergeCells>().FirstOrDefault();
            var pageMargins = worksheet.Elements<PageMargins>().FirstOrDefault();
            var pageSetup = worksheet.Elements<PageSetup>().FirstOrDefault();
            var printOptions = worksheet.Elements<PrintOptions>().FirstOrDefault();
            var rowBreaks = worksheet.Elements<RowBreaks>().FirstOrDefault();
            var columnBreaks = worksheet.Elements<ColumnBreaks>().FirstOrDefault();
            var rowBreakCount = rowBreaks?.Count?.Value ?? 0U;
            var columnBreakCount = columnBreaks?.Count?.Value ?? 0U;
            var estimatedPageCount = (int)Math.Max(1U, (rowBreakCount + 1U) * (columnBreakCount + 1U));
            worksheetSnapshots.Add(new TemplateWorksheetLayoutSnapshot(
                sheet.Name?.Value ?? "Sheet",
                columns?.OuterXml ?? "",
                mergeCells?.OuterXml ?? "",
                pageMargins?.OuterXml ?? "",
                pageSetup?.OuterXml ?? "",
                printOptions?.OuterXml ?? "",
                rowBreaks?.OuterXml ?? "",
                columnBreaks?.OuterXml ?? "",
                worksheet.Elements<SheetViews>().FirstOrDefault()?.OuterXml ?? "",
                estimatedPageCount));
        }

        var printArea = workbookPart.Workbook.DefinedNames?.Elements<DefinedName>()
            .Where(item => string.Equals(item.Name?.Value, "_xlnm.Print_Area", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Text ?? "")
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];

        return new TemplateLayoutSnapshot(
            workbookPart.WorkbookStylesPart?.Stylesheet?.OuterXml ?? "",
            string.Join("|", printArea),
            worksheetSnapshots);
    }

    public static TemplateLayoutComparisonResult CompareLayout(TemplateLayoutSnapshot before, string filePath)
    {
        var after = CaptureLayoutSnapshot(filePath);
        var differences = new List<string>();
        if (!string.Equals(before.StylesXml, after.StylesXml, StringComparison.Ordinal))
        {
            differences.Add("样式文件发生变化");
        }

        if (!string.Equals(before.PrintArea, after.PrintArea, StringComparison.Ordinal))
        {
            differences.Add("打印区域发生变化");
        }

        var beforeBySheet = before.Worksheets.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var afterBySheet = after.Worksheets.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in beforeBySheet.Keys.Union(afterBySheet.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (!beforeBySheet.TryGetValue(name, out var beforeSheet) ||
                !afterBySheet.TryGetValue(name, out var afterSheet))
            {
                differences.Add($"工作表结构发生变化：{name}");
                continue;
            }

            CompareXml("列宽", beforeSheet.ColumnsXml, afterSheet.ColumnsXml, name, differences);
            CompareXml("合并单元格", beforeSheet.MergeCellsXml, afterSheet.MergeCellsXml, name, differences);
            CompareXml("页边距", beforeSheet.PageMarginsXml, afterSheet.PageMarginsXml, name, differences);
            CompareXml("分页设置", beforeSheet.PageSetupXml, afterSheet.PageSetupXml, name, differences);
            CompareXml("打印选项", beforeSheet.PrintOptionsXml, afterSheet.PrintOptionsXml, name, differences);
            CompareXml("横向分页符", beforeSheet.RowBreaksXml, afterSheet.RowBreaksXml, name, differences);
            CompareXml("纵向分页符", beforeSheet.ColumnBreaksXml, afterSheet.ColumnBreaksXml, name, differences);
            CompareXml("缩放/视图", beforeSheet.SheetViewsXml, afterSheet.SheetViewsXml, name, differences);
            if (beforeSheet.EstimatedPageCount != afterSheet.EstimatedPageCount)
            {
                differences.Add($"打印页数估计变化：{name} {beforeSheet.EstimatedPageCount} -> {afterSheet.EstimatedPageCount}");
            }
        }

        var beforePageCount = before.Worksheets.Sum(item => item.EstimatedPageCount);
        var afterPageCount = after.Worksheets.Sum(item => item.EstimatedPageCount);
        return new TemplateLayoutComparisonResult(differences.Count == 0, beforePageCount, afterPageCount, differences);
    }

    public static void ReplaceSimplePlaceholders(WorkbookPart workbookPart, IReadOnlyDictionary<string, string> replacements)
    {
        if (workbookPart.SharedStringTablePart?.SharedStringTable is { } sharedStringTable)
        {
            foreach (var item in sharedStringTable.Elements<SharedStringItem>())
            {
                var text = item.InnerText;
                var replacement = ReplaceSimple(text, replacements);
                if (replacement == text)
                {
                    continue;
                }

                item.RemoveAllChildren();
                item.AppendChild(new Text(replacement)
                {
                    Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve
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

    public static void WriteCellText(Worksheet worksheet, string cellReference, string value)
    {
        var normalizedCell = cellReference.Trim().ToUpperInvariant();
        if (!CellReferenceRegex.IsMatch(normalizedCell))
        {
            throw new InvalidOperationException($"无效单元格地址：{cellReference}");
        }

        var cell = GetOrCreateCell(worksheet, normalizedCell);
        cell.DataType = CellValues.String;
        cell.CellValue = new CellValue(value);
        cell.InlineString = null;
    }

    public static string ReadCellText(WorksheetPart worksheetPart, string cellReference, SharedStringTable? sharedStringTable)
    {
        var cell = worksheetPart.Worksheet.Descendants<Cell>()
            .FirstOrDefault(item => string.Equals(item.CellReference?.Value, cellReference, StringComparison.OrdinalIgnoreCase));
        return cell is null ? "" : ReadCellText(cell, sharedStringTable);
    }

    public static WorksheetPart? ResolveWorksheetPart(WorkbookPart workbookPart, string? worksheetName)
    {
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToArray() ?? [];
        if (string.IsNullOrWhiteSpace(worksheetName))
        {
            return sheets.FirstOrDefault()?.Id?.Value is { } firstId
                ? workbookPart.GetPartById(firstId) as WorksheetPart
                : null;
        }

        var matched = sheets.FirstOrDefault(item => string.Equals(item.Name?.Value, worksheetName, StringComparison.OrdinalIgnoreCase));
        return matched?.Id?.Value is { } id ? workbookPart.GetPartById(id) as WorksheetPart : null;
    }

    private static void CompareXml(string label, string before, string after, string worksheetName, List<string> differences)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            differences.Add($"{worksheetName} 的{label}发生变化");
        }
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

    private static IReadOnlyList<TemplateFieldTarget> FindDateTargets(TemplateWorksheetInfo worksheet)
    {
        var targets = new List<TemplateFieldTarget>();
        foreach (var cell in worksheet.Cells)
        {
            var normalized = NormalizeText(cell.Text);
            if (!normalized.Contains("年月日", StringComparison.Ordinal))
            {
                continue;
            }

            AddTarget(targets, worksheet.Name, cell.CellReference);
        }

        return targets;
    }

    private static bool MatchesAlias(string cellText, string alias)
    {
        var normalizedCell = NormalizeText(cellText);
        var normalizedAlias = NormalizeText(alias);
        return !string.IsNullOrWhiteSpace(normalizedAlias) &&
               normalizedCell.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string text)
    {
        return string.Concat((text ?? string.Empty)
            .Replace("_x000D_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Where(ch => !char.IsWhiteSpace(ch)));
    }

    private static void AddTarget(List<TemplateFieldTarget> targets, string worksheetName, string cellReference)
    {
        if (targets.Any(item =>
                string.Equals(item.WorksheetName, worksheetName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.CellReference, cellReference, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        targets.Add(new TemplateFieldTarget(worksheetName, cellReference));
    }

    private static string? GetHorizontalTarget(TemplateWorksheetInfo worksheet, string cellReference, bool preferFarthestRight)
    {
        if (!TryParseCellReference(cellReference, out var rowIndex, out var columnIndex))
        {
            return GetNextColumnReference(cellReference);
        }

        var mergedRanges = GetMergedRanges(worksheet.WorksheetPart.Worksheet);
        var matchedRange = mergedRanges.FirstOrDefault(item => item.Contains(rowIndex, columnIndex));
        var sourceRange = matchedRange.StartRow == 0
            ? new TemplateMergedRange(rowIndex, rowIndex, columnIndex, columnIndex)
            : matchedRange;
        var candidates = mergedRanges
            .Where(item =>
                item.StartRow <= rowIndex &&
                item.EndRow >= rowIndex &&
                item.StartColumn > sourceRange.EndColumn)
            .OrderBy(item => preferFarthestRight ? -item.StartColumn : item.StartColumn)
            .ToArray();
        foreach (var candidate in candidates)
        {
            var targetReference = BuildCellReference(candidate.StartColumn, rowIndex);
            if (string.IsNullOrWhiteSpace(GetWorksheetCellText(worksheet, targetReference)))
            {
                return targetReference;
            }
        }

        return GetNextColumnReference(BuildCellReference(sourceRange.EndColumn, rowIndex));
    }

    private static string GetWorksheetCellText(TemplateWorksheetInfo worksheet, string cellReference)
    {
        return worksheet.Cells.FirstOrDefault(item =>
            string.Equals(item.CellReference, cellReference, StringComparison.OrdinalIgnoreCase))?.Text ?? string.Empty;
    }

    private static IReadOnlyList<TemplateMergedRange> GetMergedRanges(Worksheet worksheet)
    {
        var mergeCells = worksheet.Elements<MergeCells>().FirstOrDefault();
        if (mergeCells is null)
        {
            return [];
        }

        var result = new List<TemplateMergedRange>();
        foreach (var item in mergeCells.Elements<MergeCell>())
        {
            if (item.Reference?.Value is { } reference && TryParseMergedRange(reference, out var range))
            {
                result.Add(range);
            }
        }

        return result;
    }

    private static bool TryParseMergedRange(string value, out TemplateMergedRange range)
    {
        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !TryParseCellReference(parts[0], out var startRow, out var startColumn))
        {
            range = default;
            return false;
        }

        if (parts.Length == 1)
        {
            range = new TemplateMergedRange(startRow, startRow, startColumn, startColumn);
            return true;
        }

        if (!TryParseCellReference(parts[1], out var endRow, out var endColumn))
        {
            range = default;
            return false;
        }

        range = new TemplateMergedRange(
            Math.Min(startRow, endRow),
            Math.Max(startRow, endRow),
            Math.Min(startColumn, endColumn),
            Math.Max(startColumn, endColumn));
        return true;
    }

    private static bool TryParseCellReference(string value, out int rowIndex, out int columnIndex)
    {
        rowIndex = 0;
        columnIndex = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var columnName = new string(value.TakeWhile(char.IsLetter).ToArray());
        var rowName = new string(value.SkipWhile(char.IsLetter).ToArray());
        if (string.IsNullOrWhiteSpace(columnName) || !int.TryParse(rowName, out rowIndex))
        {
            return false;
        }

        columnIndex = GetColumnIndex(columnName);
        return columnIndex > 0;
    }

    private static int GetColumnIndex(string columnName)
    {
        var result = 0;
        foreach (var ch in columnName.ToUpperInvariant())
        {
            if (ch is < 'A' or > 'Z')
            {
                return 0;
            }

            result = (result * 26) + (ch - 'A' + 1);
        }

        return result;
    }

    private static string BuildCellReference(int columnIndex, int rowIndex)
    {
        return $"{BuildColumnName(columnIndex)}{rowIndex}";
    }

    private static string BuildColumnName(int columnIndex)
    {
        if (columnIndex <= 0)
        {
            return "A";
        }

        var result = string.Empty;
        var current = columnIndex;
        while (current > 0)
        {
            current--;
            result = (char)('A' + (current % 26)) + result;
            current /= 26;
        }

        return result;
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

    private static string? GetNextColumnReference(string cellReference)
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
}

internal sealed record TemplateWorkbookInfo(
    IReadOnlyList<TemplateWorksheetInfo> Worksheets,
    IReadOnlySet<string> Placeholders);

internal sealed record TemplateWorksheetInfo(
    string Name,
    WorksheetPart WorksheetPart,
    IReadOnlyList<TemplateCellTextInfo> Cells);

internal sealed record TemplateCellTextInfo(
    string CellReference,
    string Text);

internal readonly record struct TemplateMergedRange(
    int StartRow,
    int EndRow,
    int StartColumn,
    int EndColumn)
{
    public bool Contains(int rowIndex, int columnIndex)
    {
        return rowIndex >= StartRow &&
               rowIndex <= EndRow &&
               columnIndex >= StartColumn &&
               columnIndex <= EndColumn;
    }
}

internal sealed record TemplateLayoutSnapshot(
    string StylesXml,
    string PrintArea,
    IReadOnlyList<TemplateWorksheetLayoutSnapshot> Worksheets);

internal sealed record TemplateWorksheetLayoutSnapshot(
    string Name,
    string ColumnsXml,
    string MergeCellsXml,
    string PageMarginsXml,
    string PageSetupXml,
    string PrintOptionsXml,
    string RowBreaksXml,
    string ColumnBreaksXml,
    string SheetViewsXml,
    int EstimatedPageCount);
