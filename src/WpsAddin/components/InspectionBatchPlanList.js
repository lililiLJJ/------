function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function formatTime(value) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return String(value);
  }

  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
  }).format(date);
}

export class InspectionBatchPlanList {
  constructor(options) {
    this.host = options.host;
    this.onSelectPlan = options.onSelectPlan;
    this.onCreatePlan = options.onCreatePlan;
    this.onRenamePlan = options.onRenamePlan;
    this.onDuplicatePlan = options.onDuplicatePlan;
    this.onDeletePlan = options.onDeletePlan;
    this.plans = [];
    this.currentPlanId = "";
    this.unitProjectName = "";

    this.renderShell();
    this.bindEvents();
  }

  renderShell() {
    if (!this.host) {
      return;
    }

    this.host.innerHTML = `
      <div class="inspectionPlanListShell">
        <div class="inspectionPlanListHeader">
          <div>
            <strong>计划列表</strong>
            <span class="inspectionPlanListSummary" data-plan-list-summary>0 个计划</span>
          </div>
          <div class="actions compactActions">
            <button type="button" data-plan-list-action="create" class="primary">新建计划</button>
            <button type="button" data-plan-list-action="rename">重命名</button>
            <button type="button" data-plan-list-action="duplicate">复制计划</button>
            <button type="button" data-plan-list-action="delete" class="danger">删除计划</button>
          </div>
        </div>
        <div class="inspectionPlanListBody" data-plan-list-body></div>
      </div>`;
  }

  bindEvents() {
    this.host?.addEventListener("click", (event) => {
      const actionButton = event.target.closest("[data-plan-list-action]");
      if (actionButton) {
        const action = actionButton.dataset.planListAction;
        if (action === "create") {
          this.onCreatePlan?.();
        } else if (action === "rename") {
          this.onRenamePlan?.(this.getCurrentPlan());
        } else if (action === "duplicate") {
          this.onDuplicatePlan?.(this.getCurrentPlan());
        } else if (action === "delete") {
          this.onDeletePlan?.(this.getCurrentPlan());
        }
        return;
      }

      const item = event.target.closest("[data-plan-id]");
      if (item) {
        this.onSelectPlan?.(item.dataset.planId || "");
      }
    });
  }

  setData(plans, currentPlanId, unitProjectName = "") {
    this.plans = plans || [];
    this.currentPlanId = currentPlanId || "";
    this.unitProjectName = unitProjectName || "";
    this.render();
  }

  getCurrentPlan() {
    return this.plans.find((plan) => (plan.planId || plan.id) === this.currentPlanId) || null;
  }

  render() {
    const summary = this.host?.querySelector("[data-plan-list-summary]");
    const body = this.host?.querySelector("[data-plan-list-body]");
    if (!summary || !body) {
      return;
    }

    summary.textContent = `${this.plans.length} 个计划`;
    if (!this.plans.length) {
      body.innerHTML = '<p class="emptyText">当前单位工程下还没有检验批计划。</p>';
      return;
    }

    body.innerHTML = this.plans.map((plan) => {
      const planId = plan.planId || plan.id;
      const selected = planId === this.currentPlanId ? " selected" : "";
      const status = String(plan.status || "Active");
      return `
        <button type="button" class="inspectionPlanListItem${selected}" data-plan-id="${escapeHtml(planId)}">
          <span class="inspectionPlanListItemTitle">
            <strong>${escapeHtml(plan.planName || "未命名计划")}</strong>
            <span class="batchPlanStatusBadge ${status.toLowerCase()}">${escapeHtml(status)}</span>
          </span>
          <span class="inspectionPlanListMeta">单位工程：${escapeHtml(plan.unitProjectName || this.unitProjectName || "-")}</span>
          <span class="inspectionPlanListMeta">检验批数量：${plan.rowCount ?? plan.rows?.length ?? 0} ｜ 已生成：${plan.generatedCount ?? 0}</span>
          <span class="inspectionPlanListMeta">最后修改：${escapeHtml(formatTime(plan.updatedTime))}</span>
        </button>`;
    }).join("");
  }
}
