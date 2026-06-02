import { bootstrapMainWorkspace } from "./workspaces/mainWorkspace.js";
import { bootstrapInspectionPlanCenterWorkspace } from "./workspaces/inspectionPlanCenterWorkspace.js";

function getWorkspaceView() {
  const url = new URL(window.location.href);
  return url.searchParams.get("view") || "main";
}

function pickWorkspace(view) {
  if (view === "inspection-plan-center") {
    return bootstrapInspectionPlanCenterWorkspace();
  }
  return bootstrapMainWorkspace();
}

function reportBootError(error) {
  const message = error instanceof Error ? error.message : String(error || "未知错误");
  const targets = [
    "#projectManagerResult",
    "#templateResult",
    "#batchPlanResult",
    "#summaryResult"
  ];

  let reported = false;
  for (const selector of targets) {
    const node = document.querySelector(selector);
    if (node) {
      node.textContent = message;
      reported = true;
    }
  }

  if (!reported) {
    const pre = document.createElement("pre");
    pre.className = "resultBox";
    pre.textContent = message;
    document.body.appendChild(pre);
  }
}

async function boot() {
  const workspace = pickWorkspace(getWorkspaceView());
  await workspace.boot();
}

boot().catch((error) => {
  reportBootError(error);
});
