import { cpSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

const rootDir = fileURLToPath(new URL(".", import.meta.url));
const distDir = resolve(rootDir, "dist");
const frontendVersion = "20260603-global-portal";
const htmlShellFiles = [
  "index.html",
  "inspection-batch-plan-center.html",
  "material-ledger.html",
  "template-management.html"
];
const staticFiles = [
  "styles.css",
  "main.js"
];
const staticDirectories = [
  "js"
];

function rewriteIndexShell(html) {
  return html
    .replace(
      /href="\.\/styles\.css[^"]*"/,
      `href="./styles.css?v=${frontendVersion}"`
    )
    .replace(
      /<script(?=[^>]*src="\.\/boot-loader\.js[^"]*")[\s\S]*?<\/script>/,
      `<script src="./boot-loader.js?v=${frontendVersion}" data-build-mode="production" data-bundle-src="./app.bundle.js?v=${frontendVersion}"></script>`
    );
}

function copyStaticShellPlugin() {
  return {
    name: "engineering-docs-copy-static-shell",
    closeBundle() {
      mkdirSync(distDir, { recursive: true });

      for (const fileName of htmlShellFiles) {
        const sourcePath = resolve(rootDir, fileName);
        const targetPath = resolve(distDir, fileName);
        const sourceContent = readFileSync(sourcePath, "utf8");
        const content = fileName === "index.html"
          ? rewriteIndexShell(sourceContent)
          : sourceContent;

        writeFileSync(targetPath, content, "utf8");
      }

      writeFileSync(
        resolve(distDir, "boot-loader.js"),
        readFileSync(resolve(rootDir, "boot-loader.js"), "utf8"),
        "utf8"
      );

      for (const fileName of staticFiles) {
        cpSync(resolve(rootDir, fileName), resolve(distDir, fileName), { force: true });
      }

      for (const directoryName of staticDirectories) {
        cpSync(resolve(rootDir, directoryName), resolve(distDir, directoryName), {
          recursive: true,
          force: true
        });
      }
    }
  };
}

export default defineConfig({
  root: rootDir,
  publicDir: false,
  build: {
    target: "es2018",
    outDir: "dist",
    emptyOutDir: true,
    lib: {
      entry: resolve(rootDir, "app.js"),
      formats: ["iife"],
      name: "EngineeringDocsWpsAddin",
      fileName: () => "app.bundle.js"
    },
    rollupOptions: {
      output: {
        inlineDynamicImports: true
      }
    }
  },
  plugins: [copyStaticShellPlugin()]
});
