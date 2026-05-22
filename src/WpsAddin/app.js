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
}

async function retryServiceStatus() {
  const online = await refreshStatus();
  if (!online) {
    return;
  }

  await loadTemplates().catch((error) => showResult("#generateResult", error));
  await refreshLicense();
}

function setGenerateDisabled(disabled) {
  $("#generateCurrent").disabled = disabled;
  $("#generateBatch").disabled = disabled;
  $("#reloadTemplates").disabled = disabled;
  $("#refreshTemplateList").disabled = disabled;
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
    await refreshLicense();
  }
  activateTabFromHash();
}

window.addEventListener("hashchange", activateTabFromHash);
boot();
