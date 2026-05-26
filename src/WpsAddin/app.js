const serviceBaseUrl = "http://127.0.0.1:5188";
const addinVersion = "0.1.0";
const serviceStartCommand = '正式安装包：重新运行 Client\\1-Install-Client.cmd；开发调试：cd "D:\\YY\\编程\\工程资料制作"; dotnet run --project "src/GeneratorService"';
let templates = [];
let modules = [];
let templateTreeNodes = [];
let selectedTemplateNode = null;
let currentGeneratedForm = null;
let lastRowHeightFitAdjustment = null;
let summaryTreeNodes = [];
let selectedSummaryNode = null;
let currentSummaryPreview = null;
let materialItems = [];
let selectedMaterial = null;
let materialLedgerMode = "edit";
let materialLedgerRows = [];
const materialLedgerSelectedIds = new Set();
const materialLedgerColumnFilters = new Map();
let materialLedgerActiveFilterColumn = null;
let batchPlans = [];
let currentBatchPlan = null;
let batchPlanRows = [];
let batchDeviceFields = [];
let currentBatchPreview = null;
const materialLedgerWindowState = {
  maximized: false,
  restore: null,
  dragging: false,
  dragOffsetX: 0,
  dragOffsetY: 0
};
let serviceAvailable = false;
let lastExternalTabVersion = "";
const collapsedTemplateNodeIds = new Set();
let templateTreeClickTimer = null;
let directGeneratedFormCreating = false;
let activeProjectId = "project-default";
let currentProject = null;
let recentProjects = [];
let selectedRecentProjectId = "";

const tabSyncKeys = {
  targetTab: "engineering_docs_target_tab",
  targetTabVersion: "engineering_docs_target_tab_version",
  tabSignal: "engineering_docs_tab_signal"
};

const materialLedgerWindowHash = "materials-ledger-window";
const workbookManager = createWorkbookManager();

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];

const materialLedgerColumns = [
  { key: "materialName", label: "材料名称", required: true },
  { key: "specificationModel", label: "规格型号" },
  { key: "unit", label: "单位" },
  { key: "quantity", label: "数量", type: "number" },
  { key: "entryDate", label: "进场日期", type: "date" },
  { key: "usePart", label: "使用部位" },
  { key: "supplier", label: "供应商" },
  { key: "manufacturer", label: "生产厂家" },
  { key: "batchNo", label: "批号/炉批号" },
  { key: "certificateNo", label: "合格证编号" },
  { key: "factoryReportNo", label: "厂家检测报告编号" },
  { key: "isRequired", label: "是否需要送检", type: "boolean" },
  { key: "sentTime", label: "送检日期", type: "date" },
  { key: "inspectionAgency", label: "第三方检测机构" },
  { key: "reportNo", label: "第三方检测报告编号" },
  { key: "result", label: "检测结果" },
  { key: "approvalStatus", label: "报审状态", readOnly: true },
  { key: "status", label: "状态", readOnly: true },
  { key: "remark", label: "备注" }
];

const batchPlanBaseColumns = [
  { key: "template", label: "检验批模板", required: true, type: "template", width: 240 },
  { key: "partName", label: "检验批部位", required: true, width: 170 },
  { key: "capacity", label: "容量", width: 88 },
  { key: "quantityUnit", label: "单位", width: 72 },
  { key: "constructionDate", label: "施工日期", type: "date", width: 126 },
  { key: "remark", label: "备注", width: 160 }
];

function showResult(selector, data) {
  const target = $(selector);
  if (!target) {
    return;
  }

  if (typeof data === "string") {
    target.textContent = data;
    return;
  }

  if (data instanceof Error) {
    target.textContent = data.message || String(data);
    return;
  }

  if (data && typeof data === "object") {
    const message = data.message || data.error || data.detail;
    if (message && Object.keys(data).length <= 2) {
      target.textContent = message;
      return;
    }

    target.textContent = JSON.stringify(data, null, 2);
    return;
  }

  target.textContent = String(data ?? "");
}

function getUnitInfo(data, prefix) {
  return {
    name: data[`${prefix}Name`] || "",
    projectManager: data[`${prefix}ProjectManager`] || "",
    technicalManager: data[`${prefix}TechnicalManager`] || "",
    unitTechnicalManager: data[`${prefix}UnitTechnicalManager`] || ""
  };
}

function getFormData() {
  const projectData = Object.fromEntries(new FormData($("#projectInfoForm")).entries());
  const generationData = Object.fromEntries(new FormData($("#generationForm")).entries());
  const supervisorUnit = {
    ...getUnitInfo(projectData, "supervisor"),
    professionalSupervisorEngineer: projectData.supervisorProfessionalSupervisorEngineer || "",
    chiefSupervisorEngineer: projectData.supervisorChiefSupervisorEngineer || ""
  };

  return {
    projectName: projectData.projectName,
    developerUnit: getUnitInfo(projectData, "developer"),
    constructorUnit: getUnitInfo(projectData, "constructor"),
    designUnit: getUnitInfo(projectData, "design"),
    supervisorUnit,
    professionalSubcontractorUnit: getUnitInfo(projectData, "professionalSubcontractor"),
    thirdPartyInspectionUnit: getUnitInfo(projectData, "thirdPartyInspection"),
    capacity: generationData.capacity,
    constructionDate: generationData.constructionDate,
    acceptanceDate: generationData.acceptanceDate,
    templateType: generationData.templateType,
    templateName: generationData.templateName,
    exportPath: generationData.exportPath || null
  };
}

function buildGeneratedFormFields(data) {
  const formData = getFormData();
  const fields = {
    projectName: formData.projectName || "",
    developerUnitName: formData.developerUnit.name || "",
    constructorUnitName: formData.constructorUnit.name || "",
    designUnitName: formData.designUnit.name || "",
    supervisorUnitName: formData.supervisorUnit.name || "",
    professionalSubcontractorUnitName: formData.professionalSubcontractorUnit.name || "",
    thirdPartyInspectionUnitName: formData.thirdPartyInspectionUnit.name || "",
    partName: data.formName || "",
    capacity: data.capacity || formData.capacity || "",
    constructionDate: data.constructionDate || formData.constructionDate || "",
    acceptanceDate: data.acceptanceDate || formData.acceptanceDate || ""
  };

  fields["\u5de5\u7a0b\u540d\u79f0"] = fields.projectName;
  fields["\u5efa\u8bbe\u5355\u4f4d"] = fields.developerUnitName;
  fields["\u65bd\u5de5\u5355\u4f4d"] = fields.constructorUnitName;
  fields["\u8bbe\u8ba1\u5355\u4f4d"] = fields.designUnitName;
  fields["\u76d1\u7406\u5355\u4f4d"] = fields.supervisorUnitName;
  fields["\u4e13\u4e1a\u5206\u5305\u5355\u4f4d"] = fields.professionalSubcontractorUnitName;
  fields["\u7b2c\u4e09\u65b9\u68c0\u6d4b\u5355\u4f4d"] = fields.thirdPartyInspectionUnitName;
  fields["\u90e8\u4f4d\u540d\u79f0"] = fields.partName;
  fields["\u68c0\u9a8c\u6279\u90e8\u4f4d"] = fields.partName;
  fields["\u65bd\u5de5\u90e8\u4f4d"] = fields.partName;
  fields["\u68c0\u9a8c\u6279\u5bb9\u91cf"] = fields.capacity;
  fields["\u65bd\u5de5\u65e5\u671f"] = fields.constructionDate;
  fields["\u9a8c\u6536\u65e5\u671f"] = fields.acceptanceDate;
  return fields;
}

function getProjectManagerFormData() {
  return Object.fromEntries(new FormData($("#projectManagerForm")).entries());
}

function renderCurrentProject(project) {
  currentProject = project;
  activeProjectId = project?.projectId || "project-default";
  const summary = $("#currentProjectSummary");
  if (!project) {
    summary.textContent = "尚未打开工程，将使用默认工程。";
    return;
  }

  summary.textContent = `${project.projectName || "默认工程"}｜${project.projectRootPath || ""}`;
  const form = $("#projectManagerForm");
  if (form) {
    form.elements.projectName.value = project.projectName || "";
    form.elements.projectRootPath.value = project.projectRootPath || "";
    form.elements.moduleName.value = project.moduleName || "";
    form.elements.templateVersion.value = project.templateVersion || "";
  }
}

async function loadCurrentProject() {
  try {
    showResult("#projectManagerResult", "正在刷新当前工程...");
    const result = await api("/api/projects/current");
    renderCurrentProject(result.project);
    showResult("#projectManagerResult", result);
    return result.project;
  } catch (error) {
    showResult("#projectManagerResult", error);
    throw error;
  }
}

function hasUnsavedProjectSwitchChanges() {
  return hasUnsavedMaterialLedgerChanges() ||
    batchPlanRows.some((row) => row._dirty);
}

function canSwitchProject() {
  if (!hasUnsavedProjectSwitchChanges()) {
    return true;
  }

  window.alert("当前存在未保存内容，请先保存材料台账或批量创建计划后再切换工程。");
  return false;
}

async function refreshProjectWorkspace(project, resultSelector = "#projectManagerResult") {
  renderCurrentProject(project);
  closeMaterialLedgerDialog();
  selectedMaterial = null;
  materialItems = [];
  materialLedgerRows = [];
  materialLedgerSelectedIds.clear();
  materialLedgerColumnFilters.clear();
  selectedTemplateNode = null;
  currentGeneratedForm = null;
  selectedSummaryNode = null;
  currentSummaryPreview = null;
  currentBatchPlan = null;
  batchPlanRows = [];
  currentBatchPreview = null;

  await loadTemplateLibraryTree().catch((error) => showResult("#templateResult", error));
  await loadSummaryTree().catch((error) => showResult("#summaryResult", error));
  await loadBatchPlans().catch((error) => showResult("#batchPlanResult", error));
  await loadMaterials().catch((error) => showResult("#materialResult", error));
  await loadRecentProjects().catch((error) => showResult(resultSelector, error));
  $("#logsResult").textContent = "";
}

async function requestProjectFolder(description) {
  const form = $("#projectManagerForm");
  const currentPath = form?.elements.projectRootPath.value || "";
  return api("/api/projects/select-folder", {
    method: "POST",
    body: JSON.stringify({
      description,
      initialDirectory: currentPath
    })
  });
}

function shouldPromptForProjectPath(projectRootPath) {
  const value = (projectRootPath || "").trim().toLowerCase();
  const currentValue = (currentProject?.projectRootPath || "").trim().toLowerCase();
  return !value || (!!currentValue && value === currentValue);
}

async function getProjectPathForAction(description) {
  const data = getProjectManagerFormData();
  if (!shouldPromptForProjectPath(data.projectRootPath)) {
    return data.projectRootPath;
  }

  showResult("#projectManagerResult", "正在打开目录选择窗口...");
  try {
    const result = await requestProjectFolder(description);
    if (result.projectRootPath) {
      $("#projectManagerForm").elements.projectRootPath.value = result.projectRootPath;
      return result.projectRootPath;
    }
  } catch (error) {
    showResult("#projectManagerResult", {
      success: false,
      message: `${error.message || "未选择工程目录"}。也可以在“工程目录”输入框手动输入或粘贴路径后再试。`,
      detail: error.detail
    });
  }

  return "";
}

async function createProject() {
  try {
    if (!canSwitchProject()) {
      return;
    }

    const projectRootPath = await getProjectPathForAction("选择新工程保存目录");
    if (!projectRootPath) {
      return;
    }

    const data = getProjectManagerFormData();
    showResult("#projectManagerResult", "正在创建工程...");
    const result = await api("/api/projects/create", {
      method: "POST",
      body: JSON.stringify({
        projectName: data.projectName || "",
        projectRootPath,
        moduleName: data.moduleName || "",
        templateVersion: data.templateVersion || ""
      })
    });
    showResult("#projectManagerResult", result);
    await refreshProjectWorkspace(result.project);
  } catch (error) {
    showResult("#projectManagerResult", error);
  }
}

async function saveProject() {
  try {
    const data = getProjectManagerFormData();
    showResult("#projectManagerResult", "正在保存工程信息...");
    const result = await api("/api/projects/current", {
      method: "POST",
      body: JSON.stringify({
        projectName: data.projectName || "",
        projectRootPath: data.projectRootPath || "",
        moduleName: data.moduleName || "",
        templateVersion: data.templateVersion || ""
      })
    });
    showResult("#projectManagerResult", result);
    await refreshProjectWorkspace(result.project);
  } catch (error) {
    showResult("#projectManagerResult", error);
  }
}

async function openProject() {
  try {
    if (!canSwitchProject()) {
      return;
    }

    showResult("#projectManagerResult", "正在打开目录选择窗口...");
    const folderResult = await requestProjectFolder("选择要打开的工程目录");
    const projectRootPath = folderResult.projectRootPath || "";
    if (!projectRootPath) {
      showResult("#projectManagerResult", folderResult);
      return;
    }

    showResult("#projectManagerResult", "正在打开工程...");
    const result = await api("/api/projects/open", {
      method: "POST",
      body: JSON.stringify({
        projectRootPath
      })
    });
    showResult("#projectManagerResult", result);
    await refreshProjectWorkspace(result.project);
  } catch (error) {
    const message = error?.message || "";
    if (message.includes("ProjectInfo.json")) {
      showResult("#projectManagerResult", {
        success: false,
        message: "该目录不是有效工程目录",
        detail: message
      });
      return;
    }

    showResult("#projectManagerResult", error);
  }
}

function formatProjectDate(value) {
  if (!value) {
    return "";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return String(value);
  }

  return date.toLocaleString("zh-CN", { hour12: false });
}

function getRecentProjectStatusClass(status) {
  if (status === "正常") {
    return "ok";
  }

  if (status === "路径不存在" || status === "无效工程") {
    return "error";
  }

  return "warning";
}

function renderProjectSelectionDialog() {
  const current = currentProject;
  const currentPanel = $("#projectSelectionCurrent");
  if (currentPanel) {
    currentPanel.innerHTML = current
      ? `
        <dl class="projectSelectionMeta">
          <div><dt>工程名称</dt><dd>${escapeHtml(current.projectName || "未命名工程")}</dd></div>
          <div><dt>工程路径</dt><dd title="${escapeHtml(current.projectRootPath || "")}">${escapeHtml(current.projectRootPath || "")}</dd></div>
          <div><dt>创建时间</dt><dd>${escapeHtml(formatProjectDate(current.createdAt))}</dd></div>
          <div><dt>最近打开</dt><dd>${escapeHtml(formatProjectDate(recentProjects.find((item) => item.projectId === current.projectId)?.lastOpenedAt))}</dd></div>
          <div><dt>默认模块</dt><dd>${escapeHtml(current.moduleName || "-")}</dd></div>
          <div><dt>模板版本</dt><dd>${escapeHtml(current.templateVersion || "-")}</dd></div>
        </dl>`
      : '<p class="emptyText">当前未打开工程。</p>';
  }

  const body = $("#recentProjectRows");
  if (!body) {
    return;
  }

  if (!recentProjects.length) {
    body.innerHTML = '<tr><td colspan="5" class="emptyText">暂无最近工程记录。</td></tr>';
    return;
  }

  body.innerHTML = recentProjects.map((item) => {
    const selected = item.projectId === selectedRecentProjectId;
    const currentRow = current?.projectId === item.projectId;
    const statusClass = getRecentProjectStatusClass(item.status);
    return `
      <tr class="${selected ? "selectedRow" : ""} ${currentRow ? "currentProjectRow" : ""}" data-recent-project-id="${escapeHtml(item.projectId)}" title="${escapeHtml(item.projectPath)}">
        <td>${escapeHtml(item.projectName || "未命名工程")}</td>
        <td class="projectPathCell">${escapeHtml(item.projectPath || "")}</td>
        <td>${escapeHtml(formatProjectDate(item.lastOpenedAt))}</td>
        <td>${escapeHtml(item.templateVersion || item.defaultModule || "-")}</td>
        <td><span class="projectStatusBadge ${statusClass}">${escapeHtml(item.status || "未知")}</span></td>
      </tr>`;
  }).join("");
}

async function loadRecentProjects() {
  const result = await api("/api/projects/recent");
  recentProjects = result.items || [];
  if (!recentProjects.some((item) => item.projectId === selectedRecentProjectId)) {
    selectedRecentProjectId = recentProjects[0]?.projectId || "";
  }
  renderProjectSelectionDialog();
  return result;
}

async function openProjectSelectionDialog() {
  activateTab("panel");
  $("#projectSelectionDialog").classList.remove("hidden");
  showResult("#projectSelectionResult", "正在读取最近工程...");
  await loadCurrentProject().catch((error) => showResult("#projectSelectionResult", error));
  try {
    const result = await loadRecentProjects();
    showResult("#projectSelectionResult", result);
  } catch (error) {
    showResult("#projectSelectionResult", error);
  }
}

function closeProjectSelectionDialog() {
  $("#projectSelectionDialog").classList.add("hidden");
}

async function openProjectFromSelection(projectRootPath) {
  if (!projectRootPath || !canSwitchProject()) {
    return;
  }

  showResult("#projectSelectionResult", "正在打开工程...");
  try {
    const result = await api("/api/projects/open", {
      method: "POST",
      body: JSON.stringify({ projectRootPath })
    });
    await refreshProjectWorkspace(result.project, "#projectSelectionResult");
    renderProjectSelectionDialog();
    showResult("#projectSelectionResult", `已切换到工程：${result.project?.projectName || ""}`);
  } catch (error) {
    const message = error?.message || "";
    showResult("#projectSelectionResult", message.includes("ProjectInfo.json")
      ? { success: false, message: "无法打开工程：未找到 ProjectInfo.json。", detail: message }
      : error);
  }
}

async function selectProjectFolderFromDialog() {
  if (!canSwitchProject()) {
    return;
  }

  showResult("#projectSelectionResult", "正在打开目录选择窗口...");
  try {
    const folderResult = await requestProjectFolder("选择要打开的工程目录");
    const projectRootPath = folderResult.projectRootPath || "";
    if (!projectRootPath) {
      showResult("#projectSelectionResult", folderResult);
      return;
    }

    await openProjectFromSelection(projectRootPath);
  } catch (error) {
    showResult("#projectSelectionResult", error);
  }
}

async function openSelectedRecentProject() {
  const item = recentProjects.find((project) => project.projectId === selectedRecentProjectId);
  if (!item) {
    showResult("#projectSelectionResult", "请先选择一个最近工程。");
    return;
  }

  if (item.status === "路径不存在") {
    showResult("#projectSelectionResult", "工程路径不存在，请检查文件夹是否被移动或删除。");
    return;
  }

  if (item.status === "无效工程") {
    showResult("#projectSelectionResult", "无法打开工程：未找到 ProjectInfo.json。");
    return;
  }

  await openProjectFromSelection(item.projectPath);
}

async function removeSelectedRecentProject() {
  if (!selectedRecentProjectId) {
    showResult("#projectSelectionResult", "请先选择要移除的最近工程。");
    return;
  }

  try {
    const result = await api(`/api/projects/recent/${encodeURIComponent(selectedRecentProjectId)}`, { method: "DELETE" });
    recentProjects = result.items || [];
    selectedRecentProjectId = recentProjects[0]?.projectId || "";
    renderProjectSelectionDialog();
    showResult("#projectSelectionResult", result);
  } catch (error) {
    showResult("#projectSelectionResult", error);
  }
}

async function clearRecentProjects() {
  if (!window.confirm("确定清空全部最近工程记录吗？")) {
    return;
  }

  try {
    const result = await api("/api/projects/recent", { method: "DELETE" });
    recentProjects = [];
    selectedRecentProjectId = "";
    renderProjectSelectionDialog();
    showResult("#projectSelectionResult", result);
  } catch (error) {
    showResult("#projectSelectionResult", error);
  }
}

async function validateRecentProjects() {
  try {
    const result = await api("/api/projects/recent/validate", { method: "POST" });
    recentProjects = result.items || [];
    renderProjectSelectionDialog();
    showResult("#projectSelectionResult", result);
  } catch (error) {
    showResult("#projectSelectionResult", error);
  }
}

async function api(path, options = {}) {
  let response;
  try {
    const headers = options.body instanceof FormData
      ? (options.headers || {})
      : {
          "Content-Type": "application/json; charset=utf-8",
          ...(options.headers || {})
        };
    response = await fetch(`${serviceBaseUrl}${path}`, {
      ...options,
      headers
    });
  } catch (error) {
    throw {
      success: false,
      message: `无法连接本地服务：${serviceBaseUrl}。请先启动 GeneratorService，然后点击右上角刷新按钮。`,
      detail: error?.message || String(error)
    };
  }

  let data;
  try {
    data = await response.json();
  } catch (error) {
    data = {
      success: false,
      message: `服务返回内容无法解析：HTTP ${response.status}`,
      detail: error?.message || String(error)
    };
  }

  if (!response.ok) {
    throw data;
  }
  return data;
}

