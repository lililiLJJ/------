(function loadEngineeringDocsApp() {
  const search = new URLSearchParams(window.location.search);
  const currentScript = document.currentScript
    || document.querySelector('script[src*="boot-loader.js"]');
  const dataset = currentScript?.dataset || {};
  const buildMode = dataset.buildMode || search.get("buildMode") || "development";
  const moduleSrc = dataset.moduleSrc || "./app.js";
  const bundleSrc = dataset.bundleSrc || "./app.bundle.js";
  const forceBundle = buildMode === "production"
    || search.get("bundle") === "1"
    || window.localStorage?.getItem("engineering-docs.forceBundle") === "1";
  const supportsModuleScripts = "noModule" in document.createElement("script");

  function appendScript(attributes) {
    const script = document.createElement("script");
    for (const [key, value] of Object.entries(attributes)) {
      if (value === true) {
        script.setAttribute(key, "");
      } else if (value !== false && value != null) {
        script.setAttribute(key, value);
      }
    }
    document.body.appendChild(script);
    return script;
  }

  function showBootError(message) {
    const pre = document.createElement("pre");
    pre.className = "resultBox";
    pre.textContent = message;
    document.body.appendChild(pre);
  }

  if (!forceBundle && supportsModuleScripts) {
    appendScript({
      type: "module",
      src: moduleSrc
    });
    return;
  }

  const bundleScript = appendScript({
    src: bundleSrc
  });

  bundleScript.addEventListener("error", () => {
    showBootError("当前宿主未启用 ES Module，且 Vite bundle 尚未生成。请先执行 `npm install` 和 `npm run build`。");
  });
})();
