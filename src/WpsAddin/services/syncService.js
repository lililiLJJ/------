import { emit, on, EVENTS } from "./eventBus.js";

const FORMS_CHANNEL = "engineering-docs-forms-changed";
const FORMS_STORAGE_KEY = "engineering-docs.forms-changed";
const WINDOW_INSTANCE_ID = `forms-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

function createPayload(detail = {}) {
  return {
    eventId: `forms-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
    senderId: WINDOW_INSTANCE_ID,
    reason: detail.reason || "updated",
    scope: detail.scope || "project",
    affectedTypes: detail.affectedTypes || ["templateTree", "inspectionPlans", "summary", "viewer"],
    timestamp: detail.timestamp || new Date().toISOString(),
    ...detail
  };
}

function broadcast(payload) {
  try {
    if (typeof BroadcastChannel === "function") {
      const channel = new BroadcastChannel(FORMS_CHANNEL);
      channel.postMessage(payload);
      channel.close();
    }
  } catch {
    // Ignore BroadcastChannel failures in embedded hosts.
  }

  try {
    window.localStorage?.setItem(FORMS_STORAGE_KEY, JSON.stringify(payload));
  } catch {
    // Ignore localStorage failures.
  }
}

export function emitFormsChanged(detail = {}) {
  const payload = createPayload(detail);
  emit(EVENTS.formsChanged, payload);
  broadcast(payload);
  return payload;
}

export function bindFormsChanged(listener) {
  const seen = new Set();
  const handle = (payload) => {
    if (!payload || payload.senderId === WINDOW_INSTANCE_ID || !payload.eventId || seen.has(payload.eventId)) {
      return;
    }
    seen.add(payload.eventId);
    if (seen.size > 200) {
      const first = seen.values().next().value;
      if (first) {
        seen.delete(first);
      }
    }
    listener(payload);
  };

  const unbindLocal = on(EVENTS.formsChanged, listener);

  let channel = null;
  try {
    if (typeof BroadcastChannel === "function") {
      channel = new BroadcastChannel(FORMS_CHANNEL);
      channel.addEventListener("message", (event) => handle(event.data));
    }
  } catch {
    channel = null;
  }

  const storageHandler = (event) => {
    if (event.key !== FORMS_STORAGE_KEY || !event.newValue) {
      return;
    }
    try {
      handle(JSON.parse(event.newValue));
    } catch {
      // Ignore malformed storage events.
    }
  };

  try {
    window.addEventListener("storage", storageHandler);
  } catch {
    // Ignore listener failures.
  }

  return () => {
    unbindLocal?.();
    try {
      channel?.close();
    } catch {
      // Ignore cleanup failures.
    }
    try {
      window.removeEventListener("storage", storageHandler);
    } catch {
      // Ignore cleanup failures.
    }
  };
}
