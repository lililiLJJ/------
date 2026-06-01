const state = {
  serviceAvailable: false,
  project: null,
  unitProjects: [],
  activeUnitProjectId: "",
  currentUnitProject: null,
  templateTree: null,
  selectedTemplateNode: null,
  selectedDocument: null,
  inspectionPlans: [],
  currentInspectionPlan: null,
  inspectionPreview: null,
  summaryTree: null,
  selectedSummaryNode: null,
  summaryPreview: null
};

const listeners = new Set();

function notify() {
  for (const listener of listeners) {
    listener(state);
  }
}

export function getState() {
  return state;
}

export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function patchState(partial) {
  Object.assign(state, partial);
  notify();
}

export function setProjectContext(project, unitProjects, activeUnitProjectId, currentUnitProject) {
  patchState({
    project,
    unitProjects,
    activeUnitProjectId,
    currentUnitProject
  });
}
