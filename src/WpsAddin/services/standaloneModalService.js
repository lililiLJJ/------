const modalHosts = new Map();

function $(selector, root = document) {
  return selector ? root.querySelector(selector) : null;
}

function $$(selector, root = document) {
  return selector ? [...root.querySelectorAll(selector)] : [];
}

function clamp(value, min, max) {
  return Math.min(Math.max(value, min), max);
}

function getViewportLimits() {
  const viewportWidth = Math.max(320, window.innerWidth || 320);
  const viewportHeight = Math.max(320, window.innerHeight || 320);
  return {
    margin: 8,
    minWidth: Math.min(900, Math.max(320, viewportWidth - 16)),
    minHeight: Math.min(600, Math.max(320, viewportHeight - 16)),
    maxWidth: Math.max(320, viewportWidth - 16),
    maxHeight: Math.max(320, viewportHeight - 16)
  };
}

function centerRect() {
  const limits = getViewportLimits();
  const width = Math.min(limits.maxWidth, Math.max(limits.minWidth, Math.round(window.innerWidth * 0.9)));
  const height = Math.min(limits.maxHeight, Math.max(limits.minHeight, Math.round(window.innerHeight * 0.85)));
  return {
    width,
    height,
    x: Math.max(limits.margin, Math.round((window.innerWidth - width) / 2)),
    y: Math.max(limits.margin, Math.round((window.innerHeight - height) / 2))
  };
}

function normalizeRect(rect) {
  const limits = getViewportLimits();
  const width = clamp(rect.width, limits.minWidth, limits.maxWidth);
  const height = clamp(rect.height, limits.minHeight, limits.maxHeight);
  return {
    width,
    height,
    x: clamp(rect.x, limits.margin, Math.max(limits.margin, window.innerWidth - width - limits.margin)),
    y: clamp(rect.y, limits.margin, Math.max(limits.margin, window.innerHeight - height - limits.margin))
  };
}

function applyRect(host) {
  if (!host.window) {
    return;
  }
  host.state.rect = normalizeRect(host.state.rect || centerRect());
  const { x, y, width, height } = host.state.rect;
  host.window.style.left = `${x}px`;
  host.window.style.top = `${y}px`;
  host.window.style.width = `${width}px`;
  host.window.style.height = `${height}px`;
}

function resolveHost(dialogId, options = {}) {
  const dialog = options.dialog || document.getElementById(dialogId);
  if (!dialog) {
    return null;
  }

  const windowElement = options.windowElement
    || $(options.windowSelector, dialog)
    || dialog.firstElementChild;

  if (!windowElement) {
    return null;
  }

  const host = {
    dialogId,
    dialog,
    window: windowElement,
    dragHandle: options.dragHandle || $(options.dragHandleSelector, dialog),
    closeButtons: options.closeButtons || $$(options.closeButtonSelector, dialog),
    maximizeButton: options.maximizeButton || $(options.maximizeButtonSelector, dialog),
    restoreButton: options.restoreButton || $(options.restoreButtonSelector, dialog),
    resizeHandles: options.resizeHandles || $$(options.resizeHandleSelector, dialog),
    body: options.body || $(options.bodySelector, dialog),
    onBeforeClose: options.onBeforeClose,
    onAfterOpen: options.onAfterOpen,
    state: {
      ready: false,
      initializedRect: false,
      maximized: false,
      previousRect: null,
      rect: centerRect()
    }
  };

  modalHosts.set(dialogId, host);
  return host;
}

function getHost(dialogId, options = {}) {
  return modalHosts.get(dialogId) || resolveHost(dialogId, options);
}

function updateMaximizeButtons(host) {
  host.maximizeButton?.classList.toggle("hidden", host.state.maximized);
  host.restoreButton?.classList.toggle("hidden", !host.state.maximized);
}

function bindDrag(host) {
  if (!host.dragHandle) {
    return;
  }

  host.dragHandle.addEventListener("pointerdown", (event) => {
    if (host.state.maximized) {
      return;
    }
    if (event.target?.closest?.("button, input, select, textarea, a")) {
      return;
    }

    event.preventDefault();
    const startX = event.clientX;
    const startY = event.clientY;
    const origin = { ...(host.state.rect || centerRect()) };
    document.body.classList.add("standaloneModalDragging");

    const onMove = (moveEvent) => {
      host.state.rect = {
        ...origin,
        x: origin.x + moveEvent.clientX - startX,
        y: origin.y + moveEvent.clientY - startY
      };
      applyRect(host);
    };

    const onUp = () => {
      document.body.classList.remove("standaloneModalDragging");
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
    };

    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
  });
}

