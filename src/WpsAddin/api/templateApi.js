import { requestJson } from "./projectApi.js";

function buildProjectV2BasePath(projectId) {
  return `/api/v2/projects/${encodeURIComponent(projectId)}`;
}

function buildProjectV2Path(projectId, relativePath = "") {
  const normalized = String(relativePath || "").replace(/^\/+/, "");
  return normalized ? `${buildProjectV2BasePath(projectId)}/${normalized}` : buildProjectV2BasePath(projectId);
}

function withUnitProject(url, unitProjectId) {
  if (!unitProjectId) {
    return url;
  }

  const separator = url.includes("?") ? "&" : "?";
  return `${url}${separator}unitProjectId=${encodeURIComponent(unitProjectId)}`;
}

export async function getTemplateTree(projectId, unitProjectId) {
  return requestJson(withUnitProject(buildProjectV2Path(projectId, "template-tree"), unitProjectId));
}

export async function listDocuments(projectId, unitProjectId, query = {}) {
  const params = new URLSearchParams();
  if (unitProjectId) {
    params.set("unitProjectId", unitProjectId);
  }
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  const text = params.toString();
  return requestJson(`${buildProjectV2Path(projectId, "documents")}${text ? `?${text}` : ""}`);
}

export async function getDocument(projectId, unitProjectId, documentId) {
  return requestJson(withUnitProject(buildProjectV2Path(projectId, `documents/${encodeURIComponent(documentId)}`), unitProjectId));
}

export async function createDocument(projectId, payload) {
  return requestJson(buildProjectV2Path(projectId, "documents"), {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function openDocument(projectId, unitProjectId, documentId) {
  return requestJson(withUnitProject(buildProjectV2Path(projectId, `documents/${encodeURIComponent(documentId)}/open`), unitProjectId), {
    method: "POST"
  });
}

export async function deleteDocument(projectId, unitProjectId, documentId) {
  return requestJson(withUnitProject(buildProjectV2Path(projectId, `documents/${encodeURIComponent(documentId)}`), unitProjectId), {
    method: "DELETE"
  });
}

export async function restoreDocument(projectId, unitProjectId, documentId) {
  return requestJson(withUnitProject(buildProjectV2Path(projectId, `documents/${encodeURIComponent(documentId)}/restore`), unitProjectId), {
    method: "POST"
  });
}

export async function batchDeleteDocuments(projectId, unitProjectId, documentIds) {
  return requestJson(withUnitProject(buildProjectV2Path(projectId, "documents/batch-delete"), unitProjectId), {
    method: "POST",
    body: JSON.stringify({ documentIds })
  });
}

export async function getSummaryTree(projectId, unitProjectId) {
  return requestJson(withUnitProject(buildProjectV2Path(projectId, "summary/tree"), unitProjectId));
}

export async function getSummaryPreview(projectId, unitProjectId, type, categoryId) {
  const params = new URLSearchParams();
  if (unitProjectId) {
    params.set("unitProjectId", unitProjectId);
  }
  params.set("type", type);
  params.set("categoryId", categoryId);
  return requestJson(`${buildProjectV2Path(projectId, "summary/preview")}?${params.toString()}`);
}

export async function generateSummary(projectId, payload) {
  return requestJson(buildProjectV2Path(projectId, "summary/generate"), {
    method: "POST",
    body: JSON.stringify(payload)
  });
}
