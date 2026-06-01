import { emit, on, EVENTS } from "./eventBus.js";

export function emitFormsChanged(detail = {}) {
  emit(EVENTS.formsChanged, {
    reason: detail.reason || "updated",
    scope: detail.scope || "project",
    affectedTypes: detail.affectedTypes || ["templateTree", "inspectionPlans", "summary", "viewer"],
    timestamp: new Date().toISOString(),
    ...detail
  });
}

export function bindFormsChanged(listener) {
  return on(EVENTS.formsChanged, listener);
}
