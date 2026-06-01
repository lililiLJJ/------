function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function composeCapacitySummary(capacities) {
  return (capacities || [])
    .filter((item) => String(item?.value || "").trim())
    .sort((a, b) => Number(a.sortOrder || 0) - Number(b.sortOrder || 0))
    .map((item) => `${item.capacityName || item.capacityKey || "容量"}${item.value || ""}${item.unit || ""}`)
    .join("；");
}

function formatStatus(row) {
  if (String(row.status || "").toLowerCase() === "deleted") {
    return "Deleted";
  }
  return row.generateStatus || row.status || "None";
}

function createDraftPlan() {
  const id = `draft-plan:${Date.now()}`;
  return {
    planId: id,
    planName: `检验批计划-${new Date().toISOString().slice(0, 10)}`,
    remark: "",
    status: "Active",
    rowCount: 0,
    generatedCount: 0,
    rows: []
  };
}

function createEmptyRow(templateOption = null) {
  return {
    planRowId: "",
    divisionId: "",
    divisionName: templateOption?.divisionName || "",
    subDivisionId: "",
    subDivisionName: templateOption?.subDivisionName || "",
    subItemId: "",
    subItemName: templateOption?.subItemName || "",
    templateNodeId: templateOption?.templateNodeId || "",
    templateItemId: Number(templateOption?.templateItemId || 0),
    templateName: templateOption?.templateName || "",
    inspectionPart: "",
    constructionDate: "",
    capacitySummary: "",
    remark: "",
    status: "Active",
    generateStatus: "None",
    activeDocumentId: "",
    capacities: [],
    _selected: false
  };
}

export class PlanEditor {
  constructor(options) {
    this.options = options;
    this.plans = [];
    this.currentPlanId = "";
    this.rows = [];
    this.preview = null;
    this.hierarchyCollapsed = false;
    this.capacityEditingIndex = -1;
    this.capacityFields = [];

    this.bindEvents();
  }

  bindEvents() {
    document.querySelector("#refreshBatchPlans")?.addEventListener("click", () => this.options.onRefresh());
    document.querySelector("#batchPlanCreatePlan")?.addEventListener("click", () => this.createPlan());
    document.querySelector("#batchPlanDeletePlan")?.addEventListener("click", () => this.deleteCurrentPlan());
    document.querySelector("#batchPlanAddRow")?.addEventListener("click", () => this.addRow());
    document.querySelector("#batchPlanDeleteRows")?.addEventListener("click", () => this.deleteSelectedRows());
    document.querySelector("#batchPlanSave")?.addEventListener("click", () => this.saveCurrentPlan());
    document.querySelector("#batchPlanPreview")?.addEventListener("click", () => this.previewCurrentPlan());
    document.querySelector("#batchPlanGenerate")?.addEventListener("click", () => this.generateCurrentPlan());
    document.querySelector("#batchPlanToggleHierarchy")?.addEventListener("click", () => {
      this.hierarchyCollapsed = !this.hierarchyCollapsed;
      this.renderTable();
    });
    document.querySelector("#batchPlanUseSelectedTemplate")?.addEventListener("click", () => {
      const template = this.options.getSelectedTemplateOption?.();
      this.addRow(template || null);
    });
    document.querySelector("#batchPlanApplyDate")?.addEventListener("click", () => this.applyBulkValue("constructionDate", document.querySelector("#batchPlanBulkDate")?.value || ""));
    document.querySelector("#batchPlanList")?.addEventListener("click", (event) => {
      const button = event.target.closest("[data-plan-id]");
      if (button) {
        this.selectPlan(button.dataset.planId);
      }
    });
    document.querySelector("#batchPlanBody")?.addEventListener("change", (event) => this.handleTableChange(event));
    document.querySelector("#batchPlanBody")?.addEventListener("click", (event) => this.handleTableClick(event));
    document.querySelector("#batchPlanCapacityForm")?.addEventListener("submit", (event) => this.saveCapacity(event));
    document.querySelector("#closeBatchPlanCapacityModal")?.addEventListener("click", () => this.closeCapacityModal());
    document.querySelector("#cancelBatchPlanCapacity")?.addEventListener("click", () => this.closeCapacityModal());
    document.querySelector("#batchPlanCapacityModal")?.addEventListener("click", (event) => {
      if (event.target?.id === "batchPlanCapacityModal") {
        this.closeCapacityModal();
      }
    });
  }

