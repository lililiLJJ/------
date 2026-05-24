using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using GeneratorService.Projects;

namespace GeneratorService.Materials;

public sealed class MaterialService
{
    private readonly ProjectManager _projectManager;
    private readonly MaterialRepository _repository;

    public MaterialService(ProjectManager projectManager, MaterialRepository repository)
    {
        _projectManager = projectManager;
        _repository = repository;
    }

    public MaterialListResult List(MaterialQuery query)
    {
        var entries = _repository.ListEntries(query);
        var infos = BuildInfos(query.ProjectId, entries)
            .Where(item => MatchesComputedFilters(item, query))
            .ToArray();
        return new MaterialListResult(true, query.ProjectId, infos.Length, infos);
    }

    public MaterialEntryInfo Create(MaterialEntryCreateRequest request)
    {
        var project = _projectManager.ResolveProject(request.ProjectId);
        var entry = _repository.InsertEntry(project.ProjectId, request);
        return Get(project.ProjectId, entry.Id);
    }

    public MaterialEntryInfo Update(string id, MaterialEntryUpdateRequest request)
    {
        var project = _projectManager.GetCurrentProject();
        _repository.UpdateEntry(project.ProjectId, id, request);
        return Get(project.ProjectId, id);
    }

    public MaterialEntryInfo Get(string projectId, string id)
    {
        var entry = _repository.GetEntry(projectId, id) ?? throw new InvalidOperationException("材料进场记录不存在。");
        return BuildInfos(projectId, [entry]).Single();
    }