async function refreshStatus() {
  setServiceChecking();
  try {
    const data = await api("/api/health");
    setServiceOnline(data);
    return true;
  } catch {
    setServiceOffline();
    return false;
  }
}

function setServiceChecking() {
  const status = $("#serviceStatus");
  status.textContent = "正在检查本地服务...";
  status.className = "statusText checking";
}

function setServiceOnline(data) {
  serviceAvailable = true;
  const status = $("#serviceStatus");
  status.textContent = `服务正常｜服务 ${data.version}｜插件 ${addinVersion}｜AI ${data.aiEnabled ? "已启用" : "未启用"}`;
  status.className = "statusText online";
  $("#serviceGuide").classList.add("hidden");
  setGenerateDisabled(false);
}

function setServiceOffline() {
  serviceAvailable = false;
  const status = $("#serviceStatus");
  status.textContent = "本地生成服务未启动";
  status.className = "statusText offline";
  $("#serviceGuide").classList.remove("hidden");
  $("#serviceStartCommand").textContent = serviceStartCommand;
  setGenerateDisabled(true);
  renderTemplateList();
  $("#templateSummary").textContent = "本地服务未启动，暂时无法读取工程资料规范层级树。";
  $("#templateTreeView").innerHTML = '<p class="emptyText">请先启动 GeneratorService。</p>';
  $("#summaryTreeView").innerHTML = '<p class="emptyText">请先启动 GeneratorService。</p>';
  $("#summaryPreview").innerHTML = '<p class="emptyText">请先启动 GeneratorService。</p>';
  $("#summarySummary").textContent = "本地服务未启动，暂时无法读取分部分项汇总。";
  if ($("#batchPlanBody")) {
    $("#batchPlanBody").innerHTML = '<tr><td colspan="12">请先启动 GeneratorService。</td></tr>';
    $("#batchPlanSummary").textContent = "本地服务未启动，暂时无法读取批量创建计划。";
  }
  if ($("#materialRows")) {
    $("#materialRows").innerHTML = '<tr><td colspan="9">请先启动 GeneratorService。</td></tr>';
    $("#materialSummary").textContent = "本地服务未启动，暂时无法读取材料管理数据。";
  }
  renderSpreadsheetPlaceholder();
  $("#knowledgeSummary").textContent = "本地服务未启动，暂时无法读取知识库。";
  renderKnowledgeItems([]);
  showResult("#knowledgeResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“查询知识库”。");
  $("#aiSummary").textContent = "本地服务未启动，暂时无法生成 AI 文本预览。";
  showResult("#aiResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“生成预览”。");
  $("#settingsSummary").textContent = "本地服务未启动，暂时无法读取设置。";
  $("#settingsGrid").innerHTML = "";
  showResult("#settingsResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“刷新设置”。");
  $("#moduleSummary").textContent = "本地服务未启动，暂时无法读取模块库。";
  $("#moduleGrid").innerHTML = "";
  showResult("#moduleResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“刷新模块”。");
  $("#environmentSummary").textContent = "本地服务未启动，暂时无法执行环境自检。";
  $("#environmentGrid").innerHTML = "";
  showResult("#environmentResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“开始自检”。");
}

async function retryServiceStatus() {
  const online = await refreshStatus();
  if (!online) {
    return;
  }

  await loadTemplates().catch((error) => showResult("#generateResult", error));
  await loadModules().catch((error) => showResult("#templateResult", error));
  await loadCurrentProject().catch((error) => showResult("#projectManagerResult", error));
  await loadRecentProjects().catch((error) => showResult("#projectSelectionResult", error));
  await loadTemplateLibraryTree().catch((error) => showResult("#templateResult", error));
  await loadSummaryTree().catch((error) => showResult("#summaryResult", error));
  await loadBatchPlans().catch((error) => showResult("#batchPlanResult", error));
  await loadMaterials().catch((error) => showResult("#materialResult", error));
  await loadKnowledgeItems().catch((error) => showResult("#knowledgeResult", error));
  await loadSettings().catch((error) => showResult("#settingsResult", error));
  await runEnvironmentCheck().catch((error) => showResult("#environmentResult", error));
  await refreshLicense();
}

function setGenerateDisabled(disabled) {
  if ($("#openProjectSelection")) $("#openProjectSelection").disabled = disabled;
  $("#generateCurrent").disabled = disabled;
  $("#generateBatch").disabled = disabled;
  $("#reloadTemplates").disabled = disabled;
  $("#refreshTemplateList").disabled = disabled;
  $("#openTemplateFolder").disabled = disabled;
  $("#refreshKnowledge").disabled = disabled;
  $("#previewAiText").disabled = disabled;
  $("#loadSettings").disabled = disabled;
  $("#rescanModules").disabled = disabled;
  $("#runEnvironmentCheck").disabled = disabled;
  if ($("#refreshMaterials")) $("#refreshMaterials").disabled = disabled;
  if ($("#resetMaterialForm")) $("#resetMaterialForm").disabled = disabled;
  if ($("#openMaterialLedgerDialog")) $("#openMaterialLedgerDialog").disabled = disabled;
  if ($("#exportMaterialLedger")) $("#exportMaterialLedger").disabled = disabled;
  if ($("#refreshBatchPlans")) $("#refreshBatchPlans").disabled = disabled;
  if ($("#batchPlanAddRow")) $("#batchPlanAddRow").disabled = disabled;
  if ($("#batchPlanDeleteRows")) $("#batchPlanDeleteRows").disabled = disabled;
  if ($("#batchPlanSave")) $("#batchPlanSave").disabled = disabled;
  if ($("#batchPlanPreview")) $("#batchPlanPreview").disabled = disabled;
  if ($("#batchPlanGenerate")) $("#batchPlanGenerate").disabled = disabled;
  updateMaterialActionState();
  updateTemplateToolbarState(disabled);
}

async function copyStartCommand() {
  const command = $("#serviceStartCommand").textContent;
  try {
    await navigator.clipboard.writeText(command);
    showResult("#generateResult", "处理方式已复制。正式安装包请重新运行 Client\\1-Install-Client.cmd；开发调试请执行复制的调试命令，然后点击“重新检测服务”。");
  } catch {
    showResult("#generateResult", `无法自动复制，请手动复制：\n${command}`);
  }
}

async function loadTemplates() {
  const data = await api("/api/templates");
  templates = data.templates || [];
  const select = $("#templateSelect");
  select.innerHTML = "";
  for (const template of templates) {
    const option = document.createElement("option");
    option.value = template.name;
    option.textContent = template.name;
    select.appendChild(option);
  }
  refreshBatchTemplateOptions();
  renderTemplateList();
  return templates;
}

function renderTemplateList() {
  const list = $("#templateList");
  const summary = $("#templateSummary");
  if (!list) {
    if (serviceAvailable && summary) {
      summary.textContent = templates.length === 0
        ? "当前模板库为空。"
        : `当前工程资料规范树已加载，基础模板 ${templates.length} 个。`;
    }
    return;
  }
  list.innerHTML = "";

  if (!serviceAvailable) {
    summary.textContent = "本地服务未启动，暂时无法读取模板库。";
    showResult("#templateResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“刷新模板”。");
    return;
  }

  if (templates.length === 0) {
    summary.textContent = "当前模板库为空。";
    showResult("#templateResult", "请把 .xlsx 模板文件放入 Templates 目录，然后点击“刷新模板”。");
    return;
  }

  summary.textContent = `当前共读取到 ${templates.length} 个模板。`;
  for (const template of templates) {
    const item = document.createElement("article");
    item.className = "templateItem";

    const content = document.createElement("div");
    const name = document.createElement("h3");
    name.textContent = template.name;
    const path = document.createElement("p");
    path.textContent = template.fullPath;
    content.append(name, path);

    const badge = document.createElement("span");
    badge.className = "templateBadge";
    badge.textContent = "xlsx";

    item.append(content, badge);
    list.appendChild(item);
  }
  showResult("#templateResult", templates);
}

async function refreshTemplateManagement() {
  showResult("#templateResult", "正在刷新工程资料规范层级树...");
  try {
    await rescanModules();
    await loadTemplates();
    await loadTemplateLibraryTree();
  } catch (error) {
    $("#templateSummary").textContent = "模板读取失败。";
    showResult("#templateResult", error);
  }
}

async function loadModules() {
  const result = await api("/api/modules");
  modules = result.modules || [];
  renderModules(modules);
  showResult("#moduleResult", result);
  return modules;
}

async function rescanModules() {
  $("#moduleSummary").textContent = "正在重新扫描 Modules 目录...";
  const result = await api("/api/modules/rescan", { method: "POST" });
  modules = result.modules || [];
  renderModules(modules);
  showResult("#moduleResult", result);
  return result;
}

function renderModules(items) {
  const grid = $("#moduleGrid");
  const summary = $("#moduleSummary");
  if (!grid || !summary) {
    return;
  }

  grid.innerHTML = "";
  if (!serviceAvailable) {
    summary.textContent = "本地服务未启动，暂时无法读取模块库。";
    return;
  }

  if (!items || items.length === 0) {
    summary.textContent = "当前没有发现 .module 模块包。";
    grid.innerHTML = '<p class="emptyText">请把 .module 文件放入 Modules 目录后点击“刷新模块”。</p>';
    return;
  }

  const validCount = items.filter((item) => item.isValid).length;
  summary.textContent = `已发现 ${items.length} 个模块，有效 ${validCount} 个。`;
  for (const item of items) {
    const card = document.createElement("article");
    card.className = `moduleCard ${item.isValid ? "ok" : "error"}`;

    const header = document.createElement("div");
    header.className = "moduleCardHeader";
    const title = document.createElement("h3");
    title.textContent = item.name || item.moduleId || "未识别模块";
    const badge = document.createElement("span");
    badge.className = `diagnosticBadge ${item.isValid ? "ok" : "error"}`;
    badge.textContent = item.isValid ? "有效" : "无效";
    header.append(title, badge);

    const meta = document.createElement("dl");
    meta.className = "moduleMeta";
    appendModuleMeta(meta, "模块ID", item.moduleId || "未读取");
    appendModuleMeta(meta, "版本", item.version || "未读取");
    appendModuleMeta(meta, "地区", item.province || "未读取");
    appendModuleMeta(meta, "专业", item.major || "未读取");
    appendModuleMeta(meta, "年份", item.year || "未读取");
    appendModuleMeta(meta, "文件", item.packagePath || "");
    card.append(header, meta);

    if (item.errors && item.errors.length > 0) {
      const errors = document.createElement("ul");
      errors.className = "moduleErrors";
      for (const error of item.errors) {
        const li = document.createElement("li");
        li.textContent = error;
        errors.appendChild(li);
      }
      card.appendChild(errors);
    }

    grid.appendChild(card);
  }
}

function appendModuleMeta(container, label, value) {
  const dt = document.createElement("dt");
  dt.textContent = label;
  const dd = document.createElement("dd");
  dd.textContent = value;
  container.append(dt, dd);
}

async function loadTemplateLibraryTree() {
  const result = await api(`/api/template-library/tree?projectId=${encodeURIComponent(activeProjectId)}`);
  templateTreeNodes = result.nodes || [];
  selectedTemplateNode = null;
  currentGeneratedForm = null;
  lastRowHeightFitAdjustment = null;
  $("#templateSummary").textContent = `${result.projectName}｜已加载工程资料规范层级树。`;
  renderTemplateTreeView();
  renderSpreadsheetPlaceholder();
  updateTemplateToolbarState(false);
  showResult("#templateResult", result);
  return result;
}

function renderTemplateTreeView() {
  const tree = $("#templateTreeView");
  if (!tree) {
    return;
  }

  tree.innerHTML = "";
  const filterText = ($("#templateSearch")?.value || "").trim();
  const nodes = filterTreeNodes(templateTreeNodes, filterText);
  if (nodes.length === 0) {
    tree.innerHTML = '<p class="emptyText">没有匹配的模板或资料表。</p>';
    return;
  }

  const root = document.createElement("div");
  root.className = "specTreeRoot";
  for (const node of nodes) {
    root.appendChild(createSpecTreeNode(node));
  }
  tree.appendChild(root);
}

function filterTreeNodes(nodes, filterText) {
  if (!filterText) {
    return nodes;
  }

  const normalized = filterText.toLowerCase();
  return nodes
    .map((node) => {
      const children = filterTreeNodes(node.children || [], filterText);
      const selfMatches = [
        node.name,
        node.templateCode,
        node.discipline
      ].some((value) => String(value || "").toLowerCase().includes(normalized));
      return selfMatches || children.length > 0 ? { ...node, children } : null;
    })
    .filter(Boolean);
}

function createSpecTreeNode(node) {
  if (node.nodeType === "folder") {
    const details = document.createElement("details");
    details.className = "specTreeFolder";
    details.open = true;

    const summary = document.createElement("summary");
    const label = document.createElement("span");
    label.textContent = node.name;
    const meta = document.createElement("small");
    meta.textContent = resolveFolderMeta(node);
    summary.append(label, meta);
    details.appendChild(summary);

    const children = document.createElement("div");
    children.className = "specTreeChildren";
    for (const child of node.children || []) {
      children.appendChild(createSpecTreeNode(child));
    }
    details.appendChild(children);
    return details;
  }

  const button = document.createElement("button");
  button.type = "button";
  button.className = `specTreeNode ${node.nodeType}`;
  button.dataset.nodeId = node.id;
  button.classList.toggle("selected", selectedTemplateNode?.id === node.id);

  const title = document.createElement("span");
  title.textContent = node.name;
  const meta = document.createElement("small");
  meta.textContent = node.nodeType === "template"
    ? [node.templateCode || "检验批模板", node.moduleName || ""].filter(Boolean).join("｜")
    : "已创建资料表";
  button.append(title, meta);
  button.addEventListener("click", (event) => {
    handleSpecTreeNodeClick(event, node);
  });
  button.addEventListener("dblclick", (event) => {
    handleSpecTreeNodeDoubleClick(event, node);
  });

  if (!node.children || node.children.length === 0) {
    return button;
  }

  const isCollapsed = collapsedTemplateNodeIds.has(node.id);
  button.classList.add("hasChildren");
  button.setAttribute("aria-expanded", String(!isCollapsed));

  const branch = document.createElement("div");
  branch.className = `specTreeBranch ${node.nodeType}${isCollapsed ? " collapsed" : ""}`;
  branch.appendChild(button);

  if (isCollapsed) {
    return branch;
  }

  const children = document.createElement("div");
  children.className = "specTreeChildren";
  for (const child of node.children) {
    children.appendChild(createSpecTreeNode(child));
  }
  branch.appendChild(children);
  return branch;
}

function handleSpecTreeNodeClick(event, node) {
  window.clearTimeout(templateTreeClickTimer);
  if (node.nodeType === "template") {
    templateTreeClickTimer = window.setTimeout(async () => {
      if (node.children?.length > 0) {
        toggleTemplateNode(node.id);
        await selectTemplateTreeNode(node);
        renderTemplateTreeView();
        return;
      }

      await selectTemplateTreeNode(node);
    }, 220);
    return;
  }

  selectTemplateTreeNode(node);
}

function handleSpecTreeNodeDoubleClick(event, node) {
  event.preventDefault();
  event.stopPropagation();
  window.clearTimeout(templateTreeClickTimer);

  if (node.nodeType !== "template") {
    selectTemplateTreeNode(node);
    return;
  }

  createGeneratedFormDirectly(node);
}

function toggleTemplateNode(nodeId) {
  if (collapsedTemplateNodeIds.has(nodeId)) {
    collapsedTemplateNodeIds.delete(nodeId);
    return;
  }

  collapsedTemplateNodeIds.add(nodeId);
}

function resolveFolderLevelLabel(folderLevel) {
  const normalized = String(folderLevel || "").trim();
  return {
    discipline: "专业",
    division: "分部",
    sub_division: "子分部",
    sub_item: "分项",
    inspection_batch: "检验批",
    category: "分类",
    "1": "分部",
    "2": "子分部",
    "3": "分项",
    "4": "检验批",
    "专业": "专业",
    "分部": "分部",
    "分部工程": "分部",
    "子分部": "子分部",
    "子分部工程": "子分部",
    "分项": "分项",
    "分项工程": "分项",
    "检验批": "检验批"
  }[normalized] || "分类";
}

function resolveFolderMeta(node) {
  if (node.folderLevel === "module") {
    return [node.province, node.major, node.year].filter(Boolean).join("｜") || "模块";
  }

  return resolveFolderLevelLabel(node.folderLevel);
}

async function selectTemplateTreeNode(node) {
  selectedTemplateNode = node;
  currentGeneratedForm = null;
  lastRowHeightFitAdjustment = null;
  for (const item of $$(".specTreeNode")) {
    item.classList.toggle("selected", item.dataset.nodeId === node.id);
  }

  if (node.nodeType === "template") {
    $("#spreadsheetTitle").textContent = node.name;
    $("#spreadsheetSummary").textContent = `模板编码：${node.templateCode || "未配置"}｜模块：${node.moduleName || "兼容模板库"}`;
    $("#spreadsheetPreview").innerHTML = `
      <div class="templateInfoPanel">
        <div class="templateInfoGrid">
          <span>模块</span><strong>${escapeHtml(node.moduleName || "兼容模板库")}</strong>
          <span>地区</span><strong>${escapeHtml(node.province || "未配置")}</strong>
          <span>专业</span><strong>${escapeHtml(node.major || node.discipline || "未配置")}</strong>
          <span>年份</span><strong>${escapeHtml(node.year || "未配置")}</strong>
          <span>模板文件</span><strong>${escapeHtml(node.templateFilePath || "未配置")}</strong>
        </div>
        <div id="templateRulesPanel" class="templateRulesPanel">
          <p class="emptyText">正在读取模板规则...</p>
        </div>
      </div>`;
    await loadTemplateRules(node);
    updateTemplateToolbarState(false);
    return;
  }

  if (node.nodeType === "generated_form") {
    await openGeneratedForm(node);
  }
}

async function loadTemplateRules(node) {
  const panel = $("#templateRulesPanel");
  if (!panel) {
    return;
  }

  if (!node.moduleId) {
    panel.innerHTML = '<p class="emptyText">兼容模板暂无模块规则。</p>';
    return;
  }

  try {
    const result = await api(`/api/templates/${encodeURIComponent(node.id)}/rules`);
    renderTemplateRules(result.rules || []);
    showResult("#templateResult", result);
  } catch (error) {
    panel.innerHTML = `<p class="emptyText">${escapeHtml(error.message || "规则读取失败。")}</p>`;
  }
}

function renderTemplateRules(rules) {
  const panel = $("#templateRulesPanel");
  if (!panel) {
    return;
  }

  if (rules.length === 0) {
    panel.innerHTML = '<p class="emptyText">该模板暂未配置 InspectionRule。</p>';
    return;
  }

  const groups = rules.reduce((acc, rule) => {
    const key = rule.ruleType || "未分类";
    acc[key] = acc[key] || [];
    acc[key].push(rule);
    return acc;
  }, {});

  panel.innerHTML = "";
  for (const [ruleType, items] of Object.entries(groups)) {
    const section = document.createElement("section");
    section.className = "ruleGroup";
    const title = document.createElement("h3");
    title.textContent = ruleType;
    section.appendChild(title);

    for (const rule of items) {
      const article = document.createElement("article");
      article.className = "ruleItem";
      article.innerHTML = `
        <strong>${escapeHtml(rule.itemName || "未命名规则")}</strong>
        <p>${escapeHtml(rule.requirement || "未配置要求")}</p>
        <small>${escapeHtml([rule.checkMethod, rule.allowedDeviation, rule.source].filter(Boolean).join("｜"))}</small>`;
      section.appendChild(article);
    }

    panel.appendChild(section);
  }
}

async function openGeneratedForm(node) {
  $("#spreadsheetTitle").textContent = node.name;
  $("#spreadsheetSummary").textContent = "正在打开资料表...";
  try {
    const form = await api(`/api/generated-forms/${encodeURIComponent(node.id)}`);
    currentGeneratedForm = form;
    lastRowHeightFitAdjustment = null;
    $("#spreadsheetSummary").textContent = `模板编码：${form.templateCode || "未配置"}｜可编辑：${form.canEdit ? "是" : "否"}`;
    $("#spreadsheetPreview").innerHTML = `
      <div class="spreadsheetFileCard">
        <strong>${escapeHtml(form.name)}</strong>
        <p>${escapeHtml(form.generatedFilePath)}</p>
      </div>`;
    await openSpreadsheetPath(form);
    updateTemplateToolbarState(false);
    showResult("#templateResult", form);
  } catch (error) {
    $("#spreadsheetSummary").textContent = "资料表打开失败。";
    showResult("#templateResult", error);
  }
}

