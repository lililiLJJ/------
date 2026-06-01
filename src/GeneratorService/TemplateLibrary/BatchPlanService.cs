using GeneratorService.Models;
using GeneratorService.Modules;
using GeneratorService.Projects;

namespace GeneratorService.TemplateLibrary;

public sealed class BatchPlanService
{
    private readonly BatchPlanRepository _repository;
    private readonly TemplateTreeRepository _templateTreeRepository;
    private readonly GeneratedFormService _generatedFormService;
    private readonly TemplateService _templateService;
    private readonly TemplateValidationService _templateValidationService;
    private readonly ProjectManager _projectManager;
    private readonly UnitProjectService _unitProjectService;
    private readonly ModuleManager _moduleManager;

    public BatchPlanService(
        BatchPlanRepository repository,
        TemplateTreeRepository templateTreeRepository,
        GeneratedFormService generatedFormService,
        TemplateService templateService,
        TemplateValidationService templateValidationService,
        ProjectManager projectManager,
        UnitProjectService unitProjectService,
        ModuleManager moduleManager)
    {
        _repository = repository;
        _templateTreeRepository = templateTreeRepository;
        _generatedFormService = generatedFormService;
        _templateService = templateService;
        _templateValidationService = templateValidationService;
        _projectManager = projectManager;
        _unitProjectService = unitProjectService;
        _moduleManager = moduleManager;
    }

