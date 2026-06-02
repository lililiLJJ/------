import {
  getServiceHealth,
  getCurrentProject,
  listUnitProjects,
  getCurrentUnitProject
} from "../api/projectApi.js";
import { getTemplateTree } from "../api/templateApi.js";
import {
  listInspectionPlans,
  getInspectionPlan,
  getInspectionPlanRows,
  createInspectionPlan,
  updateInspectionPlan,
  deleteInspectionPlan,
  previewInspectionPlan,
  generateInspectionPlan,
  getCapacityConfigs
} from "../api/inspectionApi.js";
import { openDocument } from "../api/templateApi.js";
import { InspectionBatchPlanCenter } from "../components/InspectionBatchPlanCenter.js";
import { collectTemplateOptions } from "../components/TemplateTree.js";
import { bindInspectionPlanCenterCommands } from "../services/windowHostService.js";
import { bindFormsChanged, emitFormsChanged } from "../services/syncService.js";
import { patchState, setProjectContext } from "../state/projectState.js";

function ensureStandaloneRoot() {
  const root = document.querySelector("#inspectionPlanCenterStandaloneRoot") || document.createElement("div");
  if (!root.id) {
    root.id = "inspectionPlanCenterStandaloneRoot";
    document.body.appendChild(root);
  }
  root.classList.remove("hidden");
  return root;
}

function createPlanName() {
  const now = new Date();
  const pad = (value) => String(value).padStart(2, "0");
  return `检验批计划-${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}`;
}

function showResult(center, error) {
  center.showResult(error);
}