async function openSpreadsheetPath(form) {
  try {
    if (workbookManager.openOrActivate(form.generatedFilePath)) {
      return;
    }
  } catch {
    // Fallback to local service if WPS object model is unavailable or rejects the path.
  }

  await api(`/api/generated-forms/${encodeURIComponent(form.id)}/open`, { method: "POST" });
}

async function openLocalSpreadsheetFile(filePath) {
  if (!filePath) {
    throw new Error("生成成功，但服务未返回可打开的文件路径。");
  }

  try {
    if (workbookManager.openOrActivate(filePath)) {
      return;
    }
  } catch {
    // Fallback to local service if WPS object model is unavailable or rejects the path.
  }

  await api("/api/files/open", {
    method: "POST",
    body: JSON.stringify({ filePath })
  });
}

function createWorkbookManager() {
  const cache = new Map();

  function openOrActivate(filePath) {
    if (!filePath) {
      return false;
    }

    const app = window.Application;
    const workbooks = app?.Workbooks;
    if (!workbooks || typeof workbooks.Open !== "function") {
      return false;
    }

    const openedWorkbook = getOpenedWorkbook(filePath);
    if (openedWorkbook) {
      activateWorkbook(openedWorkbook);
      return true;
    }

    const workbook = workbooks.Open(filePath);
    const key = normalizeWorkbookPath(filePath);
    const opened = workbook || app.ActiveWorkbook;
    if (isWorkbookMatch(opened, key)) {
      cache.set(key, opened);
      activateWorkbook(opened);
    }

    return true;
  }

  function getOpenedWorkbook(filePath) {
    const key = normalizeWorkbookPath(filePath);
    if (!key) {
      return null;
    }

    const cached = cache.get(key);
    if (isWorkbookMatch(cached, key)) {
      return cached;
    }

    cache.delete(key);
    const workbook = findOpenedWorkbook(key);
    if (workbook) {
      cache.set(key, workbook);
    }

    return workbook;
  }

  function findOpenedWorkbook(key) {
    const workbooks = window.Application?.Workbooks;
    const count = getWorkbookCount(workbooks);
    const targetName = getPathFileName(key);

    for (let index = 1; index <= count; index += 1) {
      const workbook = getWorkbookByIndex(workbooks, index);
      if (isWorkbookMatch(workbook, key, targetName)) {
        return workbook;
      }
    }

    return null;
  }

  function activateWorkbook(workbook) {
    if (!workbook) {
      return false;
    }

    try {
      workbook.Activate?.();
    } catch {
      // Continue with sheet activation fallback.
    }

    try {
      workbook.ActiveSheet?.Activate?.();
    } catch {
      // Some WPS versions do not expose ActiveSheet on workbook.
    }

    try {
      const firstSheet = workbook.Worksheets?.Item?.(1) || workbook.Sheets?.Item?.(1);
      firstSheet?.Activate?.();
    } catch {
      // Best effort only; workbook activation is enough for switching.
    }

    return true;
  }

  function forgetWorkbook(filePath) {
    cache.delete(normalizeWorkbookPath(filePath));
  }

  function normalizeWorkbookPath(filePath) {
    let value = String(filePath || "").trim();
    try {
      value = decodeURIComponent(value);
    } catch {
      // Keep the original value if it is not URL-encoded.
    }

    return normalizeLocalPath(value);
  }

  function isWorkbookMatch(workbook, key, targetName = getPathFileName(key)) {
    if (!workbook || !key) {
      return false;
    }

    const workbookPath = normalizeWorkbookPath(getWorkbookPath(workbook));
    if (!workbookPath) {
      return false;
    }

    if (workbookPath === key) {
      return true;
    }

    return targetName && getPathFileName(workbookPath) === targetName;
  }

  function getWorkbookCount(workbooks) {
    try {
      const count = typeof workbooks?.Count === "function" ? workbooks.Count() : workbooks?.Count;
      return Number(count) || 0;
    } catch {
      return 0;
    }
  }

  function getWorkbookByIndex(workbooks, index) {
    try {
      if (typeof workbooks?.Item === "function") {
        return workbooks.Item(index);
      }
    } catch {
      // Try the alternate COM collection call shape below.
    }

    try {
      return typeof workbooks === "function" ? workbooks(index) : null;
    } catch {
      return null;
    }
  }

  function getPathFileName(path) {
    return String(path || "").split("/").filter(Boolean).pop() || "";
  }

  return {
    openOrActivate,
    getOpenedWorkbook,
    activateWorkbook,
    forgetWorkbook,
    normalizeWorkbookPath
  };
}

function renderSpreadsheetPlaceholder() {
  $("#spreadsheetTitle").textContent = "请选择资料表";
  $("#spreadsheetSummary").textContent = "选择左侧检验批模板可新建资料；选择已创建的具体部位表后，会用 WPS 原生表格打开对应 .xlsx。";
  $("#spreadsheetPreview").innerHTML = `
    <div class="spreadsheetEmpty">
      <strong>WPS 表格编辑区</strong>
      <p>这里不重绘 Excel。点击已创建资料表后，系统会打开真实 .xlsx 文件，以保留合并单元格、边框、字体、行高和列宽。</p>
    </div>`;
}

function updateTemplateToolbarState(forceDisabled = false) {
  const canCreate = !forceDisabled && selectedTemplateNode?.nodeType === "template";
  const canOperateForm = !forceDisabled && !!currentGeneratedForm;
  const canUndoRowHeightFit = canOperateForm && !!lastRowHeightFitAdjustment;
  $("#newGeneratedForm").disabled = !canCreate;
  $("#saveSpreadsheet").disabled = !canOperateForm;
  $("#exportSpreadsheet").disabled = !canOperateForm;
  $("#rowHeightFitMode").disabled = !canOperateForm;
  $("#fitRowHeights").disabled = !canOperateForm;
  $("#undoRowHeightFit").disabled = !canUndoRowHeightFit;
  $("#deleteGeneratedForm").disabled = !canOperateForm;
}

async function loadSummaryTree() {
  if (!serviceAvailable) {
    return;
  }

  selectedSummaryNode = null;
  currentSummaryPreview = null;
  $("#summarySummary").textContent = "正在读取分部分项汇总...";
  const result = await api(`/api/summary/tree?projectId=${encodeURIComponent(activeProjectId)}`);
  summaryTreeNodes = result.nodes || [];
  renderSummaryTree();
  renderSummaryPlaceholder(result.warnings || []);
  $("#summarySummary").textContent = summaryTreeNodes.length === 0
    ? "当前工程还没有可汇总的已创建检验批资料。"
    : `已读取 ${summaryTreeNodes.length} 个分部汇总节点。`;
  showResult("#summaryResult", result);
  return result;
}

function renderSummaryTree() {
  const tree = $("#summaryTreeView");
  if (!tree) {
    return;
  }

  tree.innerHTML = "";
  if (!summaryTreeNodes.length) {
    tree.innerHTML = '<p class="emptyText">当前工程暂无可汇总资料。</p>';
    return;
  }

  const root = document.createElement("div");
  root.className = "summaryTreeRoot";
  for (const node of summaryTreeNodes) {
    root.appendChild(createSummaryTreeNode(node));
  }
  tree.appendChild(root);
}

function createSummaryTreeNode(node) {
  const branch = document.createElement("div");
  branch.className = "summaryTreeBranch";

  const button = document.createElement("button");
  button.type = "button";
  button.className = `summaryTreeNode ${node.summaryType}`;
  button.classList.toggle("selected", selectedSummaryNode?.id === node.id);

  const title = document.createElement("span");
  title.textContent = node.name;
  const meta = document.createElement("small");
  meta.textContent = buildSummaryNodeMeta(node);
  button.append(title, meta);
  button.addEventListener("click", () => selectSummaryNode(node));
  branch.appendChild(button);

  if (node.children?.length) {
    const children = document.createElement("div");
    children.className = "summaryTreeChildren";
    for (const child of node.children) {
      children.appendChild(createSummaryTreeNode(child));
    }
    branch.appendChild(children);
  }

  return branch;
}

function buildSummaryNodeMeta(node) {
  if (node.summaryType === "Division") {
    return `${node.subDivisionCount} 个子分部｜${node.subItemCount} 个分项`;
  }

  if (node.summaryType === "SubDivision") {
    return `${node.subItemCount} 个分项｜${node.inspectionBatchCount} 个检验批`;
  }

  return `${node.inspectionBatchCount} 个检验批`;
}

async function selectSummaryNode(node) {
  selectedSummaryNode = node;
  for (const item of $$(".summaryTreeNode")) {
    item.classList.remove("selected");
  }
  renderSummaryTree();
  $("#summaryPreview").innerHTML = '<p class="emptyText">正在生成预览...</p>';
  $("#generateSummary").disabled = true;

  try {
    const query = new URLSearchParams({
      projectId: activeProjectId,
      type: node.summaryType,
      categoryId: node.categoryId
    });
    const preview = await api(`/api/summary/preview?${query.toString()}`);
    currentSummaryPreview = preview;
    renderSummaryPreview(preview);
    $("#generateSummary").disabled = (preview.rows || []).length === 0;
    showResult("#summaryResult", preview);
  } catch (error) {
    currentSummaryPreview = null;
    $("#summaryPreview").innerHTML = `<p class="emptyText">${escapeHtml(error.message || "汇总预览失败。")}</p>`;
    showResult("#summaryResult", error);
  }
}

function renderSummaryPlaceholder(warnings = []) {
  const warningMarkup = warnings.length
    ? `<ul class="summaryWarnings">${warnings.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}</ul>`
    : "";
  $("#summaryPreview").innerHTML = `
    <div class="summaryEmpty">
      <strong>请选择左侧分部、子分部或分项。</strong>
      <p>系统只统计当前工程中已经创建且有效的检验批资料。</p>
      ${warningMarkup}
    </div>`;
  $("#generateSummary").disabled = true;
}

function renderSummaryPreview(preview) {
  const rows = preview.rows || [];
  const columns = preview.summaryType === "SubItem"
    ? ["序号", "检验批名称", "检验批容量", "检验批部位", "施工单位检查结果", "监理单位验收结论"]
    : preview.summaryType === "SubDivision"
      ? ["序号", "分项工程名称", "检验批数", "施工单位检查评定结果", "监理单位验收结论"]
      : ["序号", "子分部工程名称", "分项数", "施工单位检查评定结果", "监理单位验收结论"];

  const body = rows.map((row) => {
    const values = preview.summaryType === "SubItem"
      ? [row.sequence, row.name, row.capacity, row.partName, row.constructorResult, row.supervisorConclusion]
      : [row.sequence, row.name, row.count, row.constructorResult, row.supervisorConclusion];
    return `<tr>${values.map((value) => `<td>${escapeHtml(value)}</td>`).join("")}</tr>`;
  }).join("");

  const warnings = (preview.warnings || []).length
    ? `<ul class="summaryWarnings">${preview.warnings.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}</ul>`
    : "";

  $("#summaryPreview").innerHTML = `
    <section class="summaryPreviewPanel">
      <div class="summaryPreviewHeader">
        <h3>${escapeHtml(preview.title)}</h3>
        <p>检验批 ${preview.totals.inspectionBatchCount} 个｜分项 ${preview.totals.subItemCount} 个｜子分部 ${preview.totals.subDivisionCount} 个</p>
      </div>
      <div class="tableScroller">
        <table class="summaryTable">
          <thead><tr>${columns.map((column) => `<th>${column}</th>`).join("")}</tr></thead>
          <tbody>${body || `<tr><td colspan="${columns.length}">暂无可汇总数据</td></tr>`}</tbody>
        </table>
      </div>
      ${warnings}
    </section>`;
}

async function generateSummary() {
  if (!selectedSummaryNode || !currentSummaryPreview) {
    showResult("#summaryResult", "请先选择一个汇总节点并确认预览。");
    return;
  }

  $("#generateSummary").disabled = true;
  showResult("#summaryResult", "正在生成汇总表...");
  try {
    const result = await api("/api/summary/generate", {
      method: "POST",
      body: JSON.stringify({
        projectId: activeProjectId,
        type: selectedSummaryNode.summaryType,
        categoryId: selectedSummaryNode.categoryId
      })
    });
    showResult("#summaryResult", result);
    if (result.filePath || result.summaryDocument?.filePath) {
      await openSummarySpreadsheet(result.filePath || result.summaryDocument.filePath);
    }
    await loadSummaryTree();
  } catch (error) {
    showResult("#summaryResult", error);
  } finally {
    $("#generateSummary").disabled = !currentSummaryPreview;
  }
}

async function openSummarySpreadsheet(filePath) {
  try {
    if (workbookManager.openOrActivate(filePath)) {
      return;
    }
  } catch {
    // 浏览器预览环境无法直接打开本地 WPS 文件，生成结果中会显示完整路径。
  }
  await openLocalSpreadsheetFile(filePath);
}

const rowHeightBalanceOptions = {
  topProtectedRows: 5,
  bottomProtectedRows: 6,
  protectedRows: new Set([1, 2, 3, 4, 5, 8, 30, 31, 32]),
  maxRounds: 3,
  minRoundIncrease: 0.5,
  maxRoundIncrease: 1.5,
  maxTotalIncrease: 18,
  maxRowHeight: 120,
  minRowHeight: 12,
  maxRoundShrink: 1,
  maxTotalShrink: 4,
  defaultColumnWidth: 8.43,
  defaultFontSize: 11,
  lineHeightRatio: 1.25,
  cellPadding: 4,
  cellHorizontalPadding: 6
};

const rowHeightFitModes = {
  normal: {
    maxRounds: 3,
    maxRoundIncrease: 1.5,
    maxRoundShrink: 1,
    maxTotalIncrease: 18,
    maxTotalShrink: 4,
    balancedGrowthLimit: 0.7,
    maxNetIncrease: 12
  },
  "strict-print": {
    maxRounds: 2,
    maxRoundIncrease: 1,
    maxRoundShrink: 0.8,
    maxTotalIncrease: 10,
    maxTotalShrink: 3,
    balancedGrowthLimit: 0.35,
    maxNetIncrease: 6
  }
};