  setData(result) {
    this.plans = result?.plans || [];
    if (!this.plans.length) {
      const draft = createDraftPlan();
      this.plans = [draft];
    }

    this.currentPlanId = result?.currentPlanId || this.currentPlanId || this.plans[0]?.planId || this.plans[0]?.id || "";
    this.selectPlan(this.currentPlanId, { silent: true });
  }

  getCurrentPlan() {
    return this.plans.find((item) => (item.planId || item.id) === this.currentPlanId) || null;
  }

  selectPlan(planId, options = {}) {
    const plan = this.plans.find((item) => (item.planId || item.id) === planId) || this.plans[0] || createDraftPlan();
    this.currentPlanId = plan.planId || plan.id;
    this.rows = (plan.rows || []).map((row) => ({
      ...row,
      capacities: row.capacities || [],
      _selected: false
    }));
    if (!this.rows.length) {
      this.rows = [createEmptyRow()];
    }

    document.querySelector("#batchPlanName").value = plan.planName || "";
    document.querySelector("#batchPlanRemark").value = plan.remark || "";
    this.preview = null;
    this.renderList();
    this.renderTable();
    this.renderPreview();
    document.querySelector("#batchPlanSummary").textContent = `已切换计划：${plan.planName || "未命名计划"}，共 ${this.rows.length} 行。`;
    if (!options.silent) {
      this.options.onPlanChanged?.(plan);
    }
  }

  renderList() {
    const host = document.querySelector("#batchPlanList");
    const summary = document.querySelector("#batchPlanListSummary");
    if (!host || !summary) {
      return;
    }

    summary.textContent = `${this.plans.length} 个计划`;
    host.innerHTML = this.plans.map((plan) => {
      const planId = plan.planId || plan.id;
      const selected = planId === this.currentPlanId ? " selected" : "";
      return `
        <button type="button" class="batchPlanListItem${selected}" data-plan-id="${escapeHtml(planId)}">
          <span class="batchPlanListItemTitle">
            <strong>${escapeHtml(plan.planName || "未命名计划")}</strong>
            <span class="batchPlanStatusBadge ${String(plan.status || "").toLowerCase()}">${escapeHtml(plan.status || "Active")}</span>
          </span>
          <span class="batchPlanListMeta">行数 ${plan.rowCount ?? plan.rows?.length ?? 0} ｜ 已生成 ${plan.generatedCount ?? 0}</span>
          <span class="batchPlanListMeta">${escapeHtml(plan.remark || "无备注")}</span>
        </button>`;
    }).join("");
  }

  getColumns() {
    const columns = [
      { key: "unitProjectName", label: "单位工程", readOnly: true },
      { key: "divisionName", label: "分部工程", readOnly: true },
      { key: "subDivisionName", label: "子分部工程", readOnly: true },
      { key: "subItemName", label: "分项工程", readOnly: true },
      { key: "templateNodeId", label: "检验批名称", type: "template" },
      { key: "inspectionPart", label: "部位" },
      { key: "constructionDate", label: "施工日期", type: "date" },
      { key: "capacitySummary", label: "容量摘要", type: "capacity" },
      { key: "remark", label: "备注" }
    ];
    if (!this.hierarchyCollapsed) {
      return columns;
    }
    return columns.filter((item) => !["unitProjectName", "divisionName", "subDivisionName", "subItemName"].includes(item.key));
  }

