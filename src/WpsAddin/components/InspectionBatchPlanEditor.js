function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function cloneCapacities(items) {
  return (items || []).map((item) => ({ ...item }));
}

function composeCapacitySummary(capacities) {
  return (capacities || [])
    .filter((item) => String(item?.value || "").trim())
    .sort((a, b) => Number(a.sortOrder || 0) - Number(b.sortOrder || 0))
    .map((item) => `${item.capacityName || item.capacityKey || "容量"}${item.value || ""}${item.unit || ""}`)
    .join("；");
}

function formatRowStatus(row) {
  if (String(row.status || "").toLowerCase() === "deleted") {
    return "Deleted";
  }
  return row.generateStatus || "None";
}

function createEmptyRow(templateOption = null, unitProjectName = "") {
  return {
    planRowId: "",
    unitProjectName,
    divisionId: templateOption?.divisionId || "",
    divisionName: templateOption?.divisionName || "",
    subDivisionId: templateOption?.subDivisionId || "",
    subDivisionName: templateOption?.subDivisionName || "",
    subItemId: templateOption?.subItemId || "",
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

function linesToRows(text) {
  return String(text || "")
    .split(/\r?\n/)
    .map((line) => line.trimEnd())
    .filter(Boolean)
    .map((line) => line.split("\t"));
}

export class InspectionBatchPlanEditor {
  constructor(options) {
    this.host = options.host;
    this.showResult = options.showResult;
    this.onSavePlan = options.onSavePlan;
    this.onPreviewPlan = options.onPreviewPlan;
    this.onGeneratePlan = options.onGeneratePlan;
    this.onLoadCapacityConfigs = options.onLoadCapacityConfigs;
    this.onOpenDocument = options.onOpenDocument;
    this.templateOptions = [];
    this.unitProjectName = "";
    this.currentPlan = null;
    this.rows = [];
    this.preview = null;
    this.mode = "plans";
    this.hierarchyCollapsed = false;
    this.dirty = false;
    this.capacityFields = [];
    this.capacityTargets = [];
    this.capacityMode = "single";

    this.renderShell();
    this.bindEvents();
  }

  renderShell() {
    if (!this.host) {
      return;
    }

    this.host.innerHTML = `
      <div class="inspectionPlanEditorShell">
        <div class="inspectionPlanEditorHeader">
          <div class="inspectionPlanEditorMeta">
            <label>计划名称<input type="text" data-plan-input="planName" placeholder="请输入计划名称"></label>
            <label>备注<input type="text" data-plan-input="remark" placeholder="补充楼栋、专业或批次说明"></label>
          </div>
          <div class="inspectionPlanEditorActions">
            <button type="button" data-editor-action="save">保存计划</button>
            <button type="button" data-editor-action="preview">预检</button>
            <button type="button" data-editor-action="generate" class="primary">批量生成</button>
          </div>
        </div>

        <div class="inspectionPlanEditorToolbar">
          <div class="actions compactActions">
            <button type="button" data-editor-action="add-row">新增计划行</button>
            <button type="button" data-editor-action="insert-row">插入行</button>
            <button type="button" data-editor-action="delete-rows" class="danger">删除行</button>
            <button type="button" data-editor-action="copy-rows">复制选中</button>
            <button type="button" data-editor-action="paste-rows">粘贴新增</button>
            <button type="button" data-editor-action="bulk-capacity">批量容量</button>
            <button type="button" data-editor-action="toggle-hierarchy">折叠层级</button>
          </div>
          <div class="inspectionPlanBulkForm">
            <label>批量部位<input type="text" data-bulk-input="inspectionPart" placeholder="批量填写部位"></label>
            <label>批量日期<input type="date" data-bulk-input="constructionDate"></label>
            <div class="actions compactActions">
              <button type="button" data-editor-action="apply-part">应用部位</button>
              <button type="button" data-editor-action="apply-date">应用日期</button>
            </div>
          </div>
        </div>

        <div class="inspectionPlanModeBar">
          <span class="inspectionPlanModeChip" data-plan-mode-label>当前模式：计划编辑</span>
          <span class="inspectionPlanModeChip" data-plan-status-label>未加载计划</span>
        </div>

        <div class="batchPlanTableWrap inspectionPlanTableWrap">
          <table class="batchPlanTable inspectionPlanTable">
            <thead data-plan-table-head></thead>
            <tbody data-plan-table-body></tbody>
          </table>
        </div>

        <section class="batchPlanPreviewPanel inspectionPlanPreviewPanel" data-plan-preview>
          <p class="emptyText">保存计划后点击预检，可查看可生成数量、阻止原因和模板映射提醒。</p>
        </section>

        <div class="modalOverlay hidden" data-plan-capacity-modal role="dialog" aria-modal="true" aria-labelledby="inspectionPlanCapacityTitle">
          <section class="modalPanel compactModal">
            <div class="modalHeader">
              <div>
                <h2 id="inspectionPlanCapacityTitle">容量详情</h2>
                <p data-plan-capacity-summary>根据模板动态填写容量项。</p>
              </div>
              <button type="button" class="iconButton" data-editor-action="close-capacity-modal" title="关闭">×</button>
            </div>
            <form data-plan-capacity-form class="generatedFormForm">
              <div data-plan-capacity-fields class="batchPlanCapacityFields"></div>
              <div class="actions compactActions">
                <button type="button" data-editor-action="cancel-capacity-modal">取消</button>
                <button type="submit" class="primary">保存容量</button>
              </div>
            </form>
          </section>
        </div>
      </div>`;
  }

  bindEvents() {
    this.host?.addEventListener("click", (event) => {
      const action = event.target.closest("[data-editor-action]")?.dataset.editorAction;
      if (action) {
        this.handleAction(action, event).catch((error) => this.report(error));
        return;
      }

      const rowCheck = event.target.closest("[data-row-check]");
      if (rowCheck) {
        this.toggleRowSelection(Number(rowCheck.dataset.rowCheck), rowCheck.checked);
        return;
      }

      const rowAction = event.target.closest("[data-row-action]");
      if (rowAction) {
        this.handleRowAction(rowAction.dataset.rowAction, Number(rowAction.dataset.rowIndex)).catch((error) => this.report(error));
      }
    });

    this.host?.addEventListener("change", (event) => {
      const field = event.target.closest("[data-row-field]");
      if (field) {
        this.updateRowField(Number(field.dataset.rowIndex), field.dataset.rowField, field.value);
        return;
      }

      if (event.target.matches("[data-plan-input]")) {
        this.dirty = true;
        this.renderModeStatus();
      }

      if (event.target.matches("[data-select-all]")) {
        const checked = event.target.checked;
        this.rows = this.rows.map((row) => ({ ...row, _selected: checked }));
        this.renderTable();
      }
    });

    this.host?.querySelector("[data-plan-capacity-form]")?.addEventListener("submit", (event) => {
      event.preventDefault();
      this.saveCapacityModal();
    });
  }

  setContext({ templateOptions, unitProjectName, mode }) {
    this.templateOptions = templateOptions || [];
    this.unitProjectName = unitProjectName || "";
    this.mode = mode || this.mode;
    this.rows = this.rows.map((row) => ({ ...row, unitProjectName: this.unitProjectName || row.unitProjectName || "" }));
    this.renderModeStatus();
    this.renderTable();
  }

  setMode(mode) {
    this.mode = mode || "plans";
    this.renderModeStatus();
    if (mode === "generate") {
      this.host?.querySelector("[data-plan-preview]")?.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }
  }

  setPlan(plan, rows) {
    this.currentPlan = plan;
    this.rows = (rows || []).map((row) => ({
      ...row,
      unitProjectName: row.unitProjectName || this.unitProjectName || "",
      capacities: cloneCapacities(row.capacities),
      _selected: false
    }));
    this.preview = null;
    this.dirty = false;
    this.host.querySelector('[data-plan-input="planName"]').value = plan?.planName || "";
    this.host.querySelector('[data-plan-input="remark"]').value = plan?.remark || "";
    this.renderModeStatus();
    this.renderTable();
    this.renderPreview();
  }

  clearPlan() {
    this.currentPlan = null;
    this.rows = [];
    this.preview = null;
    this.dirty = false;
    this.host.querySelector('[data-plan-input="planName"]').value = "";
    this.host.querySelector('[data-plan-input="remark"]').value = "";
    this.renderModeStatus();
    this.renderTable();
    this.renderPreview();
  }

  hasDirtyChanges() {
    return this.dirty;
  }

  getCurrentPlan() {
    return this.currentPlan;
  }

  getCurrentPlanId() {
    return this.currentPlan?.planId || "";
  }

  appendTemplateRow(templateOption) {
    this.rows = [
      ...this.rows,
      createEmptyRow(templateOption, this.unitProjectName)
    ];
    this.dirty = true;
    this.renderModeStatus();
    this.renderTable();
  }

  buildSavePayload() {
    return {
      unitProjectId: this.currentPlan?.unitProjectId || "",
      planName: this.host.querySelector('[data-plan-input="planName"]').value.trim() || "检验批计划",
      remark: this.host.querySelector('[data-plan-input="remark"]').value.trim() || "",
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
        capacities: cloneCapacities(row.capacities)
      }))
    };
  }

  async saveCurrentPlan() {
    if (!this.currentPlan?.planId) {
      this.report("当前没有可保存的计划。");
      return null;
    }

    const result = await this.onSavePlan?.(this.currentPlan.planId, this.buildSavePayload());
    if (result?.plan) {
      this.setPlan(result.plan, result.plan.rows || this.rows);
    }
    return result;
  }

  async previewCurrentPlan() {
    if (!this.currentPlan?.planId) {
      this.report("当前没有可预检的计划。");
      return null;
    }

    await this.saveCurrentPlan();
    const result = await this.onPreviewPlan?.(this.currentPlan.planId);
    this.preview = result || null;
    this.renderPreview();
    return result;
  }

  async generateCurrentPlan() {
    if (!this.currentPlan?.planId) {
      this.report("当前没有可生成的计划。");
      return null;
    }

    await this.saveCurrentPlan();
    const selectedRowIds = this.rows.filter((row) => row._selected && row.planRowId).map((row) => row.planRowId);
    const result = await this.onGeneratePlan?.(this.currentPlan.planId, {
      selectedRowIds,
      overwrite: false
    });
    return result;
  }

  report(message) {
    this.showResult?.(message);
  }

  handleAction(action) {
    switch (action) {
      case "save":
        return this.saveCurrentPlan();
      case "preview":
        return this.previewCurrentPlan();
      case "generate":
        return this.generateCurrentPlan();
      case "add-row":
        this.rows = [...this.rows, createEmptyRow(null, this.unitProjectName)];
        this.dirty = true;
        this.renderModeStatus();
        this.renderTable();
        return Promise.resolve();
      case "insert-row":
        return Promise.resolve(this.insertRow());
      case "delete-rows":
        return Promise.resolve(this.deleteSelectedRows());
      case "copy-rows":
        return this.copySelectedRows();
      case "paste-rows":
        return this.pasteRows();
      case "toggle-hierarchy":
        this.hierarchyCollapsed = !this.hierarchyCollapsed;
        this.renderTable();
        return Promise.resolve();
      case "apply-part":
        return Promise.resolve(this.applyBulkValue("inspectionPart", this.host.querySelector('[data-bulk-input="inspectionPart"]').value || ""));
      case "apply-date":
        return Promise.resolve(this.applyBulkValue("constructionDate", this.host.querySelector('[data-bulk-input="constructionDate"]').value || ""));
      case "bulk-capacity":
        return this.openCapacityModal(this.getSelectedRowIndexes(), "batch");
      case "close-capacity-modal":
      case "cancel-capacity-modal":
        this.closeCapacityModal();
        return Promise.resolve();
      default:
        return Promise.resolve();
    }
  }

  handleRowAction(action, rowIndex) {
    if (action === "capacity") {
      return this.openCapacityModal([rowIndex], "single");
    }
    if (action === "open") {
      const row = this.rows[rowIndex];
      if (row?.activeDocumentId) {
        return this.onOpenDocument?.(row.activeDocumentId);
      }
    }
    return Promise.resolve();
  }

  getSelectedRowIndexes() {
    return this.rows
      .map((row, index) => ({ row, index }))
      .filter((item) => item.row._selected)
      .map((item) => item.index);
  }

  toggleRowSelection(rowIndex, checked) {
    if (!this.rows[rowIndex]) {
      return;
    }
    this.rows[rowIndex] = { ...this.rows[rowIndex], _selected: checked };
  }

  insertRow() {
    const selectedIndexes = this.getSelectedRowIndexes();
    const insertAt = selectedIndexes.length ? selectedIndexes[selectedIndexes.length - 1] + 1 : this.rows.length;
    this.rows.splice(insertAt, 0, createEmptyRow(null, this.unitProjectName));
    this.rows = [...this.rows];
    this.dirty = true;
    this.renderModeStatus();
    this.renderTable();
  }

  deleteSelectedRows() {
    const selectedIndexes = new Set(this.getSelectedRowIndexes());
    if (!selectedIndexes.size) {
      this.report("请先勾选要删除的计划行。");
      return;
    }

    this.rows = this.rows.filter((_, index) => !selectedIndexes.has(index));
    this.dirty = true;
    this.renderModeStatus();
    this.renderTable();
  }

  async copySelectedRows() {
    const selectedRows = this.rows.filter((row) => row._selected);
    if (!selectedRows.length) {
      this.report("请先勾选要复制的计划行。");
      return;
    }

    const text = selectedRows.map((row) => ([
      row.unitProjectName || this.unitProjectName || "",
      row.divisionName || "",
      row.subDivisionName || "",
      row.subItemName || "",
      row.templateName || "",
      row.inspectionPart || "",
      row.constructionDate || "",
      row.capacitySummary || "",
      row.remark || ""
    ].join("\t"))).join("\n");

    try {
      await navigator.clipboard.writeText(text);
      this.report("已复制选中的计划行。");
    } catch {
      window.prompt("当前宿主不支持直接写入剪贴板，请手动复制下面内容：", text);
    }
  }

  async pasteRows() {
    let text = "";
    try {
      text = await navigator.clipboard.readText();
    } catch {
      text = window.prompt("请粘贴从 Excel 或本工作台复制的计划行内容：", "") || "";
    }

    if (!text.trim()) {
      return;
    }

    const rows = linesToRows(text);
    if (!rows.length) {
      return;
    }

    const created = rows.map((columns) => {
      const next = createEmptyRow(null, this.unitProjectName);
      const values = columns.map((item) => item.trim());
      const descriptor = values[4] || values[0] || "";
      const matchedTemplate = this.findTemplateOptionByLabel(descriptor, values);
      if (matchedTemplate) {
        this.applyTemplateOption(next, matchedTemplate);
      } else if (values.length >= 5) {
        next.divisionName = values[1] || "";
        next.subDivisionName = values[2] || "";
        next.subItemName = values[3] || "";
        next.templateName = values[4] || "";
      }

      if (values.length >= 9) {
        next.inspectionPart = values[5] || "";
        next.constructionDate = values[6] || "";
        next.capacitySummary = values[7] || "";
        next.remark = values[8] || "";
      } else if (values.length >= 5) {
        next.inspectionPart = values[1] || "";
        next.constructionDate = values[2] || "";
        next.capacitySummary = values[3] || "";
        next.remark = values[4] || "";
      } else if (values.length >= 3) {
        next.inspectionPart = values[1] || "";
        next.constructionDate = values[2] || "";
      } else if (values.length === 1) {
        next.inspectionPart = values[0] || "";
      }

      return next;
    });

    this.rows = [...this.rows, ...created];
    this.dirty = true;
    this.renderModeStatus();
    this.renderTable();
    this.report(`已粘贴 ${created.length} 行计划。`);
  }

  applyBulkValue(field, value) {
    const selectedIndexes = this.getSelectedRowIndexes();
    if (!selectedIndexes.length) {
      this.report("请先勾选要批量修改的计划行。");
      return;
    }

    for (const index of selectedIndexes) {
      this.rows[index] = {
        ...this.rows[index],
        [field]: value
      };
    }

    this.dirty = true;
    this.renderModeStatus();
    this.renderTable();
  }

  updateRowField(rowIndex, field, value) {
    const row = this.rows[rowIndex];
    if (!row) {
      return;
    }

    if (field === "templateNodeId") {
      const template = this.templateOptions.find((item) => item.templateNodeId === value);
      if (template) {
        const next = { ...row };
        this.applyTemplateOption(next, template);
        next.capacities = [];
        next.capacitySummary = "";
        this.rows[rowIndex] = next;
      } else {
        this.rows[rowIndex] = {
          ...row,
          templateNodeId: "",
          templateItemId: 0,
          templateName: ""
        };
      }
    } else {
      this.rows[rowIndex] = {
        ...row,
        [field]: value
      };
    }

    this.dirty = true;
    this.renderModeStatus();
    this.renderTable();
  }

  applyTemplateOption(targetRow, templateOption) {
    targetRow.templateNodeId = templateOption.templateNodeId || "";
    targetRow.templateItemId = Number(templateOption.templateItemId || 0);
    targetRow.templateName = templateOption.templateName || "";
    targetRow.divisionId = templateOption.divisionId || "";
    targetRow.divisionName = templateOption.divisionName || "";
    targetRow.subDivisionId = templateOption.subDivisionId || "";
    targetRow.subDivisionName = templateOption.subDivisionName || "";
    targetRow.subItemId = templateOption.subItemId || "";
    targetRow.subItemName = templateOption.subItemName || "";
  }

  findTemplateOptionByLabel(label, columns = []) {
    if (!label) {
      return null;
    }

    const normalized = String(label).trim();
    const division = columns[1] || columns[0] || "";
    const subDivision = columns[2] || "";
    const subItem = columns[3] || "";

    return this.templateOptions.find((item) =>
      item.templateNodeId === normalized
      || item.templateName === normalized
      || item.fullPath === normalized
      || (
        item.templateName === normalized
        && (!division || item.divisionName === division)
        && (!subDivision || item.subDivisionName === subDivision)
        && (!subItem || item.subItemName === subItem)
      )) || null;
  }

  async openCapacityModal(targetIndexes, mode) {
    if (!targetIndexes.length) {
      this.report("请先选择需要填写容量的计划行。");
      return;
    }

    const rows = targetIndexes.map((index) => this.rows[index]).filter(Boolean);
    const templateNodeId = rows[0]?.templateNodeId || "";
    if (!templateNodeId) {
      this.report("请先选择检验批模板，再填写容量详情。");
      return;
    }

    const sameTemplate = rows.every((row) => row.templateNodeId === templateNodeId);
    if (!sameTemplate) {
      this.report("批量容量编辑只支持同一模板的计划行。");
      return;
    }

    const result = await this.onLoadCapacityConfigs?.(templateNodeId);
    this.capacityFields = result?.items || [];
    this.capacityTargets = targetIndexes;
    this.capacityMode = mode;

    const currentRow = rows[0];
    const summary = this.host.querySelector("[data-plan-capacity-summary]");
    const fieldsHost = this.host.querySelector("[data-plan-capacity-fields]");
    summary.textContent = mode === "batch"
      ? `批量容量编辑：将把容量详情应用到 ${targetIndexes.length} 行。`
      : `${currentRow.templateName || "当前模板"}：根据容量配置填写容量详情。`;

    fieldsHost.innerHTML = this.capacityFields.length
      ? this.capacityFields.map((item) => {
        const existing = currentRow.capacities?.find((capacity) => capacity.capacityKey === item.capacityKey);
        return `
          <div class="batchPlanCapacityFieldRow">
            <label>${escapeHtml(item.capacityName)}${item.required ? " *" : ""}</label>
            <input
              type="text"
              data-capacity-key="${escapeHtml(item.capacityKey)}"
              value="${escapeHtml(existing?.value || "")}"
              placeholder="请输入${escapeHtml(item.capacityName)}">
            <small>${escapeHtml(item.defaultUnit || existing?.unit || "")}</small>
          </div>`;
      }).join("")
      : '<p class="emptyText">当前模板未配置容量字段。</p>';

    this.host.querySelector("[data-plan-capacity-modal]").classList.remove("hidden");
  }

  closeCapacityModal() {
    this.host.querySelector("[data-plan-capacity-modal]").classList.add("hidden");
    this.host.querySelector("[data-plan-capacity-fields]").innerHTML = "";
    this.capacityFields = [];
    this.capacityTargets = [];
    this.capacityMode = "single";
  }

  saveCapacityModal() {
    if (!this.capacityTargets.length) {
      this.closeCapacityModal();
      return;
    }

    const inputs = [...this.host.querySelectorAll("[data-capacity-key]")];
    const capacities = this.capacityFields.map((item, index) => {
      const input = inputs.find((node) => node.dataset.capacityKey === item.capacityKey);
      return {
        capacityId: "",
        planRowId: "",
        capacityKey: item.capacityKey,
        capacityName: item.capacityName,
        value: input?.value.trim() || "",
        unit: item.defaultUnit || "",
        sortOrder: item.sortOrder ?? index
      };
    }).filter((item) => item.value);

    const capacitySummary = composeCapacitySummary(capacities);
    for (const index of this.capacityTargets) {
      this.rows[index] = {
        ...this.rows[index],
        capacities: cloneCapacities(capacities),
        capacitySummary
      };
    }

    this.dirty = true;
    this.renderModeStatus();
    this.renderTable();
    this.closeCapacityModal();
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

    return this.hierarchyCollapsed
      ? columns.filter((item) => !["unitProjectName", "divisionName", "subDivisionName", "subItemName"].includes(item.key))
      : columns;
  }

  renderModeStatus() {
    const modeLabel = this.host?.querySelector("[data-plan-mode-label]");
    const statusLabel = this.host?.querySelector("[data-plan-status-label]");
    if (!modeLabel || !statusLabel) {
      return;
    }

    modeLabel.textContent = this.mode === "generate" ? "当前模式：批量生成" : "当前模式：计划编辑";
    if (!this.currentPlan) {
      statusLabel.textContent = "未加载计划";
      return;
    }

    const dirtyText = this.dirty ? "有未保存修改" : "已与服务端同步";
    statusLabel.textContent = `当前计划：${this.currentPlan.planName || "未命名计划"} ｜ ${dirtyText}`;
  }

  renderTable() {
    const head = this.host?.querySelector("[data-plan-table-head]");
    const body = this.host?.querySelector("[data-plan-table-body]");
    if (!head || !body) {
      return;
    }

    const columns = this.getColumns();
    head.innerHTML = `
      <tr>
        <th><input type="checkbox" data-select-all ${this.rows.length && this.rows.every((row) => row._selected) ? "checked" : ""}></th>
        <th>序号</th>
        ${columns.map((column) => `<th>${escapeHtml(column.label)}</th>`).join("")}
        <th>状态</th>
        <th>操作</th>
      </tr>`;

    if (!this.rows.length) {
      body.innerHTML = `<tr><td colspan="${columns.length + 4}" class="emptyText">当前计划还没有计划行，请点击“新增计划行”或从主界面模板树加入。</td></tr>`;
      return;
    }

    const rowsHtml = [];
    let previousGroup = "";
    for (let index = 0; index < this.rows.length; index += 1) {
      const row = this.rows[index];
      const groupKey = [row.divisionName, row.subDivisionName, row.subItemName, row.templateName].join("/");
      if (!this.hierarchyCollapsed && groupKey !== previousGroup) {
        previousGroup = groupKey;
        rowsHtml.push(`
          <tr class="inspectionPlanGroupRow">
            <td colspan="${columns.length + 4}">
              ${escapeHtml([row.divisionName, row.subDivisionName, row.subItemName, row.templateName].filter(Boolean).join(" / ") || "未选择模板")}
            </td>
          </tr>`);
      }

      rowsHtml.push(`
        <tr>
          <td><input type="checkbox" data-row-check="${index}" ${row._selected ? "checked" : ""}></td>
          <td>${index + 1}</td>
          ${columns.map((column) => this.renderCell(row, index, column)).join("")}
          <td><span class="batchPlanStatusBadge ${String(formatRowStatus(row)).toLowerCase()}">${escapeHtml(formatRowStatus(row))}</span></td>
          <td>
            <div class="batchPlanCellActions">
              <button type="button" data-row-action="capacity" data-row-index="${index}">容量详情</button>
              ${row.activeDocumentId ? `<button type="button" data-row-action="open" data-row-index="${index}">打开表格</button>` : ""}
            </div>
          </td>
        </tr>`);
    }

    body.innerHTML = rowsHtml.join("");
  }

  renderCell(row, rowIndex, column) {
    if (column.type === "template") {
      return `
        <td>
          <select data-row-field="templateNodeId" data-row-index="${rowIndex}" class="batchPlanSelectCell">
            <option value="">请选择模板</option>
            ${this.templateOptions.map((option) => `
              <option value="${escapeHtml(option.templateNodeId)}" ${option.templateNodeId === row.templateNodeId ? "selected" : ""}>
                ${escapeHtml(option.templateName)}
              </option>`).join("")}
          </select>
        </td>`;
    }

    if (column.type === "date") {
      return `<td><input type="date" data-row-field="${escapeHtml(column.key)}" data-row-index="${rowIndex}" value="${escapeHtml(row[column.key] || "")}"></td>`;
    }

    if (column.type === "capacity") {
      return `
        <td class="batchPlanReadonlyCell">
          <div class="inspectionPlanCapacitySummaryCell">
            <span>${escapeHtml(row.capacitySummary || "未填写容量详情")}</span>
            <button type="button" data-row-action="capacity" data-row-index="${rowIndex}">编辑</button>
          </div>
        </td>`;
    }

    if (column.readOnly) {
      return `<td class="batchPlanReadonlyCell">${escapeHtml(row[column.key] || (column.key === "unitProjectName" ? this.unitProjectName : ""))}</td>`;
    }

    return `<td><input type="text" data-row-field="${escapeHtml(column.key)}" data-row-index="${rowIndex}" value="${escapeHtml(row[column.key] || "")}"></td>`;
  }

  renderPreview() {
    const host = this.host?.querySelector("[data-plan-preview]");
    if (!host) {
      return;
    }

    if (!this.preview) {
      host.innerHTML = '<p class="emptyText">保存计划后点击预检，可查看可生成数量、阻止原因和模板映射提醒。</p>';
      return;
    }

    const warnings = this.preview.warnings || [];
    host.innerHTML = `
      <div class="batchPlanPreviewHeader">
        <strong>预检结果</strong>
        <span>总计 ${this.preview.totalCount} 行 ｜ 可生成 ${this.preview.generatableCount} 行 ｜ 阻止 ${this.preview.blockedCount} 行</span>
      </div>
      ${warnings.length ? `<ul class="inspectionPlanWarningList">${warnings.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}</ul>` : ""}
      <div class="tableScroller">
        <table class="summaryTable inspectionPlanPreviewTable">
          <thead>
            <tr>
              <th>序号</th>
              <th>检验批名称</th>
              <th>部位</th>
              <th>施工日期</th>
              <th>容量摘要</th>
              <th>结果</th>
              <th>原因</th>
            </tr>
          </thead>
          <tbody>
            ${(this.preview.rows || []).map((row) => `
              <tr>
                <td>${row.rowIndex + 1}</td>
                <td>${escapeHtml(row.templateName || "-")}</td>
                <td>${escapeHtml(row.inspectionPart || "-")}</td>
                <td>${escapeHtml(row.constructionDate || "-")}</td>
                <td>${escapeHtml(row.capacitySummary || "-")}</td>
                <td>${row.canGenerate ? "可生成" : "已阻止"}</td>
                <td>${escapeHtml([...(row.errors || []), ...(row.warnings || [])].join("；") || "-")}</td>
              </tr>`).join("")}
          </tbody>
        </table>
      </div>`;
  }
}