const protectedRowKeywords = [
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

function openGeneratedFormModal() {
  if (!selectedTemplateNode || selectedTemplateNode.nodeType !== "template") {
    showResult("#templateResult", "请先在左侧选择一个检验批模板。");
    return;
  }

  $("#generatedFormTemplateName").textContent = selectedTemplateNode.name;
  $("#generatedFormModal").classList.remove("hidden");
}

function closeGeneratedFormModal() {
  $("#generatedFormModal").classList.add("hidden");
}

async function createGeneratedForm(event) {
  event.preventDefault();
  if (!selectedTemplateNode || selectedTemplateNode.nodeType !== "template") {
    showResult("#templateResult", "请先选择检验批模板。");
    return;
  }

  const data = Object.fromEntries(new FormData($("#generatedFormForm")).entries());
  try {
    const result = await api("/api/generated-forms", {
      method: "POST",
      body: JSON.stringify({
        projectId: activeProjectId,
        templateNodeId: selectedTemplateNode.id,
        formName: data.formName,
        fields: buildGeneratedFormFields(data)
      })
    });
    closeGeneratedFormModal();
    await loadTemplateLibraryTree();
    await loadSummaryTree();
    await openGeneratedForm(result.node);
  } catch (error) {
    showResult("#templateResult", error);
  }
}

async function createGeneratedFormDirectly(node) {
  if (!node || node.nodeType !== "template" || directGeneratedFormCreating) {
    return;
  }

  directGeneratedFormCreating = true;
  selectedTemplateNode = node;
  for (const item of $$(".specTreeNode")) {
    item.classList.toggle("selected", item.dataset.nodeId === node.id);
  }

  const form = $("#generatedFormForm");
  const data = form ? Object.fromEntries(new FormData(form).entries()) : {};
  data.formName = node.name;

  try {
    showResult("#templateResult", `正在创建资料：${node.name}`);
    const result = await api("/api/generated-forms", {
      method: "POST",
      body: JSON.stringify({
        projectId: activeProjectId,
        templateNodeId: node.id,
        formName: data.formName,
        fields: buildGeneratedFormFields(data)
      })
    });
    await loadTemplateLibraryTree();
    await loadSummaryTree();
    await openGeneratedForm(result.node);
  } catch (error) {
    showResult("#templateResult", error);
  } finally {
    directGeneratedFormCreating = false;
  }
}

function saveSpreadsheet() {
  try {
    if (window.Application?.ActiveWorkbook?.Save) {
      window.Application.ActiveWorkbook.Save();
      showResult("#templateResult", "当前 WPS 工作簿已保存。");
      return;
    }
  } catch (error) {
    showResult("#templateResult", error);
    return;
  }

  showResult("#templateResult", "当前环境无法调用 WPS 保存接口，请在 WPS 表格中使用 Ctrl+S 保存。");
}

function exportSpreadsheet() {
  showResult("#templateResult", "导出功能将基于当前 WPS 工作簿扩展。当前请先使用 WPS 的“另存为/输出为PDF”完成导出。");
}

async function fitRowHeights() {
  if (!currentGeneratedForm) {
    showResult("#templateResult", "请先选择并打开一个已创建的资料表。");
    return;
  }

  try {
    const app = getWpsApplication();
    const workbook = app.ActiveWorkbook;
    const sheet = app.ActiveSheet || workbook?.ActiveSheet;
    if (!workbook || !sheet) {
      showResult("#templateResult", "未检测到当前 WPS 工作簿，请先打开资料表。");
      return;
    }

    if (!isActiveWorkbookForCurrentForm(workbook)) {
      showResult("#templateResult", "当前活动工作簿不是所选资料表，请先打开当前资料表后再适配行高。");
      return;
    }

    if (typeof workbook.Save !== "function") {
      showResult("#templateResult", "当前环境无法调用 WPS 保存接口，无法在适配前完成安全备份。");
      return;
    }

    const mode = $("#rowHeightFitMode")?.value || "normal";
    const confirmed = window.confirm("系统将执行一键版式平衡：通过增高内容不足行、压缩富裕行的方式平衡表格版式。调整前会自动备份，是否继续？");
    if (!confirmed) {
      return;
    }

    workbook.Save();
    const backup = await api(`/api/generated-forms/${encodeURIComponent(currentGeneratedForm.id)}/backups`, { method: "POST" });
    const rangeInfo = getRowHeightTargetRange(sheet);
    const eligibleRows = buildEligibleRows(sheet, rangeInfo);
    if (eligibleRows.length === 0) {
      showResult("#templateResult", "未找到可安全适配的资料内容区域。");
      return;
    }

    const pageCountBefore = getWorksheetPageCount(sheet);
    const adjustment = applyBalancedRowHeight(sheet, rangeInfo, eligibleRows, mode);
    adjustment.backupId = backup.backupId;
    adjustment.backupPath = backup.backupPath;
    adjustment.formId = currentGeneratedForm.id;
    adjustment.workbookPath = getWorkbookPath(workbook);
    adjustment.sheetName = getSheetName(sheet);
    adjustment.pageCountBefore = pageCountBefore;
    adjustment.pageCountAfter = getWorksheetPageCount(sheet);

    if (adjustment.rows.length === 0) {
      showResult("#templateResult", {
        success: true,
        message: "当前资料内容区域无需调整行高，已完成适配前备份。",
        backupPath: backup.backupPath
      });
      return;
    }

    if (mode === "strict-print" && hasPageCountIncreased(adjustment.pageCountBefore, adjustment.pageCountAfter)) {
      restoreRowHeightAdjustment(sheet, adjustment, "originalHeight");
      const confirmed = window.confirm(
        `智能适配后打印页数可能从 ${adjustment.pageCountBefore} 页增加到 ${adjustment.pageCountAfter} 页。已先恢复本次调整，是否仍要重新应用？`
      );

      if (!confirmed) {
        lastRowHeightFitAdjustment = null;
        updateTemplateToolbarState(false);
        showResult("#templateResult", {
          success: false,
          message: "已取消智能行高适配，资料表已恢复到调整前行高。",
          backupPath: backup.backupPath
        });
        return;
      }

      restoreRowHeightAdjustment(sheet, adjustment, "targetHeight");
    }

    lastRowHeightFitAdjustment = adjustment;
    updateTemplateToolbarState(false);
    const pageWarning = hasPageCountIncreased(adjustment.pageCountBefore, adjustment.pageCountAfter)
      ? `但打印页数可能从 ${adjustment.pageCountBefore} 页增加到 ${adjustment.pageCountAfter} 页。可检查版式或点击“撤销行高适配”。`
      : "请检查版式后点击“保存”。";
    showResult("#templateResult", {
      success: true,
      message: `已完成一键版式平衡，调整 ${adjustment.rows.length} 行，${pageWarning}`,
      backupPath: backup.backupPath,
      pageCountBefore: adjustment.pageCountBefore,
      pageCountAfter: adjustment.pageCountAfter,
      unresolvedRows: adjustment.unresolvedRows
    });
  } catch (error) {
    showResult("#templateResult", error);
  }
}

function undoRowHeightFit() {
  if (!lastRowHeightFitAdjustment) {
    showResult("#templateResult", "当前没有可撤销的行高适配。");
    return;
  }

  try {
    const app = getWpsApplication();
    if (!isUndoTargetActive(app)) {
      showResult("#templateResult", "当前活动工作簿不是上次适配的资料表，无法安全撤销。");
      return;
    }

    const sheet = app.ActiveSheet || app.ActiveWorkbook?.ActiveSheet;
    restoreRowHeightAdjustment(sheet, lastRowHeightFitAdjustment, "originalHeight");
    const restoredRows = lastRowHeightFitAdjustment.rows.length;
    lastRowHeightFitAdjustment = null;
    updateTemplateToolbarState(false);
    showResult("#templateResult", `已撤销本次行高适配，恢复 ${restoredRows} 行。`);
  } catch (error) {
    showResult("#templateResult", error);
  }
}

function isUndoTargetActive(app) {
  if (!lastRowHeightFitAdjustment) {
    return false;
  }

  const activePath = normalizeLocalPath(getWorkbookPath(app.ActiveWorkbook));
  const targetPath = normalizeLocalPath(lastRowHeightFitAdjustment.workbookPath);
  const activeSheetName = getSheetName(app.ActiveSheet || app.ActiveWorkbook?.ActiveSheet);
  if (targetPath && activePath && activePath !== targetPath) {
    return false;
  }

  return !lastRowHeightFitAdjustment.sheetName || activeSheetName === lastRowHeightFitAdjustment.sheetName;
}

function getWpsApplication() {
  if (!window.Application) {
    throw new Error("当前环境无法访问 WPS 表格对象模型。");
  }

  return window.Application;
}

function getWorkbookPath(workbook) {
  return String(workbook?.FullName || workbook?.Path || "");
}

function getSheetName(sheet) {
  return String(sheet?.Name || "");
}

function isActiveWorkbookForCurrentForm(workbook) {
  const activePath = normalizeLocalPath(getWorkbookPath(workbook));
  const formPath = normalizeLocalPath(currentGeneratedForm?.generatedFilePath || "");
  if (!activePath || !formPath) {
    return true;
  }

  const activeName = activePath.split("/").pop();
  const formName = formPath.split("/").pop();
  return activePath === formPath || activeName === formName;
}

function normalizeLocalPath(value) {
  return String(value || "").replaceAll("\\", "/").toLowerCase();
}

function getRowHeightTargetRange(sheet) {
  const used = getUsedRangeInfo(sheet);
  const selection = getSelectionRangeInfo();
  if (selection && rangesIntersect(selection, used) && selection.rowCount * selection.columnCount > 1) {
    return intersectRanges(selection, used);
  }

  return {
    startRow: used.startRow,
    endRow: used.endRow,
    startColumn: used.startColumn,
    endColumn: used.endColumn,
    rowCount: used.rowCount,
    columnCount: used.columnCount,
    fromSelection: false
  };
}

function getSelectionRangeInfo() {
  try {
    const selection = window.Application?.Selection;
    if (!selection) {
      return null;
    }

    return getRangeInfo(selection, true);
  } catch {
    return null;
  }
}

function getUsedRangeInfo(sheet) {
  const used = sheet.UsedRange;
  return getRangeInfo(used, false);
}

function getRangeInfo(range, fromSelection) {
  const startRow = Number(range.Row || 1);
  const startColumn = Number(range.Column || 1);
  const rowCount = Number(range.Rows?.Count || 1);
  const columnCount = Number(range.Columns?.Count || 1);
  return {
    startRow,
    endRow: startRow + rowCount - 1,
    startColumn,
    endColumn: startColumn + columnCount - 1,
    rowCount,
    columnCount,
    fromSelection
  };
}

function rangesIntersect(left, right) {
  return left.startRow <= right.endRow &&
    left.endRow >= right.startRow &&
    left.startColumn <= right.endColumn &&
    left.endColumn >= right.startColumn;
}

function intersectRanges(left, right) {
  const startRow = Math.max(left.startRow, right.startRow);
  const endRow = Math.min(left.endRow, right.endRow);
  const startColumn = Math.max(left.startColumn, right.startColumn);
  const endColumn = Math.min(left.endColumn, right.endColumn);
  return {
    startRow,
    endRow,
    startColumn,
    endColumn,
    rowCount: endRow - startRow + 1,
    columnCount: endColumn - startColumn + 1,
    fromSelection: left.fromSelection
  };
}

function buildEligibleRows(sheet, rangeInfo) {
  const rows = [];
  for (let row = rangeInfo.startRow; row <= rangeInfo.endRow; row++) {
    if (!rangeInfo.fromSelection && isProtectedRow(sheet, row, rangeInfo)) {
      continue;
    }

    rows.push(row);
  }

  return rows;
}

function isProtectedRow(sheet, row, rangeInfo) {
  const topLimit = rangeInfo.startRow + rowHeightBalanceOptions.topProtectedRows - 1;
  const bottomLimit = rangeInfo.endRow - rowHeightBalanceOptions.bottomProtectedRows + 1;
  if (rowHeightBalanceOptions.protectedRows.has(row) || row <= topLimit || row >= bottomLimit) {
    return true;
  }

  const rowText = getRowText(sheet, row, rangeInfo);
  return protectedRowKeywords.some(keyword => rowText.includes(keyword));
}

function getRowText(sheet, row, rangeInfo) {
  const values = [];
  const endColumn = Math.min(rangeInfo.endColumn, rangeInfo.startColumn + 24);
  for (let column = rangeInfo.startColumn; column <= endColumn; column++) {
    const text = getCellText(sheet, row, column);
    if (text) {
      values.push(text);
    }
  }

  return values.join(" ");
}

function applyBalancedRowHeight(sheet, rangeInfo, eligibleRows, modeName = "normal") {
  const mode = rowHeightFitModes[modeName] || rowHeightFitModes.normal;
  const baselineHeights = captureBaselineRowHeights(sheet, eligibleRows);
  const originalHeights = new Map();
  const targetHeights = new Map();
  const eligibleSet = new Set(eligibleRows);
  let unresolvedRows = [];

  for (let round = 0; round < mode.maxRounds; round++) {
    const blocks = buildRowBlocks(eligibleRows);
    let changed = false;
    unresolvedRows = [];

    for (const block of blocks) {
      const balance = estimateBlockBalance(sheet, rangeInfo, block, eligibleSet, baselineHeights);
      if (balance.totalDeficit <= 0) {
        continue;
      }

      unresolvedRows.push(...balance.deficitRows.map(item => item.row));
      const recovered = shrinkSurplusRows(sheet, balance.surplusRows, balance.totalDeficit, originalHeights, targetHeights, mode);
      const remainingDeficit = Math.max(0, balance.totalDeficit - recovered);
      const netBudget = Math.max(0, mode.maxNetIncrease - getNetHeightIncreaseFromBaseline(sheet, eligibleRows, baselineHeights));
      const directGrowthBudget = Math.min(netBudget, recovered + remainingDeficit * 0.65);
      const sharedGrowthBudget = Math.min(
        Math.max(0, mode.maxNetIncrease - getNetHeightIncreaseFromBaseline(sheet, eligibleRows, baselineHeights)),
        remainingDeficit * 0.35
      );
      changed = recovered > 0 || changed;
      changed = growDeficitRows(sheet, balance.deficitRows, directGrowthBudget, originalHeights, targetHeights, mode) || changed;

      if (sharedGrowthBudget > 0.5) {
        changed = growWholeBlock(sheet, block, sharedGrowthBudget, originalHeights, targetHeights, mode) || changed;
      }
    }

    if (!changed || unresolvedRows.length === 0) {
      break;
    }
  }

  return {
    rows: [...targetHeights.entries()].map(([row, targetHeight]) => ({
      row,
      originalHeight: originalHeights.get(row),
      targetHeight
    })),
    unresolvedRows: [...new Set(unresolvedRows)]
  };
}

function captureBaselineRowHeights(sheet, rows) {
  const result = new Map();
  for (const row of rows) {
    result.set(row, getRowHeight(sheet, row));
  }

  return result;
}

function getNetHeightIncreaseFromBaseline(sheet, rows, baselineHeights) {
  return rows.reduce((total, row) => total + getRowHeight(sheet, row) - (baselineHeights.get(row) || getRowHeight(sheet, row)), 0);
}

function estimateBlockBalance(sheet, rangeInfo, block, eligibleSet, baselineHeights) {
  const deficitRows = [];
  const surplusRows = [];
  for (const row of block) {
    const deficit = estimateRowDeficit(sheet, rangeInfo, row, new Set(block), eligibleSet);
    if (deficit > 0) {
      deficitRows.push({ row, deficit });
      continue;
    }

    const surplus = estimateRowSurplus(sheet, rangeInfo, row, baselineHeights);
    if (surplus > 0) {
      surplusRows.push({ row, surplus });
    }
  }

  return {
    totalDeficit: deficitRows.reduce((total, item) => total + item.deficit, 0),
    deficitRows,
    surplusRows
  };
}

function shrinkSurplusRows(sheet, surplusRows, totalNeed, originalHeights, targetHeights, mode) {
  if (surplusRows.length === 0 || totalNeed <= 0) {
    return 0;
  }

  let recovered = 0;
  const totalSurplus = surplusRows.reduce((total, item) => total + item.surplus, 0);
  for (const item of surplusRows) {
    const currentHeight = getRowHeight(sheet, item.row);
    if (!originalHeights.has(item.row)) {
      originalHeights.set(item.row, currentHeight);
    }

    const originalHeight = originalHeights.get(item.row);
    const proportionalShare = totalNeed * (item.surplus / Math.max(totalSurplus, 0.1));
    const shrink = Math.min(
      item.surplus,
      proportionalShare,
      mode.maxRoundShrink,
      Math.max(0, originalHeight - rowHeightBalanceOptions.minRowHeight),
      mode.maxTotalShrink
    );
    if (shrink <= 0) {
      continue;
    }

    const nextHeight = Math.max(rowHeightBalanceOptions.minRowHeight, currentHeight - shrink);
    if (nextHeight < currentHeight) {
      setRowHeight(sheet, item.row, nextHeight);
      targetHeights.set(item.row, nextHeight);
      recovered += currentHeight - nextHeight;
    }
  }

  return recovered;
}

function growDeficitRows(sheet, deficitRows, availableHeight, originalHeights, targetHeights, mode) {
  if (deficitRows.length === 0 || availableHeight <= 0) {
    return false;
  }

  let changed = false;
  const totalDeficit = deficitRows.reduce((total, item) => total + item.deficit, 0);
  for (const item of deficitRows) {
    const currentHeight = getRowHeight(sheet, item.row);
    if (!originalHeights.has(item.row)) {
      originalHeights.set(item.row, currentHeight);
    }

    const originalHeight = originalHeights.get(item.row);
    const proportionalShare = availableHeight * (item.deficit / Math.max(totalDeficit, 0.1));
    const increase = Math.min(
      item.deficit,
      proportionalShare,
      mode.maxRoundIncrease,
      Math.max(0, mode.maxTotalIncrease - Math.max(0, currentHeight - originalHeight))
    );
    if (increase <= 0) {
      continue;
    }

    const nextHeight = Math.min(rowHeightBalanceOptions.maxRowHeight, currentHeight + increase);
    if (nextHeight > currentHeight) {
      setRowHeight(sheet, item.row, nextHeight);
      targetHeights.set(item.row, nextHeight);
      changed = true;
    }
  }

  return changed;
}

function growWholeBlock(sheet, block, remainingDeficit, originalHeights, targetHeights, mode) {
  const perRowIncrease = Math.min(mode.balancedGrowthLimit, remainingDeficit / Math.max(1, block.length));
  if (perRowIncrease <= 0) {
    return false;
  }

  let changed = false;
  for (const row of block) {
    const currentHeight = getRowHeight(sheet, row);
    if (!originalHeights.has(row)) {
      originalHeights.set(row, currentHeight);
    }

    const originalHeight = originalHeights.get(row);
    const increase = Math.min(
      perRowIncrease,
      Math.max(0, mode.maxTotalIncrease - Math.max(0, currentHeight - originalHeight))
    );
    if (increase <= 0) {
      continue;
    }

    const nextHeight = Math.min(rowHeightBalanceOptions.maxRowHeight, currentHeight + increase);
    if (nextHeight > currentHeight) {
      setRowHeight(sheet, row, nextHeight);
      targetHeights.set(row, nextHeight);
      changed = true;
    }
  }

  return changed;
}

function buildRowBlocks(rows) {
  const blocks = [];
  let current = [];
  for (const row of rows) {
    if (current.length === 0 || row === current[current.length - 1] + 1) {
      current.push(row);
      continue;
    }

    blocks.push(current);
    current = [row];
  }

  if (current.length > 0) {
    blocks.push(current);
  }

  return blocks;
}

function estimateBlockDeficit(sheet, rangeInfo, block, eligibleSet) {
  let total = 0;
  const rows = [];
  const blockSet = new Set(block);
  for (const row of block) {
    const rowDeficit = estimateRowDeficit(sheet, rangeInfo, row, blockSet, eligibleSet);
    if (rowDeficit <= 0) {
      continue;
    }

    total += rowDeficit;
    rows.push(row);
  }

  return { total, rows };
}

function estimateRowDeficit(sheet, rangeInfo, row, blockSet, eligibleSet) {
  const currentHeight = getRowHeight(sheet, row);
  let neededHeight = currentHeight;
  for (let column = rangeInfo.startColumn; column <= rangeInfo.endColumn; column++) {
    const cell = tryGetWorksheetCell(sheet, row, column);
    if (!cell) {
      continue;
    }

    const text = getCellTextFromCell(cell);
    if (!text || text.length < 10) {
      continue;
    }

    const span = getCellRowColumnSpan(sheet, row, column);
    if (span.startRow !== row || span.startColumn !== column) {
      continue;
    }

    if (span.rows > 1 && !isSpanInsideEligibleRows(span, eligibleSet)) {
      continue;
    }

    const widthPoints = Math.max(12, getCellDisplayWidthPoints(sheet, span) - rowHeightBalanceOptions.cellHorizontalPadding);
    const fontSize = getCellFontSize(cell);
    const requiredHeight = estimateRequiredTextHeight(text, widthPoints, fontSize);
    neededHeight = Math.max(neededHeight, requiredHeight / Math.max(1, span.rows));
  }

  if (!blockSet.has(row)) {
    return 0;
  }

  return Math.max(0, neededHeight - currentHeight);
}

function estimateRowSurplus(sheet, rangeInfo, row, baselineHeights) {
  const currentHeight = getRowHeight(sheet, row);
  const baselineMinimum = Math.max(rowHeightBalanceOptions.minRowHeight, Math.min(...baselineHeights.values()));
  if (currentHeight <= baselineMinimum + 0.5) {
    return 0;
  }

  let neededHeight = rowHeightBalanceOptions.minRowHeight;
  for (let column = rangeInfo.startColumn; column <= rangeInfo.endColumn; column++) {
    const cell = tryGetWorksheetCell(sheet, row, column);
    if (!cell) {
      continue;
    }

    const text = getCellTextFromCell(cell);
    if (!text) {
      continue;
    }

    if (getTextWeight(text) > 16) {
      return 0;
    }

    const span = getCellRowColumnSpan(sheet, row, column);
    if (span.startRow !== row || span.startColumn !== column) {
      continue;
    }

    const widthPoints = Math.max(12, getCellDisplayWidthPoints(sheet, span) - rowHeightBalanceOptions.cellHorizontalPadding);
    const fontSize = getCellFontSize(cell);
    neededHeight = Math.max(neededHeight, estimateRequiredTextHeight(text, widthPoints, fontSize) / Math.max(1, span.rows));
  }

  const safeMinimum = Math.max(rowHeightBalanceOptions.minRowHeight, baselineMinimum, neededHeight);
  return Math.max(0, Math.min(rowHeightBalanceOptions.maxTotalShrink, currentHeight - safeMinimum));
}

function isSpanInsideEligibleRows(span, eligibleSet) {
  for (let row = span.startRow; row <= span.endRow; row++) {
    if (!eligibleSet.has(row)) {
      return false;
    }
  }

  return true;
}

function estimateRequiredTextHeight(text, widthPoints, fontSize) {
  const estimatedLines = estimateTextLines(text, widthPoints, fontSize);
  const lineHeight = fontSize * rowHeightBalanceOptions.lineHeightRatio + 1.5;
  return estimatedLines * lineHeight + rowHeightBalanceOptions.cellPadding;
}

function estimateTextLines(text, widthPoints, fontSize) {
  return String(text)
    .split(/\r?\n/)
    .reduce((total, line) => total + Math.max(1, Math.ceil(measureTextWidthPoints(line, fontSize) / Math.max(12, widthPoints))), 0);
}

function getTextWeight(text) {
  return [...String(text)].reduce((total, char) => total + (char.charCodeAt(0) > 255 ? 2 : 1), 0);
}

function measureTextWidthPoints(text, fontSize) {
  return [...String(text)].reduce((total, char) => {
    const code = char.charCodeAt(0);
    if ((code >= 0x4e00 && code <= 0x9fff) || (code >= 0xff00 && code <= 0xffef)) {
      return total + fontSize;
    }

    if (/[A-Z]/.test(char)) {
      return total + fontSize * 0.62;
    }

    if (/[a-z0-9]/.test(char)) {
      return total + fontSize * 0.55;
    }

    if (char === " ") {
      return total + fontSize * 0.35;
    }

    return total + fontSize * 0.5;
  }, 0);
}

function getCellRowColumnSpan(sheet, row, column) {
  try {
    const cell = getWorksheetCell(sheet, row, column);
    if (!cell?.MergeCells) {
      return { rows: 1, columns: 1, startRow: row, endRow: row, startColumn: column, endColumn: column };
    }

    const area = cell.MergeArea;
    const info = getRangeInfo(area, false);
    return {
      rows: info.rowCount,
      columns: info.columnCount,
      startRow: info.startRow,
      endRow: info.endRow,
      startColumn: info.startColumn,
      endColumn: info.endColumn
    };
  } catch {
    return { rows: 1, columns: 1, startRow: row, endRow: row, startColumn: column, endColumn: column };
  }
}

function getCellDisplayWidthPoints(sheet, span) {
  let total = 0;
  for (let column = span.startColumn; column <= span.endColumn; column++) {
    total += columnWidthToPoints(getColumnWidth(sheet, column));
  }

  return total;
}

function getColumnWidth(sheet, column) {
  try {
    const columnObject = getWorksheetColumn(sheet, column);
    const width = Number(columnObject?.ColumnWidth);
    return Number.isFinite(width) && width > 0 ? width : rowHeightBalanceOptions.defaultColumnWidth;
  } catch {
    return rowHeightBalanceOptions.defaultColumnWidth;
  }
}

function columnWidthToPoints(width) {
  const pixels = width < 1 ? width * 12 : width * 7 + 5;
  return pixels * 0.75;
}

function getCellFontSize(cell) {
  try {
    const size = Number(cell?.Font?.Size);
    return Number.isFinite(size) && size > 0 ? size : rowHeightBalanceOptions.defaultFontSize;
  } catch {
    return rowHeightBalanceOptions.defaultFontSize;
  }
}

function getCellText(sheet, row, column) {
  try {
    const cell = getWorksheetCell(sheet, row, column);
    return getCellTextFromCell(cell);
  } catch {
    return "";
  }
}

function getCellTextFromCell(cell) {
  const value = cell?.Text ?? cell?.Value2 ?? cell?.Value ?? "";
  return String(value ?? "").trim();
}

function getRowHeight(sheet, row) {
  const value = Number(getWorksheetRow(sheet, row).RowHeight);
  return Number.isFinite(value) && value > 0 ? value : 15;
}

function setRowHeight(sheet, row, height) {
  getWorksheetRow(sheet, row).RowHeight = Math.round(height * 10) / 10;
}

function getWorksheetCell(sheet, row, column) {
  const cells = sheet.Cells;
  if (typeof cells === "function") {
    return cells.call(sheet, row, column);
  }

  if (typeof cells?.Item === "function") {
    return cells.Item(row, column);
  }

  if (typeof sheet.Range === "function") {
    return sheet.Range(`${toColumnName(column)}${row}`);
  }

  throw new Error("当前 WPS 环境无法访问单元格对象。");
}

function tryGetWorksheetCell(sheet, row, column) {
  try {
    return getWorksheetCell(sheet, row, column);
  } catch {
    return null;
  }
}

function getWorksheetRow(sheet, row) {
  const rows = sheet.Rows;
  if (typeof rows === "function") {
    return rows.call(sheet, row);
  }

  if (typeof rows?.Item === "function") {
    return rows.Item(row);
  }

  if (typeof sheet.Range === "function") {
    return sheet.Range(`${row}:${row}`);
  }

  throw new Error("当前 WPS 环境无法访问行对象。");
}

function getWorksheetColumn(sheet, column) {
  const columns = sheet.Columns;
  if (typeof columns === "function") {
    return columns.call(sheet, column);
  }

  if (typeof columns?.Item === "function") {
    return columns.Item(column);
  }

  if (typeof sheet.Range === "function") {
    const columnName = toColumnName(column);
    return sheet.Range(`${columnName}:${columnName}`);
  }

  throw new Error("当前 WPS 环境无法访问列对象。");
}

function toColumnName(column) {
  let value = Number(column);
  let name = "";
  while (value > 0) {
    const remainder = (value - 1) % 26;
    name = String.fromCharCode(65 + remainder) + name;
    value = Math.floor((value - 1) / 26);
  }

  return name || "A";
}

function restoreRowHeightAdjustment(sheet, adjustment, heightKey) {
  for (const item of adjustment.rows) {
    setRowHeight(sheet, item.row, item[heightKey]);
  }
}

function getWorksheetPageCount(sheet) {
  try {
    const horizontalBreaks = sheet[`H${"Page"}${"Breaks"}`];
    const verticalBreaks = sheet[`V${"Page"}${"Breaks"}`];
    const horizontalCount = Number(horizontalBreaks?.Count ?? 0);
    const verticalCount = Number(verticalBreaks?.Count ?? 0);
    if (Number.isFinite(horizontalCount) && Number.isFinite(verticalCount)) {
      return (horizontalCount + 1) * (verticalCount + 1);
    }
  } catch {
    // Page count is best-effort in WPS compatibility mode.
  }

  return null;
}

function hasPageCountIncreased(before, after) {
  return Number.isFinite(before) && Number.isFinite(after) && after > before;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

async function deleteGeneratedForm() {
  if (!currentGeneratedForm) {
    return;
  }

  if (!window.confirm(`确认删除资料表“${currentGeneratedForm.name}”？此操作会删除生成的 .xlsx 文件。`)) {
    return;
  }

  try {
    const result = await api(`/api/generated-forms/${encodeURIComponent(currentGeneratedForm.id)}`, { method: "DELETE" });
    showResult("#templateResult", result);
    await loadTemplateLibraryTree();
    await loadSummaryTree();
  } catch (error) {
    showResult("#templateResult", error);
  }
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function getMaterialFilters() {
  const form = $("#materialFilterForm");
  if (!form) {
    return { projectId: activeProjectId };
  }

  return {
    projectId: activeProjectId,
    ...Object.fromEntries(new FormData(form).entries())
  };
}

function getMaterialQuery(exportLedger = false) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(getMaterialFilters())) {
    if (value) {
      query.set(key, value);
    }
  }

  if (exportLedger) {
    query.set("export", "true");
  }

  return query.toString();
}

async function loadMaterials() {
  if (!serviceAvailable || !$("#materialRows")) {
    return;
  }

  showResult("#materialResult", "正在读取材料进场记录...");
  const result = await api(`/api/materials?${getMaterialQuery()}`);
  materialItems = result.items || [];
  if (selectedMaterial) {
    selectedMaterial = materialItems.find((item) => item.id === selectedMaterial.id) || null;
  }
  renderMaterialRows();
  renderSelectedMaterial();
  $("#materialSummary").textContent = materialItems.length
    ? `当前工程共 ${materialItems.length} 条材料进场记录。`
    : "当前筛选条件下暂无材料进场记录。";
  showResult("#materialResult", result);
}

function renderMaterialRows() {
  const rows = $("#materialRows");
  if (!rows) {
    return;
  }

  rows.innerHTML = "";
  if (!materialItems.length) {
    rows.innerHTML = '<tr><td colspan="9">暂无材料进场记录。</td></tr>';
    return;
  }

  for (const item of materialItems) {
    const tr = document.createElement("tr");
    tr.className = selectedMaterial?.id === item.id ? "selectedRow" : "";
    const values = [
      item.materialName,
      item.specificationModel,
      `${item.quantity} ${item.unit || ""}`.trim(),
      item.entryDate,
      item.usePart,
      item.supplier,
      item.testStatus,
      item.approvalStatus,
      item.status
    ];
    tr.innerHTML = values.map((value, index) => {
      const className = index >= 6 ? " class=\"statusCell\"" : "";
      return `<td${className}>${escapeHtml(value || "")}</td>`;
    }).join("");
    tr.addEventListener("click", () => selectMaterial(item.id));
    rows.appendChild(tr);
  }
}

function selectMaterial(id) {
  selectedMaterial = materialItems.find((item) => item.id === id) || null;
  renderMaterialRows();
  renderSelectedMaterial();
}

function resetMaterialForm() {
  selectedMaterial = null;
  const form = $("#materialEntryForm");
  if (form) {
    form.reset();
    form.elements.quantity.value = "0";
    form.elements.entryDate.value = new Date().toISOString().slice(0, 10);
  }
  const testForm = $("#materialTestForm");
  if (testForm) {
    testForm.reset();
  }
  renderMaterialRows();
  renderSelectedMaterial();
}

function renderSelectedMaterial() {
  const form = $("#materialEntryForm");
  if (!form) {
    return;
  }

  $("#materialFormTitle").textContent = selectedMaterial ? "编辑材料进场" : "新增材料进场";
  $("#selectedMaterialSummary").textContent = selectedMaterial
    ? `${selectedMaterial.materialName}｜${selectedMaterial.entryDate}｜${selectedMaterial.status}`
    : "尚未选择材料。";

  if (selectedMaterial) {
    form.elements.materialName.value = selectedMaterial.materialName || "";
    form.elements.specificationModel.value = selectedMaterial.specificationModel || "";
    form.elements.unit.value = selectedMaterial.unit || "";
    form.elements.quantity.value = selectedMaterial.quantity ?? 0;
    form.elements.entryDate.value = selectedMaterial.entryDate || "";
    form.elements.supplier.value = selectedMaterial.supplier || "";
    form.elements.manufacturer.value = selectedMaterial.manufacturer || "";
    form.elements.usePart.value = selectedMaterial.usePart || "";
    form.elements.batchNo.value = selectedMaterial.batchNo || "";
    form.elements.remark.value = selectedMaterial.remark || "";
    form.elements.statusOverride.value = selectedMaterial.statusOverride || "";
  }

  renderMaterialAttachments();
  renderMaterialTest();
  renderMaterialApproval();
  updateMaterialActionState();
}

function updateMaterialActionState() {
  const hasMaterial = !!selectedMaterial && serviceAvailable;
  if ($("#uploadMaterialAttachment")) $("#uploadMaterialAttachment").disabled = !hasMaterial;
  if ($("#saveMaterialTest")) $("#saveMaterialTest").disabled = !hasMaterial;
  if ($("#generateMaterialApproval")) $("#generateMaterialApproval").disabled = !hasMaterial;
}

function renderMaterialAttachments() {
  const list = $("#materialAttachmentList");
  const select = $("#materialReportAttachmentSelect");
  if (!list || !select) {
    return;
  }

  const attachments = selectedMaterial?.certificates || [];
  list.innerHTML = attachments.length
    ? attachments.map((item) => `
        <article class="attachmentItem">
          <strong>${escapeHtml(item.fileType)} ${escapeHtml(item.certificateNo || "")}</strong>
          <p>${escapeHtml(item.originalFileName)}｜${escapeHtml(item.filePath)}</p>
        </article>`).join("")
    : '<p class="emptyText">暂无附件。</p>';

  const current = selectedMaterial?.test?.reportAttachmentId || "";
  select.innerHTML = '<option value="">未关联</option>';
  for (const item of attachments.filter((attachment) => attachment.fileType.includes("报告"))) {
    const option = document.createElement("option");
    option.value = item.id;
    option.textContent = `${item.fileType} ${item.certificateNo || item.originalFileName}`;
    select.appendChild(option);
  }
  select.value = current;
}

function renderMaterialTest() {
  const form = $("#materialTestForm");
  if (!form) {
    return;
  }

  form.reset();
  const test = selectedMaterial?.test;
  if (!test) {
    return;
  }

  form.elements.isRequired.checked = !!test.isRequired;
  form.elements.samplingTime.value = toDateTimeLocal(test.samplingTime);
  form.elements.witness.value = test.witness || "";
  form.elements.sentTime.value = toDateTimeLocal(test.sentTime);
  form.elements.inspectionAgency.value = test.inspectionAgency || "";
  form.elements.reportNo.value = test.reportNo || "";
  form.elements.result.value = test.result || "";
  form.elements.reportAttachmentId.value = test.reportAttachmentId || "";
}

function renderMaterialApproval() {
  const summary = $("#materialApprovalSummary");
  if (!summary) {
    return;
  }

  if (!selectedMaterial) {
    summary.textContent = "按模板复制并填充占位符。";
    return;
  }

  summary.textContent = selectedMaterial.approval
    ? `已生成：${selectedMaterial.approval.filePath}`
    : "尚未生成材料进场报审资料。";
}

function getMaterialCertificateNo(item, keyword) {
  const attachment = (item.certificates || []).find((cert) => (cert.fileType || "").includes(keyword));
  return attachment?.certificateNo || "";
}

function materialItemToLedgerRow(item = {}) {
  return {
    id: item.id || "",
    materialName: item.materialName || "",
    specificationModel: item.specificationModel || "",
    unit: item.unit || "",
    quantity: item.quantity ?? "",
    entryDate: item.entryDate || new Date().toISOString().slice(0, 10),
    usePart: item.usePart || "",
    supplier: item.supplier || "",
    manufacturer: item.manufacturer || "",
    batchNo: item.batchNo || "",
    certificateNo: getMaterialCertificateNo(item, "合格证"),
    factoryReportNo: getMaterialCertificateNo(item, "厂家检测报告"),
    isRequired: !!item.test?.isRequired,
    sentTime: toDateOnly(item.test?.sentTime),
    inspectionAgency: item.test?.inspectionAgency || "",
    reportNo: item.test?.reportNo || "",
    result: item.test?.result || "",
    approvalStatus: item.approvalStatus || "未报审",
    status: item.status || "",
    remark: item.remark || "",
    statusOverride: item.statusOverride || "",
    _dirty: false,
    _isNew: !item.id,
    _deleted: false
  };
}

function toDateOnly(value) {
  if (!value) {
    return "";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return String(value).slice(0, 10);
  }

  return date.toISOString().slice(0, 10);
}

async function openMaterialLedgerDialog(mode = "edit") {
  materialLedgerMode = mode;
  showResult("#materialResult", "正在加载材料台账...");
  const result = await api(`/api/materials?projectId=${encodeURIComponent(activeProjectId)}`);
  materialItems = result.items || [];
  materialLedgerRows = materialItems.map(materialItemToLedgerRow);
  materialLedgerSelectedIds.clear();
  materialLedgerColumnFilters.clear();
  materialLedgerActiveFilterColumn = null;
  if (mode === "approval-select" && selectedMaterial?.id) {
    materialLedgerSelectedIds.add(selectedMaterial.id);
  }

  $("#materialLedgerDialog").classList.remove("hidden");
  resetMaterialLedgerWindowPosition();
  $("#materialLedgerDialogTitle").textContent = mode === "approval-select" ? "选择报审材料" : "材料台账管理";
  $("#materialLedgerDialogSummary").textContent = mode === "approval-select"
    ? "勾选本次需要报审的材料，确认后按模板生成材料进场报审资料。"
    : "可直接编辑、粘贴 Excel/WPS 表格内容并批量保存。";
  $("#materialLedgerAddRow").classList.toggle("hidden", mode !== "edit");
  $("#materialLedgerDeleteRows").classList.toggle("hidden", mode !== "edit");
  $("#materialLedgerSave").classList.toggle("hidden", mode !== "edit");
  $("#materialLedgerExport").classList.toggle("hidden", mode !== "edit");
  $("#materialLedgerAttachmentForm")?.classList.toggle("hidden", mode !== "edit");
  $("#materialLedgerConfirmApproval").classList.toggle("hidden", mode !== "approval-select");
  $("#materialLedgerQuickFilter").value = "";
  closeMaterialLedgerFilterMenu();
  renderMaterialLedgerGrid();
}

async function openMaterialLedgerDesktopWindow(mode = "edit") {
  if (mode !== "edit" || window.location.hash.replace("#", "") === materialLedgerWindowHash) {
    await openMaterialLedgerDialog(mode);
    return;
  }

  const ledgerUrl = new URL("material-ledger.html", window.location.href);
  ledgerUrl.searchParams.set("v", "20260525-ledger-entry");
  const url = ledgerUrl.toString();
  try {
    await api("/api/files/open-url", {
      method: "POST",
      body: JSON.stringify({ url })
    });
    showResult("#materialResult", "已打开独立材料台账窗口。");
    return;
  } catch (error) {
    console.warn("Failed to open material ledger in system window.", error);
  }

  let popup = null;
  try {
    popup = window.open(url, "material-ledger-window", "popup=yes,width=1380,height=860,resizable=yes,scrollbars=yes");
  } catch {
    popup = null;
  }

  if (popup) {
    try {
      popup.focus();
    } catch {
      // Some WPS WebViews do not allow focusing external windows.
    }
    showResult("#materialResult", "已打开独立材料台账窗口。");
    return;
  }

  showResult("#materialResult", "当前 WPS 环境阻止了独立窗口，已改用页面内台账窗口。");
  await openMaterialLedgerDialog(mode);
}

function closeMaterialLedgerDialog() {
  $("#materialLedgerDialog").classList.add("hidden");
  closeMaterialLedgerFilterMenu();
}

function requestCloseMaterialLedgerDialog() {
  if ($("#materialLedgerDialog")?.classList.contains("hidden")) {
    return;
  }

  if (hasUnsavedMaterialLedgerChanges()) {
    const discard = window.confirm("材料台账存在未保存修改，关闭后这些修改将丢失。确定放弃修改并关闭吗？");
    if (!discard) {
      return;
    }
  }

  closeMaterialLedgerDialog();
}

function hasUnsavedMaterialLedgerChanges() {
  return materialLedgerMode === "edit" && materialLedgerRows.some((row) => row._dirty || row._isNew || row._deleted);
}

function renderMaterialLedgerGrid() {
  const head = $("#materialLedgerHead");
  const body = $("#materialLedgerBody");
  if (!head || !body) {
    return;
  }

  const showSelect = materialLedgerMode === "approval-select";
  head.innerHTML = `
    <tr>
      <th class="ledgerSelectCell">选择</th>
      ${materialLedgerColumns.map(renderMaterialLedgerHeaderCell).join("")}
    </tr>`;

  const filter = ($("#materialLedgerQuickFilter")?.value || "").trim().toLowerCase();
  const rows = getVisibleMaterialLedgerRows(filter);

  body.innerHTML = "";
  if (!rows.length) {
    body.innerHTML = `<tr><td colspan="${materialLedgerColumns.length + 1}" class="emptyText">暂无材料台账数据。</td></tr>`;
  } else {
    for (const { row, index } of rows) {
      const tr = document.createElement("tr");
      tr.className = row._dirty || row._isNew ? "ledgerDirtyRow" : "";
      const selectCell = showSelect
        ? `<td class="ledgerSelectCell"><input type="checkbox" data-ledger-select="${index}" ${materialLedgerSelectedIds.has(row.id) ? "checked" : ""} ${row.id ? "" : "disabled"}></td>`
        : `<td class="ledgerSelectCell"><input type="checkbox" data-ledger-row-check="${index}"></td>`;
      tr.innerHTML = selectCell + materialLedgerColumns.map((column) => renderMaterialLedgerCell(row, index, column)).join("");
      body.appendChild(tr);
    }
  }

  const activeFilterCount = materialLedgerColumnFilters.size;
  $("#materialLedgerStatusText").textContent = materialLedgerMode === "approval-select"
    ? `已加载 ${rows.length} 条材料，已选择 ${materialLedgerSelectedIds.size} 条。`
    : `已加载 ${rows.length} 条材料${activeFilterCount ? `，${activeFilterCount} 列正在筛选` : ""}，可从 Excel/WPS 复制多行多列后粘贴。`;
  updateMaterialLedgerAttachmentState();
}

function renderMaterialLedgerHeaderCell(column) {
  const active = materialLedgerColumnFilters.has(column.key);
  return `
    <th class="${active ? "ledgerFilteredHeader" : ""}">
      <span class="ledgerHeaderLabel">${escapeHtml(column.label)}</span>
      <button class="ledgerFilterButton" type="button" data-ledger-filter="${escapeHtml(column.key)}" title="筛选 ${escapeHtml(column.label)}">${active ? "●" : "▾"}</button>
    </th>`;
}

function getVisibleMaterialLedgerRows(filter) {
  return materialLedgerRows
    .map((row, index) => ({ row, index }))
    .filter(({ row }) => !row._deleted)
    .filter(({ row }) => !filter || materialLedgerColumns.some((column) => getLedgerFilterValue(row, column).toLowerCase().includes(filter)))
    .filter(({ row }) => matchesMaterialLedgerColumnFilters(row));
}

function getLedgerFilterValue(row, column) {
  const value = formatLedgerCellValue(row[column.key], column);
  return String(value ?? "").trim() || "空白";
}

function matchesMaterialLedgerColumnFilters(row) {
  for (const [key, values] of materialLedgerColumnFilters.entries()) {
    const column = materialLedgerColumns.find((item) => item.key === key);
    if (!column || !values.has(getLedgerFilterValue(row, column))) {
      return false;
    }
  }

  return true;
}

function getMaterialLedgerUniqueValues(column) {
  return [...new Set(
    materialLedgerRows
      .filter((row) => !row._deleted)
      .map((row) => getLedgerFilterValue(row, column))
  )].sort((left, right) => left.localeCompare(right, "zh-CN", { numeric: true }));
}

function openMaterialLedgerFilterMenu(columnKey, anchor) {
  const column = materialLedgerColumns.find((item) => item.key === columnKey);
  const menu = $("#materialLedgerFilterMenu");
  const windowElement = $(".materialLedgerWindow");
  if (!column || !menu || !windowElement) {
    return;
  }

  materialLedgerActiveFilterColumn = columnKey;
  const values = getMaterialLedgerUniqueValues(column);
  const selected = materialLedgerColumnFilters.get(columnKey) ?? new Set(values);
  menu.innerHTML = `
    <div class="ledgerFilterMenuHeader">
      <strong>${escapeHtml(column.label)}</strong>
      <button type="button" data-ledger-filter-close>×</button>
    </div>
    <div class="ledgerFilterMenuActions">
      <button type="button" data-ledger-filter-all>全选</button>
      <button type="button" data-ledger-filter-empty>清空本列</button>
      <button type="button" data-ledger-filter-clear-all>清空全部筛选</button>
    </div>
    <div class="ledgerFilterValues">
      ${values.length
        ? values.map((value) => `
            <label>
              <input type="checkbox" data-ledger-filter-value value="${escapeHtml(value)}" ${selected.has(value) ? "checked" : ""}>
              <span>${escapeHtml(value)}</span>
            </label>`).join("")
        : '<p class="emptyText">该列暂无可筛选值。</p>'}
    </div>
    <div class="ledgerFilterMenuFooter">
      <button type="button" class="primary" data-ledger-filter-apply>确定</button>
    </div>`;

  const anchorRect = anchor.getBoundingClientRect();
  const windowRect = windowElement.getBoundingClientRect();
  menu.style.left = `${Math.max(8, anchorRect.left - windowRect.left - 210)}px`;
  menu.style.top = `${Math.max(48, anchorRect.bottom - windowRect.top + 4)}px`;
  menu.classList.remove("hidden");
}

function closeMaterialLedgerFilterMenu() {
  const menu = $("#materialLedgerFilterMenu");
  if (menu) {
    menu.classList.add("hidden");
    menu.innerHTML = "";
  }
  materialLedgerActiveFilterColumn = null;
}

function applyMaterialLedgerActiveFilter() {
  if (!materialLedgerActiveFilterColumn) {
    return;
  }

  const column = materialLedgerColumns.find((item) => item.key === materialLedgerActiveFilterColumn);
  const allValues = column ? getMaterialLedgerUniqueValues(column) : [];
  const checkedValues = $$("[data-ledger-filter-value]:checked").map((item) => item.value);
  if (!checkedValues.length || checkedValues.length === allValues.length) {
    materialLedgerColumnFilters.delete(materialLedgerActiveFilterColumn);
  } else {
    materialLedgerColumnFilters.set(materialLedgerActiveFilterColumn, new Set(checkedValues));
  }

  closeMaterialLedgerFilterMenu();
  renderMaterialLedgerGrid();
}

function clearMaterialLedgerFilters() {
  materialLedgerColumnFilters.clear();
  if ($("#materialLedgerQuickFilter")) {
    $("#materialLedgerQuickFilter").value = "";
  }
  closeMaterialLedgerFilterMenu();
  renderMaterialLedgerGrid();
}

function resetMaterialLedgerWindowPosition() {
  const windowElement = $(".materialLedgerWindow");
  if (!windowElement) {
    return;
  }

  materialLedgerWindowState.maximized = false;
  materialLedgerWindowState.restore = null;
  const standalone = document.body.classList.contains("ledgerStandaloneMode");
  const width = standalone ? Math.max(780, window.innerWidth - 16) : Math.min(1360, Math.max(780, window.innerWidth - 48));
  const height = standalone ? Math.max(460, window.innerHeight - 16) : Math.min(820, Math.max(460, window.innerHeight - 64));
  windowElement.style.width = `${width}px`;
  windowElement.style.height = `${height}px`;
  windowElement.style.left = `${standalone ? 8 : Math.max(12, (window.innerWidth - width) / 2)}px`;
  windowElement.style.top = `${standalone ? 8 : Math.max(12, (window.innerHeight - height) / 2)}px`;
  updateMaterialLedgerWindowButtons();
}

function maximizeMaterialLedgerWindow() {
  const windowElement = $(".materialLedgerWindow");
  if (!windowElement || materialLedgerWindowState.maximized) {
    return;
  }

  materialLedgerWindowState.restore = {
    left: windowElement.style.left,
    top: windowElement.style.top,
    width: windowElement.style.width,
    height: windowElement.style.height
  };
  materialLedgerWindowState.maximized = true;
  windowElement.style.left = "8px";
  windowElement.style.top = "8px";
  windowElement.style.width = "calc(100vw - 16px)";
  windowElement.style.height = "calc(100vh - 16px)";
  updateMaterialLedgerWindowButtons();
}

function restoreMaterialLedgerWindow() {
  const windowElement = $(".materialLedgerWindow");
  const restore = materialLedgerWindowState.restore;
  if (!windowElement || !restore) {
    return;
  }

  materialLedgerWindowState.maximized = false;
  windowElement.style.left = restore.left;
  windowElement.style.top = restore.top;
  windowElement.style.width = restore.width;
  windowElement.style.height = restore.height;
  materialLedgerWindowState.restore = null;
  updateMaterialLedgerWindowButtons();
}

function updateMaterialLedgerWindowButtons() {
  $("#materialLedgerMaximize")?.classList.toggle("hidden", materialLedgerWindowState.maximized);
  $("#materialLedgerRestore")?.classList.toggle("hidden", !materialLedgerWindowState.maximized);
}

function beginMaterialLedgerDrag(event) {
  if (event.button !== 0 || materialLedgerWindowState.maximized || event.target.closest("button")) {
    return;
  }

  const windowElement = $(".materialLedgerWindow");
  if (!windowElement) {
    return;
  }

  const rect = windowElement.getBoundingClientRect();
  materialLedgerWindowState.dragging = true;
  materialLedgerWindowState.dragOffsetX = event.clientX - rect.left;
  materialLedgerWindowState.dragOffsetY = event.clientY - rect.top;
  document.body.classList.add("ledgerDragging");
  event.preventDefault();
}

function moveMaterialLedgerWindow(event) {
  if (!materialLedgerWindowState.dragging) {
    return;
  }

  const windowElement = $(".materialLedgerWindow");
  if (!windowElement) {
    return;
  }

  const rect = windowElement.getBoundingClientRect();
  const left = Math.min(window.innerWidth - 80, Math.max(0, event.clientX - materialLedgerWindowState.dragOffsetX));
  const top = Math.min(window.innerHeight - 64, Math.max(0, event.clientY - materialLedgerWindowState.dragOffsetY));
  windowElement.style.left = `${left}px`;
  windowElement.style.top = `${top}px`;
  windowElement.style.width = `${rect.width}px`;
  windowElement.style.height = `${rect.height}px`;
}

function endMaterialLedgerDrag() {
  if (!materialLedgerWindowState.dragging) {
    return;
  }

  materialLedgerWindowState.dragging = false;
  document.body.classList.remove("ledgerDragging");
}

function renderMaterialLedgerCell(row, rowIndex, column) {
  const value = formatLedgerCellValue(row[column.key], column);
  const readonly = column.readOnly || materialLedgerMode !== "edit";
  const className = readonly ? "ledgerReadonlyCell" : "ledgerEditableCell";
  const editable = readonly ? "false" : "true";
  return `<td class="${className}" contenteditable="${editable}" data-ledger-row="${rowIndex}" data-ledger-field="${column.key}">${escapeHtml(value)}</td>`;
}

function formatLedgerCellValue(value, column) {
  if (column.type === "boolean") {
    return value ? "是" : "否";
  }

  return value ?? "";
}

function parseLedgerCellValue(value, column) {
  const text = String(value ?? "").trim();
  if (column.type === "boolean") {
    return ["是", "true", "1", "yes", "y", "需要", "需送检"].includes(text.toLowerCase());
  }

  if (column.type === "number") {
    return text === "" ? "" : Number(text);
  }

  return text;
}

function markLedgerRowDirty(rowIndex) {
  const row = materialLedgerRows[rowIndex];
  if (row) {
    row._dirty = true;
  }
}

function updateLedgerCellFromElement(cell) {
  const rowIndex = Number(cell.dataset.ledgerRow);
  const field = cell.dataset.ledgerField;
  const column = materialLedgerColumns.find((item) => item.key === field);
  const row = materialLedgerRows[rowIndex];
  if (!row || !column || column.readOnly) {
    return;
  }

  row[field] = parseLedgerCellValue(cell.textContent, column);
  markLedgerRowDirty(rowIndex);
}

function addMaterialLedgerRow() {
  materialLedgerRows.push(materialItemToLedgerRow());
  renderMaterialLedgerGrid();
}

function deleteMaterialLedgerRows() {
  const checked = $$("[data-ledger-row-check]:checked").map((item) => Number(item.dataset.ledgerRowCheck));
  const active = checked.length ? checked : getActiveLedgerRowIndexes();
  if (!active.length) {
    showResult("#materialResult", "请先在材料台账窗口中选择要删除的行。");
    return;
  }

  for (const index of [...active].sort((left, right) => right - left)) {
    const row = materialLedgerRows[index];
    if (!row) {
      continue;
    }

    if (row.id) {
      row._deleted = true;
      row._dirty = true;
    } else {
      materialLedgerRows.splice(index, 1);
    }
  }

  renderMaterialLedgerGrid();
}

function getActiveLedgerRowIndexes() {
  const active = document.activeElement;
  if (active?.dataset?.ledgerRow) {
    return [Number(active.dataset.ledgerRow)];
  }

  return [];
}

function getMaterialLedgerAttachmentTarget() {
  const checked = $$("[data-ledger-row-check]:checked").map((item) => Number(item.dataset.ledgerRowCheck));
  const indexes = checked.length ? checked : getActiveLedgerRowIndexes();
  if (indexes.length !== 1) {
    return { row: null, index: null, reason: indexes.length > 1 ? "一次只能为一条材料上传附件。" : "请先勾选一条已保存材料。" };
  }

  const index = indexes[0];
  const row = materialLedgerRows[index];
  if (!row || row._deleted) {
    return { row: null, index: null, reason: "所选材料已被删除或不存在。" };
  }

  if (!row.id) {
    return { row: null, index, reason: "该材料尚未保存，请先批量保存后再上传附件。" };
  }

  return { row, index, reason: "" };
}

function updateMaterialLedgerAttachmentState() {
  const form = $("#materialLedgerAttachmentForm");
  if (!form) {
    return;
  }

  form.classList.toggle("hidden", materialLedgerMode !== "edit");
  if (materialLedgerMode !== "edit") {
    return;
  }

  const target = getMaterialLedgerAttachmentTarget();
  const targetText = $("#materialLedgerAttachmentTarget");
  const uploadButton = $("#materialLedgerUploadAttachment");
  if (targetText) {
    targetText.textContent = target.row
      ? `当前材料：${target.row.materialName || "未命名材料"}${target.row.specificationModel ? ` / ${target.row.specificationModel}` : ""}`
      : target.reason;
  }
  if (uploadButton) {
    uploadButton.disabled = !target.row;
  }
}

function parseLedgerPasteText(text) {
  return text
    .replace(/\r\n/g, "\n")
    .replace(/\r/g, "\n")
    .split("\n")
    .filter((line, index, lines) => line.length || index < lines.length - 1)
    .map((line) => line.split("\t"));
}

function pasteIntoMaterialLedger(startRowIndex, startField, text) {
  const rows = parseLedgerPasteText(text);
  const editableColumns = materialLedgerColumns.filter((column) => !column.readOnly);
  const startColumnIndex = editableColumns.findIndex((column) => column.key === startField);
  if (startColumnIndex < 0 || !rows.length) {
    return;
  }

  for (let rowOffset = 0; rowOffset < rows.length; rowOffset++) {
    let rowIndex = startRowIndex + rowOffset;
    while (!materialLedgerRows[rowIndex]) {
      materialLedgerRows.push(materialItemToLedgerRow());
    }

    const row = materialLedgerRows[rowIndex];
    for (let columnOffset = 0; columnOffset < rows[rowOffset].length; columnOffset++) {
      const column = editableColumns[startColumnIndex + columnOffset];
      if (!column) {
        break;
      }

      row[column.key] = parseLedgerCellValue(rows[rowOffset][columnOffset], column);
    }

    row._dirty = true;
  }

  renderMaterialLedgerGrid();
}

function buildLedgerSaveRow(row) {
  return {
    id: row.id || null,
    materialName: row.materialName || "",
    specificationModel: row.specificationModel || "",
    unit: row.unit || "",
    quantity: row.quantity === "" || row.quantity === null || row.quantity === undefined ? null : Number(row.quantity),
    entryDate: row.entryDate || null,
    supplier: row.supplier || "",
    manufacturer: row.manufacturer || "",
    usePart: row.usePart || "",
    batchNo: row.batchNo || "",
    certificateNo: row.certificateNo || "",
    factoryReportNo: row.factoryReportNo || "",
    isRequired: !!row.isRequired,
    sentTime: row.sentTime ? new Date(`${row.sentTime}T00:00:00`).toISOString() : null,
    inspectionAgency: row.inspectionAgency || "",
    reportNo: row.reportNo || "",
    result: row.result || "",
    remark: row.remark || "",
    statusOverride: row.statusOverride || null,
    delete: !!row._deleted
  };
}

function validateLedgerRows() {
  const errors = [];
  materialLedgerRows.forEach((row, index) => {
    if (row._deleted || (!row.id && !row.materialName && !row.specificationModel && !row.quantity)) {
      return;
    }

    if (!row.materialName || !String(row.materialName).trim()) {
      errors.push(`第 ${index + 1} 行缺少材料名称`);
    }

    if (row.quantity !== "" && row.quantity !== null && row.quantity !== undefined && Number.isNaN(Number(row.quantity))) {
      errors.push(`第 ${index + 1} 行数量不是有效数字`);
    }
  });
  return errors;
}

async function saveMaterialLedgerRows() {
  const errors = validateLedgerRows();
  if (errors.length) {
    showResult("#materialResult", errors.join("\n"));
    return;
  }

  showResult("#materialResult", "正在批量保存材料台账...");
  try {
    const result = await api("/api/materials/batch-save", {
      method: "POST",
      body: JSON.stringify({
        projectId: activeProjectId,
        rows: materialLedgerRows.map(buildLedgerSaveRow)
      })
    });
    materialItems = result.items || [];
    materialLedgerRows = materialItems.map(materialItemToLedgerRow);
    selectedMaterial = selectedMaterial ? materialItems.find((item) => item.id === selectedMaterial.id) || null : null;
    renderMaterialRows();
    renderSelectedMaterial();
    renderMaterialLedgerGrid();
    showResult("#materialResult", result);
  } catch (error) {
    showResult("#materialResult", error);
  }
}

async function uploadMaterialLedgerAttachment(event) {
  event.preventDefault();
  const target = getMaterialLedgerAttachmentTarget();
  if (!target.row) {
    showResult("#materialResult", target.reason);
    updateMaterialLedgerAttachmentState();
    return;
  }

  const form = $("#materialLedgerAttachmentForm");
  const formData = new FormData(form);
  const file = formData.get("file");
  if (!file || !file.name) {
    showResult("#materialResult", "请选择要上传的附件文件。");
    return;
  }

  formData.set("projectId", activeProjectId);
  const fileType = String(formData.get("fileType") || "");
  const certificateNo = String(formData.get("certificateNo") || "").trim();
  showResult("#materialResult", `正在上传 ${fileType || "材料附件"}...`);

  try {
    const result = await api(`/api/materials/${encodeURIComponent(target.row.id)}/attachments`, {
      method: "POST",
      body: formData
    });

    if (fileType === "第三方检测报告") {
      const material = materialItems.find((item) => item.id === target.row.id);
      const test = material?.test || {};
      await api(`/api/materials/${encodeURIComponent(target.row.id)}/test`, {
        method: "POST",
        body: JSON.stringify({
          projectId: activeProjectId,
          isRequired: true,
          samplingTime: test.samplingTime || null,
          witness: test.witness || "",
          sentTime: target.row.sentTime ? new Date(`${target.row.sentTime}T00:00:00`).toISOString() : (test.sentTime || null),
          inspectionAgency: target.row.inspectionAgency || test.inspectionAgency || "",
          reportNo: certificateNo || target.row.reportNo || test.reportNo || "",
          result: target.row.result || test.result || "",
          reportAttachmentId: result.attachment?.id || null
        })
      });
    }

    form.reset();
    await loadMaterials();
    materialLedgerRows = materialItems.map(materialItemToLedgerRow);
    renderMaterialLedgerGrid();
    showResult("#materialResult", {
      success: true,
      message: fileType === "第三方检测报告" ? "第三方检测报告已上传并关联送检记录。" : "材料附件已上传。",
      attachment: result.attachment
    });
  } catch (error) {
    showResult("#materialResult", error);
  }
}

async function confirmMaterialApprovalSelection() {
  const ids = [...materialLedgerSelectedIds];
  if (!ids.length) {
    showResult("#materialResult", "请先勾选需要报审的材料。");
    return;
  }

  showResult("#materialResult", "正在生成材料进场报审资料...");
  try {
    const result = await api("/api/materials/approval/generate", {
      method: "POST",
      body: JSON.stringify({
        projectId: activeProjectId,
        materialEntryIds: ids
      })
    });
    await loadMaterials();
    closeMaterialLedgerDialog();
    showResult("#materialResult", result);
    const path = result.firstAbsoluteFilePath || result.firstFilePath;
    try {
      await openLocalSpreadsheetFile(path);
    } catch (openError) {
      showResult("#materialResult", {
        success: true,
        message: `${result.message} 但自动打开失败，请手动打开：${path || "未返回路径"}`,
        openError: openError.message || openError
      });
    }
  } catch (error) {
    showResult("#materialResult", error);
  }
}

function toDateTimeLocal(value) {
  if (!value) {
    return "";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  const offsetDate = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return offsetDate.toISOString().slice(0, 16);
}

function fromDateTimeLocal(value) {
  return value ? new Date(value).toISOString() : null;
}

function getMaterialEntryPayload() {
  const data = Object.fromEntries(new FormData($("#materialEntryForm")).entries());
  return {
    projectId: activeProjectId,
    materialName: data.materialName || "",
    specificationModel: data.specificationModel || "",
    unit: data.unit || "",
    quantity: Number(data.quantity || 0),
    entryDate: data.entryDate || new Date().toISOString().slice(0, 10),
    supplier: data.supplier || "",
    manufacturer: data.manufacturer || "",
    usePart: data.usePart || "",
    batchNo: data.batchNo || "",
    remark: data.remark || "",
    statusOverride: data.statusOverride || null
  };
}

async function saveMaterialEntry(event) {
  event.preventDefault();
  showResult("#materialResult", "正在保存材料进场记录...");
  try {
    const payload = getMaterialEntryPayload();
    const result = selectedMaterial
      ? await api(`/api/materials/${encodeURIComponent(selectedMaterial.id)}`, {
          method: "PUT",
          body: JSON.stringify(payload)
        })
      : await api("/api/materials", {
          method: "POST",
          body: JSON.stringify(payload)
        });
    selectedMaterial = result.item;
    showResult("#materialResult", result);
    await loadMaterials();
    selectMaterial(result.item.id);
  } catch (error) {
    showResult("#materialResult", error);
  }
}

async function uploadMaterialAttachment(event) {
  event.preventDefault();
  if (!selectedMaterial) {
    showResult("#materialResult", "请先选择或保存一条材料进场记录。");
    return;
  }

  const formData = new FormData($("#materialAttachmentForm"));
  formData.set("projectId", activeProjectId);
  showResult("#materialResult", "正在上传材料附件...");
  try {
    const result = await api(`/api/materials/${encodeURIComponent(selectedMaterial.id)}/attachments`, {
      method: "POST",
      body: formData
    });
    showResult("#materialResult", result);
    $("#materialAttachmentForm").reset();
    await loadMaterials();
    selectMaterial(selectedMaterial.id);
  } catch (error) {
    showResult("#materialResult", error);
  }
}

async function saveMaterialTest(event) {
  event.preventDefault();
  if (!selectedMaterial) {
    showResult("#materialResult", "请先选择或保存一条材料进场记录。");
    return;
  }

  const data = Object.fromEntries(new FormData($("#materialTestForm")).entries());
  const payload = {
    projectId: activeProjectId,
    isRequired: $("#materialTestForm").elements.isRequired.checked,
    samplingTime: fromDateTimeLocal(data.samplingTime),
    witness: data.witness || "",
    sentTime: fromDateTimeLocal(data.sentTime),
    inspectionAgency: data.inspectionAgency || "",
    reportNo: data.reportNo || "",
    result: data.result || "",
    reportAttachmentId: data.reportAttachmentId || null
  };

  showResult("#materialResult", "正在保存送检记录...");
  try {
    const result = await api(`/api/materials/${encodeURIComponent(selectedMaterial.id)}/test`, {
      method: "POST",
      body: JSON.stringify(payload)
    });
    showResult("#materialResult", result);
    await loadMaterials();
    selectMaterial(selectedMaterial.id);
  } catch (error) {
    showResult("#materialResult", error);
  }
}

async function generateMaterialApproval() {
  try {
    await openMaterialLedgerDialog("approval-select");
  } catch (error) {
    showResult("#materialResult", error);
  }
}

async function exportMaterialLedger() {
  showResult("#materialResult", "正在导出材料台账...");
  try {
    const result = await api("/api/materials/ledger/export", {
      method: "POST",
      body: JSON.stringify(getMaterialFilters())
    });
    showResult("#materialResult", result);
    const path = result.absoluteFilePath || result.filePath;
    try {
      await openLocalSpreadsheetFile(path);
    } catch (openError) {
      showResult("#materialResult", {
        success: true,
        message: `${result.message} 但自动打开失败，请手动打开：${path || "未返回路径"}`,
        openError: openError.message || openError
      });
    }
  } catch (error) {
    showResult("#materialResult", error);
  }
}

async function openTemplateFolder() {
  showResult("#templateResult", "正在打开模板库文件夹...");
  try {
    const result = await api("/api/templates/open-folder", { method: "POST" });
    showResult("#templateResult", result);
  } catch (error) {
    showResult("#templateResult", error);
  }
}

function openTemplatePreviewModal() {
  $("#templatePreviewModal").classList.remove("hidden");
}

function closeTemplatePreviewModal() {
  $("#templatePreviewModal").classList.add("hidden");
}

async function previewTemplateLibrary() {
  openTemplatePreviewModal();
  $("#templatePreviewSummary").textContent = "正在读取模板库...";
  $("#templateTree").innerHTML = "";

  try {
    const result = await api("/api/templates/tree");
    $("#templatePreviewSummary").textContent = `模板库：${result.rootPath}｜共 ${result.totalTemplates} 个模板`;
    renderTemplateTree(result.tree);
    showResult("#templateResult", result);
  } catch (error) {
    $("#templatePreviewSummary").textContent = "模板库预览读取失败。";
    $("#templateTree").innerHTML = '<p class="emptyText">无法读取模板库，请确认本地服务已启动。</p>';
    showResult("#templateResult", error);
  }
}

function renderTemplateTree(root) {
  const tree = $("#templateTree");
  tree.innerHTML = "";

  if (!root || !root.children || root.children.length === 0) {
    tree.innerHTML = '<p class="emptyText">模板库为空，请把 .xlsx 模板文件放入 Templates 目录。</p>';
    return;
  }

  const container = document.createElement("div");
  container.className = "templateTreeRoot";
  for (const child of root.children) {
    container.appendChild(createTemplateTreeNode(child));
  }
  tree.appendChild(container);
}

function createTemplateTreeNode(node) {
  if (node.type === "folder") {
    const details = document.createElement("details");
    details.className = "templateFolder";
    details.open = true;

    const summary = document.createElement("summary");
    const name = document.createElement("strong");
    name.textContent = node.name;
    const meta = document.createElement("span");
    meta.textContent = `${countTemplatesInNode(node)} 个模板`;
    summary.append(name, meta);
    details.appendChild(summary);

    const children = document.createElement("div");
    children.className = "templateFolderChildren";
    if (!node.children || node.children.length === 0) {
      const empty = document.createElement("p");
      empty.className = "emptyText";
      empty.textContent = "此文件夹内没有模板。";
      children.appendChild(empty);
    } else {
      for (const child of node.children) {
        children.appendChild(createTemplateTreeNode(child));
      }
    }
    details.appendChild(children);
    return details;
  }

  const item = document.createElement("article");
  item.className = "templateTreeFile";

  const content = document.createElement("div");
  const name = document.createElement("strong");
  name.textContent = node.name;
  const path = document.createElement("p");
  path.textContent = node.relativePath || node.fullPath;
  content.append(name, path);

  const badge = document.createElement("span");
  badge.className = "templateBadge";
  badge.textContent = "xlsx";

  item.append(content, badge);
  return item;
}

function countTemplatesInNode(node) {
  if (!node) {
    return 0;
  }

  if (node.type === "template") {
    return 1;
  }

  return (node.children || []).reduce((total, child) => total + countTemplatesInNode(child), 0);
}

function getKnowledgeFilters() {
  const data = Object.fromEntries(new FormData($("#knowledgeForm")).entries());
  return {
    division: data.division || "",
    subItem: data.subItem || "",
    itemType: data.itemType || ""
  };
}

function buildQueryString(params) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) {
      query.set(key, value);
    }
  }

  const text = query.toString();
  return text ? `?${text}` : "";
}