    public InspectionPlanListResult List(string? projectId, string? unitProjectId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, unitProjectId);
        var plans = EnrichPlans(_repository.ListPlans(project.ProjectId, unitProject.Id));
        return new InspectionPlanListResult(
            true,
            project.ProjectId,
            unitProject.Id,
            plans.FirstOrDefault()?.PlanId,
            plans);
    }

    public InspectionBatchPlanDto Get(string planId)
    {
        return EnrichPlan(_repository.GetPlan(planId) ?? throw new InvalidOperationException("检验批计划不存在。"));
    }

    public InspectionPlanSaveResult Create(InspectionPlanSaveRequest request, string? projectId = null)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, request.UnitProjectId);
        var plan = _repository.SavePlan(null, project.ProjectId, unitProject.Id, request);
        return new InspectionPlanSaveResult(true, EnrichPlan(plan), "检验批计划已创建。");
    }

    public InspectionPlanSaveResult Update(string planId, InspectionPlanSaveRequest request, string? projectId = null)
    {
        var existing = _repository.GetPlan(planId, includeDeletedRows: true) ?? throw new InvalidOperationException("检验批计划不存在。");
        var project = _projectManager.ResolveProject(projectId ?? existing.ProjectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, request.UnitProjectId ?? existing.UnitProjectId);
        var existingRowsById = existing.Rows.ToDictionary(row => row.PlanRowId, StringComparer.OrdinalIgnoreCase);

        var saved = _repository.SavePlan(planId, project.ProjectId, unitProject.Id, request);
        var enriched = EnrichPlan(saved);
        var updatedRowsById = enriched.Rows.ToDictionary(row => row.PlanRowId, StringComparer.OrdinalIgnoreCase);

        foreach (var existingRow in existing.Rows)
        {
            var activeDocument = !string.IsNullOrWhiteSpace(existingRow.ActiveDocumentId)
                ? _templateTreeRepository.GetActiveDocumentByPlanRowId(existingRow.PlanRowId)
                : null;
            if (activeDocument is null)
            {
                continue;
            }

            if (!updatedRowsById.TryGetValue(existingRow.PlanRowId, out var updatedRow) ||
                string.Equals(updatedRow.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
            {
                _generatedFormService.DeleteDocument(activeDocument.DocumentId, project.ProjectId, unitProject.Id);
                continue;
            }

            if (HasTemplateIdentityChanged(existingRow, updatedRow))
            {
                TryRegenerate(project, unitProject, enriched, updatedRow, overwrite: true);
                continue;
            }

            if (HasSyncRelevantChanges(existingRow, updatedRow))
            {
                TrySync(project, unitProject, activeDocument.DocumentId, updatedRow);
            }
        }

        return new InspectionPlanSaveResult(true, EnrichPlan(_repository.GetPlan(planId) ?? enriched), "检验批计划已保存。");
    }

    public InspectionPlanDeleteResult Delete(string planId, string? projectId = null, string? unitProjectId = null)
    {
        var plan = _repository.GetPlan(planId) ?? throw new InvalidOperationException("检验批计划不存在。");
        var project = _projectManager.ResolveProject(projectId ?? plan.ProjectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, unitProjectId ?? plan.UnitProjectId);
        var enriched = EnrichPlan(plan);
        foreach (var row in enriched.Rows.Where(row => !string.IsNullOrWhiteSpace(row.ActiveDocumentId)))
        {
            _generatedFormService.DeleteDocument(row.ActiveDocumentId!, project.ProjectId, unitProject.Id);
        }

        return _repository.DeletePlan(planId);
    }

    public InspectionPlanPreviewResult Preview(string planId)
    {
        var plan = EnrichPlan(_repository.GetPlan(planId) ?? throw new InvalidOperationException("检验批计划不存在。"));
        var rows = plan.Rows
            .Select((row, index) => BuildPreviewRow(index + 1, row))
            .ToArray();
        var warnings = rows.SelectMany(row => row.Warnings.Select(item => $"第 {row.RowIndex} 行：{item}")).ToArray();

        return new InspectionPlanPreviewResult(
            true,
            plan.ProjectId,
            plan.UnitProjectId,
            plan.PlanId,
            rows.Length,
            rows.Count(row => row.CanGenerate),
            rows.Count(row => !row.CanGenerate),
            rows,
            warnings);
    }

    public InspectionPlanGenerateResult Generate(string planId, InspectionPlanGenerateRequest? request)
    {
        var plan = EnrichPlan(_repository.GetPlan(planId) ?? throw new InvalidOperationException("检验批计划不存在。"));
        var project = _projectManager.ResolveProject(plan.ProjectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, plan.UnitProjectId);
        var selectedRows = request?.SelectedRowIds is { Count: > 0 }
            ? new HashSet<string>(request.SelectedRowIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()), StringComparer.OrdinalIgnoreCase)
            : null;
        var previewRows = plan.Rows
            .Where(row => selectedRows is null || selectedRows.Contains(row.PlanRowId))
            .Select((row, index) => BuildPreviewRow(index + 1, row))
            .ToArray();
        var results = new List<InspectionPlanGenerateRowResult>();

        foreach (var previewRow in previewRows)
        {
            if (!previewRow.CanGenerate)
            {
                _repository.UpdatePlanRowGenerateStatus(previewRow.PlanRowId, "Failed");
                results.Add(new InspectionPlanGenerateRowResult(
                    previewRow.RowIndex,
                    previewRow.PlanRowId,
                    false,
                    true,
                    previewRow.ActiveDocumentId,
                    null,
                    string.Join("；", previewRow.Errors)));
                continue;
            }

            var row = plan.Rows.First(item => string.Equals(item.PlanRowId, previewRow.PlanRowId, StringComparison.OrdinalIgnoreCase));
            try
            {
                var created = _generatedFormService.CreateInspectionBatchDocument(
                    project,
                    unitProject,
                    plan,
                    row,
                    request?.Fields,
                    request?.Overwrite == true);
                results.Add(new InspectionPlanGenerateRowResult(
                    previewRow.RowIndex,
                    previewRow.PlanRowId,
                    true,
                    false,
                    created.DocumentId,
                    created.GeneratedFilePath,
                    "生成成功。"));
            }
            catch (Exception ex)
            {
                _repository.UpdatePlanRowGenerateStatus(previewRow.PlanRowId, "Failed");
                results.Add(new InspectionPlanGenerateRowResult(
                    previewRow.RowIndex,
                    previewRow.PlanRowId,
                    false,
                    false,
                    previewRow.ActiveDocumentId,
                    null,
                    ex.Message));
            }
        }

        var successCount = results.Count(item => item.Success);
        var skippedCount = results.Count(item => item.Skipped);
        var failedCount = results.Count - successCount - skippedCount;
        return new InspectionPlanGenerateResult(
            failedCount == 0 && successCount > 0,
            plan.ProjectId,
            plan.UnitProjectId,
            plan.PlanId,
            successCount,
            failedCount,
            skippedCount,
            results,
            $"批量创建完成：成功 {successCount} 条，失败 {failedCount} 条，跳过 {skippedCount} 条。");
    }

    public CapacityFieldConfigResult GetCapacityFieldConfigs(string? projectId, string? unitProjectId, string templateNodeId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, unitProjectId);
        var items = ResolveCapacityFieldConfigs(project.ProjectId, unitProject.Id, templateNodeId);
        return new CapacityFieldConfigResult(true, project.ProjectId, unitProject.Id, templateNodeId, items);
    }

    public CapacityFieldConfigResult SaveCapacityFieldConfigs(string? projectId, SaveCapacityFieldConfigRequest request)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, request.UnitProjectId);
        _repository.SaveCapacityFieldOverrides(project.ProjectId, unitProject.Id, request.TemplateNodeId, request.Items ?? []);
        return GetCapacityFieldConfigs(project.ProjectId, unitProject.Id, request.TemplateNodeId);
    }

    public FieldMappingOverrideResult GetFieldMappings(string? projectId, string? unitProjectId, string templateNodeId)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, unitProjectId);
        return new FieldMappingOverrideResult(
            true,
            project.ProjectId,
            unitProject.Id,
            templateNodeId,
            _repository.GetFieldMappingOverride(project.ProjectId, unitProject.Id, templateNodeId));
    }

    public FieldMappingOverrideResult SaveFieldMappings(string? projectId, SaveFieldMappingOverrideRequest request)
    {
        var project = _projectManager.ResolveProject(projectId);
        var unitProject = _unitProjectService.ResolveUnitProject(project.ProjectId, request.UnitProjectId);
        var data = _repository.SaveFieldMappingOverride(project.ProjectId, unitProject.Id, request.TemplateNodeId, request.Data ?? new Dictionary<string, string>());
        return new FieldMappingOverrideResult(true, project.ProjectId, unitProject.Id, request.TemplateNodeId, data);
    }

    private InspectionBatchPlanDto EnrichPlan(InspectionBatchPlanDto plan)
    {
        var activeDocuments = _templateTreeRepository.ListGeneratedDocuments(plan.ProjectId, plan.UnitProjectId, "InspectionBatch", "Active")
            .Where(item => string.Equals(item.SourceType, "InspectionBatchPlanRow", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.SourceId, item => item.DocumentId, StringComparer.OrdinalIgnoreCase);

        var rows = plan.Rows
            .Select(row => row with
            {
                ActiveDocumentId = activeDocuments.TryGetValue(row.PlanRowId, out var documentId) ? documentId : null
            })
            .ToArray();
        return plan with
        {
            GeneratedCount = rows.Count(row => string.Equals(row.GenerateStatus, "Generated", StringComparison.OrdinalIgnoreCase)),
            Rows = rows
        };
    }

    private IReadOnlyList<InspectionBatchPlanDto> EnrichPlans(IReadOnlyList<InspectionBatchPlanDto> plans)
    {
        return plans.Select(EnrichPlan).ToArray();
    }

    private InspectionPlanPreviewRow BuildPreviewRow(int rowIndex, InspectionBatchPlanRowDto row)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        if (string.Equals(row.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("计划行已删除。");
        }

        if (string.IsNullOrWhiteSpace(row.TemplateNodeId))
        {
            errors.Add("未选择检验批模板。");
        }

        if (string.IsNullOrWhiteSpace(row.InspectionPart))
        {
            errors.Add("未填写部位。");
        }

        if (string.IsNullOrWhiteSpace(row.ConstructionDate))
        {
            errors.Add("未填写施工日期。");
        }

        foreach (var config in ResolveCapacityFieldConfigs(row.ProjectId, row.UnitProjectId, row.TemplateNodeId).Where(item => item.Required && item.Enabled))
        {
            var value = row.Capacities.FirstOrDefault(item => string.Equals(item.CapacityKey, config.CapacityKey, StringComparison.OrdinalIgnoreCase))?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"必填容量未填写：{config.CapacityName}");
            }
        }

        if (!string.IsNullOrWhiteSpace(row.TemplateNodeId))
        {
            try
            {
                var template = _templateService.ResolveTemplate(row.TemplateNodeId);
                if (TemplateAdaptationFields.IsManagedModule(template.ModuleId))
                {
                    var validation = _templateValidationService.Validate(template);
                    if (!validation.CanGenerate)
                    {
                        errors.AddRange(validation.MissingFields.Select(fieldKey => $"缺少模板映射：{TemplateAdaptationFields.GetDisplayName(fieldKey)}"));
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"模板无法解析：{ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(row.ActiveDocumentId))
        {
            warnings.Add("当前计划行已存在已生成表格。");
        }

        if (row.Capacities.Count == 0)
        {
            warnings.Add("未填写容量详情。");
        }

        return new InspectionPlanPreviewRow(
            rowIndex,
            row.PlanRowId,
            row.TemplateNodeId,
            row.TemplateName,
            row.DivisionName,
            row.SubDivisionName,
            row.SubItemName,
            row.InspectionPart,
            row.ConstructionDate,
            row.CapacitySummary,
            row.Status,
            row.GenerateStatus,
            row.ActiveDocumentId,
            warnings,
            errors,
            errors.Count == 0);
    }

    private IReadOnlyList<CapacityFieldConfigInfo> ResolveCapacityFieldConfigs(string projectId, string unitProjectId, string templateNodeId)
    {
        if (string.IsNullOrWhiteSpace(templateNodeId))
        {
            return [];
        }

        var overrides = _repository.ListCapacityFieldOverrides(projectId, unitProjectId, templateNodeId);
        if (overrides.Count > 0)
        {
            return overrides;
        }

        var template = _templateService.ResolveTemplate(templateNodeId);
        return BuildDefaultCapacityConfigs(projectId, unitProjectId, template);
    }

    private static IReadOnlyList<CapacityFieldConfigInfo> BuildDefaultCapacityConfigs(string projectId, string unitProjectId, TemplateResolution template)
    {
        var name = template.TemplateName;
        var items = new List<(string Key, string Name, string Unit, bool Required, int SortOrder)>();
        if (name.Contains("报警", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("探测器", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("smoke_detector_count", "感烟探测器数量", "个", true, 10));
            items.Add(("heat_detector_count", "感温探测器数量", "个", true, 20));
            items.Add(("manual_alarm_button_count", "手动报警按钮数量", "个", false, 30));
            items.Add(("sound_light_alarm_count", "声光警报器数量", "个", false, 40));
        }
        else if (name.Contains("喷淋", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("pipe_length", "管道长度", "m", true, 10));
            items.Add(("sprinkler_count", "喷头数量", "个", true, 20));
            items.Add(("valve_count", "阀门数量", "个", false, 30));
        }
        else if (name.Contains("管道", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("给水", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("pipe_length", "管道长度", "m", true, 10));
            items.Add(("valve_count", "阀门数量", "个", false, 20));
            items.Add(("support_count", "支架数量", "个", false, 30));
        }
        else if (name.Contains("电缆", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("桥架", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(("cable_length", "电缆长度", "m", true, 10));
            items.Add(("box_count", "箱盒数量", "个", false, 20));
        }
        else
        {
            items.Add(("quantity", "数量", "项", true, 10));
        }

        return items.Select(item => new CapacityFieldConfigInfo(
            $"default-capacity-config:{template.TemplateNodeId}:{item.Key}",
            projectId,
            unitProjectId,
            template.TemplateNodeId,
            item.Key,
            item.Name,
            item.Unit,
            item.Required,
            item.SortOrder,
            true,
            false)).ToArray();
    }

    private static bool HasTemplateIdentityChanged(InspectionBatchPlanRowDto existingRow, InspectionBatchPlanRowDto updatedRow)
    {
        return !string.Equals(existingRow.TemplateNodeId, updatedRow.TemplateNodeId, StringComparison.OrdinalIgnoreCase) ||
               existingRow.TemplateItemId != updatedRow.TemplateItemId;
    }

    private static bool HasSyncRelevantChanges(InspectionBatchPlanRowDto existingRow, InspectionBatchPlanRowDto updatedRow)
    {
        return !string.Equals(existingRow.InspectionPart, updatedRow.InspectionPart, StringComparison.Ordinal) ||
               !string.Equals(existingRow.ConstructionDate, updatedRow.ConstructionDate, StringComparison.Ordinal) ||
               !string.Equals(existingRow.CapacitySummary, updatedRow.CapacitySummary, StringComparison.Ordinal) ||
               !string.Equals(existingRow.Remark, updatedRow.Remark, StringComparison.Ordinal) ||
               !CapacitiesEqual(existingRow.Capacities, updatedRow.Capacities);
    }

    private static bool CapacitiesEqual(IReadOnlyList<PlanRowCapacityDto> left, IReadOnlyList<PlanRowCapacityDto> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.OrderBy(item => item.SortOrder).ThenBy(item => item.CapacityKey, StringComparer.OrdinalIgnoreCase)
            .Zip(
                right.OrderBy(item => item.SortOrder).ThenBy(item => item.CapacityKey, StringComparer.OrdinalIgnoreCase),
                (a, b) =>
                    string.Equals(a.CapacityKey, b.CapacityKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.CapacityName, b.CapacityName, StringComparison.Ordinal) &&
                    string.Equals(a.Value, b.Value, StringComparison.Ordinal) &&
                    string.Equals(a.Unit, b.Unit, StringComparison.Ordinal) &&
                    a.SortOrder == b.SortOrder)
            .All(item => item);
    }

    private void TrySync(ProjectContext project, UnitProjectInfo unitProject, string documentId, InspectionBatchPlanRowDto row)
    {
        try
        {
            _generatedFormService.SyncInspectionBatchDocument(documentId, project, unitProject, row);
        }
        catch
        {
            _repository.UpdatePlanRowGenerateStatus(row.PlanRowId, "NeedSync");
        }
    }

    private void TryRegenerate(ProjectContext project, UnitProjectInfo unitProject, InspectionBatchPlanDto plan, InspectionBatchPlanRowDto row, bool overwrite)
    {
        try
        {
            var previewRow = BuildPreviewRow(0, row);
            if (!previewRow.CanGenerate)
            {
                _repository.UpdatePlanRowGenerateStatus(row.PlanRowId, "NeedSync");
                return;
            }

            _generatedFormService.CreateInspectionBatchDocument(project, unitProject, plan, row, null, overwrite);
        }
        catch
        {
            _repository.UpdatePlanRowGenerateStatus(row.PlanRowId, "NeedSync");
        }
    }
}
