import { requestJson } from "./projectApi.js";

function buildInspectionBase(projectId, planId = "") {
  return planId
    ? `/api/v2/projects/${encodeURIComponent(projectId)}/inspection-plans/${encodeURIComponent(planId)}`
    : `/api/v2/projects/${encodeURIComponent(projectId)}/inspection-plans`;
}

function withUnitProject(url, unitProjectId) {
  if (!unitProjectId) {
    return url;
  }

  const separator = url.includes("?") ? "&" : "?";
  return `${url}${separator}unitProjectId=${encodeURIComponent(unitProjectId)}`;
}

export async function listInspectionPlans(projectId, unitProjectId) {
  return requestJson(withUnitProject(buildInspectionBase(projectId), unitProjectId));
}

export async function getInspectionPlan(projectId, planId) {
  return requestJson(buildInspectionBase(projectId, planId));
}

export async function getInspectionPlanRows(projectId, planId) {
  return requestJson(`${buildInspectionBase(projectId, planId)}/rows`);
}

export async function createInspectionPlan(projectId, payload) {
  return requestJson(buildInspectionBase(projectId), {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function updateInspectionPlan(projectId, planId, payload) {
  return requestJson(buildInspectionBase(projectId, planId), {
    method: "PUT",
    body: JSON.stringify(payload)
  });
}

export async function deleteInspectionPlan(projectId, unitProjectId, planId) {
  return requestJson(withUnitProject(buildInspectionBase(projectId, planId), unitProjectId), {
    method: "DELETE"
  });
}

export async function previewInspectionPlan(projectId, planId) {
  return requestJson(`${buildInspectionBase(projectId, planId)}/preview`, {
    method: "POST"
  });
}

export async function generateInspectionPlan(projectId, planId, payload) {
  return requestJson(`${buildInspectionBase(projectId, planId)}/generate`, {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function getCapacityConfigs(projectId, unitProjectId, templateNodeId) {
  const params = new URLSearchParams();
  if (unitProjectId) {
    params.set("unitProjectId", unitProjectId);
  }
  params.set("templateNodeId", templateNodeId);
  return requestJson(`/api/v2/projects/${encodeURIComponent(projectId)}/capacity-configs?${params.toString()}`);
}