function bindResize(host) {
  for (const handle of host.resizeHandles) {
    handle.addEventListener("pointerdown", (event) => {
      if (host.state.maximized) {
        return;
      }

      event.preventDefault();
      const direction = handle.dataset.standaloneModalResize
        || handle.dataset.desktopWindowResize
        || handle.dataset.templateManagementResize
        || handle.dataset.materialLedgerResize
        || "";
      const startX = event.clientX;
      const startY = event.clientY;
      const origin = { ...(host.state.rect || centerRect()) };
      const limits = getViewportLimits();
      document.body.classList.add("standaloneModalResizing");

      const onMove = (moveEvent) => {
        const next = { ...origin };
        const dx = moveEvent.clientX - startX;
        const dy = moveEvent.clientY - startY;

        if (direction.includes("e")) {
          next.width = clamp(origin.width + dx, limits.minWidth, limits.maxWidth);
        }
        if (direction.includes("s")) {
          next.height = clamp(origin.height + dy, limits.minHeight, limits.maxHeight);
        }
        if (direction.includes("w")) {
          next.width = clamp(origin.width - dx, limits.minWidth, limits.maxWidth);
          next.x = origin.x + (origin.width - next.width);
        }
        if (direction.includes("n")) {
          next.height = clamp(origin.height - dy, limits.minHeight, limits.maxHeight);
          next.y = origin.y + (origin.height - next.height);
        }

        host.state.rect = next;
        applyRect(host);
      };

      const onUp = () => {
        document.body.classList.remove("standaloneModalResizing");
        window.removeEventListener("pointermove", onMove);
        window.removeEventListener("pointerup", onUp);
      };

      window.addEventListener("pointermove", onMove);
      window.addEventListener("pointerup", onUp);
    });
  }
}

function initializeHost(host) {
  if (host.state.ready) {
    return host;
  }

  host.dialog.classList.add("standalone-modal-host");
  host.window.classList.add("standalone-modal");
  if (!host.window.hasAttribute("tabindex")) {
    host.window.setAttribute("tabindex", "-1");
  }
  host.body?.classList.add("standalone-modal__body");

  for (const button of host.closeButtons) {
    button.addEventListener("click", () => closeStandaloneModal(host.dialogId));
  }
  host.maximizeButton?.addEventListener("click", () => toggleStandaloneModalMaximize(host.dialogId, true));
  host.restoreButton?.addEventListener("click", () => toggleStandaloneModalMaximize(host.dialogId, false));
  host.window.addEventListener("pointerdown", () => host.window.focus?.());

  bindDrag(host);
  bindResize(host);
  try {
    if (typeof ResizeObserver === "function") {
      host.resizeObserver = new ResizeObserver(() => {
        if (host.state.maximized || host.dialog.classList.contains("hidden")) {
          return;
        }
        const rect = host.window.getBoundingClientRect();
        host.state.rect = {
          x: rect.left,
          y: rect.top,
          width: rect.width,
          height: rect.height
        };
      });
      host.resizeObserver.observe(host.window);
    }
  } catch {
    // ResizeObserver can be unavailable in older embedded hosts.
  }
  updateMaximizeButtons(host);

  host.state.ready = true;
  return host;
}

export function ensureGlobalModalRoot() {
  let root = document.getElementById("global-modal-root");
  if (!root) {
    root = document.createElement("div");
    root.id = "global-modal-root";
    document.body.appendChild(root);
  }
  return root;
}

export function registerStandaloneModal(dialogId, options = {}) {
  const host = getHost(dialogId, options);
  return host ? initializeHost(host) : null;
}

export function openStandaloneModal(dialogId, options = {}) {
  const host = registerStandaloneModal(dialogId, options);
  const root = ensureGlobalModalRoot();
  if (!host || !root) {
    return null;
  }

  if (host.dialog.parentElement !== root) {
    root.appendChild(host.dialog);
  }

  host.dialog.classList.remove("hidden");
  host.dialog.removeAttribute("aria-hidden");
  if (!host.state.initializedRect) {
    host.state.rect = centerRect();
    host.state.initializedRect = true;
  }
  if (!host.state.maximized) {
    applyRect(host);
  }
  host.onAfterOpen?.(host);
  return host;
}

export function closeStandaloneModal(dialogId) {
  const host = modalHosts.get(dialogId) || resolveHost(dialogId);
  if (!host) {
    return false;
  }

  if (host.onBeforeClose?.(host) === false) {
    return false;
  }
  host.dialog.classList.add("hidden");
  host.dialog.setAttribute("aria-hidden", "true");
  return true;
}

export function toggleStandaloneModalMaximize(dialogId, force) {
  const host = modalHosts.get(dialogId) || resolveHost(dialogId);
  if (!host) {
    return false;
  }

  const shouldMaximize = typeof force === "boolean" ? force : !host.state.maximized;
  if (shouldMaximize === host.state.maximized) {
    return true;
  }

  if (shouldMaximize) {
    host.state.previousRect = { ...(host.state.rect || centerRect()) };
    host.state.maximized = true;
    host.window.classList.add("is-maximized");
  } else {
    host.state.maximized = false;
    host.window.classList.remove("is-maximized");
    host.state.rect = host.state.previousRect ? { ...host.state.previousRect } : centerRect();
    applyRect(host);
  }

  updateMaximizeButtons(host);
  return true;
}

export function isStandaloneModalOpen(dialogId) {
  const host = modalHosts.get(dialogId) || resolveHost(dialogId);
  return !!host && !host.dialog.classList.contains("hidden");
}
