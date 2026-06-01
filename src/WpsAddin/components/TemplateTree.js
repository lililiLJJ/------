function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function normalizeNodeType(node) {
  const rawType = String(node?.nodeType || "").toLowerCase();
  if (rawType === "template") {
    return "template";
  }
  if (rawType === "document") {
    return "document";
  }
  return "folder";
}

function createMeta(node) {
  if (normalizeNodeType(node) === "document") {
    return [node.documentStatus || node.status, node.syncStatus].filter(Boolean).join(" / ");
  }

  if (normalizeNodeType(node) === "template") {
    return [node.templateCode, node.moduleName].filter(Boolean).join(" / ");
  }

  return node.fullPath || node.breadcrumb || "";
}

export class TemplateTree {
  constructor(options) {
    this.host = options.host;
    this.summary = options.summary;
    this.onSelectTemplate = options.onSelectTemplate;
    this.onSelectDocument = options.onSelectDocument;
    this.onDeleteDocument = options.onDeleteDocument;
    this.tree = null;
    this.selectedNodeId = "";

    this.host?.addEventListener("click", (event) => {
      const deleteButton = event.target.closest("[data-template-document-delete]");
      if (deleteButton) {
        event.stopPropagation();
        const documentId = deleteButton.dataset.templateDocumentDelete;
        if (documentId) {
          this.onDeleteDocument?.(documentId);
        }
        return;
      }

      const button = event.target.closest("[data-template-node-id]");
      if (!button) {
        return;
      }

      const nodeId = button.dataset.templateNodeId;
      if (!nodeId) {
        return;
      }

      const node = this.findNode(nodeId);
      if (!node) {
        return;
      }

      this.selectedNodeId = nodeId;
      this.render();
      if (normalizeNodeType(node) === "document") {
        this.onSelectDocument?.(node);
      } else if (normalizeNodeType(node) === "template") {
        this.onSelectTemplate?.(node);
      }
    });
  }

  setTree(tree) {
    this.tree = tree;
    if (!this.selectedNodeId) {
      const firstTemplate = this.getTemplateOptions()[0];
      this.selectedNodeId = firstTemplate?.templateNodeId || "";
    }
    this.render();
  }

  findNode(nodeId, nodes = this.tree?.nodes || []) {
    for (const node of nodes) {
      if (node.id === nodeId) {
        return node;
      }
      const child = this.findNode(nodeId, node.children || []);
      if (child) {
        return child;
      }
    }
    return null;
  }

  getSelectedTemplateNode() {
    const node = this.findNode(this.selectedNodeId);
    return normalizeNodeType(node) === "template" ? node : null;
  }

  getTemplateOptions() {
    const options = [];
    const visit = (nodes, path = []) => {
      for (const node of nodes || []) {
        const nextPath = normalizeNodeType(node) === "folder" ? [...path, node.name] : path;
        if (normalizeNodeType(node) === "template") {
          options.push({
            templateNodeId: node.id,
            templateItemId: Number(node.templateItemId || 0),
            templateName: node.name,
            fullPath: [...path, node.name].join(" / "),
            divisionName: path[0] || "",
            subDivisionName: path[1] || "",
            subItemName: path[2] || ""
          });
        }
        visit(node.children || [], nextPath);
      }
    };
    visit(this.tree?.nodes || []);
    return options;
  }

  render() {
    if (!this.host) {
      return;
    }

    const nodes = this.tree?.nodes || [];
    this.summary.textContent = this.tree
      ? `${this.tree.unitProjectName || "当前单位工程"}｜模块 ${this.tree.moduleName || "-"} ${this.tree.moduleVersion || ""}`.trim()
      : "正在读取模板树...";

    if (!nodes.length) {
      this.host.innerHTML = '<p class="emptyText">当前单位工程下暂无模板树数据。</p>';
      return;
    }

    this.host.innerHTML = this.renderNodes(nodes);
  }

  renderNodes(nodes) {
    return `<ul class="specTreeList">${nodes.map((node) => this.renderNode(node)).join("")}</ul>`;
  }

  renderNode(node) {
    const type = normalizeNodeType(node);
    const selected = this.selectedNodeId === node.id ? " selected" : "";
    const hasChildren = (node.children || []).length > 0;
    return `
      <li class="specTreeNode ${type}${selected}">
        <button type="button" class="specTreeNodeButton" data-template-node-id="${escapeHtml(node.id)}">
          <span class="specTreeNodeLabel">${escapeHtml(node.name)}</span>
          <small>${escapeHtml(createMeta(node))}</small>
        </button>
        ${type === "document" ? `<button type="button" class="iconButton danger" data-template-document-delete="${escapeHtml(node.documentId || node.id)}" title="删除">×</button>` : ""}
        ${hasChildren ? this.renderNodes(node.children || []) : ""}
      </li>`;
  }
}