function renderKnowledgeItems(items) {
  const rows = $("#knowledgeRows");
  rows.innerHTML = "";

  for (const item of items) {
    const tr = document.createElement("tr");
    const values = [
      item.profession,
      item.division,
      item.subItem,
      item.itemType,
      item.itemName,
      item.qualifiedStandard,
      item.allowableDeviation || "",
      item.checkMethod,
      item.standardCode,
      item.standardVersion
    ];

    for (const value of values) {
      const td = document.createElement("td");
      td.textContent = value;
      tr.appendChild(td);
    }

    rows.appendChild(tr);
  }
}

async function loadKnowledgeItems() {
  showResult("#knowledgeResult", "正在查询知识库...");
  try {
    const result = await api(`/api/knowledge/items${buildQueryString(getKnowledgeFilters())}`);
    const items = result.items || [];
    $("#knowledgeSummary").textContent = `当前查询到 ${items.length} 条规范数据。`;
    renderKnowledgeItems(items);
    showResult("#knowledgeResult", result);
  } catch (error) {
    $("#knowledgeSummary").textContent = "知识库读取失败。";
    renderKnowledgeItems([]);
    showResult("#knowledgeResult", error);
  }
}

function getAiFormData() {
  const data = Object.fromEntries(new FormData($("#aiForm")).entries());
  return {
    fieldName: data.fieldName || "申请语"
  };
}

