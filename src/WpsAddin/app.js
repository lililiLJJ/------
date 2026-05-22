const serviceBaseUrl = "http://127.0.0.1:5188";
const serviceStartCommand = 'cd "D:\\YY\\编程\\工程资料制作"; dotnet run --project "src/GeneratorService"';
let templates = [];
let serviceAvailable = false;

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];

function showResult(selector, data) {
  $(selector).textContent = typeof data === "string" ? data : JSON.stringify(data, null, 2);
}

function getFormData() {
  const data = Object.fromEntries(new FormData($("#generateForm")).entries());
  return {
    projectName: data.projectName,
    constructor: data.constructor,
    supervisor: data.supervisor,
    division: data.division,
    subItem: data.subItem,
    location: data.location,
    capacity: data.capacity,
    constructionDate: data.constructionDate,
    acceptanceDate: data.acceptanceDate,
    templateType: data.templateType,
    templateName: data.templateName,
    exportPath: data.exportPath || null
  };
}

async function api(path, options = {}) {
  const response = await fetch(`${serviceBaseUrl}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(options.headers || {})
    }
  });
  const data = await response.json();
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
  $("#knowledgeSummary").textContent = "本地服务未启动，暂时无法读取知识库。";
  renderKnowledgeItems([]);
  showResult("#knowledgeResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“查询知识库”。");
  $("#aiSummary").textContent = "本地服务未启动，暂时无法生成 AI 文本预览。";
  showResult("#aiResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“生成预览”。");
  $("#settingsSummary").textContent = "本地服务未启动，暂时无法读取设置。";
  $("#settingsGrid").innerHTML = "";
  showResult("#settingsResult", "请先启动 GeneratorService，然后点击“重新检测服务”或“刷新设置”。");
}

async function retryServiceStatus() {
  const online = await refreshStatus();
  if (!online) {
    return;
  }

  await loadTemplates().catch((error) => showResult("#generateResult", error));
  await loadKnowledgeItems().catch((error) => showResult("#knowledgeResult", error));
  await loadSettings().catch((error) => showResult("#settingsResult", error));
  await refreshLicense();
}

function setGenerateDisabled(disabled) {
  $("#generateCurrent").disabled = disabled;
  $("#generateBatch").disabled = disabled;
  $("#reloadTemplates").disabled = disabled;
  $("#refreshTemplateList").disabled = disabled;
  $("#refreshKnowledge").disabled = disabled;
  $("#previewAiText").disabled = disabled;
  $("#loadSettings").disabled = disabled;
}

async function copyStartCommand() {
  const command = $("#serviceStartCommand").textContent;
  try {
    await navigator.clipboard.writeText(command);
    showResult("#generateResult", "启动命令已复制。请打开 PowerShell，粘贴并执行，然后点击“重新检测服务”。");
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
  showResult("#templateResult", "正在刷新模板列表...");
  try {
    await loadTemplates();
  } catch (error) {
    $("#templateSummary").textContent = "模板读取失败。";
    showResult("#templateResult", error);
  }
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
    <td><input class="batch-subitem" value="${values.subItem || "钢筋安装"}"></td>
    <td><input class="batch-location" value="${values.location || "3层梁板"}"></td>
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
    subItem: row.querySelector(".batch-subitem").value,
    location: row.querySelector(".batch-location").value,
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
  } catch (error) {
    $("#licenseStatus").textContent = "授权状态读取失败";
    showResult("#licenseResult", error);
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
  const targetButton = $(`.tab[data-tab="${tabId}"]`);
  if (!targetPanel || !targetButton) {
    return;
  }

  for (const tab of $$(".tab")) tab.classList.remove("active");
  for (const panel of $$(".tabPanel")) panel.classList.remove("active");
  targetButton.classList.add("active");
  targetPanel.classList.add("active");
  if (window.location.hash !== `#${tabId}`) {
    window.history.replaceState(null, "", `#${tabId}`);
  }
}

function activateTabFromHash() {
  const tabId = window.location.hash.replace("#", "") || "panel";
  activateTab(tabId);
}

async function boot() {
  bindTabs();
  $("#refreshStatus").addEventListener("click", refreshStatus);
  $("#retryServiceStatus").addEventListener("click", retryServiceStatus);
  $("#copyStartCommand").addEventListener("click", copyStartCommand);
  $("#reloadTemplates").addEventListener("click", loadTemplates);
  $("#refreshTemplateList").addEventListener("click", refreshTemplateManagement);
  $("#refreshKnowledge").addEventListener("click", loadKnowledgeItems);
  $("#previewAiText").addEventListener("click", previewAiText);
  $("#loadSettings").addEventListener("click", loadSettings);
  $("#generateCurrent").addEventListener("click", generateCurrent);
  $("#addBatchRow").addEventListener("click", () => createBatchRow());
  $("#generateBatch").addEventListener("click", generateBatch);
  $("#refreshLicense").addEventListener("click", refreshLicense);
  $("#activateLicense").addEventListener("click", activateLicense);
  $("#loadLogs").addEventListener("click", loadLogs);

  createBatchRow();
  createBatchRow({ location: "4层梁板", date: "2026-05-23" });
  const online = await refreshStatus();
  if (online || serviceAvailable) {
    await loadTemplates().catch((error) => showResult("#generateResult", error));
    await loadKnowledgeItems().catch((error) => showResult("#knowledgeResult", error));
    await loadSettings().catch((error) => showResult("#settingsResult", error));
    await refreshLicense();
  }
  activateTabFromHash();
}

window.addEventListener("hashchange", activateTabFromHash);
boot();
