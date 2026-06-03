import {
  closeStandaloneModal,
  openStandaloneModal,
  registerStandaloneModal
} from "./standaloneModalService.js";

const COMMAND_CHANNEL = "engineering-docs-inspection-plan-center-command";
const COMMAND_STORAGE_KEY = "engineering-docs.inspection-plan-center.command";
const WINDOW_INSTANCE_ID = `plan-center-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
const STANDALONE_URL = "./inspection-batch-plan-center.html#inspection-plan-center-window";
const EMBEDDED_URL = "./index.html?view=inspection-plan-center#inspection-plan-center-window";

let fallbackHost = null;
let popupWindowRef = null;

function createCommandPayload(detail = {}) {
  return {
    commandId: `cmd-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
    senderId: WINDOW_INSTANCE_ID,
    sentAt: new Date().toISOString(),
    mode: "plans",
    ...detail
  };
}

function broadcast(channelName, storageKey, payload) {
  try {
    if (typeof BroadcastChannel === "function") {
      const channel = new BroadcastChannel(channelName);
      channel.postMessage(payload);
      channel.close();
    }
  } catch {
    // Some embedded hosts disable BroadcastChannel.
  }

  try {
    window.localStorage?.setItem(storageKey, JSON.stringify(payload));
  } catch {
    // localStorage can be unavailable inside the host.
  }
}