async function previewAiText() {
  const aiForm = getAiFormData();
  showResult("#aiResult", "正在生成 AI 文本预览...");
  try {
    const result = await api("/api/ai/preview", {
      method: "POST",
      body: JSON.stringify({
        fieldName: aiForm.fieldName,
        context: getFormData()
      })
    });
    $("#aiSummary").textContent = `${result.fieldName}预览已生成。`;
    showResult("#aiResult", result);
  } catch (error) {
    $("#aiSummary").textContent = "AI文本预览生成失败。";
    showResult("#aiResult", error);
  }
}

function renderSettings(settings) {
  const grid = $("#settingsGrid");
  grid.innerHTML = "";

  const rows = [
    ["服务端口", settings.servicePort],
    ["AI启用", settings.enableAI ? "已启用" : "未启用"],
    ["模块目录", settings.modulesPath],
    ["模块缓存", settings.moduleCachePath],
    ["模板目录", settings.templatePath],
    ["输出目录", settings.exportPath],
    ["知识库路径", settings.knowledgeBasePath],
    ["日志目录", settings.logPath],
    ["DeepSeek地址", settings.deepSeekBaseUrl],
    ["DeepSeek模型", settings.deepSeekModel],
    ["API Key", settings.hasDeepSeekApiKey ? "已配置" : "未配置"]
  ];

  for (const [label, value] of rows) {
    const item = document.createElement("div");
    item.className = "settingItem";

    const labelElement = document.createElement("span");
    labelElement.textContent = label;

    const valueElement = document.createElement("strong");
    valueElement.textContent = String(value);

    item.append(labelElement, valueElement);
    grid.appendChild(item);
  }
}

