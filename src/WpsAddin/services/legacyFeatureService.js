import { requestJson } from "../api/projectApi.js";
import {
  closeStandaloneModal,
  openStandaloneModal,
  toggleStandaloneModalMaximize
} from "./standaloneModalService.js";
import { getState, setProjectContext } from "../state/projectState.js";

function $(selector) {
  return document.querySelector(selector);
}

function $$(selector) {
  return [...document.querySelectorAll(selector)];
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

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
    target.textContent = message && Object.keys(data).length <= 2
      ? message
      : JSON.stringify(data, null, 2);
    return;
  }

  target.textContent = String(data ?? "");
}

function getProjectContext() {
  const { project, activeUnitProjectId } = getState();
  return {
    projectId: project?.projectId || "",
    unitProjectId: activeUnitProjectId || ""
  };
}

function appendContext(params = new URLSearchParams()) {
  const { projectId, unitProjectId } = getProjectContext();
  if (projectId) {
    params.set("projectId", projectId);
  }
  if (unitProjectId) {
    params.set("unitProjectId", unitProjectId);
  }
  return params;
}

function formData(selector) {
  const form = $(selector);
  return form ? Object.fromEntries(new FormData(form).entries()) : {};
}

function buildQuery(data) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(data || {})) {
    if (value !== "" && value != null) {
      params.set(key, value);
    }
  }
  return params.toString();
}

let materialItems = [];
let selectedMaterialId = "";
let unitProjects = [];
let selectedUnitProjectId = "";
let recentProjects = [];
let selectedRecentProjectId = "";
let ledgerRows = [];
const selectedLedgerIds = new Set();
let materialLedgerWindowRef = null;
let templateManagementWindowRef = null;

function getStandaloneMode() {
  try {
    return new URL(window.location.href).searchParams.get("standalone") || "";
  } catch {
    return "";
  }
}

function openManagedPopup(currentRef, url, name, features) {
  if (currentRef && !currentRef.closed) {
    try {
      currentRef.focus();
      return currentRef;
    } catch {
      // Fall through and create a new popup reference.
    }
  }

  try {
    if (typeof window.open === "function") {
      const opened = window.open(url, name, features);
      opened?.focus?.();
      return opened || null;
    }
  } catch {
    // Embedded hosts can block popup windows.
  }
  return null;
}

function renderModules(items = []) {
  const grid = $("#moduleGrid");
  const summary = $("#moduleSummary");
  if (!grid || !summary) {
    return;
  }

  if (!items.length) {
    summary.textContent = "当前没有发现 .module 模块包。";
    grid.innerHTML = '<p class="emptyText">请把 .module 文件放入 Modules 目录后点击“刷新模块”。</p>';
    return;
  }

  const validCount = items.filter((item) => item.isValid).length;
  summary.textContent = `已发现 ${items.length} 个模块，有效 ${validCount} 个。`;
  grid.innerHTML = items.map((item) => `
    <article class="moduleCard ${item.isValid ? "ok" : "error"}">
      <div class="moduleCardHeader">
        <h3>${escapeHtml(item.name || item.moduleId || "未识别模块")}</h3>
        <span class="diagnosticBadge ${item.isValid ? "ok" : "error"}">${item.isValid ? "有效" : "无效"}</span>
      </div>
      <dl class="moduleMeta">
        <dt>模块ID</dt><dd>${escapeHtml(item.moduleId || "未读取")}</dd>
        <dt>版本</dt><dd>${escapeHtml(item.version || "未读取")}</dd>
        <dt>地区</dt><dd>${escapeHtml(item.province || "未读取")}</dd>
        <dt>专业</dt><dd>${escapeHtml(item.major || "未读取")}</dd>
        <dt>年份</dt><dd>${escapeHtml(item.year || "未读取")}</dd>
        <dt>文件</dt><dd>${escapeHtml(item.packagePath || "")}</dd>
      </dl>
      ${(item.errors || []).length ? `<ul class="moduleErrors">${item.errors.map((error) => `<li>${escapeHtml(error)}</li>`).join("")}</ul>` : ""}
    </article>`).join("");
}

async function loadModules(rescan = false) {
  $("#moduleSummary") && ($("#moduleSummary").textContent = rescan ? "正在重新扫描 Modules 目录..." : "正在读取模块库...");
  const result = await requestJson(rescan ? "/api/modules/rescan" : "/api/modules", {
    method: rescan ? "POST" : "GET"
  });
  renderModules(result.modules || []);
  showResult("#moduleResult", result);
}

function getMaterialQuery() {
  return buildQuery(Object.fromEntries(appendContext(new URLSearchParams(formData("#materialFilterForm")))));
}

