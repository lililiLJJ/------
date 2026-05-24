using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace GeneratorService.TemplateLibrary;

public sealed class RowHeightBalanceService
{
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
        "合计",
        "结论",
        "说明"
    ];

    public void ApplyLight(string filePath, string formName, IReadOnlyDictionary<string, string> fields)
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

        using var document = SpreadsheetDocument.Open(filePath, true);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return;
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            ApplyLightToWorksheet(worksheetPart, sharedStrings, targetValues);
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
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStrings,
        IReadOnlyList<string> targetValues)
    {
        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
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

        var defaultHeight = worksheetPart.Worksheet.SheetFormatProperties?.DefaultRowHeight?.Value ?? 15D;
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
            .ToArray();
        if (targetRows.Length == 0)
        {
            return;
        }

        var requiredIncreases = targetRows
            .Select(row => new RowNeed(row, EstimateRowDeficit(rows[row], sharedStrings, defaultHeight)))
            .Where(item => item.Deficit > 0.5D)
            .ToArray();
        if (requiredIncreases.Length == 0)
        {
            return;
        }

        var totalNeed = Math.Min(8D, requiredIncreases.Sum(item => Math.Min(2D, item.Deficit)));
        var surplusRows = eligibleRows
            .Except(targetRows)
            .Select(row => new RowSurplus(row, EstimateRowSurplus(rows[row], sharedStrings, defaultHeight)))
            .Where(item => item.Surplus > 0.5D)
            .ToArray();

        var recovered = DistributeShrink(rows, surplusRows, totalNeed);
        var remaining = Math.Max(0D, totalNeed - recovered);
        DistributeGrowth(rows, requiredIncreases, totalNeed);
        if (remaining > 0.5D)
        {
            DistributeBalancedGrowth(rows, eligibleRows, remaining);
        }

        worksheetPart.Worksheet.Save();
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

    private static double EstimateRowDeficit(Row row, SharedStringTable? sharedStrings, double defaultHeight)
    {
        var text = GetRowText(row, sharedStrings);
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0D;
        }

        var currentHeight = GetRowHeight(row, defaultHeight);
        var estimatedLines = Math.Max(1D, Math.Ceiling(GetTextWeight(text) / 42D));
        var requiredHeight = estimatedLines * 14D + 3D;
        return Math.Max(0D, requiredHeight - currentHeight);
    }

    private static double EstimateRowSurplus(Row row, SharedStringTable? sharedStrings, double defaultHeight)
    {
        var textWeight = GetTextWeight(GetRowText(row, sharedStrings));
        var currentHeight = GetRowHeight(row, defaultHeight);
        if (textWeight > 12 || currentHeight <= defaultHeight + 1D)
        {
            return 0D;
        }

        return Math.Min(0.8D, currentHeight - defaultHeight);
    }

    private static double DistributeShrink(
        IReadOnlyDictionary<uint, Row> rows,
        IReadOnlyList<RowSurplus> surplusRows,
        double totalNeed)
    {
        if (surplusRows.Count == 0 || totalNeed <= 0D)
        {
            return 0D;
        }

        var recovered = 0D;
        var perRow = Math.Min(0.6D, totalNeed / surplusRows.Count);
        foreach (var item in surplusRows)
        {
            var shrink = Math.Min(item.Surplus, perRow);
            if (shrink <= 0D)
            {
                continue;
            }

            SetRowHeight(rows[item.RowIndex], GetRowHeight(rows[item.RowIndex], 15D) - shrink);
            recovered += shrink;
        }

        return recovered;
    }

    private static void DistributeGrowth(
        IReadOnlyDictionary<uint, Row> rows,
        IReadOnlyList<RowNeed> needs,
        double totalNeed)
    {
        var perTargetLimit = Math.Min(1.2D, totalNeed / Math.Max(1, needs.Count));
        foreach (var item in needs)
        {
            var increase = Math.Min(item.Deficit, perTargetLimit);
            if (increase <= 0D)
            {
                continue;
            }

            SetRowHeight(rows[item.RowIndex], GetRowHeight(rows[item.RowIndex], 15D) + increase);
        }
    }

    private static void DistributeBalancedGrowth(
        IReadOnlyDictionary<uint, Row> rows,
        IReadOnlyList<uint> eligibleRows,
        double remaining)
    {
        var perRow = Math.Min(0.35D, remaining / Math.Max(1, eligibleRows.Count));
        if (perRow <= 0D)
        {
            return;
        }

        foreach (var rowIndex in eligibleRows)
        {
            SetRowHeight(rows[rowIndex], GetRowHeight(rows[rowIndex], 15D) + perRow);
        }
    }

    private static double GetRowHeight(Row row, double defaultHeight)
    {
        return row.Height?.Value is > 0D ? row.Height.Value : defaultHeight;
    }

    private static void SetRowHeight(Row row, double height)
    {
        row.Height = Math.Round(height, 2);
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

    private static int GetTextWeight(string text)
    {
        return text.Sum(character => character > 255 ? 2 : 1);
    }

    private sealed record RowNeed(uint RowIndex, double Deficit);
    private sealed record RowSurplus(uint RowIndex, double Surplus);
}