async function loadSettings() {
  showResult("#settingsResult", "正在读取设置...");
  try {
    const result = await api("/api/settings");
    $("#settingsSummary").textContent = "当前为只读设置，修改配置请编辑 config.json 后重启服务。";
    renderSettings(result.settings);
    showResult("#settingsResult", result);
  } catch (error) {
    $("#settingsSummary").textContent = "设置读取失败。";
    $("#settingsGrid").innerHTML = "";
    showResult("#settingsResult", error);
  }
}

function renderEnvironmentCheck(result) {
  const grid = $("#environmentGrid");
  grid.innerHTML = "";

  for (const item of result.items || []) {
    const card = document.createElement("article");
    card.className = `diagnosticCard ${item.status}`;

    const header = document.createElement("div");
    header.className = "diagnosticHeader";

    const label = document.createElement("h3");
    label.textContent = item.label;

    const badge = document.createElement("span");
    badge.className = `diagnosticBadge ${item.status}`;
    badge.textContent = item.status === "ok" ? "正常" : item.status === "warning" ? "警告" : "错误";

    header.append(label, badge);

    const detail = document.createElement("p");
    detail.textContent = item.detail;

    card.append(header, detail);
    grid.appendChild(card);
  }
}

async function runEnvironmentCheck() {
  showResult("#environmentResult", "正在执行环境自检...");
  try {
    const result = await api("/api/environment/check");
    $("#environmentSummary").textContent = `正常 ${result.okCount} 项｜警告 ${result.warningCount} 项｜错误 ${result.errorCount} 项`;
    renderEnvironmentCheck(result);
    showResult("#environmentResult", result);
  } catch (error) {
    $("#environmentSummary").textContent = "环境自检失败。";
    $("#environmentGrid").innerHTML = "";
    showResult("#environmentResult", error);
  }
}

function getBatchPlanColumns() {
  return [
    ...batchPlanBaseColumns,
    ...batchDeviceFields.map((field) => ({
      key: `device:${field.key}`,
      label: field.displayName || field.key,
      type: "number",
      width: 110
    }))
  ];
}

function createEmptyBatchPlanRow(templateNode = null) {
  return {
    id: "",
    moduleId: templateNode?.moduleId || "",
    templateItemId: templateNode?.templateItemId || 0,
    templateName: templateNode?.name || "",
    partName: "",
    capacity: "",
    quantityUnit: "",
    constructionDate: "",
    remark: "",
    deviceQuantities: {},
    status: "planned",
    errorMessage: "",
    _selected: false,
    _dirty: true
  };
}

function flattenTemplateOptions(nodes = templateTreeNodes) {
  const items = [];
  const visit = (node) => {
    if (!node) return;
    if (node.nodeType === "template" && node.moduleId && node.templateItemId) {
      items.push({
        id: `${node.moduleId}:${node.templateItemId}`,
        moduleId: node.moduleId,
        templateItemId: node.templateItemId,
        name: node.name
      });
    }
    for (const child of node.children || []) visit(child);
  };
  for (const node of nodes || []) visit(node);
  return items;
}

async function loadBatchPlans() {
  if (!serviceAvailable) {
    return;
  }

  $("#batchPlanSummary").textContent = "正在读取批量创建计划...";
  await loadBatchDeviceFields();
  const result = await api(`/api/batch-plans?projectId=${encodeURIComponent(activeProjectId)}`);
  batchPlans = result.plans || [];
  currentBatchPlan = batchPlans[0] || null;
  if (currentBatchPlan) {
    $("#batchPlanName").value = currentBatchPlan.name || "";
    $("#batchPlanRemark").value = currentBatchPlan.remark || "";
    batchPlanRows = (currentBatchPlan.items || []).map(batchPlanItemToRow);
  } else {
    $("#batchPlanName").value = `检验批划分计划-${new Date().toISOString().slice(0, 10)}`;
    $("#batchPlanRemark").value = "";
    batchPlanRows = [createEmptyBatchPlanRow()];
  }
  currentBatchPreview = null;
  renderBatchPlanTable();
  renderBatchPlanPreview(null);
  $("#batchPlanSummary").textContent = currentBatchPlan
    ? `已加载计划：${currentBatchPlan.name}，共 ${batchPlanRows.length} 行。`
    : "当前工程暂无批量计划，已创建一张空白划分表。";
  showResult("#batchPlanResult", result);
}

async function loadBatchDeviceFields(moduleId = "", templateItemId = 0) {
  const result = await api(`/api/device-fields?moduleId=${encodeURIComponent(moduleId)}&templateItemId=${encodeURIComponent(templateItemId)}`);
  batchDeviceFields = result.fields || [];
  return batchDeviceFields;
}

function batchPlanItemToRow(item) {
  return {
    id: item.id || "",
    moduleId: item.moduleId || "",
    templateItemId: item.templateItemId || 0,
    templateName: item.templateName || "",
    partName: item.partName || "",
    capacity: item.capacity || "",
    quantityUnit: item.quantityUnit || "",
    constructionDate: item.constructionDate || "",
    remark: item.remark || "",
    deviceQuantities: { ...(item.deviceQuantities || {}) },
    generatedDocumentId: item.generatedDocumentId || "",
    status: item.status || "planned",
    errorMessage: item.errorMessage || "",
    _selected: false,
    _dirty: false
  };
}

function renderBatchPlanTable() {
  const head = $("#batchPlanHead");
  const body = $("#batchPlanBody");
  if (!head || !body) {
    return;
  }

  const columns = getBatchPlanColumns();
  head.innerHTML = `
    <tr>
      <th class="batchPlanSelectCell">选择</th>
      ${columns.map((column) => `<th style="width:${column.width || 120}px">${escapeHtml(column.label)}${column.required ? '<span class="requiredMark">*</span>' : ""}</th>`).join("")}
      <th>状态</th>
      <th>错误</th>
    </tr>`;

  if (!batchPlanRows.length) {
    body.innerHTML = `<tr><td colspan="${columns.length + 3}" class="emptyText">暂无划分行。</td></tr>`;
    return;
  }

  body.innerHTML = "";
  for (let rowIndex = 0; rowIndex < batchPlanRows.length; rowIndex++) {
    const row = batchPlanRows[rowIndex];
    const tr = document.createElement("tr");
    tr.className = row._dirty ? "ledgerDirtyRow" : "";
    tr.innerHTML = `
      <td class="batchPlanSelectCell"><input type="checkbox" data-batch-row-check="${rowIndex}" ${row._selected ? "checked" : ""}></td>
      ${columns.map((column) => renderBatchPlanCell(row, rowIndex, column)).join("")}
      <td>${escapeHtml(row.status || "planned")}</td>
      <td>${escapeHtml(row.errorMessage || "")}</td>`;
    body.appendChild(tr);
  }
}

function renderBatchPlanCell(row, rowIndex, column) {
  if (column.type === "template") {
    const options = flattenTemplateOptions();
    const selectedValue = row.moduleId && row.templateItemId ? `${row.moduleId}:${row.templateItemId}` : "";
    return `
      <td>
        <select data-batch-row="${rowIndex}" data-batch-field="template">
          <option value="">请选择模板</option>
          ${options.map((option) => `<option value="${escapeHtml(option.id)}" ${option.id === selectedValue ? "selected" : ""}>${escapeHtml(option.name)}</option>`).join("")}
        </select>
      </td>`;
  }

  if (column.key.startsWith("device:")) {
    const deviceKey = column.key.slice("device:".length);
    const value = row.deviceQuantities?.[deviceKey] ?? "";
    return `<td contenteditable="true" data-batch-row="${rowIndex}" data-batch-field="${escapeHtml(column.key)}">${escapeHtml(value)}</td>`;
  }

  return `<td contenteditable="true" data-batch-row="${rowIndex}" data-batch-field="${escapeHtml(column.key)}">${escapeHtml(row[column.key] || "")}</td>`;
}

function updateBatchPlanCell(rowIndex, field, value) {
  const row = batchPlanRows[rowIndex];
  if (!row) {
    return;
  }

  if (field === "template") {
    const option = flattenTemplateOptions().find((item) => item.id === value);
    row.moduleId = option?.moduleId || "";
    row.templateItemId = option?.templateItemId || 0;
    row.templateName = option?.name || "";
  } else if (field.startsWith("device:")) {
    const key = field.slice("device:".length);
    const numberValue = parseNumber(value);
    if (numberValue === null) {
      delete row.deviceQuantities[key];
    } else {
      row.deviceQuantities[key] = numberValue;
    }
  } else {
    row[field] = value.trim();
  }

  row._dirty = true;
}

function parseNumber(value) {
  const text = String(value || "").trim();
  if (!text) {
    return null;
  }
  const numberValue = Number(text.replace(",", ""));
  return Number.isFinite(numberValue) ? numberValue : null;
}

function addBatchPlanRow(useSelectedTemplate = false) {
  const templateNode = useSelectedTemplate && selectedTemplateNode?.nodeType === "template"
    ? selectedTemplateNode
    : null;
  batchPlanRows.push(createEmptyBatchPlanRow(templateNode));
  renderBatchPlanTable();
}

function deleteSelectedBatchPlanRows() {
  batchPlanRows = batchPlanRows.filter((row) => !row._selected);
  if (!batchPlanRows.length) {
    batchPlanRows.push(createEmptyBatchPlanRow());
  }
  renderBatchPlanTable();
}

function applyBatchPlanBulk(field, value) {
  if (!value) {
    return;
  }
  for (const row of batchPlanRows) {
    row[field] = value;
    row._dirty = true;
  }
  renderBatchPlanTable();
}

function buildBatchPlanSaveRequest() {
  return {
    projectId: activeProjectId,
    name: $("#batchPlanName").value.trim() || "检验批划分计划",
    remark: $("#batchPlanRemark").value.trim(),
    items: batchPlanRows.map((row) => ({
      id: row.id || null,
      moduleId: row.moduleId || "",
      templateItemId: Number(row.templateItemId || 0),
      templateName: row.templateName || "",
      partName: row.partName || "",
      capacity: row.capacity || "",
      quantityUnit: row.quantityUnit || "",
      constructionDate: row.constructionDate || "",
      deviceQuantities: row.deviceQuantities || {},
      status: row.status || "planned",
      errorMessage: row.errorMessage || ""
    }))
  };
}

async function saveBatchPlan() {
  const payload = buildBatchPlanSaveRequest();
  const isUpdate = Boolean(currentBatchPlan?.id);
  const result = await api(isUpdate ? `/api/batch-plans/${encodeURIComponent(currentBatchPlan.id)}` : "/api/batch-plans", {
    method: isUpdate ? "PUT" : "POST",
    body: JSON.stringify(payload)
  });
  currentBatchPlan = result.plan;
  batchPlanRows = (currentBatchPlan.items || []).map(batchPlanItemToRow);
  renderBatchPlanTable();
  $("#batchPlanSummary").textContent = `计划已保存：${currentBatchPlan.name}，共 ${batchPlanRows.length} 行。`;
  showResult("#batchPlanResult", result);
  return currentBatchPlan;
}

async function previewBatchPlan() {
  const plan = await saveBatchPlan();
  const result = await api(`/api/batch-plans/${encodeURIComponent(plan.id)}/preview`, { method: "POST" });
  currentBatchPreview = result;
  renderBatchPlanPreview(result);
  $("#batchPlanSummary").textContent = `预览完成：可生成 ${result.generatableCount} 张，阻止 ${result.blockedCount} 张。`;
  showResult("#batchPlanResult", result);
  return result;
}

function renderBatchPlanPreview(result) {
  const panel = $("#batchPlanPreviewPanel");
  if (!panel) {
    return;
  }

  if (!result) {
    panel.innerHTML = '<p class="emptyText">保存计划后点击预览，可查看将生成的资料、映射命中和警告。</p>';
    return;
  }

  panel.innerHTML = `
    <div class="batchPlanPreviewHeader">
      <strong>将生成 ${result.generatableCount} 张资料</strong>
      <span>阻止 ${result.blockedCount} 行｜总计 ${result.totalCount} 行</span>
    </div>
    <div class="tableScroller">
      <table class="summaryTable">
        <thead>
          <tr><th>行</th><th>资料名称</th><th>部位</th><th>容量</th><th>施工日期</th><th>映射</th><th>提示</th></tr>
        </thead>
        <tbody>
          ${(result.rows || []).map((row) => `
            <tr>
              <td>${row.rowIndex}</td>
              <td>${escapeHtml(row.outputName)}</td>
              <td>${escapeHtml(row.partName)}</td>
              <td>${escapeHtml([row.capacity, row.quantityUnit].filter(Boolean).join(""))}</td>
              <td>${escapeHtml(row.constructionDate || "")}</td>
              <td>${escapeHtml((row.mappings || []).map((item) => `${item.deviceDisplayName}:${item.inspectionItemName}`).join("；") || "无")}</td>
              <td>${escapeHtml([...(row.errors || []), ...(row.warnings || [])].join("；"))}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>`;
}

async function generateBatchPlan() {
  const preview = await previewBatchPlan();
  if (!preview.generatableCount) {
    showResult("#batchPlanResult", "没有可生成的划分行，请先处理预览错误。");
    return;
  }

  const result = await api(`/api/batch-plans/${encodeURIComponent(currentBatchPlan.id)}/generate`, {
    method: "POST",
    body: JSON.stringify({ fields: buildGeneratedFormFields({ formName: "" }) })
  });
  showResult("#batchPlanResult", result);
  $("#batchPlanSummary").textContent = result.message || "批量创建完成。";
  await loadTemplateLibraryTree();
  await loadBatchPlans();
}

function pasteIntoBatchPlan(startRowIndex, startField, text) {
  const rows = parseLedgerPasteText(text);
  const columns = getBatchPlanColumns().filter((column) => column.type !== "template");
  const startColumnIndex = columns.findIndex((column) => column.key === startField);
  if (startColumnIndex < 0 || !rows.length) {
    return;
  }

  for (let rowOffset = 0; rowOffset < rows.length; rowOffset++) {
    const targetRowIndex = startRowIndex + rowOffset;
    while (targetRowIndex >= batchPlanRows.length) {
      batchPlanRows.push(createEmptyBatchPlanRow());
    }

    for (let columnOffset = 0; columnOffset < rows[rowOffset].length; columnOffset++) {
      const column = columns[startColumnIndex + columnOffset];
      if (!column) {
        continue;
      }
      updateBatchPlanCell(targetRowIndex, column.key, rows[rowOffset][columnOffset]);
    }
  }

  renderBatchPlanTable();
}

async function generateCurrent() {
  showResult("#generateResult", "正在生成...");
  try {
    const result = await api("/api/generate/current", {
      method: "POST",
      body: JSON.stringify(getFormData())
    });
    showResult("#generateResult", result);
  } catch (error) {
    showResult("#generateResult", error);
  }
}

function createBatchRow(values = {}) {
  const tr = document.createElement("tr");
  tr.innerHTML = `
    <td><input type="checkbox" class="batch-enabled" ${values.enabled === false ? "" : "checked"}></td>
    <td><input class="batch-type" value="${values.materialType || "检验批"}"></td>
    <td><input class="batch-date" type="date" value="${values.date || "2026-05-22"}"></td>
    <td><select class="batch-template"></select></td>
  `;
  $("#batchRows").appendChild(tr);
  refreshBatchTemplateOptions();
}

function refreshBatchTemplateOptions() {
  for (const select of $$(".batch-template")) {
    const current = select.value;
    select.innerHTML = "";
    for (const template of templates) {
      const option = document.createElement("option");
      option.value = template.name;
      option.textContent = template.name;
      select.appendChild(option);
    }
    if (current) {
      select.value = current;
    }
  }
}

async function generateBatch() {
  showResult("#batchResult", "正在批量生成...");
  const baseRequest = getFormData();
  const items = $$("#batchRows tr").map((row) => ({
    enabled: row.querySelector(".batch-enabled").checked,
    materialType: row.querySelector(".batch-type").value,
    date: row.querySelector(".batch-date").value,
    templateName: row.querySelector(".batch-template").value || baseRequest.templateName,
    baseRequest
  }));

  try {
    const result = await api("/api/generate/batch", {
      method: "POST",
      body: JSON.stringify({ items })
    });
    showResult("#batchResult", result);
  } catch (error) {
    showResult("#batchResult", error);
  }
}

async function refreshLicense() {
  try {
    const status = await api("/api/license/status");
    $("#licenseStatus").textContent = `${status.message}｜机器码Hash：${status.machineCodeHash}｜试用：${status.trialUsed}/${status.trialLimit}`;
    $("#licenseMachineCode").textContent = status.machineCodeHash;
    $("#licenseTypeText").textContent = status.licenseType;
    $("#licenseModules").textContent = (status.modules || []).join("、") || "无";
    $("#licenseExpireDate").textContent = status.expireDate || "无期限 / 试用版";
  } catch (error) {
    $("#licenseStatus").textContent = "授权状态读取失败";
    $("#licenseMachineCode").textContent = "读取失败";
    $("#licenseTypeText").textContent = "读取失败";
    $("#licenseModules").textContent = "读取失败";
    $("#licenseExpireDate").textContent = "读取失败";
    showResult("#licenseResult", error);
  }
}

async function copyMachineCode() {
  const machineCode = $("#licenseMachineCode").textContent.trim();
  if (!machineCode || machineCode === "读取中..." || machineCode === "读取失败") {
    showResult("#licenseResult", "当前没有可复制的机器码，请先刷新授权状态。");
    return;
  }

  try {
    await navigator.clipboard.writeText(machineCode);
    showResult("#licenseResult", "机器码已复制，可发送给授权方生成离线激活码。");
  } catch {
    showResult("#licenseResult", `无法自动复制，请手动复制：\n${machineCode}`);
  }
}

async function activateLicense() {
  try {
    const result = await api("/api/license/activate", {
      method: "POST",
      body: JSON.stringify({ activationCode: $("#activationCode").value.trim() })
    });
    showResult("#licenseResult", result);
    await refreshLicense();
  } catch (error) {
    showResult("#licenseResult", error);
  }
}

async function loadLogs() {
  try {
    const result = await api("/api/logs/latest");
    showResult("#logsResult", result);
  } catch (error) {
    showResult("#logsResult", error);
  }
}

function bindTabs() {
  for (const button of $$(".tab")) {
    button.addEventListener("click", () => {
      activateTab(button.dataset.tab);
    });
  }
}

function activateTab(tabId) {
  const targetPanel = $(`#${tabId}`);
  if (!targetPanel) {
    return false;
  }

  for (const panel of $$(".tabPanel")) panel.classList.remove("active");
  targetPanel.classList.add("active");
  if (window.location.hash !== `#${tabId}`) {
    window.history.replaceState(null, "", `#${tabId}`);
  }
  return true;
}

