import {
  getServiceHealth,
  getCurrentProject,
  listUnitProjects,
  getCurrentUnitProject,
  setCurrentUnitProject
} from "../api/projectApi.js";
import {
  getTemplateTree,
  getDocument,
  createDocument,
  openDocument,
  deleteDocument,
  getSummaryTree,
  getSummaryPreview,
  generateSummary
} from "../api/templateApi.js";
import { TemplateTree } from "../components/TemplateTree.js";
import { DocumentViewer } from "../components/DocumentViewer.js";
import { emit, EVENTS, on } from "../services/eventBus.js";
import { bindFormsChanged, emitFormsChanged } from "../services/syncService.js";
import { openInspectionPlanCenter, registerInspectionPlanCenterFallbackHost } from "../services/windowHostService.js";
import { getState, patchState, setProjectContext } from "../state/projectState.js";

function $(selector) {
  return document.querySelector(selector);
}

function $$(selector) {
  return [...document.querySelectorAll(selector)];
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function showResult(selector, data) {
  const target = $(selector);
  if (!target) {
    return;
  }

  if (typeof data === "string") {
    target.textContent = data;
    return;
  }

  if (data instanceof Error) {
    target.textContent = data.message || String(data);
    return;
  }

  if (data && typeof data === "object") {
    const message = data.message || data.error || data.detail;
    if (message && Object.keys(data).length <= 2) {
      target.textContent = message;
      return;
    }

    target.textContent = JSON.stringify(data, null, 2);
    return;
  }

  target.textContent = String(data ?? "");
}

function activateTabFromHash() {
  const activeId = window.location.hash.replace(/^#/, "") || "panel";
  for (const panel of $$(".tabPanel")) {
    panel.classList.toggle("active", panel.id === activeId);
  }
}

function getProjectFormData() {
  return Object.fromEntries(new FormData($("#projectInfoForm")).entries());
}

function getGeneratedFormFields(data = {}) {
  const projectData = getProjectFormData();
  return {
    projectName: projectData.projectName || "",
    developerUnitName: projectData.developerName || "",
    constructorUnitName: projectData.constructorName || "",
    designUnitName: projectData.designName || "",
    supervisorUnitName: projectData.supervisorName || "",
    professionalSubcontractorUnitName: projectData.professionalSubcontractorName || "",
    thirdPartyInspectionUnitName: projectData.thirdPartyInspectionName || "",
    partName: data.partName || data.documentName || "",
    inspectionPart: data.partName || data.documentName || "",
    capacity: data.capacity || "",
    capacitySummary: data.capacitySummary || data.capacity || "",
    constructionDate: data.constructionDate || "",
    acceptanceDate: data.acceptanceDate || ""
  };
}

function ensureTemplateContextMenuAction() {
  const menu = $("#templateTreeContextMenu");
  if (!menu || menu.querySelector('[data-tree-context-action="append-plan"]')) {
    return;
  }

  const openButton = menu.querySelector('[data-tree-context-action="open"]');
  const button = document.createElement("button");
  button.type = "button";
  button.dataset.treeContextAction = "append-plan";
  button.textContent = "加入检验批计划";
  openButton?.insertAdjacentElement("afterend", button);
}

function renderProjectContext() {
  const { project, unitProjects, currentUnitProject, activeUnitProjectId } = getState();
  $("#currentProjectSummary").textContent = project
    ? `${project.projectName || "当前工程"}｜${project.projectRootPath || ""}`
    : "尚未打开工程。";
  $("#currentUnitProjectSummary").textContent = currentUnitProject
    ? `当前单位工程：${currentUnitProject.unitProjectName || currentUnitProject.name || ""}｜资料 ${currentUnitProject.documentCount ?? 0}｜材料 ${currentUnitProject.materialCount ?? 0}`
    : "当前单位工程：尚未加载。";

  const select = $("#unitProjectSelect");
  if (select) {
    select.innerHTML = unitProjects.length
      ? unitProjects.map((item) => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.unitProjectName || item.name || "未命名单体工程")}</option>`).join("")
      : '<option value="">暂无单位工程</option>';
    select.value = activeUnitProjectId || "";
  }
}

function normalizeSummaryNodeId(nodes) {
  for (const node of nodes || []) {
    if (node.id) {
      return node.id;
    }
    const child = normalizeSummaryNodeId(node.children || []);
    if (child) {
      return child;
    }
  }
  return "";
}

function findSummaryNode(nodes, id) {
  for (const node of nodes || []) {
    if (node.id === id) {
      return node;
    }
    const child = findSummaryNode(node.children || [], id);
    if (child) {
      return child;
    }
  }
  return null;
}

export function bootstrapMainWorkspace() {
  ensureTemplateContextMenuAction();
  registerInspectionPlanCenterFallbackHost();
  document.body.classList.remove("inspectionPlanCenterStandaloneBody");

  function openPlanCenter(options = {}) {
    const { project, activeUnitProjectId } = getState();
    if (!project?.projectId) {
      showResult("#batchPlanResult", "请先打开工程，再进入检验批计划中心。");
      return;
    }

    openInspectionPlanCenter({
      projectId: project.projectId,
      unitProjectId: activeUnitProjectId,
      ...options
    });
  }

  const templateTree = new TemplateTree({
    host: $("#templateTreeView"),
    summary: $("#templateSummary"),
    contextMenuHost: $("#templateTreeContextMenu"),
    onSelectTemplate: async (node) => {
      patchState({ selectedTemplateNode: node, selectedDocument: null });
      viewer.showTemplateNode(node);
      emit(EVENTS.templateSelectionChanged, { node });
    },
    onSelectDocument: async (node) => {
      const { project, activeUnitProjectId } = getState();
      const documentInfo = await getDocument(project.projectId, activeUnitProjectId, node.documentId || node.id);
      patchState({ selectedDocument: documentInfo });
      viewer.showDocument(documentInfo);
    },
    onDeleteDocument: async (documentId) => {
      const { project, activeUnitProjectId } = getState();
      if (!window.confirm("确定删除该资料吗？文件会移动到 .trash/forms/。")) {
        return;
      }
      const result = await deleteDocument(project.projectId, activeUnitProjectId, documentId);
      showResult("#templateResult", result);
      emitFormsChanged({
        reason: "document-deleted",
        projectId: project.projectId,
        unitProjectId: activeUnitProjectId,
        documentId
      });
    },
    onAppendToPlan: async (node) => {
      openPlanCenter({
        mode: "append-template",
        templateNodeId: node.id
      });
    }
  });

  const viewer = new DocumentViewer({
    title: $("#spreadsheetTitle"),
    summary: $("#spreadsheetSummary"),
    preview: $("#spreadsheetPreview"),
    summaryTreeView: $("#summaryTreeView"),
    summaryPreview: $("#summaryPreview"),
    summarySummary: $("#summarySummary"),
    generateSummaryButton: $("#generateSummary"),
    onCreateDocument: () => openGeneratedDocumentModal(),
    onAddPlanRow: () => {
      openPlanCenter({
        mode: "append-template",
        templateNodeId: getState().selectedTemplateNode?.id || ""
      });
    },
    onOpenDocument: async () => {
      const { project, activeUnitProjectId, selectedDocument } = getState();
      if (!selectedDocument?.documentId) {
        return;
      }
      const result = await openDocument(project.projectId, activeUnitProjectId, selectedDocument.documentId);
      showResult("#templateResult", result);
    },
    onDeleteDocument: async () => {
      const { selectedDocument } = getState();
      if (selectedDocument?.documentId) {
        await templateTree.onDeleteDocument?.(selectedDocument.documentId);
      }
    },
    onSelectSummaryNode: async (summaryNodeId) => {
      const { project, activeUnitProjectId, summaryTree } = getState();
      const node = findSummaryNode(summaryTree?.nodes || [], summaryNodeId);
      if (!node) {
        return;
      }
      patchState({ selectedSummaryNode: node });
      const preview = await getSummaryPreview(project.projectId, activeUnitProjectId, node.summaryType, node.categoryId);
      patchState({ summaryPreview: preview });
      viewer.renderSummaryPreview(preview);
      showResult("#summaryResult", preview);
    },
    onGenerateSummary: async (summaryNodeId) => {
      const { project, activeUnitProjectId, summaryTree } = getState();
      const node = findSummaryNode(summaryTree?.nodes || [], summaryNodeId);
      if (!node) {
        return;
      }
      const result = await generateSummary(project.projectId, {
        projectId: project.projectId,
        unitProjectId: activeUnitProjectId,
        type: node.summaryType,
        categoryId: node.categoryId
      });
      showResult("#summaryResult", result);
      emitFormsChanged({
        reason: "summary-generated",
        projectId: project.projectId,
        unitProjectId: activeUnitProjectId
      });
    }
  });

  async function loadProjectContext() {
    const projectResult = await getCurrentProject();
    const project = projectResult.project || projectResult;
    const unitProjectResult = await listUnitProjects(project.projectId);
    const currentUnitProjectResult = await getCurrentUnitProject(project.projectId);
    const unitProjects = unitProjectResult.items || unitProjectResult.unitProjects || unitProjectResult;
    const currentUnitProject = currentUnitProjectResult.unitProject || currentUnitProjectResult.current || currentUnitProjectResult;
    const activeUnitProjectId = currentUnitProject?.id || currentUnitProject?.unitProjectId || unitProjects[0]?.id || "";
    setProjectContext(project, unitProjects, activeUnitProjectId, currentUnitProject);
    renderProjectContext();
  }

  async function loadTemplateWorkspace() {
    const { project, activeUnitProjectId } = getState();
    if (!project?.projectId) {
      return;
    }

    const tree = await getTemplateTree(project.projectId, activeUnitProjectId);
    patchState({ templateTree: tree });
    templateTree.setTree(tree);
    if (templateTree.getSelectedTemplateNode()) {
      viewer.showTemplateNode(templateTree.getSelectedTemplateNode());
    } else {
      viewer.showViewerPlaceholder("请选择模板节点或已生成资料。");
    }
  }

  async function loadSummaryWorkspace() {
    const { project, activeUnitProjectId } = getState();
    if (!project?.projectId) {
      return;
    }

    const result = await getSummaryTree(project.projectId, activeUnitProjectId);
    patchState({ summaryTree: result });
    viewer.selectedSummaryNodeId = normalizeSummaryNodeId(result.nodes || []);
    viewer.renderSummaryTree(result.nodes || []);
    if (viewer.selectedSummaryNodeId) {
      await viewer.onSelectSummaryNode?.(viewer.selectedSummaryNodeId);
    } else {
      viewer.renderSummaryPreview(null);
    }
  }

  function openGeneratedDocumentModal() {
    const selectedTemplate = getState().selectedTemplateNode;
    if (!selectedTemplate) {
      showResult("#templateResult", "请先选择一个检验批模板。");
      return;
    }

    $("#generatedFormTemplateName").textContent = selectedTemplate.name || "检验批模板";
    $("#generatedFormModal").classList.remove("hidden");
  }

  function closeGeneratedDocumentModal() {
    $("#generatedFormModal").classList.add("hidden");
  }

  async function handleGeneratedDocumentSubmit(event) {
    event.preventDefault();
    const selectedTemplate = getState().selectedTemplateNode;
    const { project, activeUnitProjectId } = getState();
    if (!selectedTemplate || !project?.projectId) {
      return;
    }

    const formData = Object.fromEntries(new FormData($("#generatedFormForm")).entries());
    const payload = {
      unitProjectId: activeUnitProjectId || null,
      templateNodeId: selectedTemplate.id,
      documentName: formData.formName || selectedTemplate.name,
      documentType: "InspectionBatch",
      sourceType: "Manual",
      sourceId: "",
      fields: getGeneratedFormFields({
        documentName: formData.formName,
        partName: formData.formName,
        capacity: formData.capacity || "",
        capacitySummary: formData.capacity || "",
        constructionDate: formData.constructionDate || "",
        acceptanceDate: formData.acceptanceDate || ""
      })
    };

    const result = await createDocument(project.projectId, payload);
    closeGeneratedDocumentModal();
    showResult("#templateResult", result);
    emitFormsChanged({
      reason: "document-created",
      projectId: project.projectId,
      unitProjectId: activeUnitProjectId,
      templateNodeId: selectedTemplate.id
    });
  }

  async function switchUnitProject() {
    const { project } = getState();
    const unitProjectId = $("#unitProjectSelect")?.value || "";
    if (!project?.projectId || !unitProjectId) {
      return;
    }

    await setCurrentUnitProject(project.projectId, unitProjectId);
    const current = getState().unitProjects.find((item) => item.id === unitProjectId) || null;
    patchState({
      activeUnitProjectId: unitProjectId,
      currentUnitProject: current
    });
    renderProjectContext();
    emit(EVENTS.projectContextChanged, {
      projectId: project.projectId,
      unitProjectId
    });
  }

  async function refreshWorkspace() {
    await loadProjectContext();
    await Promise.all([
      loadTemplateWorkspace(),
      loadSummaryWorkspace()
    ]);
  }

  function bindUiEvents() {
    window.addEventListener("hashchange", activateTabFromHash);
    $("#refreshStatus")?.addEventListener("click", () => boot().catch((error) => showResult("#projectManagerResult", error)));
    $("#retryServiceStatus")?.addEventListener("click", () => boot().catch((error) => showResult("#projectManagerResult", error)));
    $("#refreshCurrentProject")?.addEventListener("click", () => refreshWorkspace().catch((error) => showResult("#projectManagerResult", error)));
    $("#switchUnitProject")?.addEventListener("click", () => switchUnitProject().then(() => refreshWorkspace()).catch((error) => showResult("#projectManagerResult", error)));
    $("#refreshSummary")?.addEventListener("click", () => loadSummaryWorkspace().catch((error) => showResult("#summaryResult", error)));
    $("#generatedFormForm")?.addEventListener("submit", (event) => handleGeneratedDocumentSubmit(event).catch((error) => showResult("#templateResult", error)));
    $("#closeGeneratedFormModal")?.addEventListener("click", closeGeneratedDocumentModal);
    $("#cancelGeneratedForm")?.addEventListener("click", closeGeneratedDocumentModal);
    $("#openInspectionPlanCenterButton")?.addEventListener("click", () => openPlanCenter({ mode: "plans" }));
  }

  async function updateServiceStatus() {
    const statusElement = $("#serviceStatus");
    const guide = $("#serviceGuide");
    try {
      statusElement.textContent = "正在检查本地服务...";
      const health = await getServiceHealth();
      patchState({ serviceAvailable: true });
      statusElement.textContent = `服务正常｜版本 ${health.version || "-"}｜AI ${health.aiEnabled ? "已启用" : "未启用"}`;
      statusElement.className = "statusText online";
      guide.classList.add("hidden");
      return true;
    } catch (error) {
      patchState({ serviceAvailable: false });
      statusElement.textContent = "本地生成服务未启动。";
      statusElement.className = "statusText offline";
      guide.classList.remove("hidden");
      showResult("#projectManagerResult", error);
      return false;
    }
  }

  async function boot() {
    activateTabFromHash();
    const serviceReady = await updateServiceStatus();
    if (!serviceReady) {
      return;
    }

    await refreshWorkspace();
  }

  bindUiEvents();
  bindFormsChanged(async () => {
    await Promise.all([
      loadTemplateWorkspace(),
      loadSummaryWorkspace()
    ]);
  });
  on(EVENTS.projectContextChanged, async () => {
    await Promise.all([
      loadTemplateWorkspace(),
      loadSummaryWorkspace()
    ]);
  });

  return {
    boot
  };
}
