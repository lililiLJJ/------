const WINDOW_NAME = "engineering-docs-inspection-plan-center";
const COMMAND_CHANNEL = "engineering-docs-inspection-plan-center-command";
const COMMAND_STORAGE_KEY = "engineering-docs.inspection-plan-center.command";
const WINDOW_INSTANCE_ID = `plan-center-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
const STANDALONE_URL = "./inspection-batch-plan-center.html?v=20260602-project-v2#inspection-plan-center-window";
const EMBEDDED_URL = "./index.html?view=inspection-plan-center#inspection-plan-center-window";

let popupWindowRef = null;
let fallbackHost = null;

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
      <div id="inspectionPlanCenterWindow" class="desktopWindow">
        <div id="inspectionPlanCenterDragHandle" class="desktopWindowHeader">
          <div>
            <h2 id="inspectionPlanCenterDialogTitle">检验批计划中心</h2>
            <p>当宿主环境无法打开系统独立窗口时，将回退到应用内桌面窗体。</p>
          </div>
          <div class="desktopWindowControls">
            <button id="inspectionPlanCenterMaximize" type="button" title="最大化">□</button>
            <button id="inspectionPlanCenterRestore" type="button" class="hidden" title="还原">❐</button>
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

function clamp(value, min, max) {
  return Math.min(Math.max(value, min), max);
}

function applyRect(host) {
  const { x, y, width, height } = host.state.rect;
  host.window.style.left = `${x}px`;
  host.window.style.top = `${y}px`;
  host.window.style.width = `${width}px`;
  host.window.style.height = `${height}px`;
}

function centerRect() {
  const width = Math.min(window.innerWidth - 48, 1480);
  const height = Math.min(window.innerHeight - 48, 900);
  return {
    width,
    height,
    x: Math.max(24, Math.round((window.innerWidth - width) / 2)),
    y: Math.max(24, Math.round((window.innerHeight - height) / 2))
  };
}

function maximizeFallbackHost(host) {
  if (host.state.maximized) {
    return;
  }
  host.state.previousRect = { ...host.state.rect };
  host.state.rect = {
    x: 16,
    y: 16,
    width: Math.max(960, window.innerWidth - 32),
    height: Math.max(620, window.innerHeight - 32)
  };
  host.state.maximized = true;
  host.maximizeButton.classList.add("hidden");
  host.restoreButton.classList.remove("hidden");
  applyRect(host);
}

function restoreFallbackHost(host) {
  if (!host.state.previousRect) {
    host.state.rect = centerRect();
  } else {
    host.state.rect = { ...host.state.previousRect };
  }
  host.state.maximized = false;
  host.maximizeButton.classList.remove("hidden");
  host.restoreButton.classList.add("hidden");
  applyRect(host);
}

function showFallbackHost(host) {
  host.dialog.classList.remove("hidden");
  if (!host.state.initializedRect) {
    host.state.rect = centerRect();
    host.state.initializedRect = true;
    applyRect(host);
  }
  try {
    host.frame.contentWindow?.focus();
  } catch {
    // Ignore focus failures.
  }
}

function hideFallbackHost(host) {
  host.dialog.classList.add("hidden");
}

function bindDrag(host) {
  const startDrag = (event) => {
    if (host.state.maximized) {
      return;
    }

    event.preventDefault();
    const startX = event.clientX;
    const startY = event.clientY;
    const origin = { ...host.state.rect };
    document.body.classList.add("desktopWindowDragging");

    const onMove = (moveEvent) => {
      host.state.rect.x = clamp(origin.x + moveEvent.clientX - startX, 8, Math.max(8, window.innerWidth - host.state.rect.width - 8));
      host.state.rect.y = clamp(origin.y + moveEvent.clientY - startY, 8, Math.max(8, window.innerHeight - host.state.rect.height - 8));
      applyRect(host);
    };

    const onUp = () => {
      document.body.classList.remove("desktopWindowDragging");
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
    };

    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
  };

  host.dragHandle.addEventListener("pointerdown", startDrag);
}

function bindResize(host) {
  const minWidth = 960;
  const minHeight = 620;

  for (const handle of host.resizeHandles) {
    handle.addEventListener("pointerdown", (event) => {
      if (host.state.maximized) {
        return;
      }

      event.preventDefault();
      const direction = handle.dataset.desktopWindowResize || "";
      const startX = event.clientX;
      const startY = event.clientY;
      const origin = { ...host.state.rect };
      document.body.classList.add("desktopWindowResizing");

      const onMove = (moveEvent) => {
        const next = { ...origin };
        const dx = moveEvent.clientX - startX;
        const dy = moveEvent.clientY - startY;

        if (direction.includes("e")) {
          next.width = Math.max(minWidth, origin.width + dx);
        }
        if (direction.includes("s")) {
          next.height = Math.max(minHeight, origin.height + dy);
        }
        if (direction.includes("w")) {
          next.width = Math.max(minWidth, origin.width - dx);
          next.x = origin.x + (origin.width - next.width);
        }
        if (direction.includes("n")) {
          next.height = Math.max(minHeight, origin.height - dy);
          next.y = origin.y + (origin.height - next.height);
        }

        next.x = clamp(next.x, 8, Math.max(8, window.innerWidth - next.width - 8));
        next.y = clamp(next.y, 8, Math.max(8, window.innerHeight - next.height - 8));
        host.state.rect = next;
        applyRect(host);
      };

      const onUp = () => {
        document.body.classList.remove("desktopWindowResizing");
        window.removeEventListener("pointermove", onMove);
        window.removeEventListener("pointerup", onUp);
      };

      window.addEventListener("pointermove", onMove);
      window.addEventListener("pointerup", onUp);
    });
  }
}

function initializeFallbackHost(host) {
  if (host.state.ready) {
    return host;
  }

  host.closeButtons.forEach((button) => button.addEventListener("click", () => hideFallbackHost(host)));
  host.maximizeButton.addEventListener("click", () => maximizeFallbackHost(host));
  host.restoreButton.addEventListener("click", () => restoreFallbackHost(host));
  host.dialog.addEventListener("click", (event) => {
    if (event.target?.id === "inspectionPlanCenterDialog") {
      hideFallbackHost(host);
    }
  });
  window.addEventListener("resize", () => {
    if (host.state.maximized) {
      host.state.rect = {
        x: 16,
        y: 16,
        width: Math.max(960, window.innerWidth - 32),
        height: Math.max(620, window.innerHeight - 32)
      };
      applyRect(host);
    }
  });

  bindDrag(host);
  bindResize(host);
  host.state.ready = true;
  return host;
}

export function registerInspectionPlanCenterFallbackHost(options = {}) {
  if (fallbackHost) {
    return fallbackHost;
  }

  ensureFallbackMarkup();
  const host = {
    dialog: options.dialog || document.querySelector("#inspectionPlanCenterDialog"),
    window: options.window || document.querySelector("#inspectionPlanCenterWindow"),
    frame: options.frame || document.querySelector("#inspectionPlanCenterFrame"),
    dragHandle: options.dragHandle || document.querySelector("#inspectionPlanCenterDragHandle"),
    closeButtons: options.closeButtons || [
      document.querySelector("#inspectionPlanCenterCloseTop"),
      document.querySelector("#inspectionPlanCenterClose")
    ].filter(Boolean),
    maximizeButton: options.maximizeButton || document.querySelector("#inspectionPlanCenterMaximize"),
    restoreButton: options.restoreButton || document.querySelector("#inspectionPlanCenterRestore"),
    resizeHandles: options.resizeHandles || [...document.querySelectorAll("[data-desktop-window-resize]")],
    state: {
      ready: false,
      initializedRect: false,
      maximized: false,
      previousRect: null,
      rect: centerRect()
    }
  };

  fallbackHost = initializeFallbackHost(host);
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

  if (fallbackHost) {
    showFallbackHost(fallbackHost);
    return true;
  }

  return false;
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
        WINDOW_NAME,
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

  const host = registerInspectionPlanCenterFallbackHost();
  showFallbackHost(host);
  setTimeout(() => dispatchCommandPayload(payload), 120);
  return { mode: "embedded", payload };
}