function normalizeTabId(tabId) {
  const legacyTabs = {
    batch: "batchPlan",
    "batch-plan": "batchPlan",
    ai: "generation"
  };
  return legacyTabs[tabId] || tabId;
}

function activateTabFromHash() {
  if (isMaterialLedgerStandaloneWindow()) {
    return;
  }

  const requestedTab = window.location.hash.replace("#", "") || "panel";
  if (requestedTab === "projectSelector") {
    activateTab("panel");
    setTimeout(() => openProjectSelectionDialog().catch((error) => showResult("#projectSelectionResult", error)), 0);
    return;
  }

  if (requestedTab === materialLedgerWindowHash) {
    activateTab("materials");
    return;
  }

  activateTab(normalizeTabId(requestedTab));
}

function isMaterialLedgerStandaloneWindow() {
  const params = new URLSearchParams(window.location.search);
  return params.get("view") === materialLedgerWindowHash
    || window.location.hash.replace("#", "") === materialLedgerWindowHash;
}

async function bootMaterialLedgerStandaloneWindow() {
  document.body.classList.add("ledgerStandaloneMode");
  await refreshStatus();
  await loadCurrentProject().catch((error) => showResult("#materialResult", error));
  await openMaterialLedgerDialog("edit");
}

function applyExternalTabSignal(message) {
  if (!message || message.type !== "engineering-docs-switch-tab" || !message.tabName) {
    return;
  }

  const version = String(message.version || "");
  if (version && version === lastExternalTabVersion) {
    return;
  }

  if (message.tabName === "projectSelector") {
    activateTab("panel");
    openProjectSelectionDialog().catch((error) => showResult("#projectSelectionResult", error));
    if (version) {
      lastExternalTabVersion = version;
    }
    return;
  }

  if (activateTab(normalizeTabId(message.tabName)) && version) {
    lastExternalTabVersion = version;
  }
}

function readPluginStorageTabSignal() {
  try {
    if (!window.Application || !window.Application.PluginStorage) {
      return;
    }

    const tabName = window.Application.PluginStorage.getItem(tabSyncKeys.targetTab);
    const version = window.Application.PluginStorage.getItem(tabSyncKeys.targetTabVersion);
    if (!tabName || !version || version === lastExternalTabVersion) {
      return;
    }

    applyExternalTabSignal({
      type: "engineering-docs-switch-tab",
      tabName,
      version
    });
  } catch {
    // Plain browser previews do not expose WPS PluginStorage.
  }
}

function bindExternalTabSwitching() {
  try {
    if (typeof BroadcastChannel === "function") {
      const channel = new BroadcastChannel(tabSyncKeys.tabSignal);
      channel.onmessage = (event) => applyExternalTabSignal(event.data);
    }
  } catch {
    // Some WPS WebViews disable BroadcastChannel.
  }

  window.addEventListener("storage", (event) => {
    if (event.key !== tabSyncKeys.tabSignal || !event.newValue) {
      return;
    }

    try {
      applyExternalTabSignal(JSON.parse(event.newValue));
    } catch {
      // Ignore malformed messages from storage.
    }
  });

  window.addEventListener("focus", readPluginStorageTabSignal);
  document.addEventListener("visibilitychange", () => {
    if (!document.hidden) {
      readPluginStorageTabSignal();
    }
  });
  window.setInterval(readPluginStorageTabSignal, 500);
  readPluginStorageTabSignal();
}

async function boot() {
  bindTabs();
  bindExternalTabSwitching();
  activateTabFromHash();
  $("#refreshStatus").addEventListener("click", refreshStatus);
  $("#retryServiceStatus").addEventListener("click", retryServiceStatus);
  $("#copyStartCommand").addEventListener("click", copyStartCommand);
  $("#openProjectSelection").addEventListener("click", () => openProjectSelectionDialog().catch((error) => showResult("#projectSelectionResult", error)));
  $("#refreshCurrentProject").addEventListener("click", loadCurrentProject);
  $("#createProject").addEventListener("click", createProject);
  $("#saveProject").addEventListener("click", saveProject);
  $("#openProject").addEventListener("click", openProject);
  $("#closeProjectSelection").addEventListener("click", closeProjectSelectionDialog);
  $("#projectSelectionPickFolder").addEventListener("click", selectProjectFolderFromDialog);
  $("#projectSelectionOpenRecent").addEventListener("click", openSelectedRecentProject);
  $("#projectSelectionRemoveRecent").addEventListener("click", removeSelectedRecentProject);
  $("#projectSelectionClearRecent").addEventListener("click", clearRecentProjects);
  $("#projectSelectionValidateRecent").addEventListener("click", validateRecentProjects);
  $("#recentProjectRows").addEventListener("click", (event) => {
    const row = event.target.closest("[data-recent-project-id]");
    if (!row) {
      return;
    }

    selectedRecentProjectId = row.dataset.recentProjectId || "";
    renderProjectSelectionDialog();
  });
  $("#recentProjectRows").addEventListener("dblclick", (event) => {
    const row = event.target.closest("[data-recent-project-id]");
    if (!row) {
      return;
    }

    selectedRecentProjectId = row.dataset.recentProjectId || "";
    openSelectedRecentProject();
  });
  $("#projectSelectionDialog").addEventListener("click", (event) => {
    if (event.target.id === "projectSelectionDialog") {
      closeProjectSelectionDialog();
    }
  });
  $("#reloadTemplates").addEventListener("click", loadTemplates);
  $("#refreshTemplateList").addEventListener("click", refreshTemplateManagement);
  $("#openTemplateFolder").addEventListener("click", openTemplateFolder);
  $("#refreshSummary").addEventListener("click", loadSummaryTree);
  $("#generateSummary").addEventListener("click", generateSummary);
  $("#refreshBatchPlans").addEventListener("click", () => loadBatchPlans().catch((error) => showResult("#batchPlanResult", error)));
  $("#batchPlanAddRow").addEventListener("click", () => addBatchPlanRow(false));
  $("#batchPlanUseSelectedTemplate").addEventListener("click", () => addBatchPlanRow(true));
  $("#batchPlanDeleteRows").addEventListener("click", deleteSelectedBatchPlanRows);
  $("#batchPlanSave").addEventListener("click", () => saveBatchPlan().catch((error) => showResult("#batchPlanResult", error)));
  $("#batchPlanPreview").addEventListener("click", () => previewBatchPlan().catch((error) => showResult("#batchPlanResult", error)));
  $("#batchPlanGenerate").addEventListener("click", () => generateBatchPlan().catch((error) => showResult("#batchPlanResult", error)));
  $("#batchPlanApplyDate").addEventListener("click", () => applyBatchPlanBulk("constructionDate", $("#batchPlanBulkDate").value));
  $("#batchPlanApplyUnit").addEventListener("click", () => applyBatchPlanBulk("quantityUnit", $("#batchPlanBulkUnit").value));
  $("#refreshMaterials").addEventListener("click", loadMaterials);
  $("#resetMaterialForm").addEventListener("click", resetMaterialForm);
  $("#openMaterialLedgerDialog").addEventListener("click", async () => {
    try {
      await openMaterialLedgerDesktopWindow("edit");
    } catch (error) {
      showResult("#materialResult", error);
    }
  });
  $("#exportMaterialLedger").addEventListener("click", exportMaterialLedger);
  $("#materialFilterForm").addEventListener("submit", (event) => {
    event.preventDefault();
    loadMaterials();
  });
  $("#materialFilterForm").addEventListener("change", loadMaterials);
  $("#materialEntryForm").addEventListener("submit", saveMaterialEntry);
  $("#materialAttachmentForm").addEventListener("submit", uploadMaterialAttachment);
  $("#materialTestForm").addEventListener("submit", saveMaterialTest);
  $("#generateMaterialApproval").addEventListener("click", generateMaterialApproval);
  $("#materialLedgerClose").addEventListener("click", requestCloseMaterialLedgerDialog);
  $("#materialLedgerCloseTop").addEventListener("click", requestCloseMaterialLedgerDialog);
  $("#materialLedgerMaximize").addEventListener("click", maximizeMaterialLedgerWindow);
  $("#materialLedgerRestore").addEventListener("click", restoreMaterialLedgerWindow);
  $("#materialLedgerAddRow").addEventListener("click", addMaterialLedgerRow);
  $("#materialLedgerDeleteRows").addEventListener("click", deleteMaterialLedgerRows);
  $("#materialLedgerSave").addEventListener("click", saveMaterialLedgerRows);
  $("#materialLedgerExport").addEventListener("click", exportMaterialLedger);
  $("#materialLedgerConfirmApproval").addEventListener("click", confirmMaterialApprovalSelection);
  $("#materialLedgerAttachmentForm").addEventListener("submit", uploadMaterialLedgerAttachment);
  $("#materialLedgerQuickFilter").addEventListener("input", renderMaterialLedgerGrid);
  $("#materialLedgerClearFilters").addEventListener("click", clearMaterialLedgerFilters);
  $("#materialLedgerDragHandle").addEventListener("mousedown", beginMaterialLedgerDrag);
  document.addEventListener("mousemove", moveMaterialLedgerWindow);
  document.addEventListener("mouseup", endMaterialLedgerDrag);
  $("#materialLedgerHead").addEventListener("click", (event) => {
    const button = event.target.closest("[data-ledger-filter]");
    if (!button) {
      return;
    }

    event.stopPropagation();
    openMaterialLedgerFilterMenu(button.dataset.ledgerFilter, button);
  });
  $("#materialLedgerFilterMenu").addEventListener("click", (event) => {
    event.stopPropagation();
    if (event.target.closest("[data-ledger-filter-close]")) {
      closeMaterialLedgerFilterMenu();
      return;
    }

    if (event.target.closest("[data-ledger-filter-all]")) {
      for (const checkbox of $$("[data-ledger-filter-value]")) {
        checkbox.checked = true;
      }
      return;
    }

    if (event.target.closest("[data-ledger-filter-empty]")) {
      for (const checkbox of $$("[data-ledger-filter-value]")) {
        checkbox.checked = false;
      }
      return;
    }

    if (event.target.closest("[data-ledger-filter-clear-all]")) {
      clearMaterialLedgerFilters();
      return;
    }

    if (event.target.closest("[data-ledger-filter-apply]")) {
      applyMaterialLedgerActiveFilter();
    }
  });
  $("#batchPlanBody").addEventListener("input", (event) => {
    const cell = event.target.closest("[data-batch-field]");
    if (!cell || cell.tagName === "SELECT") {
      return;
    }

    updateBatchPlanCell(Number(cell.dataset.batchRow), cell.dataset.batchField, cell.textContent || "");
  });
  $("#batchPlanBody").addEventListener("change", (event) => {
    const selector = event.target.closest("[data-batch-field='template']");
    if (selector) {
      updateBatchPlanCell(Number(selector.dataset.batchRow), "template", selector.value);
      renderBatchPlanTable();
      return;
    }

    const checkbox = event.target.closest("[data-batch-row-check]");
    if (checkbox) {
      const row = batchPlanRows[Number(checkbox.dataset.batchRowCheck)];
      if (row) {
        row._selected = checkbox.checked;
      }
    }
  });
  $("#batchPlanBody").addEventListener("paste", (event) => {
    const cell = event.target.closest("[data-batch-field]");
    if (!cell || cell.dataset.batchField === "template") {
      return;
    }

    const text = event.clipboardData?.getData("text/plain") || "";
    if (!text.includes("\t") && !text.includes("\n")) {
      return;
    }

    event.preventDefault();
    pasteIntoBatchPlan(Number(cell.dataset.batchRow), cell.dataset.batchField, text);
  });
  $("#materialLedgerBody").addEventListener("input", (event) => {
    if (event.target.matches("[data-ledger-field]")) {
      updateLedgerCellFromElement(event.target);
    }
  });
  $("#materialLedgerBody").addEventListener("focusin", (event) => {
    if (event.target.closest("[data-ledger-field]")) {
      updateMaterialLedgerAttachmentState();
    }
  });
  $("#materialLedgerBody").addEventListener("change", (event) => {
    if (event.target.closest("[data-ledger-row-check]")) {
      updateMaterialLedgerAttachmentState();
      return;
    }

    const approvalSelect = event.target.closest("[data-ledger-select]");
    if (approvalSelect) {
      const row = materialLedgerRows[Number(approvalSelect.dataset.ledgerSelect)];
      if (row?.id && approvalSelect.checked) {
        materialLedgerSelectedIds.add(row.id);
      } else if (row?.id) {
        materialLedgerSelectedIds.delete(row.id);
      }
      renderMaterialLedgerGrid();
    }
  });
  $("#materialLedgerBody").addEventListener("paste", (event) => {
    const cell = event.target.closest("[data-ledger-field]");
    if (!cell || materialLedgerMode !== "edit") {
      return;
    }

    const text = event.clipboardData?.getData("text/plain") || "";
    if (!text.includes("\t") && !text.includes("\n")) {
      return;
    }

    event.preventDefault();
    pasteIntoMaterialLedger(Number(cell.dataset.ledgerRow), cell.dataset.ledgerField, text);
  });
  $("#materialLedgerDialog").addEventListener("click", (event) => {
    if (event.target.id === "materialLedgerDialog") {
      requestCloseMaterialLedgerDialog();
    }
  });
  document.addEventListener("click", (event) => {
    if (!event.target.closest("#materialLedgerFilterMenu") && !event.target.closest("[data-ledger-filter]")) {
      closeMaterialLedgerFilterMenu();
    }
  });
  $("#newGeneratedForm").addEventListener("click", openGeneratedFormModal);
  $("#saveSpreadsheet").addEventListener("click", saveSpreadsheet);
  $("#exportSpreadsheet").addEventListener("click", exportSpreadsheet);
  $("#fitRowHeights").addEventListener("click", fitRowHeights);
  $("#undoRowHeightFit").addEventListener("click", undoRowHeightFit);
  $("#deleteGeneratedForm").addEventListener("click", deleteGeneratedForm);
  $("#templateSearch").addEventListener("input", renderTemplateTreeView);
  $("#generatedFormForm").addEventListener("submit", createGeneratedForm);
  $("#closeGeneratedFormModal").addEventListener("click", closeGeneratedFormModal);
  $("#cancelGeneratedForm").addEventListener("click", closeGeneratedFormModal);
  $("#generatedFormModal").addEventListener("click", (event) => {
    if (event.target.id === "generatedFormModal") {
      closeGeneratedFormModal();
    }
  });
  $("#closeTemplatePreview").addEventListener("click", closeTemplatePreviewModal);
  $("#templatePreviewModal").addEventListener("click", (event) => {
    if (event.target.id === "templatePreviewModal") {
      closeTemplatePreviewModal();
    }
  });
  window.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      closeTemplatePreviewModal();
      closeGeneratedFormModal();
      requestCloseMaterialLedgerDialog();
    }
  });
  $("#refreshKnowledge").addEventListener("click", loadKnowledgeItems);
  $("#previewAiText").addEventListener("click", previewAiText);
  $("#loadSettings").addEventListener("click", loadSettings);
  $("#rescanModules").addEventListener("click", async () => {
    try {
      await rescanModules();
      await loadTemplateLibraryTree();
      await loadSummaryTree();
      await loadBatchPlans();
    } catch (error) {
      $("#moduleSummary").textContent = "模块刷新失败。";
      showResult("#moduleResult", error);
    }
  });
  $("#runEnvironmentCheck").addEventListener("click", runEnvironmentCheck);
  $("#generateCurrent").addEventListener("click", generateCurrent);
  $("#addBatchRow").addEventListener("click", () => createBatchRow());
  $("#generateBatch").addEventListener("click", generateBatch);
  $("#refreshLicense").addEventListener("click", refreshLicense);
  $("#copyMachineCode").addEventListener("click", copyMachineCode);
  $("#activateLicense").addEventListener("click", activateLicense);
  $("#loadLogs").addEventListener("click", loadLogs);

  if (isMaterialLedgerStandaloneWindow()) {
    try {
      await bootMaterialLedgerStandaloneWindow();
    } catch (error) {
      showResult("#materialResult", error);
    }
    return;
  }

  createBatchRow();
  createBatchRow({ date: "2026-05-23" });
  const online = await refreshStatus();
  if (online || serviceAvailable) {
    await loadTemplates().catch((error) => showResult("#generateResult", error));
    await loadModules().catch((error) => showResult("#templateResult", error));
    await loadCurrentProject().catch((error) => showResult("#projectManagerResult", error));
    await loadRecentProjects().catch((error) => showResult("#projectSelectionResult", error));
    await loadTemplateLibraryTree().catch((error) => showResult("#templateResult", error));
    await loadSummaryTree().catch((error) => showResult("#summaryResult", error));
    await loadBatchPlans().catch((error) => showResult("#batchPlanResult", error));
    await loadMaterials().catch((error) => showResult("#materialResult", error));
    await loadKnowledgeItems().catch((error) => showResult("#knowledgeResult", error));
    await loadSettings().catch((error) => showResult("#settingsResult", error));
    await runEnvironmentCheck().catch((error) => showResult("#environmentResult", error));
    await refreshLicense();
  }
}

window.addEventListener("hashchange", activateTabFromHash);
boot();
