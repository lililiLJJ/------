import { InspectionBatchPlanList } from "./InspectionBatchPlanList.js";
import { InspectionBatchPlanEditor } from "./InspectionBatchPlanEditor.js";

function stringifyResult(result) {
  if (!result) {
    return "";
  }
  if (typeof result === "string") {
    return result;
  }
  if (result instanceof Error) {
    return result.message || String(result);
  }
  return JSON.stringify(result, null, 2);
}

export class InspectionBatchPlanCenter {
  constructor(options) {
    this.host = options.host;
    this.options = options;
    this.renderShell();

    this.list = new InspectionBatchPlanList({
      host: this.host.querySelector("[data-plan-center-list]"),
      onSelectPlan: (planId) => this.options.onSelectPlan?.(planId),
      onCreatePlan: () => this.options.onCreatePlan?.(),
      onRenamePlan: (plan) => this.options.onRenamePlan?.(plan),
      onDuplicatePlan: (plan) => this.options.onDuplicatePlan?.(plan),
      onDeletePlan: (plan) => this.options.onDeletePlan?.(plan)
    });

    this.editor = new InspectionBatchPlanEditor({
      host: this.host.querySelector("[data-plan-center-editor]"),
      showResult: (result) => this.showResult(result),
      onSavePlan: (planId, payload) => this.options.onSavePlan?.(planId, payload),
      onPreviewPlan: (planId) => this.options.onPreviewPlan?.(planId),
      onGeneratePlan: (planId, payload) => this.options.onGeneratePlan?.(planId, payload),
      onLoadCapacityConfigs: (templateNodeId) => this.options.onLoadCapacityConfigs?.(templateNodeId),
      onOpenDocument: (documentId) => this.options.onOpenDocument?.(documentId)
    });
  }

  renderShell() {
    if (!this.host) {
      return;
    }

    this.host.innerHTML = `
      <div class="inspectionPlanCenterRoot">
        <header class="inspectionPlanCenterHeader">
          <div>
            <h1>检验批计划中心</h1>
            <p data-plan-center-summary>正在加载 Project V2 检验批计划工作台...</p>
          </div>
          <div class="inspectionPlanCenterMode" data-plan-center-mode>模式：计划编辑</div>
        </header>
        <div class="inspectionPlanCenterBody">
          <aside class="inspectionPlanCenterSidebar" data-plan-center-list></aside>
          <section class="inspectionPlanCenterEditorWrap" data-plan-center-editor></section>
        </div>
        <pre class="resultBox compactResult" data-plan-center-result></pre>
      </div>`;
  }

  setContext({ projectName, unitProjectName, mode }) {
    const summary = this.host.querySelector("[data-plan-center-summary]");
    const modeNode = this.host.querySelector("[data-plan-center-mode]");
    summary.textContent = `${projectName || "当前工程"} ｜ ${unitProjectName || "未选择单位工程"}`;
    modeNode.textContent = mode === "generate" ? "模式：批量生成" : "模式：计划编辑";
    this.editor.setMode(mode);
  }

  setPlans(plans, currentPlanId, unitProjectName) {
    this.list.setData(plans, currentPlanId, unitProjectName);
  }

  setPlan(plan, rows) {
    this.editor.setPlan(plan, rows);
  }

  clearPlan() {
    this.editor.clearPlan();
  }

  showBusy(message) {
    const summary = this.host.querySelector("[data-plan-center-summary]");
    if (summary) {
      summary.textContent = message;
    }
  }

  showResult(result) {
    const node = this.host.querySelector("[data-plan-center-result]");
    if (node) {
      node.textContent = stringifyResult(result);
    }
  }
}
