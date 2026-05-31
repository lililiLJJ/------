using System.Text.Json;
using GeneratorService.Models;
using GeneratorService.Projects;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateTestHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly TemplateService _templateService;
    private readonly TemplateAdaptationRepository _repository;
    private readonly TemplateMappingService _mappingService;
    private readonly TemplateValidationService _validationService;
    private readonly RowHeightBalanceService _rowHeightBalanceService;
    private readonly ProjectManager _projectManager;

    public TemplateTestHarness(
        TemplateService templateService,
        TemplateAdaptationRepository repository,
        TemplateMappingService mappingService,
        TemplateValidationService validationService,
        RowHeightBalanceService rowHeightBalanceService,
        ProjectManager projectManager)
    {
        _templateService = templateService;
        _repository = repository;
        _mappingService = mappingService;
        _validationService = validationService;
        _rowHeightBalanceService = rowHeightBalanceService;
        _projectManager = projectManager;
    }

    public TemplateTestResult RunTemplateTest(string templateNodeId)
    {
        var template = _templateService.ResolveTemplate(templateNodeId);
        var project = _projectManager.GetCurrentProject();
        var tempDirectory = Path.Combine(project.TempPath, "TemplateAdaptationTests");
        Directory.CreateDirectory(tempDirectory);
        var targetPath = Path.Combine(
            tempDirectory,
            $"{SanitizeFileName(template.TemplateName)}-{DateTimeOffset.Now:yyyyMMddHHmmssfff}{Path.GetExtension(template.TemplatePath)}");
        File.Copy(template.TemplatePath, targetPath, overwrite: true);

        _validationService.EnsureReadyForGeneration(template);
        var layoutBefore = TemplateWorkbookHelper.CaptureLayoutSnapshot(targetPath);
        var fields = BuildSampleFields();
        var baseline = _rowHeightBalanceService.CaptureBaseline(targetPath);
        var applyResult = _mappingService.Apply(targetPath, template, fields);
        _rowHeightBalanceService.ApplyLight(targetPath, fields[TemplateAdaptationFields.PartName], fields, baseline);
        var layoutResult = TemplateWorkbookHelper.CompareLayout(layoutBefore, targetPath);
        var messages = new List<string>();
        if (!layoutResult.Success)
        {
            messages.AddRange(layoutResult.Differences);
        }

        var success = applyResult.VerificationResults.All(item => item.Success) && layoutResult.Success;
        var result = new TemplateTestResult(
            success,
            template.TemplateNodeId,
            template.TemplateName,
            template.ModuleId,
            template.ModuleVersion,
            targetPath,
            _repository.GetAdaptationDetail(template).Profile.MappingMode,
            applyResult.VerificationResults,
            layoutResult,
            success ? "PASS" : "FAIL",
            DateTimeOffset.Now,
            messages);

        var reportPath = WriteSingleReport(project, result);
        _repository.RecordTestResult(template, result, reportPath);
        return result;
    }

    public TemplateBatchTestResult RunBatchTest(TemplateBatchTestRequest? request)
    {
        var project = _projectManager.GetCurrentProject();
        var moduleId = string.IsNullOrWhiteSpace(request?.ModuleId) ? "gd_installation_2024" : request!.ModuleId!.Trim();
        var requestedCount = Math.Max(1, request?.Count ?? 10);
        var candidates = _repository.ListProfiles(moduleId)
            .Where(item =>
                string.Equals(item.Status, "configured", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Status, "ready", StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => Random.Shared.Next())
            .Take(requestedCount)
            .ToArray();

        var results = new List<TemplateTestResult>();
        foreach (var profile in candidates)
        {
            try
            {
                results.Add(RunTemplateTest(profile.TemplateNodeId));
            }
            catch (Exception ex)
            {
                results.Add(BuildFailureResult(profile, ex));
            }
        }

        var reportPath = WriteBatchReport(project, moduleId, results);
        return new TemplateBatchTestResult(
            results.All(item => item.Success),
            moduleId,
            candidates.FirstOrDefault()?.ModuleVersion ?? "",
            requestedCount,
            results.Count,
            results.Count(item => item.Success),
            results.Count(item => !item.Success),
            reportPath,
            results,
            DateTimeOffset.Now);
    }

    private static TemplateTestResult BuildFailureResult(TemplateProfileInfo profile, Exception exception)
    {
        var message = exception is TemplateAdaptationException adaptationException
            ? adaptationException.Message
            : exception.GetBaseException().Message;
        var missingFields = exception is TemplateAdaptationException templateException
            ? templateException.MissingFields
                .Select(fieldKey => new TemplateFieldTestResult(
                    fieldKey,
                    "",
                    "Cell",
                    [],
                    [],
                    false,
                    $"缺少字段映射：{TemplateAdaptationFields.GetDisplayName(fieldKey)}"))
                .ToArray()
            : Array.Empty<TemplateFieldTestResult>();
        return new TemplateTestResult(
            false,
            profile.TemplateNodeId,
            profile.TemplateName,
            profile.ModuleId,
            profile.ModuleVersion,
            "",
            profile.MappingMode,
            missingFields,
            new TemplateLayoutComparisonResult(false, 0, 0, [message]),
            "FAIL",
            DateTimeOffset.Now,
            [message]);
    }

    private static IReadOnlyDictionary<string, string> BuildSampleFields()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TemplateAdaptationFields.ProjectName] = "测试工程",
            [TemplateAdaptationFields.ConstructionUnit] = "测试施工单位",
            [TemplateAdaptationFields.SupervisionUnit] = "测试监理单位",
            [TemplateAdaptationFields.PartName] = "地下室",
            [TemplateAdaptationFields.Capacity] = "5套",
            [TemplateAdaptationFields.ConstructionDate] = "2026-05-31",
            ["projectName"] = "测试工程",
            ["developerUnitName"] = "测试建设单位",
            ["constructorUnitName"] = "测试施工单位",
            ["designUnitName"] = "测试设计单位",
            ["supervisorUnitName"] = "测试监理单位",
            ["professionalSubcontractorUnitName"] = "测试专业分包单位",
            ["thirdPartyInspectionUnitName"] = "测试第三方检测单位",
            ["partName"] = "地下室",
            ["capacity"] = "5套",
            ["constructionDate"] = "2026-05-31",
            ["acceptanceDate"] = "2026-06-01",
            ["工程名称"] = "测试工程",
            ["建设单位"] = "测试建设单位",
            ["施工单位"] = "测试施工单位",
            ["设计单位"] = "测试设计单位",
            ["监理单位"] = "测试监理单位",
            ["专业分包单位"] = "测试专业分包单位",
            ["第三方检测单位"] = "测试第三方检测单位",
            ["检验批部位"] = "地下室",
            ["施工部位"] = "地下室",
            ["检验批容量"] = "5套",
            ["施工日期"] = "2026-05-31",
            ["验收日期"] = "2026-06-01"
        };
    }

    private static string WriteSingleReport(ProjectContext project, TemplateTestResult result)
    {
        var reportDirectory = Path.Combine(project.LogsPath, "TemplateAdaptationReports");
        Directory.CreateDirectory(reportDirectory);
        var path = Path.Combine(reportDirectory, $"single-{DateTimeOffset.Now:yyyyMMddHHmmssfff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
        return path;
    }

    private static string WriteBatchReport(ProjectContext project, string moduleId, IReadOnlyList<TemplateTestResult> results)
    {
        var reportDirectory = Path.Combine(project.LogsPath, "TemplateAdaptationReports");
        Directory.CreateDirectory(reportDirectory);
        var path = Path.Combine(reportDirectory, $"batch-{moduleId}-{DateTimeOffset.Now:yyyyMMddHHmmssfff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, JsonOptions));
        return path;
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = value;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized;
    }
}
