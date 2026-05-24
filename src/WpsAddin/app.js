const serviceBaseUrl = "http://127.0.0.1:5188";
const serviceStartCommand = '正式安装包：重新运行 Client\\1-Install-Client.cmd；开发调试：cd "D:\\YY\\编程\\工程资料制作"; dotnet run --project "src/GeneratorService"';
let templates = [];
let modules = [];
let templateTreeNodes = [];
let selectedTemplateNode = null;
let currentGeneratedForm = null;
let lastRowHeightFitAdjustment = null;
let serviceAvailable = false;
let lastExternalTabVersion = "";
const collapsedTemplateNodeIds = new Set();
let activeProjectId = "project-default";
let currentProject = null;

const tabSyncKeys = {
  targetTab: "engineering_docs_target_tab",
  targetTabVersion: "engineering_docs_target_tab_version",
  tabSignal: "engineering_docs_tab_signal"
};

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];

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
    renderCurrentProject(result.project);
    showResult("#projectManagerResult", result);
    await loadTemplateLibraryTree();
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
    renderCurrentProject(result.project);
    showResult("#projectManagerResult", result);
    await loadTemplateLibraryTree();
  } catch (error) {
    showResult("#projectManagerResult", error);
  }
}

async function openProject() {
  try {
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
    renderCurrentProject(result.project);
    showResult("#projectManagerResult", result);
    await loadTemplateLibraryTree();
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

async function api(path, options = {}) {
  let response;
  try {
    response = await fetch(`${serviceBaseUrl}${path}`, {
      ...options,
      headers: {
        "Content-Type": "application/json",
        ...(options.headers || {})
      }
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
  status.textContent = `服务正常｜版本 ${data.version}｜AI ${data.aiEnabled ? "已启用" : "未启用"}`;
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
  await loadTemplateLibraryTree().catch((error) => showResult("#templateResult", error));
  await loadKnowledgeItems().catch((error) => showResult("#knowledgeResult", error));
  await loadSettings().catch((error) => showResult("#settingsResult", error));
  await runEnvironmentCheck().catch((error) => showResult("#environmentResult", error));
  await refreshLicense();
}

function setGenerateDisabled(disabled) {
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
  button.addEventListener("click", async () => {
    if (node.nodeType === "template" && node.children?.length > 0) {
      toggleTemplateNode(node.id);
      await selectTemplateTreeNode(node);
      renderTemplateTreeView();
      return;
    }

    await selectTemplateTreeNode(node);
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

function toggleTemplateNode(nodeId) {
  if (collapsedTemplateNodeIds.has(nodeId)) {
    collapsedTemplateNodeIds.delete(nodeId);
    return;
  }

  collapsedTemplateNodeIds.add(nodeId);
}

function resolveFolderLevelLabel(folderLevel) {
  return {
    discipline: "专业",
    division: "分部工程",
    sub_division: "子分部工程",
    sub_item: "分项工程"
  }[folderLevel] || "分类";
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
    if (window.Application && window.Application.Workbooks && typeof window.Application.Workbooks.Open === "function") {
      window.Application.Workbooks.Open(form.generatedFilePath);
      return;
    }
  } catch {
    // Fallback to local service if WPS object model is unavailable or rejects the path.
  }

  await api(`/api/generated-forms/${encodeURIComponent(form.id)}/open`, { method: "POST" });
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
  $("#fitRowHeights").disabled = !canOperateForm;
  $("#undoRowHeightFit").disabled = !canUndoRowHeightFit;
  $("#deleteGeneratedForm").disabled = !canOperateForm;
}

const rowHeightBalanceOptions = {
  topProtectedRows: 5,
  bottomProtectedRows: 6,
  maxRounds: 3,
  minRoundIncrease: 0.5,
  maxRoundIncrease: 1.5,
  maxTotalIncrease: 18,
  maxRowHeight: 120,
  lineHeight: 15,
  cellPadding: 4
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
  "页脚"
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
    await openGeneratedForm(result.node);
  } catch (error) {
    showResult("#templateResult", error);
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

    workbook.Save();
    const backup = await api(`/api/generated-forms/${encodeURIComponent(currentGeneratedForm.id)}/backups`, { method: "POST" });
    const rangeInfo = getRowHeightTargetRange(sheet);
    const eligibleRows = buildEligibleRows(sheet, rangeInfo);
    if (eligibleRows.length === 0) {
      showResult("#templateResult", "未找到可安全适配的资料内容区域。");
      return;
    }

    const pageCountBefore = getWorksheetPageCount(sheet);
    const adjustment = applyBalancedRowHeight(sheet, rangeInfo, eligibleRows);
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

    if (hasPageCountIncreased(adjustment.pageCountBefore, adjustment.pageCountAfter)) {
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
    showResult("#templateResult", {
      success: true,
      message: `已智能整体适配 ${adjustment.rows.length} 行。请检查版式后点击“保存”。`,
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
  if (row <= topLimit || row >= bottomLimit) {
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

function applyBalancedRowHeight(sheet, rangeInfo, eligibleRows) {
  const originalHeights = new Map();
  const targetHeights = new Map();
  const eligibleSet = new Set(eligibleRows);
  let unresolvedRows = [];

  for (let round = 0; round < rowHeightBalanceOptions.maxRounds; round++) {
    const blocks = buildRowBlocks(eligibleRows);
    let changed = false;
    unresolvedRows = [];

    for (const block of blocks) {
      const deficit = estimateBlockDeficit(sheet, rangeInfo, block, eligibleSet);
      if (deficit.total <= 0) {
        continue;
      }

      unresolvedRows.push(...deficit.rows);
      const perRowIncrease = clamp(
        deficit.total / block.length,
        rowHeightBalanceOptions.minRoundIncrease,
        rowHeightBalanceOptions.maxRoundIncrease
      );

      for (const row of block) {
        const currentHeight = getRowHeight(sheet, row);
        if (!originalHeights.has(row)) {
          originalHeights.set(row, currentHeight);
        }

        const originalHeight = originalHeights.get(row);
        const maxAllowedHeight = Math.min(
          rowHeightBalanceOptions.maxRowHeight,
          originalHeight + rowHeightBalanceOptions.maxTotalIncrease
        );
        const nextHeight = Math.min(maxAllowedHeight, currentHeight + perRowIncrease);
        if (nextHeight > currentHeight) {
          setRowHeight(sheet, row, nextHeight);
          targetHeights.set(row, nextHeight);
          changed = true;
        }
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
    const text = getCellText(sheet, row, column);
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

    const characterCapacity = Math.max(8, 12 * span.columns);
    const estimatedLines = estimateTextLines(text, characterCapacity);
    const requiredHeight = estimatedLines * rowHeightBalanceOptions.lineHeight + rowHeightBalanceOptions.cellPadding;
    neededHeight = Math.max(neededHeight, requiredHeight / Math.max(1, span.rows));
  }

  if (!blockSet.has(row)) {
    return 0;
  }

  return Math.max(0, neededHeight - currentHeight);
}

function isSpanInsideEligibleRows(span, eligibleSet) {
  for (let row = span.startRow; row <= span.endRow; row++) {
    if (!eligibleSet.has(row)) {
      return false;
    }
  }

  return true;
}

function estimateTextLines(text, characterCapacity) {
  return String(text)
    .split(/\r?\n/)
    .reduce((total, line) => total + Math.max(1, Math.ceil(getTextWeight(line) / characterCapacity)), 0);
}

function getTextWeight(text) {
  return [...String(text)].reduce((total, char) => total + (char.charCodeAt(0) > 255 ? 2 : 1), 0);
}

function getCellRowColumnSpan(sheet, row, column) {
  try {
    const cell = sheet.Cells(row, column);
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

function getCellText(sheet, row, column) {
  try {
    const cell = sheet.Cells(row, column);
    const value = cell?.Text ?? cell?.Value2 ?? cell?.Value ?? "";
    return String(value ?? "").trim();
  } catch {
    return "";
  }
}

function getRowHeight(sheet, row) {
  const value = Number(sheet.Rows(row).RowHeight);
  return Number.isFinite(value) && value > 0 ? value : 15;
}

function setRowHeight(sheet, row, height) {
  sheet.Rows(row).RowHeight = Math.round(height * 10) / 10;
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
    batch: "generation",
    ai: "generation"
  };
  return legacyTabs[tabId] || tabId;
}

function activateTabFromHash() {
  const requestedTab = window.location.hash.replace("#", "") || "panel";
  activateTab(normalizeTabId(requestedTab));
}

function applyExternalTabSignal(message) {
  if (!message || message.type !== "engineering-docs-switch-tab" || !message.tabName) {
    return;
  }

  const version = String(message.version || "");
  if (version && version === lastExternalTabVersion) {
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
  $("#refreshCurrentProject").addEventListener("click", loadCurrentProject);
  $("#createProject").addEventListener("click", createProject);
  $("#saveProject").addEventListener("click", saveProject);
  $("#openProject").addEventListener("click", openProject);
  $("#reloadTemplates").addEventListener("click", loadTemplates);
  $("#refreshTemplateList").addEventListener("click", refreshTemplateManagement);
  $("#openTemplateFolder").addEventListener("click", openTemplateFolder);
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
    }
  });
  $("#refreshKnowledge").addEventListener("click", loadKnowledgeItems);
  $("#previewAiText").addEventListener("click", previewAiText);
  $("#loadSettings").addEventListener("click", loadSettings);
  $("#rescanModules").addEventListener("click", async () => {
    try {
      await rescanModules();
      await loadTemplateLibraryTree();
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

  createBatchRow();
  createBatchRow({ date: "2026-05-23" });
  const online = await refreshStatus();
  if (online || serviceAvailable) {
    await loadTemplates().catch((error) => showResult("#generateResult", error));
    await loadModules().catch((error) => showResult("#templateResult", error));
    await loadCurrentProject().catch((error) => showResult("#projectManagerResult", error));
    await loadTemplateLibraryTree().catch((error) => showResult("#templateResult", error));
    await loadKnowledgeItems().catch((error) => showResult("#knowledgeResult", error));
    await loadSettings().catch((error) => showResult("#settingsResult", error));
    await runEnvironmentCheck().catch((error) => showResult("#environmentResult", error));
    await refreshLicense();
  }
}

window.addEventListener("hashchange", activateTabFromHash);
boot();