export function bootstrapInspectionPlanCenterWorkspace() {
  document.querySelector("#mainWorkspaceRoot")?.classList.add("hidden");
  document.body.classList.add("inspectionPlanCenterStandaloneBody");

  const root = ensureStandaloneRoot();
  const center = new InspectionBatchPlanCenter({
    host: root,
    onSelectPlan: (planId) => selectPlan(planId).catch((error) => showResult(center, error)),
    onCreatePlan: () => createPlan().catch((error) => showResult(center, error)),
    onRenamePlan: (plan) => renamePlan(plan).catch((error) => showResult(center, error)),
    onDuplicatePlan: (plan) => duplicatePlan(plan).catch((error) => showResult(center, error)),
    onDeletePlan: (plan) => removePlan(plan).catch((error) => showResult(center, error)),
    onSavePlan: (planId, payload) => persistPlan(planId, payload, { emitEvent: true }),
    onPreviewPlan: (planId) => previewPlan(planId),
    onGeneratePlan: (planId, payload) => generatePlan(planId, payload),
    onLoadCapacityConfigs: (templateNodeId) => loadCapacityConfigs(templateNodeId),
    onOpenDocument: (documentId) => openPlanDocument(documentId)
  });

  let templateOptions = [];
  let selectedPlanId = "";
  let pendingCommand = null;
  let currentProject = null;
  let currentUnitProject = null;

  async function loadProjectContext() {
    const projectResult = await getCurrentProject();
    const project = projectResult.project || projectResult;
    const unitProjectResult = await listUnitProjects(project.projectId);
    const currentUnitProjectResult = await getCurrentUnitProject(project.projectId);
    const unitProjects = unitProjectResult.items || unitProjectResult.unitProjects || unitProjectResult;
    const unitProject = currentUnitProjectResult.unitProject || currentUnitProjectResult.current || currentUnitProjectResult || unitProjects[0] || null;

    currentProject = project;
    currentUnitProject = unitProject;
    setProjectContext(project, unitProjects, unitProject?.id || "", unitProject);
    patchState({ serviceAvailable: true });
    center.setContext({
      projectName: project.projectName,
      unitProjectName: unitProject?.unitProjectName || unitProject?.name || "",
      mode: center.editor.mode
    });
  }

  async function loadTemplateOptions() {
    if (!currentProject?.projectId || !currentUnitProject?.id) {
      templateOptions = [];
      center.editor.setContext({
        templateOptions,
        unitProjectName: currentUnitProject?.unitProjectName || currentUnitProject?.name || "",
        mode: center.editor.mode
      });
      return;
    }

    const tree = await getTemplateTree(currentProject.projectId, currentUnitProject.id);
    templateOptions = collectTemplateOptions(tree);
    center.editor.setContext({
      templateOptions,
      unitProjectName: currentUnitProject?.unitProjectName || currentUnitProject?.name || "",
      mode: center.editor.mode
    });
  }

  async function loadPlanList(preferredPlanId = "") {
    if (!currentProject?.projectId || !currentUnitProject?.id) {
      center.setPlans([], "", "");
      center.clearPlan();
      return;
    }

    const result = await listInspectionPlans(currentProject.projectId, currentUnitProject.id);
    const plans = result.plans || [];
    selectedPlanId = preferredPlanId || selectedPlanId || result.currentPlanId || plans[0]?.planId || "";
    if (!plans.some((plan) => plan.planId === selectedPlanId)) {
      selectedPlanId = plans[0]?.planId || "";
    }

    center.setPlans(plans, selectedPlanId, currentUnitProject.unitProjectName || currentUnitProject.name || "");
    if (selectedPlanId) {
      await selectPlan(selectedPlanId, { skipDirtyCheck: true });
    } else {
      center.clearPlan();
    }
  }

  async function selectPlan(planId, options = {}) {
    if (!planId || !currentProject?.projectId) {
      center.clearPlan();
      selectedPlanId = "";
      center.setPlans(center.list.plans || [], "", currentUnitProject?.unitProjectName || currentUnitProject?.name || "");
      return;
    }

    if (!options.skipDirtyCheck && center.editor.hasDirtyChanges()) {
      const proceed = window.confirm("当前计划有未保存修改，切换计划将丢失本地变更。是否继续？");
      if (!proceed) {
        return;
      }
    }

    selectedPlanId = planId;
    center.showBusy("正在加载计划详情...");
    const [plan, rows] = await Promise.all([
      getInspectionPlan(currentProject.projectId, planId),
      getInspectionPlanRows(currentProject.projectId, planId)
    ]);
    center.setPlans(center.list.plans || [], selectedPlanId, currentUnitProject?.unitProjectName || currentUnitProject?.name || "");
    center.setPlan(plan, rows);
    center.setContext({
      projectName: currentProject.projectName,
      unitProjectName: currentUnitProject?.unitProjectName || currentUnitProject?.name || "",
      mode: center.editor.mode
    });
  }

  async function createPlan() {
    if (!currentProject?.projectId || !currentUnitProject?.id) {
      return;
    }

    const result = await createInspectionPlan(currentProject.projectId, {
      unitProjectId: currentUnitProject.id,
      planName: createPlanName(),
      remark: "",
      rows: []
    });
    selectedPlanId = result.plan?.planId || "";
    emitFormsChanged({
      reason: "inspection-plan-created",
      projectId: currentProject.projectId,
      unitProjectId: currentUnitProject.id,
      planId: selectedPlanId
    });
    await loadPlanList(selectedPlanId);
  }

  async function renamePlan(plan) {
    const target = plan || center.editor.getCurrentPlan();
    if (!target?.planId) {
      center.showResult("请先选择要重命名的计划。");
      return;
    }

    const nextName = window.prompt("请输入新的计划名称：", target.planName || "");
    if (!nextName || nextName === target.planName) {
      return;
    }

    const payload = center.editor.getCurrentPlanId() === target.planId
      ? center.editor.buildSavePayload()
      : {
        unitProjectId: target.unitProjectId,
        planName: target.planName,
        remark: target.remark,
        rows: target.rows || []
      };

    await persistPlan(target.planId, {
      ...payload,
      planName: nextName
    }, { emitEvent: true });
  }

  async function duplicatePlan(plan) {
    const target = plan || center.editor.getCurrentPlan();
    if (!target?.planId || !currentProject?.projectId) {
      center.showResult("请先选择要复制的计划。");
      return;
    }

    if (center.editor.getCurrentPlanId() === target.planId && center.editor.hasDirtyChanges()) {
      await persistPlan(target.planId, center.editor.buildSavePayload(), { emitEvent: true });
    }

    const latestPlan = await getInspectionPlan(currentProject.projectId, target.planId);
    const rows = await getInspectionPlanRows(currentProject.projectId, target.planId);
    const result = await createInspectionPlan(currentProject.projectId, {
      unitProjectId: latestPlan.unitProjectId,
      planName: `${latestPlan.planName || "检验批计划"}-复制`,
      remark: latestPlan.remark || "",
      rows: rows.map((row) => ({
        ...row,
        planRowId: null
      }))
    });

    selectedPlanId = result.plan?.planId || "";
    emitFormsChanged({
      reason: "inspection-plan-duplicated",
      projectId: currentProject.projectId,
      unitProjectId: currentUnitProject?.id || latestPlan.unitProjectId,
      planId: selectedPlanId
    });
    await loadPlanList(selectedPlanId);
  }

  async function removePlan(plan) {
    const target = plan || center.editor.getCurrentPlan();
    if (!target?.planId || !currentProject?.projectId || !currentUnitProject?.id) {
      center.showResult("请先选择要删除的计划。");
      return;
    }

    const confirmed = window.confirm("删除计划后，如果计划行已生成资料，将按后端规则同步删除关联资料。是否继续？");
    if (!confirmed) {
      return;
    }

    await deleteInspectionPlan(currentProject.projectId, currentUnitProject.id, target.planId);
    emitFormsChanged({
      reason: "inspection-plan-deleted",
      projectId: currentProject.projectId,
      unitProjectId: currentUnitProject.id,
      planId: target.planId
    });
    const nextPlanId = center.list.plans.find((item) => item.planId !== target.planId)?.planId || "";
    await loadPlanList(nextPlanId);
  }

  async function persistPlan(planId, payload, options = {}) {
    if (!currentProject?.projectId) {
      return null;
    }

    const result = await updateInspectionPlan(currentProject.projectId, planId, payload);
    center.setPlan(result.plan, result.plan.rows || center.editor.rows);
    center.showResult(result);
    await loadPlanList(result.plan.planId);
    if (options.emitEvent !== false) {
      emitFormsChanged({
        reason: "inspection-plan-saved",
        projectId: currentProject.projectId,
        unitProjectId: result.plan.unitProjectId,
        planId: result.plan.planId
      });
    }
    return result;
  }

  async function previewPlan(planId) {
    await persistPlan(planId, center.editor.buildSavePayload(), { emitEvent: false });
    const result = await previewInspectionPlan(currentProject.projectId, planId);
    center.editor.preview = result;
    center.editor.renderPreview();
    center.showResult(result);
    return result;
  }

  async function generatePlan(planId, payload) {
    await persistPlan(planId, center.editor.buildSavePayload(), { emitEvent: false });
    const result = await generateInspectionPlan(currentProject.projectId, planId, payload);
    center.showResult(result);
    emitFormsChanged({
      reason: "inspection-generated",
      projectId: currentProject.projectId,
      unitProjectId: currentUnitProject?.id || "",
      planId
    });
    await loadPlanList(planId);
    return result;
  }

  async function loadCapacityConfigs(templateNodeId) {
    return getCapacityConfigs(currentProject.projectId, currentUnitProject?.id || "", templateNodeId);
  }

  async function openPlanDocument(documentId) {
    if (!currentProject?.projectId || !currentUnitProject?.id) {
      return;
    }
    const result = await openDocument(currentProject.projectId, currentUnitProject.id, documentId);
    center.showResult(result);
  }

  async function ensurePlanForCommand() {
    if (center.editor.getCurrentPlanId()) {
      return center.editor.getCurrentPlan();
    }
    await createPlan();
    return center.editor.getCurrentPlan();
  }

  async function handleOpenCommand(command) {
    if (!command) {
      return;
    }

    pendingCommand = command;
    if (!currentProject?.projectId || !currentUnitProject?.id || !templateOptions.length) {
      return;
    }

    if (command.unitProjectId && command.unitProjectId !== currentUnitProject.id) {
      center.showResult("当前单位工程与入口请求不一致，请先在主界面切换单位工程后再进入计划中心。");
      return;
    }

    center.setContext({
      projectName: currentProject.projectName,
      unitProjectName: currentUnitProject.unitProjectName || currentUnitProject.name || "",
      mode: command.mode || "plans"
    });

    if (command.planId && command.planId !== center.editor.getCurrentPlanId()) {
      await selectPlan(command.planId, { skipDirtyCheck: true });
    }

    if (command.templateNodeId) {
      await ensurePlanForCommand();
      const template = templateOptions.find((item) => item.templateNodeId === command.templateNodeId);
      if (template) {
        center.editor.appendTemplateRow(template);
      } else {
        center.showResult(`未找到模板节点：${command.templateNodeId}`);
      }
    }

    center.editor.setMode(command.mode || "plans");
    pendingCommand = null;
  }

  async function refreshAll(options = {}) {
    center.showBusy("正在加载检验批计划中心...");
    const service = await getServiceHealth();
    patchState({ serviceAvailable: true, serviceVersion: service.version || "" });
    await loadProjectContext();
    await loadTemplateOptions();
    await loadPlanList(options.preferredPlanId || selectedPlanId);
    center.setContext({
      projectName: currentProject?.projectName,
      unitProjectName: currentUnitProject?.unitProjectName || currentUnitProject?.name || "",
      mode: center.editor.mode
    });
    if (pendingCommand) {
      await handleOpenCommand(pendingCommand);
    }
  }

  bindFormsChanged(async (detail) => {
    if (!currentProject?.projectId) {
      return;
    }
    if (detail.projectId && detail.projectId !== currentProject.projectId) {
      return;
    }
    await loadPlanList(selectedPlanId || center.editor.getCurrentPlanId());
  });

  bindInspectionPlanCenterCommands((payload) => {
    handleOpenCommand(payload).catch((error) => center.showResult(error));
  });

  return {
    boot: () => refreshAll().catch((error) => center.showResult(error))
  };
}
