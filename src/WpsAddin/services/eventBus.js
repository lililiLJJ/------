export const EVENTS = {
  formsChanged: "formsChanged",
  projectContextChanged: "projectContextChanged",
  templateSelectionChanged: "templateSelectionChanged",
  documentSelectionChanged: "documentSelectionChanged",
  inspectionPlanSelectionChanged: "inspectionPlanSelectionChanged"
};

const bus = new EventTarget();

export function emit(eventName, detail = {}) {
  bus.dispatchEvent(new CustomEvent(eventName, { detail }));
}

export function on(eventName, listener) {
  const handler = (event) => listener(event.detail, event);
  bus.addEventListener(eventName, handler);
  return () => bus.removeEventListener(eventName, handler);
}
