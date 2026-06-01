function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function createSummaryLines(documentInfo) {
  return [
    `文档状态：${documentInfo.documentStatus || "-"}`,
    `同步状态：${documentInfo.syncStatus || "-"}`,
    `部位：${documentInfo.inspectionPart || "-"}`,
    `施工日期：${documentInfo.constructionDate || "-"}`,
    `容量摘要：${documentInfo.capacitySummary || "-"}`
  ];
}

export class DocumentViewer {
  constructor(options) {
    this.title = options.title;
    this.summary = options.summary;
    this.preview = options.preview;
    this.summaryTreeView = options.summaryTreeView;
    this.summaryPreview = options.summaryPreview;
    this.summarySummary = options.summarySummary;
    this.generateSummaryButton = options.generateSummaryButton;
    this.onCreateDocument = options.onCreateDocument;
    this.onOpenDocument = options.onOpenDocument;
    this.onDeleteDocument = options.onDeleteDocument;
    this.onAddPlanRow = options.onAddPlanRow;
    this.onSelectSummaryNode = options.onSelectSummaryNode;
    this.onGenerateSummary = options.onGenerateSummary;
    this.selectedSummaryNodeId = "";

    this.preview?.addEventListener("click", (event) => {
      const action = event.target.closest("[data-viewer-action]");
      if (!action) {
        return;
      }

      const type = action.dataset.viewerAction;
      if (type === "create") {
        this.onCreateDocument?.();
      } else if (type === "add-plan-row") {
        this.onAddPlanRow?.();
      } else if (type === "open-document") {
        this.onOpenDocument?.();
      } else if (type === "delete-document") {
        this.onDeleteDocument?.();
      }
    });

    this.summaryTreeView?.addEventListener("click", (event) => {
      const button = event.target.closest("[data-summary-node-id]");
      if (!button) {
        return;
      }

      this.selectedSummaryNodeId = button.dataset.summaryNodeId || "";
      this.renderSummaryTree(this._summaryNodes || []);
      this.onSelectSummaryNode?.(this.selectedSummaryNodeId);
    });

    this.generateSummaryButton?.addEventListener("click", () => {
      if (this.selectedSummaryNodeId) {
        this.onGenerateSummary?.(this.selectedSummaryNodeId);
      }
    });
  }

  showTemplateNode(node) {
    if (!this.title || !this.summary || !this.preview) {
      return;
    }

    this.title.textContent = node.name || "检验批模板";
    this.summary.textContent = [node.templateCode || "未配置模板编码", node.moduleName || "Project V2 模板树"].filter(Boolean).join("｜");
    this.preview.innerHTML = `
      <section class="templateInfoPanel">
        <div class="templateInfoGrid">
          <span>模板名称</span><strong>${escapeHtml(node.name)}</strong>
          <span>完整路径</span><strong>${escapeHtml(node.fullPath || node.breadcrumb || "-")}</strong>
          <span>模块</span><strong>${escapeHtml(node.moduleName || "-")}</strong>
          <span>模板编码</span><strong>${escapeHtml(node.templateCode || "-")}</strong>
        </div>
        <div class="actions compactActions">
          <button type="button" class="primary" data-viewer-action="create">新建资料</button>
          <button type="button" data-viewer-action="add-plan-row">加入计划</button>
        </div>
      </section>`;
  }

  showDocument(documentInfo) {
    if (!this.title || !this.summary || !this.preview) {
      return;
    }

    this.title.textContent = documentInfo.documentName || documentInfo.templateName || "已生成资料";
    this.summary.textContent = documentInfo.generatedFilePath || documentInfo.filePath || "已生成文件";
    this.preview.innerHTML = `
      <section class="spreadsheetFileCard">
        <strong>${escapeHtml(documentInfo.documentName || "-")}</strong>
        <p>${escapeHtml(documentInfo.generatedFilePath || documentInfo.filePath || "-")}</p>
        <div class="templateInfoGrid">
          ${createSummaryLines(documentInfo).map((item) => {
            const [label, value] = item.split("：");
            return `<span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong>`;
          }).join("")}
        </div>
        <div class="actions compactActions">
          <button type="button" class="primary" data-viewer-action="open-document">打开表格</button>
          <button type="button" class="danger" data-viewer-action="delete-document">删除资料</button>
        </div>
      </section>`;
  }

  showViewerPlaceholder(message) {
    if (!this.preview) {
      return;
    }
    this.preview.innerHTML = `<p class="emptyText">${escapeHtml(message)}</p>`;
  }

  renderSummaryTree(nodes) {
    this._summaryNodes = nodes;
    if (!this.summaryTreeView || !this.summarySummary) {
      return;
    }

    this.summarySummary.textContent = nodes.length
      ? `已加载 ${nodes.length} 个汇总节点。`
      : "当前单位工程下暂无可汇总资料。";

    if (!nodes.length) {
      this.summaryTreeView.innerHTML = '<p class="emptyText">暂无汇总节点。</p>';
      this.generateSummaryButton.disabled = true;
      return;
    }

    const renderNodes = (items) => `<ul class="summaryTreeList">${items.map((node) => `
      <li class="summaryTreeNode">
        <button type="button" class="summaryTreeButton${this.selectedSummaryNodeId === node.id ? " selected" : ""}" data-summary-node-id="${escapeHtml(node.id)}">
          <strong>${escapeHtml(node.name)}</strong>
          <small>${escapeHtml(`${node.sourceDocumentCount} 份资料 / ${node.inspectionBatchCount} 个检验批`)}</small>
        </button>
        ${(node.children || []).length ? renderNodes(node.children || []) : ""}
      </li>`).join("")}</ul>`;

    this.summaryTreeView.innerHTML = renderNodes(nodes);
    this.generateSummaryButton.disabled = !this.selectedSummaryNodeId;
  }

  renderSummaryPreview(preview) {
    if (!this.summaryPreview) {
      return;
    }

    if (!preview) {
      this.summaryPreview.innerHTML = '<p class="emptyText">请选择左侧汇总节点查看预览。</p>';
      this.generateSummaryButton.disabled = true;
      return;
    }

    this.generateSummaryButton.disabled = false;
    this.summaryPreview.innerHTML = `
      <section class="summaryPreviewCard">
        <div class="summaryPreviewHeader">
          <strong>${escapeHtml(preview.title || "汇总预览")}</strong>
          <span>${escapeHtml(`来源资料 ${preview.totals?.sourceDocumentCount ?? 0} / 检验批 ${preview.totals?.inspectionBatchCount ?? 0}`)}</span>
        </div>
        <div class="tableScroller">
          <table class="summaryTable">
            <thead>
              <tr>
                <th>序号</th>
                <th>名称</th>
                <th>容量</th>
                <th>部位</th>
                <th>数量</th>
                <th>施工单位检查结果</th>
                <th>监理结论</th>
              </tr>
            </thead>
            <tbody>
              ${(preview.rows || []).map((row) => `
                <tr>
                  <td>${row.sequence}</td>
                  <td>${escapeHtml(row.name)}</td>
                  <td>${escapeHtml(row.capacity)}</td>
                  <td>${escapeHtml(row.partName)}</td>
                  <td>${row.count}</td>
                  <td>${escapeHtml(row.constructorResult)}</td>
                  <td>${escapeHtml(row.supervisorConclusion)}</td>
                </tr>`).join("")}
            </tbody>
          </table>
        </div>
      </section>`;
  }
}