  renderTable() {
    const head = document.querySelector("#batchPlanHead");
    const body = document.querySelector("#batchPlanBody");
    const toggle = document.querySelector("#batchPlanToggleHierarchy");
    if (!head || !body || !toggle) {
      return;
    }

    toggle.textContent = this.hierarchyCollapsed ? "展开层级" : "折叠层级";
    const columns = this.getColumns();
    head.innerHTML = `
      <tr>
        <th>选择</th>
        <th>序号</th>
        ${columns.map((column) => `<th>${escapeHtml(column.label)}</th>`).join("")}
        <th>状态</th>
        <th>操作</th>
      </tr>`;

    body.innerHTML = this.rows.map((row, index) => `
      <tr>
        <td><input type="checkbox" data-row-check="${index}" ${row._selected ? "checked" : ""}></td>
        <td>${index + 1}</td>
        ${columns.map((column) => this.renderCell(row, index, column)).join("")}
        <td><span class="batchPlanStatusBadge ${String(formatStatus(row)).toLowerCase()}">${escapeHtml(formatStatus(row))}</span></td>
        <td>
          <div class="batchPlanCellActions">
            <button type="button" data-row-action="capacity" data-row-index="${index}">容量详情</button>
            ${row.activeDocumentId ? `<button type="button" data-row-action="open" data-row-index="${index}">打开表格</button>` : ""}
          </div>
        </td>
      </tr>`).join("");
  }

  renderCell(row, index, column) {
    if (column.type === "template") {
      const options = this.options.getTemplateOptions?.() || [];
      return `
        <td>
          <select data-row-index="${index}" data-row-field="templateNodeId">
            <option value="">请选择模板</option>
            ${options.map((option) => `<option value="${escapeHtml(option.templateNodeId)}" ${option.templateNodeId === row.templateNodeId ? "selected" : ""}>${escapeHtml(option.fullPath)}</option>`).join("")}
          </select>
        </td>`;
    }

    if (column.type === "capacity") {
      return `<td><div>${escapeHtml(row.capacitySummary || "-")}</div></td>`;
    }

    if (column.readOnly) {
      return `<td class="batchPlanReadonlyCell">${escapeHtml(row[column.key] || "")}</td>`;
    }

    if (column.type === "date") {
      return `<td><input type="date" data-row-index="${index}" data-row-field="${escapeHtml(column.key)}" value="${escapeHtml(row[column.key] || "")}"></td>`;
    }

    return `<td><input type="text" data-row-index="${index}" data-row-field="${escapeHtml(column.key)}" value="${escapeHtml(row[column.key] || "")}"></td>`;
  }

  handleTableChange(event) {
    const rowIndex = Number(event.target?.dataset?.rowIndex);
    const field = event.target?.dataset?.rowField;
    if (!Number.isFinite(rowIndex) || !field) {
      return;
    }

    const row = this.rows[rowIndex];
    if (!row) {
      return;
    }

    if (field === "templateNodeId") {
      const option = (this.options.getTemplateOptions?.() || []).find((item) => item.templateNodeId === event.target.value);
      row.templateNodeId = option?.templateNodeId || "";
      row.templateItemId = Number(option?.templateItemId || 0);
      row.templateName = option?.templateName || "";
      row.divisionName = option?.divisionName || "";
      row.subDivisionName = option?.subDivisionName || "";
      row.subItemName = option?.subItemName || "";
      row.capacitySummary = "";
      row.capacities = [];
    } else {
      row[field] = event.target.value.trim();
    }

    this.renderTable();
  }

  async handleTableClick(event) {
    const action = event.target?.dataset?.rowAction;
    const rowIndex = Number(event.target?.dataset?.rowIndex);
    if (action === "capacity" && Number.isFinite(rowIndex)) {
      await this.openCapacityModal(rowIndex);
      return;
    }

    if (action === "open" && Number.isFinite(rowIndex)) {
      const row = this.rows[rowIndex];
      if (row?.activeDocumentId) {
        await this.options.onOpenDocument?.(row.activeDocumentId);
      }
      return;
    }

    const check = event.target?.dataset?.rowCheck;
    if (check !== undefined) {
      const row = this.rows[Number(check)];
      if (row) {
        row._selected = Boolean(event.target.checked);
      }
    }
  }

  addRow(templateOption = null) {
    this.rows.push(createEmptyRow(templateOption));
    this.renderTable();
  }

  addRowFromSelectedTemplate(templateOption) {
    this.addRow(templateOption || null);
  }

  applyBulkValue(field, value) {
    if (!value) {
      return;
    }

    for (const row of this.rows) {
      row[field] = value;
    }
    this.renderTable();
  }

