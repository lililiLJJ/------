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
    case "btnOpenPanel":
      openEngineeringDocsPane("panel");
      break;
    case "btnGenerateCurrent":
      openEngineeringDocsPane("panel");
      break;
    case "btnBatchGenerate":
      openEngineeringDocsPane("batch");
      break;
    case "btnTemplates":
      openEngineeringDocsPane("templates");
      break;
    case "btnKnowledge":
      openEngineeringDocsPane("knowledge");
      break;
    case "btnAiText":
      openEngineeringDocsPane("ai");
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

function openEngineeringDocsPane(tabName) {
  const paneUrl = `${GetUrlPath()}/index.html#${tabName}`;
  const storageKey = "engineering_docs_taskpane_id";
  let paneId = window.Application.PluginStorage.getItem(storageKey);

  if (!paneId) {
    const taskPane = window.Application.CreateTaskPane(paneUrl);
    paneId = taskPane.ID;
    window.Application.PluginStorage.setItem(storageKey, paneId);
    taskPane.Visible = true;
    return;
  }

  const taskPane = window.Application.GetTaskPane(paneId);
  taskPane.Visible = true;
}
