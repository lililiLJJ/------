const serviceBaseUrl = "http://127.0.0.1:5188";
let templates = [];

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
  try {
    const data = await api("/api/health");
    $("#serviceStatus").textContent = `服务正常｜版本 ${data.version}｜AI ${data.aiEnabled ? "已启用" : "未启用"}`;
  } catch {
    $("#serviceStatus").textContent = "本地服务未启动，请先运行 GeneratorService";
    $("#serviceStatus").classList.add("error");
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
  $("#reloadTemplates").addEventListener("click", loadTemplates);
  $("#generateCurrent").addEventListener("click", generateCurrent);
  $("#addBatchRow").addEventListener("click", () => createBatchRow());
  $("#generateBatch").addEventListener("click", generateBatch);
  $("#refreshLicense").addEventListener("click", refreshLicense);
  $("#activateLicense").addEventListener("click", activateLicense);
  $("#loadLogs").addEventListener("click", loadLogs);

  createBatchRow();
  createBatchRow({ location: "4层梁板", date: "2026-05-23" });
  await refreshStatus();
  await loadTemplates().catch((error) => showResult("#generateResult", error));
  await refreshLicense();
  activateTabFromHash();
}

window.addEventListener("hashchange", activateTabFromHash);
boot();