  deleteSelectedRows() {
    const selectedRows = this.rows.filter((item) => item._selected);
    if (!selectedRows.length) {
      return;
    }

    const hasGenerated = selectedRows.some((item) => item.activeDocumentId || item.generateStatus === "Generated");
    const message = hasGenerated
      ? "所选计划行包含已生成资料，保存计划时会同步删除关联资料。确定继续吗？"
      : `确定删除选中的 ${selectedRows.length} 行计划吗？`;
    if (!window.confirm(message)) {
      return;
    }

    this.rows = this.rows.filter((item) => !item._selected);
    if (!this.rows.length) {
      this.rows = [createEmptyRow()];
    }
    this.renderTable();
  }

  createPlan() {
    const draft = createDraftPlan();
    this.plans = [draft, ...this.plans];
    this.selectPlan(draft.planId);
  }

  async deleteCurrentPlan() {
    const plan = this.getCurrentPlan();
    if (!plan) {
      return;
    }

    const planId = plan.planId || plan.id;
    if (String(planId).startsWith("draft-plan:")) {
      this.plans = this.plans.filter((item) => (item.planId || item.id) !== planId);
      this.selectPlan((this.plans[0]?.planId || this.plans[0]?.id || createDraftPlan().planId), { silent: true });
      return;
    }

    if (!window.confirm(`确定删除计划“${plan.planName || "未命名计划"}”吗？`)) {
      return;
    }

    await this.options.onDeletePlan?.(planId);
  }

  buildPayload() {
    return {
      unitProjectId: this.options.getUnitProjectId?.() || null,
      planName: document.querySelector("#batchPlanName")?.value.trim() || "检验批计划",
      remark: document.querySelector("#batchPlanRemark")?.value.trim() || "",
      rows: this.rows.map((row) => ({
        planRowId: row.planRowId || null,
        divisionId: row.divisionId || "",
        divisionName: row.divisionName || "",
        subDivisionId: row.subDivisionId || "",
        subDivisionName: row.subDivisionName || "",
        subItemId: row.subItemId || "",
        subItemName: row.subItemName || "",
        templateNodeId: row.templateNodeId || "",
        templateItemId: Number(row.templateItemId || 0),
        templateName: row.templateName || "",
        inspectionPart: row.inspectionPart || "",
        constructionDate: row.constructionDate || "",
        remark: row.remark || "",
        status: row.status || "Active",
        generateStatus: row.generateStatus || "None",
        capacities: row.capacities || []
      }))
    };
  }

  async saveCurrentPlan() {
    const plan = this.getCurrentPlan();
    const planId = plan?.planId || plan?.id || "";
    const result = await this.options.onSavePlan?.(planId, this.buildPayload());
    if (!result?.plan) {
      return null;
    }

    const currentId = result.plan.planId || result.plan.id;
    const others = this.plans.filter((item) => (item.planId || item.id) !== currentId && (item.planId || item.id) !== planId);
    this.plans = [result.plan, ...others];
    this.currentPlanId = currentId;
    this.selectPlan(currentId, { silent: true });
    document.querySelector("#batchPlanSummary").textContent = `计划已保存：${result.plan.planName || "未命名计划"}，共 ${this.rows.length} 行。`;
    return result.plan;
  }

  async previewCurrentPlan() {
    const plan = await this.saveCurrentPlan();
    if (!plan) {
      return;
    }

    this.preview = await this.options.onPreviewPlan?.(plan.planId || plan.id);
    this.renderPreview();
    if (this.preview) {
      document.querySelector("#batchPlanSummary").textContent = `预览完成：可生成 ${this.preview.generatableCount} 张，阻止 ${this.preview.blockedCount} 张。`;
    }
  }

  async generateCurrentPlan() {
    const plan = await this.saveCurrentPlan();
    if (!plan) {
      return;
    }

    const selectedRowIds = this.rows.filter((item) => item._selected).map((item) => item.planRowId).filter(Boolean);
    await this.options.onGeneratePlan?.(plan.planId || plan.id, {
      selectedRowIds: selectedRowIds.length ? selectedRowIds : null,
      overwrite: false
    });
  }

