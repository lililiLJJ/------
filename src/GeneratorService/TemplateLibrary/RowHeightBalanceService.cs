using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace GeneratorService.TemplateLibrary;

public sealed class RowHeightBalanceService
{
    private const double DefaultColumnWidth = 8.43D;
    private const double DefaultFontSize = 11D;
    private const double MinimumRowHeight = 12D;

    private static readonly HashSet<uint> ProtectedRows = [1, 2, 3, 4, 5, 8, 30, 31, 32];

    private static readonly string[] ProtectedKeywords =
    [
        "标题",
        "表头",
        "签字",
        "签名",
        "盖章",
        "建设单位",
        "施工单位",
        "监理单位",
        "项目负责人",
        "验收结论",
        "页脚",
        "固定说明",
        "合计",
        "结论",
        "说明"
    ];

    public RowHeightBaseline CaptureBaseline(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(filePath))
        {
            return RowHeightBaseline.Empty;
        }

        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return RowHeightBaseline.Empty;
        }

        var worksheets = new Dictionary<string, WorksheetRowHeightBaseline>(StringComparer.OrdinalIgnoreCase);
        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            var defaultHeight = worksheetPart.Worksheet.SheetFormatProperties?.DefaultRowHeight?.Value ?? 15D;
            var rowHeights = sheetData?.Elements<Row>()
                .Where(row => row.RowIndex?.Value is not null)
                .ToDictionary(row => row.RowIndex!.Value, row => GetRowHeight(row, defaultHeight))
                ?? new Dictionary<uint, double>();
            var minimumHeight = rowHeights.Count == 0
                ? defaultHeight
                : Math.Max(MinimumRowHeight, rowHeights.Values.Where(height => height > 0D).DefaultIfEmpty(defaultHeight).Min());

            worksheets[worksheetPart.Uri.ToString()] = new WorksheetRowHeightBaseline(defaultHeight, minimumHeight, rowHeights);
        }

        return new RowHeightBaseline(worksheets);
    }

    public void ApplyLight(
        string filePath,
        string formName,
        IReadOnlyDictionary<string, string> fields,
        RowHeightBaseline? baseline = null)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetValues = BuildTargetValues(formName, fields);
        if (targetValues.Count == 0)
        {
            return;
        }

        baseline ??= CaptureBaseline(filePath);
        using var document = SpreadsheetDocument.Open(filePath, true);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return;
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            baseline.Worksheets.TryGetValue(worksheetPart.Uri.ToString(), out var worksheetBaseline);
            ApplyLightToWorksheet(workbookPart, worksheetPart, sharedStrings, targetValues, worksheetBaseline);
        }
    }

    private static List<string> BuildTargetValues(string formName, IReadOnlyDictionary<string, string> fields)
    {
        var keys = new[]
        {
            "projectName",
            "unitProjectName",
            "division",
            "subItem",
            "subdivision",
            "developerUnitName",
            "constructorUnitName",
            "supervisorUnitName",
            "partName",
            "工程名称",
            "单位工程名称",
            "分部名称",
            "分项名称",
            "施工单位",
            "验收部位",
            "部位名称",
            "检验批部位",
            "施工部位"
        };

        var values = new List<string>();
        AddIfLong(values, formName);
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value))
            {
                AddIfLong(values, value);
            }
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddIfLong(List<string> values, string? value)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && GetTextWeight(trimmed) >= 18)
        {
            values.Add(trimmed);
        }
    }

    private static void ApplyLightToWorksheet(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStrings,
        IReadOnlyList<string> targetValues,
        WorksheetRowHeightBaseline? baseline)
    {
        var worksheet = worksheetPart.Worksheet;
        var sheetData = worksheet.GetFirstChild<SheetData>();
        if (sheetData is null)
        {
            return;
        }

        var rows = sheetData.Elements<Row>()
            .Where(row => row.RowIndex?.Value is not null)
            .ToDictionary(row => row.RowIndex!.Value);
        if (rows.Count == 0)
        {
            return;
        }

        var defaultHeight = baseline?.DefaultHeight ?? worksheet.SheetFormatProperties?.DefaultRowHeight?.Value ?? 15D;
        var baselineHeights = BuildBaselineHeights(rows, baseline, defaultHeight);
        var originalHeights = rows.ToDictionary(item => item.Key, item => GetRowHeight(item.Value, defaultHeight));
        var maxRow = rows.Keys.Max();
        var eligibleRows = rows
            .Where(item => !IsProtectedRow(item.Key, maxRow, item.Value, sharedStrings))
            .Select(item => item.Key)
            .OrderBy(row => row)
            .ToArray();
        if (eligibleRows.Length == 0)
        {
            return;
        }

        var targetRows = eligibleRows
            .Where(row => RowContainsAny(rows[row], sharedStrings, targetValues))
            .ToHashSet();
        if (targetRows.Count == 0)
        {
            return;
        }

        var mergeAreas = GetMergeAreas(worksheet);
        var config = BalanceConfig.Light;
        var changed = false;
        for (var round = 0; round < config.MaxRounds; round++)
        {
            var deficits = MeasureRowDeficits(
                    workbookPart,
                    worksheet,
                    rows,
                    sharedStrings,
                    mergeAreas,
                    targetRows,
                    defaultHeight)
                .Where(item => item.Deficit > 0.25D)
                .OrderByDescending(item => item.Deficit)
                .ToArray();
            if (deficits.Length == 0)
            {
                break;
            }

            var totalNeed = Math.Min(config.MaxRoundNeed, deficits.Sum(item => item.Deficit));
            var deficitRowSet = deficits.Select(item => item.RowIndex).ToHashSet();
            var surplusRows = eligibleRows
                .Where(row => !deficitRowSet.Contains(row))
                .Select(row => new RowSurplus(
                    row,
                    EstimateRowSurplus(workbookPart, worksheet, rows[row], sharedStrings, mergeAreas, defaultHeight, baseline?.MinimumHeight ?? MinimumRowHeight)))
                .Where(item => item.Surplus > 0.2D)
                .ToArray();

            var recovered = DistributeShrink(rows, surplusRows, totalNeed, defaultHeight, config);
            var netBudget = Math.Max(0D, config.MaxNetIncrease - GetNetHeightIncrease(rows, baselineHeights, defaultHeight));
            var directBudget = Math.Min(netBudget, recovered + Math.Max(0D, totalNeed - recovered) * 0.65D);
            var grown = DistributeGrowth(rows, deficits, directBudget, defaultHeight, originalHeights, config);
            var remaining = Math.Max(0D, totalNeed - recovered - directBudget);
            var sharedBudget = Math.Min(
                Math.Max(0D, config.MaxNetIncrease - GetNetHeightIncrease(rows, baselineHeights, defaultHeight)),
                remaining * 0.35D);

            if (sharedBudget > 0.2D)
            {
                grown = DistributeBalancedGrowth(rows, eligibleRows, sharedBudget, defaultHeight, originalHeights, config) || grown;
            }

            changed = changed || recovered > 0D || grown;
            if (!grown && recovered <= 0D)
            {
                break;
            }
        }

        if (!changed)
        {
            return;
        }

        if (GetNetHeightIncrease(rows, baselineHeights, defaultHeight) > config.MaxAllowedNetIncrease)
        {
            RestoreHeights(rows, originalHeights);
            return;
        }

        worksheet.Save();
    }

    private static Dictionary<uint, double> BuildBaselineHeights(
        IReadOnlyDictionary<uint, Row> rows,
        WorksheetRowHeightBaseline? baseline,
        double defaultHeight)
    {
        return rows.ToDictionary(
            item => item.Key,
            item => baseline?.RowHeights.TryGetValue(item.Key, out var height) == true ? height : defaultHeight);
    }

    private static bool IsProtectedRow(uint rowIndex, uint maxRow, Row row, SharedStringTable? sharedStrings)
    {
        if (ProtectedRows.Contains(rowIndex) || rowIndex <= 5 || rowIndex >= maxRow - 5)
        {
            return true;
        }

        var text = GetRowText(row, sharedStrings);
        return ProtectedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RowContainsAny(Row row, SharedStringTable? sharedStrings, IReadOnlyList<string> targetValues)
    {
        var rowText = GetRowText(row, sharedStrings);
        return targetValues.Any(value => rowText.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<RowNeed> MeasureRowDeficits(
        WorkbookPart workbookPart,
        Worksheet worksheet,
        IReadOnlyDictionary<uint, Row> rows,
        SharedStringTable? sharedStrings,
        IReadOnlyList<CellArea> mergeAreas,
        IReadOnlySet<uint> targetRows,
        double defaultHeight)
    {
        var deficits = new Dictionary<uint, double>();
        var measuredAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rowIndex in targetRows)
        {
            if (!rows.TryGetValue(rowIndex, out var row))
            {
                continue;
            }

            foreach (var cell in row.Elements<Cell>())
            {
                var text = GetCellText(cell, sharedStrings).Trim();
                if (GetTextWeight(text) < 10 || !TryParseCellReference(cell.CellReference?.Value, out var address))
                {
                    continue;
                }

                var area = GetDisplayArea(address, mergeAreas);
                var areaKey = $"{area.StartColumn}:{area.StartRow}:{area.EndColumn}:{area.EndRow}";
                if (!measuredAreas.Add(areaKey) || address.Row != area.StartRow || address.Column != area.StartColumn)
                {
                    continue;
                }

                var availableWidth = Math.Max(12D, GetAreaWidthPoints(worksheet, area) - 4D);
                var fontSize = GetCellFontSize(workbookPart, cell);
                var requiredHeight = EstimateRequiredHeight(text, availableWidth, fontSize);
                var currentAreaHeight = GetAreaHeight(rows, area, defaultHeight);
                var missing = requiredHeight - currentAreaHeight;
                if (missing <= 0.25D)
                {
                    continue;
                }

                var affectedRows = Enumerable.Range((int)area.StartRow, (int)(area.EndRow - area.StartRow + 1))
                    .Select(Convert.ToUInt32)
                    .Where(targetRows.Contains)
                    .ToArray();
                if (affectedRows.Length == 0)
                {
                    affectedRows = [rowIndex];
                }

                var perRow = missing / affectedRows.Length;
                foreach (var affectedRow in affectedRows)
                {
                    deficits[affectedRow] = deficits.TryGetValue(affectedRow, out var current)
                        ? Math.Max(current, perRow)
                        : perRow;
                }
            }
        }

        return deficits.Select(item => new RowNeed(item.Key, item.Value));
    }

    private static double EstimateRowSurplus(
        WorkbookPart workbookPart,
        Worksheet worksheet,
        Row row,
        SharedStringTable? sharedStrings,
        IReadOnlyList<CellArea> mergeAreas,
        double defaultHeight,
        double baselineMinimumHeight)
    {
        var currentHeight = GetRowHeight(row, defaultHeight);
        if (currentHeight <= baselineMinimumHeight + 0.2D)
        {
            return 0D;
        }

        var requiredHeight = MinimumRowHeight;
        foreach (var cell in row.Elements<Cell>())
        {
            var text = GetCellText(cell, sharedStrings).Trim();
            if (GetTextWeight(text) > 16 || !TryParseCellReference(cell.CellReference?.Value, out var address))
            {
                return 0D;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var area = GetDisplayArea(address, mergeAreas);
            if (address.Row != area.StartRow || address.Column != area.StartColumn)
            {
                continue;
            }

            var availableWidth = Math.Max(12D, GetAreaWidthPoints(worksheet, area) - 4D);
            var fontSize = GetCellFontSize(workbookPart, cell);
            requiredHeight = Math.Max(requiredHeight, EstimateRequiredHeight(text, availableWidth, fontSize) / area.RowCount);
        }

        var safeMinimum = Math.Max(baselineMinimumHeight, requiredHeight);
        return Math.Max(0D, currentHeight - safeMinimum);
    }

    private static double DistributeShrink(
        IReadOnlyDictionary<uint, Row> rows,
        IReadOnlyList<RowSurplus> surplusRows,
        double totalNeed,
        double defaultHeight,
        BalanceConfig config)
    {
        if (surplusRows.Count == 0 || totalNeed <= 0D)
        {
            return 0D;
        }

        var totalSurplus = surplusRows.Sum(item => item.Surplus);
        var recovered = 0D;
        foreach (var item in surplusRows)
        {
            var currentHeight = GetRowHeight(rows[item.RowIndex], defaultHeight);
            var proportionalShare = totalNeed * (item.Surplus / Math.Max(0.1D, totalSurplus));
            var shrink = Math.Min(Math.Min(item.Surplus, proportionalShare), config.MaxRoundShrink);
            if (shrink <= 0.05D)
            {
                continue;
            }

            SetRowHeight(rows[item.RowIndex], currentHeight - shrink);
            recovered += shrink;
        }

        return recovered;
    }

    private static bool DistributeGrowth(
        IReadOnlyDictionary<uint, Row> rows,
        IReadOnlyList<RowNeed> needs,
        double budget,
        double defaultHeight,
        IReadOnlyDictionary<uint, double> originalHeights,
        BalanceConfig config)
    {
        if (needs.Count == 0 || budget <= 0D)
        {
            return false;
        }

        var changed = false;
        var totalNeed = needs.Sum(item => item.Deficit);
        foreach (var item in needs)
        {
            var currentHeight = GetRowHeight(rows[item.RowIndex], defaultHeight);
            var proportionalShare = budget * (item.Deficit / Math.Max(0.1D, totalNeed));
            var totalGrowth = currentHeight - originalHeights[item.RowIndex];
            var increase = Math.Min(
                Math.Min(item.Deficit, proportionalShare),
                Math.Min(config.MaxRoundIncrease, Math.Max(0D, config.MaxTotalRowIncrease - totalGrowth)));
            if (increase <= 0.05D)
            {
                continue;
            }

            SetRowHeight(rows[item.RowIndex], currentHeight + increase);
            changed = true;
        }

        return changed;
    }

    private static bool DistributeBalancedGrowth(
        IReadOnlyDictionary<uint, Row> rows,
        IReadOnlyList<uint> eligibleRows,
        double budget,
        double defaultHeight,
        IReadOnlyDictionary<uint, double> originalHeights,
        BalanceConfig config)
    {
        var perRow = Math.Min(config.MaxBalancedGrowthPerRow, budget / Math.Max(1, eligibleRows.Count));
        if (perRow <= 0.05D)
        {
            return false;
        }

        var changed = false;
        foreach (var rowIndex in eligibleRows)
        {
            var currentHeight = GetRowHeight(rows[rowIndex], defaultHeight);
            var totalGrowth = currentHeight - originalHeights[rowIndex];
            var increase = Math.Min(perRow, Math.Max(0D, config.MaxTotalRowIncrease - totalGrowth));
            if (increase <= 0.05D)
            {
                continue;
            }

            SetRowHeight(rows[rowIndex], currentHeight + increase);
            changed = true;
        }

        return changed;
    }

    private static double GetNetHeightIncrease(
        IReadOnlyDictionary<uint, Row> rows,
        IReadOnlyDictionary<uint, double> baselineHeights,
        double defaultHeight)
    {
        return rows.Sum(item => GetRowHeight(item.Value, defaultHeight) - baselineHeights.GetValueOrDefault(item.Key, defaultHeight));
    }

    private static void RestoreHeights(IReadOnlyDictionary<uint, Row> rows, IReadOnlyDictionary<uint, double> originalHeights)
    {
        foreach (var item in originalHeights)
        {
            if (rows.TryGetValue(item.Key, out var row))
            {
                SetRowHeight(row, item.Value);
            }
        }
    }

    private static double GetRowHeight(Row row, double defaultHeight)
    {
        return row.Height?.Value is > 0D ? row.Height.Value : defaultHeight;
    }

    private static void SetRowHeight(Row row, double height)
    {
        row.Height = Math.Round(Math.Max(MinimumRowHeight, height), 2);
        row.CustomHeight = true;
    }

    private static string GetRowText(Row row, SharedStringTable? sharedStrings)
    {
        return string.Join(" ", row.Elements<Cell>()
            .Select(cell => GetCellText(cell, sharedStrings))
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.InlineString?.InnerText is { Length: > 0 } inlineText)
        {
            return inlineText;
        }

        var value = cell.CellValue?.Text ?? "";
        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(value, out var sharedStringIndex) &&
            sharedStrings is not null)
        {
            return sharedStrings.ElementAt(sharedStringIndex).InnerText;
        }

        return value;
    }

    private static double GetCellFontSize(WorkbookPart workbookPart, Cell cell)
    {
        var stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
        var styleIndex = cell.StyleIndex?.Value ?? 0U;
        var cellFormat = stylesheet?.CellFormats?.Elements<CellFormat>().ElementAtOrDefault((int)styleIndex);
        var fontId = cellFormat?.FontId?.Value ?? 0U;
        var font = stylesheet?.Fonts?.Elements<DocumentFormat.OpenXml.Spreadsheet.Font>().ElementAtOrDefault((int)fontId);
        return font?.FontSize?.Val?.Value is > 0D ? font.FontSize.Val.Value : DefaultFontSize;
    }

    private static double EstimateRequiredHeight(string text, double widthPoints, double fontSize)
    {
        var lines = EstimateTextLines(text, widthPoints, fontSize);
        var lineHeight = fontSize * 1.25D + 1.5D;
        return lines * lineHeight + 3D;
    }

    private static int EstimateTextLines(string text, double widthPoints, double fontSize)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Sum(line => Math.Max(1, (int)Math.Ceiling(MeasureTextWidth(line, fontSize) / Math.Max(12D, widthPoints))));
    }

    private static double MeasureTextWidth(string text, double fontSize)
    {
        var width = 0D;
        foreach (var character in text)
        {
            width += character switch
            {
                >= '\u4e00' and <= '\u9fff' => fontSize,
                >= '\uff00' and <= '\uffef' => fontSize,
                >= 'A' and <= 'Z' => fontSize * 0.62D,
                >= 'a' and <= 'z' => fontSize * 0.55D,
                >= '0' and <= '9' => fontSize * 0.55D,
                ' ' => fontSize * 0.35D,
                _ => fontSize * 0.5D
            };
        }

        return width;
    }

    private static int GetTextWeight(string text)
    {
        return text.Sum(character => character > 255 ? 2 : 1);
    }

    private static IReadOnlyList<CellArea> GetMergeAreas(Worksheet worksheet)
    {
        return worksheet.Elements<MergeCells>()
            .SelectMany(item => item.Elements<MergeCell>())
            .Select(item => ParseAreaReference(item.Reference?.Value))
            .Where(item => item is not null)
            .Cast<CellArea>()
            .ToArray();
    }

    private static CellArea GetDisplayArea(CellAddress address, IReadOnlyList<CellArea> mergeAreas)
    {
        return mergeAreas.FirstOrDefault(area => area.Contains(address)) ??
            new CellArea(address.Row, address.Row, address.Column, address.Column);
    }

    private static double GetAreaWidthPoints(Worksheet worksheet, CellArea area)
    {
        var total = 0D;
        for (var column = area.StartColumn; column <= area.EndColumn; column++)
        {
            total += ColumnWidthToPoints(GetColumnWidth(worksheet, column));
        }

        return total;
    }

    private static double GetColumnWidth(Worksheet worksheet, uint columnIndex)
    {
        foreach (var column in worksheet.Elements<Columns>().SelectMany(columns => columns.Elements<Column>()))
        {
            var minimum = column.Min?.Value ?? 1U;
            var maximum = column.Max?.Value ?? minimum;
            if (minimum <= columnIndex && columnIndex <= maximum)
            {
                return column.Width?.Value is > 0D ? column.Width.Value : DefaultColumnWidth;
            }
        }

        return worksheet.SheetFormatProperties?.DefaultColumnWidth?.Value is > 0D
            ? worksheet.SheetFormatProperties.DefaultColumnWidth.Value
            : DefaultColumnWidth;
    }

    private static double ColumnWidthToPoints(double width)
    {
        var pixels = width < 1D ? width * 12D : width * 7D + 5D;
        return pixels * 0.75D;
    }

    private static double GetAreaHeight(IReadOnlyDictionary<uint, Row> rows, CellArea area, double defaultHeight)
    {
        var total = 0D;
        for (var rowIndex = area.StartRow; rowIndex <= area.EndRow; rowIndex++)
        {
            total += rows.TryGetValue(rowIndex, out var row) ? GetRowHeight(row, defaultHeight) : defaultHeight;
        }

        return total;
    }

    private static bool TryParseCellReference(string? reference, out CellAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var clean = reference.Replace("$", "", StringComparison.Ordinal);
        var columnName = new string(clean.TakeWhile(char.IsLetter).ToArray());
        var rowName = new string(clean.SkipWhile(char.IsLetter).ToArray());
        if (string.IsNullOrWhiteSpace(columnName) || !uint.TryParse(rowName, out var row))
        {
            return false;
        }

        address = new CellAddress(row, ColumnNameToIndex(columnName));
        return true;
    }

    private static CellArea? ParseAreaReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        if (!TryParseCellReference(parts[0], out var start))
        {
            return null;
        }

        var end = parts.Length > 1 && TryParseCellReference(parts[1], out var parsedEnd) ? parsedEnd : start;
        return new CellArea(
            Math.Min(start.Row, end.Row),
            Math.Max(start.Row, end.Row),
            Math.Min(start.Column, end.Column),
            Math.Max(start.Column, end.Column));
    }

    private static uint ColumnNameToIndex(string columnName)
    {
        var result = 0U;
        foreach (var character in columnName.ToUpperInvariant())
        {
            result = result * 26 + (uint)(character - 'A' + 1);
        }

        return result;
    }

    public sealed record RowHeightBaseline(IReadOnlyDictionary<string, WorksheetRowHeightBaseline> Worksheets)
    {
        public static RowHeightBaseline Empty { get; } =
            new(new Dictionary<string, WorksheetRowHeightBaseline>(StringComparer.OrdinalIgnoreCase));
    }

    public sealed record WorksheetRowHeightBaseline(
        double DefaultHeight,
        double MinimumHeight,
        IReadOnlyDictionary<uint, double> RowHeights);

    private sealed record BalanceConfig(
        int MaxRounds,
        double MaxRoundNeed,
        double MaxRoundIncrease,
        double MaxRoundShrink,
        double MaxTotalRowIncrease,
        double MaxNetIncrease,
        double MaxAllowedNetIncrease,
        double MaxBalancedGrowthPerRow)
    {
        public static BalanceConfig Light { get; } = new(
            MaxRounds: 2,
            MaxRoundNeed: 8D,
            MaxRoundIncrease: 1.2D,
            MaxRoundShrink: 0.6D,
            MaxTotalRowIncrease: 3D,
            MaxNetIncrease: 4D,
            MaxAllowedNetIncrease: 6D,
            MaxBalancedGrowthPerRow: 0.25D);
    }

    private readonly record struct CellAddress(uint Row, uint Column);

    private sealed record CellArea(uint StartRow, uint EndRow, uint StartColumn, uint EndColumn)
    {
        public uint RowCount => EndRow - StartRow + 1;

        public bool Contains(CellAddress address)
        {
            return StartRow <= address.Row &&
                address.Row <= EndRow &&
                StartColumn <= address.Column &&
                address.Column <= EndColumn;
        }
    }

    private sealed record RowNeed(uint RowIndex, double Deficit);
    private sealed record RowSurplus(uint RowIndex, double Surplus);
}
