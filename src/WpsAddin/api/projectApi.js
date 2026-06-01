export const SERVICE_BASE_URL = "http://127.0.0.1:5188";

function buildUrl(path) {
  return `${SERVICE_BASE_URL}${path}`;
}

async function parseResponse(response) {
  const text = await response.text();
  if (!text) {
    return {};
  }

  try {
    return JSON.parse(text);
  } catch {
    return { message: text };
  }
}

export async function requestJson(path, options = {}) {
  const headers = new Headers(options.headers || {});
  const requestOptions = {
    method: options.method || "GET",
    cache: "no-store",
    ...options,
    headers
  };

  if (requestOptions.body && !(requestOptions.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(buildUrl(path), requestOptions);
  const payload = await parseResponse(response);
  if (!response.ok) {
    const message = payload?.message || payload?.error || `请求失败：${response.status}`;
    const error = new Error(message);
    error.detail = payload;
    throw error;
  }

  return payload;
}

export function createProjectQuery(projectId) {
  const query = new URLSearchParams();
  if (projectId) {
    query.set("projectId", projectId);
  }
  const text = query.toString();
  return text ? `?${text}` : "";
}

export async function getServiceHealth() {
  return requestJson("/api/health");
}

export async function getCurrentProject() {
  return requestJson("/api/projects/current");
}

export async function updateCurrentProject(payload) {
  return requestJson("/api/projects/current", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function createProject(payload) {
  return requestJson("/api/projects/create", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function openProject(payload) {
  return requestJson("/api/projects/open", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function selectProjectFolder(payload) {
  return requestJson("/api/projects/select-folder", {
    method: "POST",
    body: JSON.stringify(payload || {})
  });
}

export async function listUnitProjects(projectId) {
  return requestJson(`/api/unit-projects${createProjectQuery(projectId)}`);
}

export async function getCurrentUnitProject(projectId) {
  return requestJson(`/api/unit-projects/current${createProjectQuery(projectId)}`);
}

export async function setCurrentUnitProject(projectId, unitProjectId) {
  return requestJson("/api/unit-projects/current", {
    method: "POST",
    body: JSON.stringify({
      projectId,
      unitProjectId
    })
  });
}