    public MaterialTestInfo SaveTest(string id, MaterialTestUpsertRequest request)
    {
        var project = _projectManager.ResolveProject(request.ProjectId);
        EnsureEntry(project.ProjectId, id);
        if (!string.IsNullOrWhiteSpace(request.ReportAttachmentId))
        {
            var attachments = _repository.ListCertificates(project.ProjectId, id);
            if (!attachments.Any(item => string.Equals(item.Id, request.ReportAttachmentId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("检测报告附件不属于当前材料。");
            }
        }

        return _repository.UpsertTest(project.ProjectId, id, request);
    }

    public MaterialEntryRecord EnsureEntry(string projectId, string id)
    {
        return _repository.GetEntry(projectId, id) ?? throw new InvalidOperationException("材料进场记录不存在。");
    }

    internal MaterialEntryInfo BuildInfo(
        MaterialEntryRecord entry,
        IReadOnlyList<MaterialCertificateInfo> certificates,
        MaterialTestInfo? test,
        MaterialApprovalInfo? approval)
    {
        var testStatus = ResolveTestStatus(test);
        var approvalStatus = approval is null ? "未报审" : (approval.Status == "archived" ? "已归档" : "已报审");
        var status = ResolveStatus(entry, certificates, test, approval);
        return new MaterialEntryInfo(
            entry.Id,
            entry.ProjectId,
            entry.MaterialName,
            entry.SpecificationModel,
            entry.Unit,
            entry.Quantity,
            entry.EntryDate,
            entry.Supplier,
            entry.Manufacturer,
            entry.UsePart,
            entry.BatchNo,
            entry.Remark,
            entry.StatusOverride,
            status,
            testStatus,
            approvalStatus,
            certificates,
            test,
            approval,
            entry.CreatedAt,
            entry.UpdatedAt);
    }

    private IReadOnlyList<MaterialEntryInfo> BuildInfos(string projectId, IReadOnlyList<MaterialEntryRecord> entries)
    {
        var ids = entries.Select(item => item.Id).ToArray();
        var certificates = _repository.ListCertificatesByEntryIds(projectId, ids);
        var tests = _repository.ListTestsByEntryIds(projectId, ids);
        var approvals = _repository.ListLatestApprovalsByEntryIds(projectId, ids);
        return entries
            .Select(entry => BuildInfo(
                entry,
                certificates.GetValueOrDefault(entry.Id, []),
                tests.GetValueOrDefault(entry.Id),
                approvals.GetValueOrDefault(entry.Id)))
            .ToArray();
    }

    private static bool MatchesComputedFilters(MaterialEntryInfo item, MaterialQuery query)
    {
        return Matches(query.Status, item.Status) &&
               Matches(query.TestStatus, item.TestStatus) &&
               Matches(query.ApprovalStatus, item.ApprovalStatus);
    }

    private static bool Matches(string? expected, string actual)
    {
        return string.IsNullOrWhiteSpace(expected) ||
               string.Equals(expected.Trim(), actual, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveStatus(
        MaterialEntryRecord entry,
        IReadOnlyList<MaterialCertificateInfo> certificates,
        MaterialTestInfo? test,
        MaterialApprovalInfo? approval)
    {
        if (!string.IsNullOrWhiteSpace(entry.StatusOverride))
        {
            return entry.StatusOverride;
        }

        if (approval?.Status == "archived")
        {
            return MaterialStatuses.Archived;
        }

        if (approval is not null)
        {
            return MaterialStatuses.Approved;
        }

        if (test is not null)
        {
            if (test.Result.Contains("不合格", StringComparison.OrdinalIgnoreCase))
            {
                return MaterialStatuses.Unqualified;
            }

            if (test.Result.Contains("合格", StringComparison.OrdinalIgnoreCase))
            {
                return MaterialStatuses.Qualified;
            }

            if (!string.IsNullOrWhiteSpace(test.ReportNo) || !string.IsNullOrWhiteSpace(test.ReportAttachmentId))
            {
                return MaterialStatuses.ReportIssued;
            }

            if (test.SentTime is not null)
            {
                return MaterialStatuses.SentToTest;
            }

            if (test.IsRequired)
            {
                return MaterialStatuses.PendingTest;
            }
        }

        return HasCertificate(certificates) ? MaterialStatuses.Qualified : MaterialStatuses.MissingCertificate;
    }

    private static string ResolveTestStatus(MaterialTestInfo? test)
    {
        if (test is null || !test.IsRequired)
        {
            return "无需送检";
        }

        if (test.Result.Contains("不合格", StringComparison.OrdinalIgnoreCase))
        {
            return "不合格";
        }

        if (test.Result.Contains("合格", StringComparison.OrdinalIgnoreCase))
        {
            return "合格";
        }

        if (!string.IsNullOrWhiteSpace(test.ReportNo) || !string.IsNullOrWhiteSpace(test.ReportAttachmentId))
        {
            return "已出报告";
        }

        return test.SentTime is null ? "待送检" : "已送检";
    }

    private static bool HasCertificate(IReadOnlyList<MaterialCertificateInfo> certificates)
    {
        return certificates.Any(item =>
            item.FileType.Contains("合格证", StringComparison.OrdinalIgnoreCase) ||
            item.FileType.Contains("质保", StringComparison.OrdinalIgnoreCase) ||
            item.FileType.Contains("质量", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class MaterialAttachmentService
{
    private readonly ProjectManager _projectManager;
    private readonly MaterialRepository _repository;
    private readonly MaterialService _materialService;

    public MaterialAttachmentService(
        ProjectManager projectManager,
        MaterialRepository repository,
        MaterialService materialService)
    {
        _projectManager = projectManager;
        _repository = repository;
        _materialService = materialService;
    }

    public async Task<MaterialAttachmentResult> UploadAsync(string materialEntryId, HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            throw new InvalidOperationException("请使用 multipart/form-data 上传附件。");
        }

        var form = await request.ReadFormAsync();
        var projectId = form["projectId"].FirstOrDefault();
        var project = _projectManager.ResolveProject(projectId);
        _materialService.EnsureEntry(project.ProjectId, materialEntryId);

        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            throw new InvalidOperationException("上传文件不能为空。");
        }

        var fileType = form["fileType"].FirstOrDefault();
        var certificateNo = form["certificateNo"].FirstOrDefault() ?? form["number"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(fileType))
        {
            throw new InvalidOperationException("文件类型不能为空。");
        }

        var originalName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(originalName);
        var safeName = $"{DateTimeOffset.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}";
        var relativeDirectory = Path.Combine("ProjectFiles", "Materials", materialEntryId);
        var targetDirectory = Path.Combine(project.ProjectRootPath, relativeDirectory);
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, safeName);

        await using (var stream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = Path.Combine(relativeDirectory, safeName);
        var attachment = _repository.InsertCertificate(
            project.ProjectId,
            materialEntryId,
            fileType,
            certificateNo,
            originalName,
            relativePath);
        return new MaterialAttachmentResult(true, attachment, "附件已上传。");
    }
}

public sealed class MaterialApprovalService
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(?<type>[^:}]+):(?<name>[^}]+)\}\}", RegexOptions.Compiled);

    private readonly DirectoryInfo _rootPath;
    private readonly ProjectManager _projectManager;
    private readonly MaterialRepository _repository;
    private readonly MaterialService _materialService;

    public MaterialApprovalService(
        DirectoryInfo rootPath,
        ProjectManager projectManager,
        MaterialRepository repository,
        MaterialService materialService)
    {
        _rootPath = rootPath;
        _projectManager = projectManager;
        _repository = repository;
        _materialService = materialService;
    }

    public MaterialApprovalGenerateResult Generate(string materialEntryId, string? projectId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var entry = _materialService.EnsureEntry(project.ProjectId, materialEntryId);
        var info = _materialService.Get(project.ProjectId, materialEntryId);
        var templatePath = Path.Combine(_rootPath.FullName, "Templates", "Materials", "材料进场报审资料.xlsx");
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                "未找到材料进场报审资料模板，请提供 Templates/Materials/材料进场报审资料.xlsx。",
                templatePath);
        }

        var relativeDirectory = Path.Combine("GeneratedForms", "Materials");
        var outputDirectory = Path.Combine(project.ProjectRootPath, relativeDirectory);
        Directory.CreateDirectory(outputDirectory);
        var outputName = ResolveUniqueName(outputDirectory, $"{SanitizeFileName(entry.MaterialName)}-材料进场报审资料.xlsx");
        var outputPath = Path.Combine(outputDirectory, outputName);
        File.Copy(templatePath, outputPath);
        FillWorkbook(outputPath, project.ProjectName, info);