  renderPreview() {
    const host = document.querySelector("#batchPlanPreviewPanel");
    if (!host) {
      return;
    }

    if (!this.preview) {
      host.innerHTML = '<p class="emptyText">保存计划后点击预览，可查看生成结果和预检信息。</p>';
      return;
    }

    host.innerHTML = `
      <div class="batchPlanPreviewHeader">
        <strong>将生成 ${this.preview.generatableCount} 张资料</strong>
        <span>阻止 ${this.preview.blockedCount} 行 / 总计 ${this.preview.totalCount} 行</span>
      </div>
      <div class="tableScroller">
        <table class="summaryTable">
          <thead>
            <tr>
              <th>序号</th>
              <th>检验批名称</th>
              <th>部位</th>
              <th>容量</th>
              <th>施工日期</th>
              <th>提示</th>
            </tr>
          </thead>
          <tbody>
            ${(this.preview.rows || []).map((row) => `
              <tr>
                <td>${row.rowIndex}</td>
                <td>${escapeHtml(row.templateName)}</td>
                <td>${escapeHtml(row.inspectionPart)}</td>
                <td>${escapeHtml(row.capacitySummary)}</td>
                <td>${escapeHtml(row.constructionDate)}</td>
                <td>${escapeHtml([...(row.errors || []), ...(row.warnings || [])].join("；") || "-")}</td>
              </tr>`).join("")}
          </tbody>
        </table>
      </div>`;
  }

  async openCapacityModal(rowIndex) {
    const row = this.rows[rowIndex];
    if (!row?.templateNodeId) {
      this.options.showResult?.("#batchPlanResult", "请先选择检验批模板，再填写容量详情。");
      return;
    }

    this.capacityEditingIndex = rowIndex;
    const result = await this.options.onLoadCapacityConfigs?.(row.templateNodeId);
    this.capacityFields = result?.items || [];
    document.querySelector("#batchPlanCapacitySummary").textContent = `${row.templateName || "当前模板"}：根据容量配置填写明细。`;
    const host = document.querySelector("#batchPlanCapacityFields");
    if (!host) {
      return;
    }

    const capacities = row.capacities || [];
    host.innerHTML = this.capacityFields.length
      ? this.capacityFields.map((field) => {
          const current = capacities.find((item) => item.capacityKey === field.capacityKey) || {};
          return `
            <div class="batchPlanCapacityFieldRow">
              <label>
                <span>${escapeHtml(field.capacityName)}${field.required ? '<span class="requiredMark">*</span>' : ""}</span>
                <input type="text" data-capacity-key="${escapeHtml(field.capacityKey)}" value="${escapeHtml(current.value || "")}">
              </label>
              <label>
                <span>单位</span>
                <input type="text" data-capacity-unit="${escapeHtml(field.capacityKey)}" value="${escapeHtml(current.unit || field.defaultUnit || "")}">
              </label>
            </div>`;
        }).join("")
      : '<p class="emptyText">当前模板未配置容量项，可直接在表格里维护容量摘要。</p>';
    document.querySelector("#batchPlanCapacityModal")?.classList.remove("hidden");
  }

  closeCapacityModal() {
    document.querySelector("#batchPlanCapacityModal")?.classList.add("hidden");
    document.querySelector("#batchPlanCapacityFields").innerHTML = "";
    this.capacityEditingIndex = -1;
    this.capacityFields = [];
  }

  saveCapacity(event) {
    event.preventDefault();
    const row = this.rows[this.capacityEditingIndex];
    if (!row) {
      this.closeCapacityModal();
      return;
    }

    row.capacities = this.capacityFields.map((field, index) => ({
      capacityId: "",
      planRowId: row.planRowId || "",
      capacityKey: field.capacityKey,
      capacityName: field.capacityName,
      value: document.querySelector(`[data-capacity-key="${CSS.escape(field.capacityKey)}"]`)?.value.trim() || "",
      unit: document.querySelector(`[data-capacity-unit="${CSS.escape(field.capacityKey)}"]`)?.value.trim() || field.defaultUnit || "",
      sortOrder: Number(field.sortOrder || ((index + 1) * 10))
    })).filter((item) => item.value);
    row.capacitySummary = composeCapacitySummary(row.capacities);
    this.renderTable();
    this.closeCapacityModal();
  }
}