function bindBroadcast(channelName, storageKey, listener) {
  const seen = new Set();
  const handle = (payload) => {
    if (!payload || payload.senderId === WINDOW_INSTANCE_ID || !payload.commandId || seen.has(payload.commandId)) {
      return;
    }
    seen.add(payload.commandId);
    if (seen.size > 100) {
      const firstKey = seen.values().next().value;
      if (firstKey) {
        seen.delete(firstKey);
      }
    }
    listener(payload);
  };

  let channel = null;
  try {
    if (typeof BroadcastChannel === "function") {
      channel = new BroadcastChannel(channelName);
      channel.addEventListener("message", (event) => handle(event.data));
    }
  } catch {
    channel = null;
  }

  const storageHandler = (event) => {
    if (event.key !== storageKey || !event.newValue) {
      return;
    }
    try {
      handle(JSON.parse(event.newValue));
    } catch {
      // Ignore malformed payloads.
    }
  };

  try {
    window.addEventListener("storage", storageHandler);
  } catch {
    // Ignore storage listener failures.
  }

  try {
    const raw = window.localStorage?.getItem(storageKey);
    if (raw) {
      const payload = JSON.parse(raw);
      const age = Date.now() - Date.parse(payload?.sentAt || "");
      if (!Number.isNaN(age) && age < 30_000) {
        handle(payload);
      }
    }
  } catch {
    // Ignore replay failures.
  }

  return () => {
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

function ensureFallbackMarkup() {
  if (typeof document === "undefined") {
    return null;
  }

  const existing = document.querySelector("#inspectionPlanCenterDialog");
  if (existing) {
    return existing;
  }

  const wrapper = document.createElement("div");
  wrapper.innerHTML = `
    <div id="inspectionPlanCenterDialog" class="desktopWindowDialog hidden" role="dialog" aria-modal="true" aria-labelledby="inspectionPlanCenterDialogTitle">
      <div id="inspectionPlanCenterWindow" class="desktopWindow" tabindex="-1">
        <div id="inspectionPlanCenterDragHandle" class="desktopWindowHeader">
          <div>
            <h2 id="inspectionPlanCenterDialogTitle">检验批计划中心</h2>
            <p>当前使用全局 Portal 弹窗承载，窗口不会被当前功能模块区域裁剪。</p>
          </div>
          <div class="desktopWindowControls">
            <button id="inspectionPlanCenterMaximize" type="button" title="最大化">□</button>
            <button id="inspectionPlanCenterRestore" type="button" class="hidden" title="还原">▣</button>
            <button id="inspectionPlanCenterCloseTop" type="button" title="关闭">×</button>
          </div>
        </div>
        <div class="desktopWindowBody">
          <iframe
            id="inspectionPlanCenterFrame"
            title="检验批计划中心"
            class="desktopWindowFrame"
            src="${EMBEDDED_URL}"></iframe>
        </div>
        <button id="inspectionPlanCenterClose" type="button" class="desktopWindowFooterButton">关闭</button>
        <span class="desktopWindowResizeHandle north" data-desktop-window-resize="n"></span>
        <span class="desktopWindowResizeHandle east" data-desktop-window-resize="e"></span>
        <span class="desktopWindowResizeHandle south" data-desktop-window-resize="s"></span>
        <span class="desktopWindowResizeHandle west" data-desktop-window-resize="w"></span>
        <span class="desktopWindowResizeHandle northEast" data-desktop-window-resize="ne"></span>
        <span class="desktopWindowResizeHandle northWest" data-desktop-window-resize="nw"></span>
        <span class="desktopWindowResizeHandle southEast" data-desktop-window-resize="se"></span>
        <span class="desktopWindowResizeHandle southWest" data-desktop-window-resize="sw"></span>
      </div>
    </div>`;
  document.body.appendChild(wrapper.firstElementChild);
  return document.querySelector("#inspectionPlanCenterDialog");
}

export function registerInspectionPlanCenterFallbackHost() {
  if (fallbackHost) {
    return fallbackHost;
  }

  ensureFallbackMarkup();
  fallbackHost = registerStandaloneModal("inspectionPlanCenterDialog", {
    windowSelector: "#inspectionPlanCenterWindow",
    dragHandleSelector: "#inspectionPlanCenterDragHandle",
    closeButtonSelector: "#inspectionPlanCenterCloseTop, #inspectionPlanCenterClose",
    maximizeButtonSelector: "#inspectionPlanCenterMaximize",
    restoreButtonSelector: "#inspectionPlanCenterRestore",
    resizeHandleSelector: "[data-desktop-window-resize]",
    bodySelector: ".desktopWindowBody",
    onAfterOpen: (host) => {
      try {
        host.dialog.querySelector("#inspectionPlanCenterFrame")?.contentWindow?.focus();
      } catch {
        // Ignore focus failures.
      }
    }
  });
  return fallbackHost;
}

function dispatchCommandPayload(payload) {
  broadcast(COMMAND_CHANNEL, COMMAND_STORAGE_KEY, payload);
  return payload;
}

export function sendInspectionPlanCenterCommand(detail = {}) {
  return dispatchCommandPayload(createCommandPayload(detail));
}

export function bindInspectionPlanCenterCommands(listener) {
  return bindBroadcast(COMMAND_CHANNEL, COMMAND_STORAGE_KEY, listener);
}

export function focusInspectionPlanCenter() {
  if (popupWindowRef && !popupWindowRef.closed) {
    try {
      popupWindowRef.focus();
      return true;
    } catch {
      popupWindowRef = null;
    }
  }
  registerInspectionPlanCenterFallbackHost();
  return !!openStandaloneModal("inspectionPlanCenterDialog");
}

export function closeInspectionPlanCenterFallback() {
  return closeStandaloneModal("inspectionPlanCenterDialog");
}

export function openInspectionPlanCenter(options = {}) {
  const payload = createCommandPayload(options);
  dispatchCommandPayload(payload);

  if (popupWindowRef && !popupWindowRef.closed) {
    try {
      popupWindowRef.focus();
      setTimeout(() => dispatchCommandPayload(payload), 180);
      return { mode: "popup", payload };
    } catch {
      popupWindowRef = null;
    }
  }

  try {
    if (typeof window.open === "function") {
      popupWindowRef = window.open(
        STANDALONE_URL,
        "engineering-docs-inspection-plan-center",
        "popup=yes,width=1560,height=980,resizable=yes,scrollbars=yes"
      );
      if (popupWindowRef) {
        popupWindowRef.focus();
        setTimeout(() => dispatchCommandPayload(payload), 220);
        return { mode: "popup", payload };
      }
    }
  } catch {
    popupWindowRef = null;
  }

  registerInspectionPlanCenterFallbackHost();
  openStandaloneModal("inspectionPlanCenterDialog");
  setTimeout(() => dispatchCommandPayload(payload), 120);
  return { mode: "embedded", payload };
}