        var relativePath = Path.Combine(relativeDirectory, outputName);
        var approval = _repository.InsertApproval(
            project.ProjectId,
            materialEntryId,
            Path.GetRelativePath(_rootPath.FullName, templatePath),
            relativePath);
        return new MaterialApprovalGenerateResult(true, approval, approval.FilePath, "材料进场报审资料已生成。");
    }

    private static void FillWorkbook(string filePath, string projectName, MaterialEntryInfo material)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["工程名称"] = projectName,
            ["材料名称"] = material.MaterialName,
            ["规格型号"] = material.SpecificationModel,
            ["单位"] = material.Unit,
            ["数量"] = material.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["进场日期"] = material.EntryDate.ToString("yyyy-MM-dd"),
            ["供应商"] = material.Supplier,
            ["生产厂家"] = material.Manufacturer,
            ["使用部位"] = material.UsePart,
            ["批号"] = material.BatchNo,
            ["备注"] = material.Remark,
            ["证明文件编号"] = string.Join("、", material.Certificates.Select(item => item.CertificateNo).Where(item => !string.IsNullOrWhiteSpace(item))),
            ["检测机构"] = material.Test?.InspectionAgency ?? "",
            ["检测报告编号"] = material.Test?.ReportNo ?? "",
            ["检测结果"] = material.Test?.Result ?? ""
        };

        using var document = SpreadsheetDocument.Open(filePath, true);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("材料报审模板缺少 WorkbookPart。");

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
                return $"CL-{DateTime.Now:yyyyMMddHHmmss}";
            }

            return type == "材料" && replacements.TryGetValue(name, out var value) ? value : match.Value;
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
        return string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(rowName)
            ? null
            : $"{IncrementColumn(columnName)}{rowName}";
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

    private static string ResolveUniqueName(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return fileName;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            var candidate = $"{name}-{index}{extension}";
            if (!File.Exists(Path.Combine(directory, candidate)))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = string.IsNullOrWhiteSpace(value) ? "未命名材料" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized;
    }
}

public sealed class MaterialLedgerService
{
    private readonly ProjectManager _projectManager;
    private readonly MaterialService _materialService;

    public MaterialLedgerService(ProjectManager projectManager, MaterialService materialService)
    {
        _projectManager = projectManager;
        _materialService = materialService;
    }

    public MaterialLedgerResult GetLedger(MaterialQuery query, bool export)
    {
        var result = _materialService.List(query);
        var rows = result.Items.Select((item, index) => new MaterialLedgerRow(
            index + 1,
            item.MaterialName,
            item.SpecificationModel,
            item.Unit,
            item.Quantity,
            item.EntryDate,
            item.Supplier,
            item.Manufacturer,
            item.UsePart,
            item.BatchNo,
            string.Join("、", item.Certificates.Select(cert => cert.CertificateNo).Where(value => !string.IsNullOrWhiteSpace(value))),
            item.TestStatus,
            item.Test?.ReportNo ?? "",
            item.Test?.Result ?? "",
            item.ApprovalStatus,
            item.Status,
            item.Remark)).ToArray();

        if (!export)
        {
            return new MaterialLedgerResult(true, query.ProjectId, rows.Length, rows, null, "材料台账查询成功。");
        }

        var project = _projectManager.ResolveProject(query.ProjectId);
        var directory = Path.Combine(project.ProjectRootPath, "GeneratedForms", "Materials");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"材料台账-{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        ExportLedger(path, rows);
        var relativePath = MaterialRepository.ToPortablePath(Path.GetRelativePath(project.ProjectRootPath, path));
        return new MaterialLedgerResult(true, query.ProjectId, rows.Length, rows, relativePath, "材料台账已导出。");
    }

    private static void ExportLedger(string path, IReadOnlyList<MaterialLedgerRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("材料台账");
        var headers = new[]
        {
            "序号", "材料名称", "规格型号", "单位", "数量", "进场日期",
            "供应商", "生产厂家", "使用部位", "批号", "证明文件编号",
            "送检状态", "检测报告编号", "检测结果", "报审状态", "状态", "备注"
        };

        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var values = new object[]
            {
                row.Sequence,
                row.MaterialName,
                row.SpecificationModel,
                row.Unit,
                row.Quantity,
                row.EntryDate.ToString("yyyy-MM-dd"),
                row.Supplier,
                row.Manufacturer,
                row.UsePart,
                row.BatchNo,
                row.CertificateNos,
                row.TestStatus,
                row.ReportNo,
                row.TestResult,
                row.ApprovalStatus,
                row.Status,
                row.Remark
            };

            for (var column = 0; column < values.Length; column++)
            {
                sheet.Cell(index + 2, column + 1).Value = XLCellValue.FromObject(values[column]);
            }
        }

        sheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }
}
