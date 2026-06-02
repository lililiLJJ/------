function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

export function normalizeTemplateTreeNodeType(node) {
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
  if (normalizeTemplateTreeNodeType(node) === "document") {
    return [node.documentStatus || node.status, node.syncStatus].filter(Boolean).join(" / ");
  }

  if (normalizeTemplateTreeNodeType(node) === "template") {
    return [node.templateCode, node.moduleName].filter(Boolean).join(" / ");
  }

  return node.fullPath || node.breadcrumb || "";
}

export function collectTemplateOptions(tree) {
  const options = [];
  const visit = (nodes, path = [], idPath = []) => {
    for (const node of nodes || []) {
      const type = normalizeTemplateTreeNodeType(node);
      const nextPath = type === "folder" ? [...path, node.name] : path;
      const nextIdPath = type === "folder" ? [...idPath, node.id] : idPath;
      if (type === "template") {
        options.push({
          templateNodeId: node.id,
          templateItemId: Number(node.templateItemId || 0),
          templateName: node.name,
          fullPath: [...path, node.name].join(" / "),
          divisionId: nextIdPath[0] || node.pathIds?.[0] || "",
          divisionName: path[0] || "",
          subDivisionId: nextIdPath[1] || node.pathIds?.[1] || "",
          subDivisionName: path[1] || "",
          subItemId: nextIdPath[2] || node.pathIds?.[2] || "",
          subItemName: path[2] || ""
        });
      }
      visit(node.children || [], nextPath, nextIdPath);
    }
  };
  visit(tree?.nodes || []);
  return options;
}

export class TemplateTree {
  constructor(options) {
    this.host = options.host;
    this.summary = options.summary;
    this.contextMenuHost = options.contextMenuHost || null;
    this.onSelectTemplate = options.onSelectTemplate;
    this.onSelectDocument = options.onSelectDocument;
    this.onDeleteDocument = options.onDeleteDocument;
    this.onAppendToPlan = options.onAppendToPlan;
    this.tree = null;
    this.selectedNodeId = "";
    this.contextNodeId = "";

    this.bindTreeEvents();
    this.bindContextMenuEvents();
  }

  bindTreeEvents() {
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

      this.activateNode(nodeId);
    });

    this.host?.addEventListener("contextmenu", (event) => {
      const button = event.target.closest("[data-template-node-id]");
      if (!button || !this.contextMenuHost) {
        return;
      }

      const nodeId = button.dataset.templateNodeId || "";
      const node = this.findNode(nodeId);
      if (!node) {
        return;
      }

      event.preventDefault();
      this.activateNode(nodeId, { triggerCallbacks: false });
      this.contextNodeId = nodeId;
      this.showContextMenu(node, event.clientX, event.clientY);
    });
  }

  bindContextMenuEvents() {
    if (!this.contextMenuHost) {
      return;
    }

    this.contextMenuHost.addEventListener("click", (event) => {
      const action = event.target.closest("[data-tree-context-action]")?.dataset.treeContextAction;
      if (!action) {
        return;
      }

      const node = this.findNode(this.contextNodeId);
      this.hideContextMenu();
      if (!node) {
        return;
      }

      const type = normalizeTemplateTreeNodeType(node);
      if (action === "open") {
        this.activateNode(node.id);
      } else if (action === "delete" && type === "document") {
        this.onDeleteDocument?.(node.documentId || node.id);
      } else if (action === "append-plan" && type === "template") {
        this.onAppendToPlan?.(node);
      }
    });

    document.addEventListener("click", () => this.hideContextMenu());
    window.addEventListener("blur", () => this.hideContextMenu());
    window.addEventListener("scroll", () => this.hideContextMenu(), true);
  }

  showContextMenu(node, x, y) {
    if (!this.contextMenuHost) {
      return;
    }

    const type = normalizeTemplateTreeNodeType(node);
    const openButton = this.contextMenuHost.querySelector('[data-tree-context-action="open"]');
    const renameButton = this.contextMenuHost.querySelector('[data-tree-context-action="rename"]');
    const deleteButton = this.contextMenuHost.querySelector('[data-tree-context-action="delete"]');
    const revealButton = this.contextMenuHost.querySelector('[data-tree-context-action="reveal"]');
    const appendPlanButton = this.contextMenuHost.querySelector('[data-tree-context-action="append-plan"]');

    if (openButton) {
      openButton.disabled = false;
    }
    if (renameButton) {
      renameButton.disabled = true;
    }
    if (revealButton) {
      revealButton.disabled = true;
    }
    if (deleteButton) {
      deleteButton.disabled = type !== "document";
    }
    if (appendPlanButton) {
      appendPlanButton.hidden = type !== "template";
      appendPlanButton.disabled = type !== "template";
    }

    this.contextMenuHost.style.left = `${x}px`;
    this.contextMenuHost.style.top = `${y}px`;
    this.contextMenuHost.classList.remove("hidden");
  }

  hideContextMenu() {
    this.contextMenuHost?.classList.add("hidden");
  }

  activateNode(nodeId, options = {}) {
    const node = this.findNode(nodeId);
    if (!node) {
      return;
    }

    this.selectedNodeId = nodeId;
    this.render();
    if (options.triggerCallbacks === false) {
      return;
    }

    if (normalizeTemplateTreeNodeType(node) === "document") {
      this.onSelectDocument?.(node);
    } else if (normalizeTemplateTreeNodeType(node) === "template") {
      this.onSelectTemplate?.(node);
    }
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
    return normalizeTemplateTreeNodeType(node) === "template" ? node : null;
  }

  getTemplateOptions() {
    return collectTemplateOptions(this.tree);
  }

  render() {
    if (!this.host) {
      return;
    }

    const nodes = this.tree?.nodes || [];
    this.summary.textContent = this.tree
      ? `${this.tree.unitProjectName || "当前单位工程"}｜模板 ${this.tree.moduleName || "-"} ${this.tree.moduleVersion || ""}`.trim()
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
    const type = normalizeTemplateTreeNodeType(node);
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
