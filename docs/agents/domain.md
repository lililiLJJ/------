# Domain Docs

How engineering skills should consume this repo's domain documentation when exploring the codebase.

## Product Context

This repo is a single product: an engineering document generation application for construction project records.

The current implementation is a local C# generation service plus a Web frontend currently hosted as a WPS add-in. The strategic direction is to detach the software from WPS and ship it as a Windows desktop application while preserving the existing generation core.

Core product facts:

- `src/GeneratorService` is the business core. It owns projects, unit projects, template trees, inspection batch plans, generated document indexes, material ledgers, license checks, logs, and environment checks.
- `src/WpsAddin` is currently the Web frontend and WPS add-in shell. Treat WPS as the existing host and entry point, not as the business core.
- Document generation must preserve template formatting. The preferred model is copying the original Excel template and replacing placeholders, not rebuilding spreadsheets from scratch.
- `.module` packages, `rules.db`, and template files are source assets for rules and template metadata. Do not hard-code template rules in application code.
- Generated `.xlsx` files may still be opened with the user's system default spreadsheet application, but the app should not require WPS as its host.

## Before Exploring

Read these when they exist and are relevant:

- `CONTEXT.md` at the repo root
- `docs/adr/`
- Existing project docs under `docs/`, especially WPS integration, release package, Project V2, and implementation notes
- `.prompt.md` and `docs/PROMPT_SUMMARY.md` for repository-level generation rules

If `CONTEXT.md` or `docs/adr/` do not exist, proceed silently. Do not create them unless the user asks or a documentation-focused skill calls for them.

## Layout

This is a single-context repo. Prefer one root `CONTEXT.md` and one root `docs/adr/` directory if domain docs are added later.

Important current areas:

- Backend service: `src/GeneratorService`
- Frontend currently hosted by WPS: `src/WpsAddin`
- License issuing tool: `src/LicenseTool`
- Release packaging scripts: `tools/BuildReleasePackage.ps1`
- Templates and module cache: `Templates/`, `Modules/`, `Templates/ModuleCache/`
- Project data and generated forms: `Projects/`

## Vocabulary

Prefer the project's existing terms:

- 工程资料制作软件
- 本地生成服务
- WPS 加载项
- 桌面软件
- 工程 / 单位工程
- 模板树
- 检验批计划
- 计划行
- 容量详情
- 已生成资料索引
- 材料台账
- `.module` 模块包
- `rules.db`
- 模板复制 + 占位符替换

When proposing the WPS removal work, frame it as desktopization or replacing the host shell, not as rewriting the generation engine.

## Decision Guardrails

- Preserve user work in the current dirty worktree. Do not revert unrelated changes.
- Avoid coupling new desktop work to WPS-only files such as `manifest.xml`, `ribbon.xml`, and `js/ribbon.js`.
- Keep the local service API compatible while extracting the frontend from WPS.
- Keep release assets separated between customer runtime files and issuer-only licensing files.