function fillMaterialForm(item = null) {
  const form = $("#materialEntryForm");
  if (!form) {
    return;
  }

  $("#materialFormTitle").textContent = item ? "编辑材料进场" : "新增材料进场";
  $("#selectedMaterialSummary").textContent = item
    ? `${item.materialName || ""}｜${item.specificationModel || ""}｜${item.status || ""}`
    : "尚未选择材料。";

  if (!item) {
    form.reset();
    form.elements.quantity.value = "0";
    form.elements.entryDate.value = new Date().toISOString().slice(0, 10);
    $("#materialAttachmentList").innerHTML = "";
    $("#materialReportAttachmentSelect").innerHTML = '<option value="">未关联</option>';
    return;
  }

  for (const field of ["materialName", "specificationModel", "unit", "quantity", "entryDate", "supplier", "manufacturer", "usePart", "batchNo", "remark", "statusOverride"]) {
    if (form.elements[field]) {
      form.elements[field].value = item[field] ?? "";
    }
  }

  const attachments = item.certificates || [];
  $("#materialAttachmentList").innerHTML = attachments.length
    ? attachments.map((file) => `<div class="attachmentItem">${escapeHtml(file.fileType || "附件")}｜${escapeHtml(file.certificateNo || "-")}｜${escapeHtml(file.fileName || file.filePath || "")}</div>`).join("")
    : '<p class="emptyText">暂无附件。</p>';
  $("#materialReportAttachmentSelect").innerHTML = '<option value="">未关联</option>' + attachments
    .map((file) => `<option value="${escapeHtml(file.id)}">${escapeHtml(file.fileType || "附件")}｜${escapeHtml(file.certificateNo || file.fileName || "")}</option>`)
    .join("");

  const testForm = $("#materialTestForm");
  if (testForm) {
    const test = item.test || {};
    testForm.elements.isRequired.checked = !!test.isRequired;
    testForm.elements.samplingTime.value = toDatetimeLocal(test.samplingTime);
    testForm.elements.witness.value = test.witness || "";
    testForm.elements.sentTime.value = toDatetimeLocal(test.sentTime);
    testForm.elements.inspectionAgency.value = test.inspectionAgency || "";
    testForm.elements.reportNo.value = test.reportNo || "";
    testForm.elements.result.value = test.result || "";
    testForm.elements.reportAttachmentId.value = test.reportAttachmentId || "";
  }
}

function renderMaterials() {
  const rows = $("#materialRows");
  if (!rows) {
    return;
  }

  if (!materialItems.length) {
    rows.innerHTML = '<tr><td colspan="9">暂无材料进场记录。</td></tr>';
    return;
  }

  rows.innerHTML = materialItems.map((item) => `
    <tr class="${item.id === selectedMaterialId ? "selectedRow" : ""}" data-material-id="${escapeHtml(item.id)}">
      <td>${escapeHtml(item.materialName)}</td>
      <td>${escapeHtml(item.specificationModel)}</td>
      <td>${escapeHtml(`${item.quantity ?? ""} ${item.unit || ""}`.trim())}</td>
      <td>${escapeHtml(item.entryDate)}</td>
      <td>${escapeHtml(item.usePart)}</td>
      <td>${escapeHtml(item.supplier)}</td>
      <td class="statusCell">${escapeHtml(item.testStatus)}</td>
      <td class="statusCell">${escapeHtml(item.approvalStatus)}</td>
      <td class="statusCell">${escapeHtml(item.status)}</td>
    </tr>`).join("");

  for (const row of rows.querySelectorAll("[data-material-id]")) {
    row.addEventListener("click", () => {
      selectedMaterialId = row.dataset.materialId || "";
      renderMaterials();
      fillMaterialForm(materialItems.find((item) => item.id === selectedMaterialId) || null);
    });
  }
}

async function loadMaterials() {
  if (!$("#materialRows")) {
    return;
  }

  showResult("#materialResult", "正在读取材料进场记录...");
  const result = await requestJson(`/api/materials?${getMaterialQuery()}`);
  materialItems = result.items || [];
  if (!materialItems.some((item) => item.id === selectedMaterialId)) {
    selectedMaterialId = "";
  }
  renderMaterials();
  fillMaterialForm(materialItems.find((item) => item.id === selectedMaterialId) || null);
  $("#materialSummary").textContent = materialItems.length
    ? `当前范围共 ${materialItems.length} 条材料进场记录。`
    : "当前筛选条件下暂无材料进场记录。";
  showResult("#materialResult", result);
}

