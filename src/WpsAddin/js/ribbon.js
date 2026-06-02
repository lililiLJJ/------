// WPS 加载项加载完成时执行。ribbonUI 用来刷新 Ribbon 按钮状态。
function OnAddinLoad(ribbonUI) {
  if (typeof window.Application.ribbonUI !== "object") {
    window.Application.ribbonUI = ribbonUI;
  }

  if (typeof window.Application.Enum !== "object") {
    window.Application.Enum = WPS_Enum;
  }

  return true;
}

// Ribbon 所有按钮统一进入这里，再根据按钮 id 决定打开哪个功能页。
function OnAction(control) {
  switch (control.Id) {
    case "btnProjectSelector":
      openEngineeringDocsPane("projectSelector");
      break;
    case "btnOpenPanel":
      openEngineeringDocsPane("panel");
      break;
    case "btnBatchPlan":
      openEngineeringDocsPane("batchPlan");
      break;
    case "btnTemplates":
      openEngineeringDocsPane("templates");
      break;
    case "btnSummary":
      openEngineeringDocsPane("summary");
      break;
    case "btnMaterials":
      openEngineeringDocsPane("materials");
      break;
    case "btnModules":
      openEngineeringDocsPane("modules");
      break;
    case "btnKnowledge":
      openEngineeringDocsPane("knowledge");
      break;
    case "btnActivation":
      openEngineeringDocsPane("license");
      break;
    case "btnSettings":
      openEngineeringDocsPane("settings");
      break;
    case "btnEnvironmentCheck":
      openEngineeringDocsPane("environment");
      break;
    case "btnViewLogs":
      openEngineeringDocsPane("logs");
      break;
    default:
      openEngineeringDocsPane("panel");
      break;
  }
  return true;
}

function OnGetEnabled() {
  return true;
}

const engineeringDocsTabKeys = {
  paneId: "engineering_docs_taskpane_id",
  targetTab: "engineering_docs_target_tab",
  targetTabVersion: "engineering_docs_target_tab_version",
  tabSignal: "engineering_docs_tab_signal",
  frontendMode: "engineering_docs_frontend_mode"
};

function openEngineeringDocsPane(tabName) {
  const paneUrl = `${GetUrlPath()}/${getEngineeringDocsEntryPath()}#${tabName}`;
  signalEngineeringDocsTab(tabName);
  let paneId = window.Application.PluginStorage.getItem(engineeringDocsTabKeys.paneId);

  if (!paneId) {
    const taskPane = createEngineeringDocsTaskPane(paneUrl);
    taskPane.Visible = true;
    return;
  }

  let taskPane = null;
  try {
    taskPane = window.Application.GetTaskPane(paneId);
  } catch {
    taskPane = createEngineeringDocsTaskPane(paneUrl);
  }

  if (!taskPane) {
    taskPane = createEngineeringDocsTaskPane(paneUrl);
  }

  signalEngineeringDocsTab(tabName);
  taskPane.Visible = true;
}

function createEngineeringDocsTaskPane(paneUrl) {
  const taskPane = window.Application.CreateTaskPane(paneUrl);
  window.Application.PluginStorage.setItem(engineeringDocsTabKeys.paneId, taskPane.ID);
  return taskPane;
}

function getEngineeringDocsEntryPath() {
  const mode = getEngineeringDocsFrontendMode();
  return mode === "development" ? "index.html" : "dist/index.html";
}

function getEngineeringDocsFrontendMode() {
  try {
    const pluginMode = window.Application?.PluginStorage?.getItem(engineeringDocsTabKeys.frontendMode);
    if (pluginMode === "development" || pluginMode === "production") {
      return pluginMode;
    }
  } catch {
    // Ignore PluginStorage access failures.
  }

  try {
    const localMode = window.localStorage?.getItem("engineering-docs.frontend-mode");
    if (localMode === "development" || localMode === "production") {
      return localMode;
    }
  } catch {
    // Ignore localStorage access failures.
  }

  return "production";
}

function signalEngineeringDocsTab(tabName) {
  const message = {
    type: "engineering-docs-switch-tab",
    tabName,
    version: `${Date.now()}:${Math.random()}`
  };

  try {
    window.Application.PluginStorage.setItem(engineeringDocsTabKeys.targetTab, tabName);
    window.Application.PluginStorage.setItem(engineeringDocsTabKeys.targetTabVersion, message.version);
  } catch {
    // WPS PluginStorage can be unavailable in plain browser previews.
  }

  try {
    if (typeof BroadcastChannel === "function") {
      const channel = new BroadcastChannel(engineeringDocsTabKeys.tabSignal);
      channel.postMessage(message);
      channel.close();
    }
  } catch {
    // Some embedded WebViews disable BroadcastChannel.
  }

  try {
    if (window.localStorage) {
      window.localStorage.setItem(engineeringDocsTabKeys.tabSignal, JSON.stringify(message));
    }
  } catch {
    // localStorage may be blocked by the host.
  }
}