function buildMaterialPayload() {
  const data = formData("#materialEntryForm");
  const { projectId, unitProjectId } = getProjectContext();
  return {
    projectId,
    unitProjectId,
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

async function saveMaterial(event) {
  event.preventDefault();
  const payload = buildMaterialPayload();
  const result = selectedMaterialId
    ? await requestJson(`/api/materials/${encodeURIComponent(selectedMaterialId)}`, {
      method: "PUT",
      body: JSON.stringify(payload)
    })
    : await requestJson("/api/materials", {
      method: "POST",
      body: JSON.stringify(payload)
    });
  selectedMaterialId = result.item?.id || selectedMaterialId;
  showResult("#materialResult", result);
  await loadMaterials();
}

async function saveMaterialTest(event) {
  event.preventDefault();
  if (!selectedMaterialId) {
    showResult("#materialResult", "请先选择一条材料记录。");
    return;
  }
  const data = formData("#materialTestForm");
  const result = await requestJson(`/api/materials/${encodeURIComponent(selectedMaterialId)}/test`, {
    method: "POST",
    body: JSON.stringify({
      projectId: getProjectContext().projectId,
      isRequired: !!$("#materialTestForm")?.elements.isRequired.checked,
      samplingTime: data.samplingTime || null,
      witness: data.witness || "",
      sentTime: data.sentTime || null,
      inspectionAgency: data.inspectionAgency || "",
      reportNo: data.reportNo || "",
      result: data.result || "",
      reportAttachmentId: data.reportAttachmentId || null
    })
  });
  showResult("#materialResult", result);
  await loadMaterials();
}

async function generateMaterialApproval() {
  if (!selectedMaterialId) {
    showResult("#materialResult", "请先选择一条材料记录。");
    return;
  }
  const result = await requestJson(`/api/materials/${encodeURIComponent(selectedMaterialId)}/approval/generate?projectId=${encodeURIComponent(getProjectContext().projectId)}`, {
    method: "POST"
  });
  $("#materialApprovalSummary").textContent = result.message || "材料报审资料已生成。";
  showResult("#materialResult", result);
}

async function exportMaterialLedger() {
  const result = await requestJson("/api/materials/ledger/export", {
    method: "POST",
    body: JSON.stringify({
      ...Object.fromEntries(appendContext(new URLSearchParams(formData("#materialFilterForm"))))
    })
  });
  showResult("#materialResult", result);
}

async function uploadMaterialAttachment(event) {
  event.preventDefault();
  if (!selectedMaterialId) {
    showResult("#materialResult", "请先选择一条材料记录。");
    return;
  }

  const form = $("#materialAttachmentForm");
  const data = new FormData(form);
  const result = await requestJson(`/api/materials/${encodeURIComponent(selectedMaterialId)}/attachments`, {
    method: "POST",
    body: data
  });
  form.reset();
  showResult("#materialResult", result);
  await loadMaterials();
}

function materialRowToLedgerSaveRow(row, shouldDelete = false) {
  return {
    id: row.id || null,
    unitProjectId: getProjectContext().unitProjectId || null,
    materialName: row.materialName || "",
    specificationModel: row.specificationModel || "",
    unit: row.unit || "",
    quantity: Number(row.quantity || 0),
    entryDate: row.entryDate || new Date().toISOString().slice(0, 10),
    supplier: row.supplier || "",
    manufacturer: row.manufacturer || "",
    usePart: row.usePart || "",
    batchNo: row.batchNo || "",
    certificateNo: row.certificateNos || "",
    factoryReportNo: row.factoryReportNo || "",
    isRequired: row.isRequired ?? null,
    sentTime: row.sentTime || null,
    inspectionAgency: row.inspectionAgency || "",
    reportNo: row.reportNo || "",
    result: row.testResult || "",
    remark: row.remark || "",
    statusOverride: null,
    delete: shouldDelete
  };
}

function renderLedgerTable() {
  const head = $("#materialLedgerHead");
  const body = $("#materialLedgerBody");
  if (!head || !body) {
    return;
  }

  const columns = [
    ["materialName", "材料名称"],
    ["specificationModel", "规格型号"],
    ["unit", "单位"],
    ["quantity", "数量"],
    ["entryDate", "进场日期"],
    ["usePart", "使用部位"],
    ["supplier", "供应商"],
    ["manufacturer", "生产厂家"],
    ["batchNo", "批号"],
    ["remark", "备注"]
  ];

  head.innerHTML = `<tr><th class="ledgerSelectCell">选择</th>${columns.map(([, label]) => `<th>${escapeHtml(label)}</th>`).join("")}</tr>`;
  body.innerHTML = ledgerRows.map((row, index) => `
    <tr data-ledger-row="${index}" class="${selectedLedgerIds.has(row.id || `row-${index}`) ? "selectedRow" : ""}">
      <td class="ledgerSelectCell"><input type="checkbox" ${selectedLedgerIds.has(row.id || `row-${index}`) ? "checked" : ""}></td>
      ${columns.map(([key]) => `<td contenteditable="true" data-ledger-field="${key}">${escapeHtml(row[key] ?? "")}</td>`).join("")}
    </tr>`).join("");

  for (const tr of body.querySelectorAll("[data-ledger-row]")) {
    const index = Number(tr.dataset.ledgerRow);
    const row = ledgerRows[index];
    const key = row.id || `row-${index}`;
    tr.querySelector("input")?.addEventListener("change", (event) => {
      if (event.target.checked) {
        selectedLedgerIds.add(key);
      } else {
        selectedLedgerIds.delete(key);
      }
      renderLedgerTable();
    });
    for (const cell of tr.querySelectorAll("[data-ledger-field]")) {
      cell.addEventListener("input", () => {
        row[cell.dataset.ledgerField] = cell.textContent.trim();
      });
    }
  }

  $("#materialLedgerStatusText").textContent = `已加载 ${ledgerRows.length} 条材料台账记录。`;
}

export async function openMaterialLedgerDialog() {
  if (getStandaloneMode() !== "material-ledger") {
    materialLedgerWindowRef = openManagedPopup(
      materialLedgerWindowRef,
      "./material-ledger.html#material-ledger-window",
      "engineering-docs-material-ledger",
      "popup=yes,width=1480,height=920,resizable=yes,scrollbars=yes"
    );
    if (materialLedgerWindowRef) {
      return;
    }
  }

  openStandaloneModal("materialLedgerDialog", {
    windowSelector: ".materialLedgerWindow",
    dragHandleSelector: "#materialLedgerDragHandle",
    closeButtonSelector: "#materialLedgerCloseTop, #materialLedgerClose",
    maximizeButtonSelector: "#materialLedgerMaximize",
    restoreButtonSelector: "#materialLedgerRestore",
    bodySelector: ".materialLedgerTableWrap"
  });
  $("#materialLedgerStatusText").textContent = "正在读取材料台账...";
  const result = await requestJson(`/api/materials/ledger?${getMaterialQuery()}`);
  ledgerRows = result.rows || [];
  selectedLedgerIds.clear();
  renderLedgerTable();
  showResult("#materialResult", result);
}

function closeMaterialLedgerDialog() {
  closeStandaloneModal("materialLedgerDialog");
}

function setMaterialLedgerMaximized(maximized) {
  toggleStandaloneModalMaximize("materialLedgerDialog", maximized);
}

function addLedgerRow() {
  ledgerRows.push({
    id: "",
    materialName: "",
    specificationModel: "",
    unit: "",
    quantity: 0,
    entryDate: new Date().toISOString().slice(0, 10),
    usePart: "",
    supplier: "",
    manufacturer: "",
    batchNo: "",
    remark: ""
  });
  renderLedgerTable();
}

async function deleteLedgerRows() {
  const rowsToDelete = ledgerRows.filter((row, index) => selectedLedgerIds.has(row.id || `row-${index}`));
  if (!rowsToDelete.length) {
    showResult("#materialResult", "请先勾选要删除的台账行。");
    return;
  }
  ledgerRows = ledgerRows.filter((row, index) => !selectedLedgerIds.has(row.id || `row-${index}`));
  const savedRows = rowsToDelete.filter((row) => row.id).map((row) => materialRowToLedgerSaveRow(row, true));
  selectedLedgerIds.clear();
  if (savedRows.length) {
    const result = await requestJson("/api/materials/batch-save", {
      method: "POST",
      body: JSON.stringify({
        ...getProjectContext(),
        rows: savedRows
      })
    });
    showResult("#materialResult", result);
    await openMaterialLedgerDialog();
    return;
  }
  renderLedgerTable();
}

async function saveLedgerRows() {
  const result = await requestJson("/api/materials/batch-save", {
    method: "POST",
    body: JSON.stringify({
      ...getProjectContext(),
      rows: ledgerRows.map((row) => materialRowToLedgerSaveRow(row, false))
    })
  });
  showResult("#materialResult", result);
  await openMaterialLedgerDialog();
  await loadMaterials();
}

function filterLedgerRows() {
  const keyword = ($("#materialLedgerQuickFilter")?.value || "").trim().toLowerCase();
  for (const tr of $$("#materialLedgerBody tr")) {
    tr.classList.toggle("hidden", keyword && !tr.textContent.toLowerCase().includes(keyword));
  }
}

function toDatetimeLocal(value) {
  if (!value) {
    return "";
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : date.toISOString().slice(0, 16);
}

function getKnowledgeFilters() {
  return formData("#knowledgeForm");
}

function renderKnowledgeItems(items = []) {
  const rows = $("#knowledgeRows");
  if (!rows) {
    return;
  }
  rows.innerHTML = items.length
    ? items.map((item) => `
      <tr>
        <td>${escapeHtml(item.profession)}</td>
        <td>${escapeHtml(item.division)}</td>
        <td>${escapeHtml(item.subItem)}</td>
        <td>${escapeHtml(item.itemType)}</td>
        <td>${escapeHtml(item.itemName)}</td>
        <td>${escapeHtml(item.qualifiedStandard)}</td>
        <td>${escapeHtml(item.allowableDeviation || "")}</td>
        <td>${escapeHtml(item.checkMethod)}</td>
        <td>${escapeHtml(item.standardCode)}</td>
        <td>${escapeHtml(item.standardVersion)}</td>
      </tr>`).join("")
    : '<tr><td colspan="10">暂无知识库记录。</td></tr>';
}

async function loadKnowledgeItems() {
  showResult("#knowledgeResult", "正在查询知识库...");
  const result = await requestJson(`/api/knowledge/items?${buildQuery(getKnowledgeFilters())}`);
  const items = result.items || [];
  $("#knowledgeSummary").textContent = `当前查询到 ${items.length} 条规范数据。`;
  renderKnowledgeItems(items);
  showResult("#knowledgeResult", result);
}

function renderSettings(settings = {}) {
  const grid = $("#settingsGrid");
  if (!grid) {
    return;
  }
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
  grid.innerHTML = rows.map(([label, value]) => `
    <div class="settingItem">
      <span>${escapeHtml(label)}</span>
      <strong>${escapeHtml(value)}</strong>
    </div>`).join("");
}

async function loadSettings() {
  showResult("#settingsResult", "正在读取设置...");
  const result = await requestJson("/api/settings");
  $("#settingsSummary").textContent = "当前为只读设置，修改配置请编辑 config.json 后重启服务。";
  renderSettings(result.settings);
  showResult("#settingsResult", result);
}

function renderEnvironmentCheck(result = {}) {
  const grid = $("#environmentGrid");
  if (!grid) {
    return;
  }
  grid.innerHTML = (result.items || []).map((item) => `
    <article class="diagnosticCard ${escapeHtml(item.status)}">
      <div class="diagnosticHeader">
        <h3>${escapeHtml(item.label)}</h3>
        <span class="diagnosticBadge ${escapeHtml(item.status)}">${item.status === "ok" ? "正常" : item.status === "warning" ? "警告" : "错误"}</span>
      </div>
      <p>${escapeHtml(item.detail)}</p>
    </article>`).join("");
}

async function runEnvironmentCheck() {
  showResult("#environmentResult", "正在执行环境自检...");
  const result = await requestJson("/api/environment/check");
  $("#environmentSummary").textContent = `正常 ${result.okCount} 项｜警告 ${result.warningCount} 项｜错误 ${result.errorCount} 项`;
  renderEnvironmentCheck(result);
  showResult("#environmentResult", result);
}

function renderLicense(status = {}) {
  $("#licenseStatus").textContent = status.message || (status.activated ? "授权有效" : "试用授权");
  $("#licenseMachineCode").textContent = status.machineCodeHash || "-";
  $("#licenseTypeText").textContent = status.licenseType || "-";
  $("#licenseModules").textContent = (status.modules || []).join("、") || "-";
  $("#licenseExpireDate").textContent = status.expireDate || "未设置";
}

async function refreshLicense() {
  const result = await requestJson("/api/license/status");
  renderLicense(result);
  showResult("#licenseResult", result);
}

async function activateLicense() {
  const activationCode = $("#activationCode")?.value.trim() || "";
  const result = await requestJson("/api/license/activate", {
    method: "POST",
    body: JSON.stringify({ activationCode })
  });
  renderLicense(result.status || {});
  showResult("#licenseResult", result);
}

async function copyMachineCode() {
  const code = $("#licenseMachineCode")?.textContent || "";
  await navigator.clipboard?.writeText(code);
  showResult("#licenseResult", "机器码已复制。");
}

async function loadLogs() {
  const result = await requestJson("/api/logs/latest");
  showResult("#logsResult", result);
}

function renderUnitProjects() {
  const body = $("#unitProjectRows");
  if (!body) {
    return;
  }
  const currentId = getState().activeUnitProjectId || "";
  body.innerHTML = unitProjects.length
    ? unitProjects.map((item) => `
      <tr data-unit-project-id="${escapeHtml(item.id)}" class="${item.id === selectedUnitProjectId ? "selectedRow" : ""} ${item.id === currentId ? "currentProjectRow" : ""}">
        <td>${escapeHtml(item.unitProjectName || "未命名单位工程")}</td>
        <td>${escapeHtml(item.unitProjectCode || "-")}</td>
        <td>${escapeHtml(item.defaultModule || "-")}</td>
        <td>${escapeHtml(item.documentCount ?? 0)}</td>
        <td>${escapeHtml(item.materialCount ?? 0)}</td>
        <td><span class="projectStatusBadge ${item.status === "active" ? "ok" : "warning"}">${escapeHtml(item.status || "")}</span></td>
      </tr>`).join("")
    : '<tr><td colspan="6">暂无单位工程。</td></tr>';

  for (const row of body.querySelectorAll("[data-unit-project-id]")) {
    row.addEventListener("click", () => {
      selectedUnitProjectId = row.dataset.unitProjectId || "";
      fillUnitProjectForm(unitProjects.find((item) => item.id === selectedUnitProjectId) || null);
      renderUnitProjects();
    });
  }
}

function fillUnitProjectForm(unit = null) {
  const form = $("#unitProjectForm");
  if (!form) {
    return;
  }
  $("#unitProjectFormTitle").textContent = unit ? "编辑单位工程" : "新增单位工程";
  $("#selectedUnitProjectSummary").textContent = unit
    ? `${unit.unitProjectName || ""}｜资料 ${unit.documentCount ?? 0}｜材料 ${unit.materialCount ?? 0}`
    : "尚未选择单位工程。";
  for (const field of ["unitProjectName", "unitProjectCode", "constructionUnit", "supervisionUnit", "designUnit", "surveyUnit", "buildingArea", "structureType", "floors", "startDate", "completionDate", "defaultModule", "templateVersion"]) {
    if (form.elements[field]) {
      form.elements[field].value = unit?.[field] || "";
    }
  }
}

async function loadUnitProjects() {
  const result = await requestJson(`/api/unit-projects?${buildQuery({ projectId: getProjectContext().projectId })}`);
  unitProjects = result.items || [];
  selectedUnitProjectId = selectedUnitProjectId || result.currentUnitProjectId || unitProjects[0]?.id || "";
  renderUnitProjects();
  fillUnitProjectForm(unitProjects.find((item) => item.id === selectedUnitProjectId) || null);
  showResult("#unitProjectResult", result);
}

async function openUnitProjectDialog() {
  $("#unitProjectDialog")?.classList.remove("hidden");
  await loadUnitProjects();
}

function closeUnitProjectDialog() {
  $("#unitProjectDialog")?.classList.add("hidden");
}

function resetUnitProjectForm() {
  selectedUnitProjectId = "";
  fillUnitProjectForm(null);
  renderUnitProjects();
}

async function saveUnitProject(event) {
  event.preventDefault();
  const data = formData("#unitProjectForm");
  const payload = {
    ...getProjectContext(),
    ...data,
    copyFromUnitProjectId: null
  };
  const result = selectedUnitProjectId
    ? await requestJson(`/api/unit-projects/${encodeURIComponent(selectedUnitProjectId)}`, {
      method: "PUT",
      body: JSON.stringify(payload)
    })
    : await requestJson("/api/unit-projects", {
      method: "POST",
      body: JSON.stringify(payload)
    });
  selectedUnitProjectId = result.unitProject?.id || selectedUnitProjectId;
  showResult("#unitProjectResult", result);
  await loadUnitProjects();
  window.dispatchEvent(new CustomEvent("engineering-docs-project-context-refresh"));
}

async function setCurrentUnitProject() {
  if (!selectedUnitProjectId) {
    showResult("#unitProjectResult", "请先选择单位工程。");
    return;
  }
  const result = await requestJson("/api/unit-projects/current", {
    method: "POST",
    body: JSON.stringify({
      projectId: getProjectContext().projectId,
      unitProjectId: selectedUnitProjectId
    })
  });
  showResult("#unitProjectResult", result);
  window.dispatchEvent(new CustomEvent("engineering-docs-project-context-refresh"));
  await loadUnitProjects();
}

async function deactivateUnitProject() {
  if (!selectedUnitProjectId) {
    showResult("#unitProjectResult", "请先选择单位工程。");
    return;
  }
  const result = await requestJson(`/api/unit-projects/${encodeURIComponent(selectedUnitProjectId)}?${buildQuery({ projectId: getProjectContext().projectId })}`, {
    method: "DELETE"
  });
  selectedUnitProjectId = result.currentUnitProjectId || "";
  showResult("#unitProjectResult", result);
  window.dispatchEvent(new CustomEvent("engineering-docs-project-context-refresh"));
  await loadUnitProjects();
}

function renderRecentProjects() {
  const body = $("#recentProjectRows");
  if (!body) {
    return;
  }
  body.innerHTML = recentProjects.length
    ? recentProjects.map((item) => `
      <tr data-recent-project-id="${escapeHtml(item.projectId)}" class="${item.projectId === selectedRecentProjectId ? "selectedRow" : ""}">
        <td>${escapeHtml(item.projectName)}</td>
        <td>${escapeHtml(item.projectPath)}</td>
        <td>${escapeHtml(item.lastOpenedAt || "")}</td>
        <td>${escapeHtml([item.defaultModule, item.templateVersion].filter(Boolean).join(" / "))}</td>
        <td><span class="projectStatusBadge ${item.status === "available" ? "ok" : "warning"}">${escapeHtml(item.status || "")}</span></td>
      </tr>`).join("")
    : '<tr><td colspan="5">暂无最近工程。</td></tr>';

  for (const row of body.querySelectorAll("[data-recent-project-id]")) {
    row.addEventListener("click", () => {
      selectedRecentProjectId = row.dataset.recentProjectId || "";
      renderRecentProjects();
    });
  }
}

async function loadRecentProjects(validate = false) {
  const result = await requestJson(validate ? "/api/projects/recent/validate" : "/api/projects/recent", {
    method: validate ? "POST" : "GET"
  });
  recentProjects = result.items || [];
  selectedRecentProjectId = selectedRecentProjectId || recentProjects[0]?.projectId || "";
  renderRecentProjects();
  showResult("#projectSelectionResult", result);
}

async function openSelectedRecentProject() {
  const item = recentProjects.find((project) => project.projectId === selectedRecentProjectId);
  if (!item) {
    showResult("#projectSelectionResult", "请先选择最近工程。");
    return;
  }
  const result = await requestJson("/api/projects/open", {
    method: "POST",
    body: JSON.stringify({ projectRootPath: item.projectPath })
  });
  showResult("#projectSelectionResult", result);
  $("#projectSelectionDialog")?.classList.add("hidden");
  window.dispatchEvent(new CustomEvent("engineering-docs-project-context-refresh"));
}

async function removeSelectedRecentProject() {
  if (!selectedRecentProjectId) {
    showResult("#projectSelectionResult", "请先选择最近工程。");
    return;
  }
  const result = await requestJson(`/api/projects/recent/${encodeURIComponent(selectedRecentProjectId)}`, {
    method: "DELETE"
  });
  selectedRecentProjectId = "";
  showResult("#projectSelectionResult", result);
  await loadRecentProjects();
}

async function clearRecentProjects() {
  const result = await requestJson("/api/projects/recent", { method: "DELETE" });
  recentProjects = [];
  selectedRecentProjectId = "";
  renderRecentProjects();
  showResult("#projectSelectionResult", result);
}

async function updateProjectFromForm(mode) {
  const data = formData("#projectManagerForm");
  const path = mode === "create" ? "/api/projects/create" : mode === "open" ? "/api/projects/open" : "/api/projects/current";
  const result = await requestJson(path, {
    method: "POST",
    body: JSON.stringify(data)
  });
  const project = result.project || result;
  const unitProjects = getState().unitProjects || [];
  setProjectContext(project, unitProjects, getState().activeUnitProjectId, getState().currentUnitProject);
  showResult("#projectManagerResult", result);
  window.dispatchEvent(new CustomEvent("engineering-docs-project-context-refresh"));
}

async function pickProjectFolder() {
  const result = await requestJson("/api/projects/select-folder", {
    method: "POST",
    body: JSON.stringify({})
  });
  const path = result.path || result.selectedPath || result.projectRootPath || "";
  const input = $("#projectManagerForm")?.elements.projectRootPath;
  if (input && path) {
    input.value = path;
  }
  showResult("#projectSelectionResult", result);
}

export function openTemplateManagementCenter() {
  if (getStandaloneMode() !== "template-management") {
    templateManagementWindowRef = openManagedPopup(
      templateManagementWindowRef,
      "./template-management.html#template-management-window",
      "engineering-docs-template-management",
      "popup=yes,width=1480,height=920,resizable=yes,scrollbars=yes"
    );
    if (templateManagementWindowRef) {
      return;
    }
  }

  const legacyHost = $("#templateLegacyHost");
  const windowBody = $("#templateManagementWindowBody");
  if (legacyHost && windowBody && legacyHost.parentElement !== windowBody) {
    windowBody.appendChild(legacyHost);
  }
  legacyHost?.classList.remove("hidden");
  openStandaloneModal("templateManagementDialog", {
    windowSelector: "#templateManagementWindow",
    dragHandleSelector: "#templateManagementDragHandle",
    closeButtonSelector: "#templateManagementCloseTop, #templateManagementClose, #templateManagementMinimize",
    maximizeButtonSelector: "#templateManagementMaximize",
    restoreButtonSelector: "#templateManagementRestore",
    resizeHandleSelector: "[data-template-management-resize]",
    bodySelector: "#templateManagementWindowBody"
  });
  showResult("#templateResult", "当前宿主无法打开独立窗口，已显示模板管理兼容区。");
}

function closeSimpleDialog(selector) {
  $(selector)?.classList.add("hidden");
}

function safeRun(task, selector) {
  return (...args) => task(...args).catch((error) => showResult(selector, error));
}

export function initializeLegacyFeatureService() {
  $("#copyStartCommand")?.addEventListener("click", safeRun(async () => {
    const text = $("#serviceStartCommand")?.textContent || "";
    await navigator.clipboard?.writeText(text);
    showResult("#projectManagerResult", "启动命令已复制。");
  }, "#projectManagerResult"));
  $("#rescanModules")?.addEventListener("click", safeRun(() => loadModules(true), "#moduleResult"));
  $("#refreshMaterials")?.addEventListener("click", safeRun(loadMaterials, "#materialResult"));
  $("#resetMaterialForm")?.addEventListener("click", () => {
    selectedMaterialId = "";
    renderMaterials();
    fillMaterialForm(null);
  });
  $("#materialEntryForm")?.addEventListener("submit", safeRun(saveMaterial, "#materialResult"));
  $("#materialAttachmentForm")?.addEventListener("submit", safeRun(uploadMaterialAttachment, "#materialResult"));
  $("#materialTestForm")?.addEventListener("submit", safeRun(saveMaterialTest, "#materialResult"));
  $("#generateMaterialApproval")?.addEventListener("click", safeRun(generateMaterialApproval, "#materialResult"));
  $("#exportMaterialLedger")?.addEventListener("click", safeRun(exportMaterialLedger, "#materialResult"));
  $("#openMaterialLedgerDialog")?.addEventListener("click", safeRun(openMaterialLedgerDialog, "#materialResult"));
  $("#materialLedgerClose")?.addEventListener("click", closeMaterialLedgerDialog);
  $("#materialLedgerCloseTop")?.addEventListener("click", closeMaterialLedgerDialog);
  $("#materialLedgerAddRow")?.addEventListener("click", addLedgerRow);
  $("#materialLedgerDeleteRows")?.addEventListener("click", safeRun(deleteLedgerRows, "#materialResult"));
  $("#materialLedgerSave")?.addEventListener("click", safeRun(saveLedgerRows, "#materialResult"));
  $("#materialLedgerExport")?.addEventListener("click", safeRun(exportMaterialLedger, "#materialResult"));
  $("#materialLedgerQuickFilter")?.addEventListener("input", filterLedgerRows);
  $("#materialLedgerClearFilters")?.addEventListener("click", () => {
    $("#materialLedgerQuickFilter").value = "";
    filterLedgerRows();
  });
  $("#materialLedgerMaximize")?.addEventListener("click", () => setMaterialLedgerMaximized(true));
  $("#materialLedgerRestore")?.addEventListener("click", () => setMaterialLedgerMaximized(false));
  $("#materialLedgerConfirmApproval")?.addEventListener("click", safeRun(generateMaterialApproval, "#materialResult"));
  $("#materialLedgerAttachmentForm")?.addEventListener("submit", safeRun(uploadMaterialAttachment, "#materialResult"));
  $("#materialLedgerUploadAttachment")?.addEventListener("click", () => {});
  $("#refreshKnowledge")?.addEventListener("click", safeRun(loadKnowledgeItems, "#knowledgeResult"));
  $("#knowledgeForm")?.addEventListener("submit", (event) => {
    event.preventDefault();
    safeRun(loadKnowledgeItems, "#knowledgeResult")();
  });
  $("#loadSettings")?.addEventListener("click", safeRun(loadSettings, "#settingsResult"));
  $("#runEnvironmentCheck")?.addEventListener("click", safeRun(runEnvironmentCheck, "#environmentResult"));
  $("#loadLogs")?.addEventListener("click", safeRun(loadLogs, "#logsResult"));
  $("#refreshLicense")?.addEventListener("click", safeRun(refreshLicense, "#licenseResult"));
  $("#activateLicense")?.addEventListener("click", safeRun(activateLicense, "#licenseResult"));
  $("#copyMachineCode")?.addEventListener("click", safeRun(copyMachineCode, "#licenseResult"));
  $("#openProject")?.addEventListener("click", safeRun(() => updateProjectFromForm("open"), "#projectManagerResult"));
  $("#createProject")?.addEventListener("click", safeRun(() => updateProjectFromForm("create"), "#projectManagerResult"));
  $("#saveProject")?.addEventListener("click", safeRun(() => updateProjectFromForm("save"), "#projectManagerResult"));
  $("#openProjectSelection")?.addEventListener("click", () => $("#projectSelectionDialog")?.classList.remove("hidden"));
  $("#openProjectSelection")?.addEventListener("click", safeRun(() => loadRecentProjects(), "#projectSelectionResult"));
  $("#closeProjectSelection")?.addEventListener("click", () => closeSimpleDialog("#projectSelectionDialog"));
  $("#projectSelectionPickFolder")?.addEventListener("click", safeRun(pickProjectFolder, "#projectSelectionResult"));
  $("#projectSelectionValidateRecent")?.addEventListener("click", safeRun(() => loadRecentProjects(true), "#projectSelectionResult"));
  $("#projectSelectionOpenRecent")?.addEventListener("click", safeRun(openSelectedRecentProject, "#projectSelectionResult"));
  $("#projectSelectionRemoveRecent")?.addEventListener("click", safeRun(removeSelectedRecentProject, "#projectSelectionResult"));
  $("#projectSelectionClearRecent")?.addEventListener("click", safeRun(clearRecentProjects, "#projectSelectionResult"));
  $("#openUnitProjectManager")?.addEventListener("click", safeRun(openUnitProjectDialog, "#unitProjectResult"));
  $("#closeUnitProjectDialog")?.addEventListener("click", closeUnitProjectDialog);
  $("#refreshUnitProjects")?.addEventListener("click", safeRun(loadUnitProjects, "#unitProjectResult"));
  $("#newUnitProject")?.addEventListener("click", resetUnitProjectForm);
  $("#unitProjectForm")?.addEventListener("submit", safeRun(saveUnitProject, "#unitProjectResult"));
  $("#setCurrentUnitProject")?.addEventListener("click", safeRun(setCurrentUnitProject, "#unitProjectResult"));
  $("#deactivateUnitProject")?.addEventListener("click", safeRun(deactivateUnitProject, "#unitProjectResult"));
  $("#openTemplateManagementCenter")?.addEventListener("click", openTemplateManagementCenter);
  $("#templateManagementResumeChip")?.addEventListener("click", openTemplateManagementCenter);
  $("#closeTemplatePreview")?.addEventListener("click", () => closeSimpleDialog("#templatePreviewModal"));
}

export async function loadLegacyFeatureData() {
  await Promise.allSettled([
    loadModules(),
    loadMaterials(),
    loadKnowledgeItems(),
    loadSettings(),
    refreshLicense()
  ]);
}
