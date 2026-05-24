# 模块化工程资料生成提示 — 双语摘要

## 简短说明（中文）
这是用于本仓库的自动化提示规范，支持基于 `.module` 模块包和 SQLite (`rules.db`) 的模板驱动工程资料生成。关键点：

- 保持 WPS 兼容，不使用 Office 专属 API；模板和规则外置于模块包中，不硬编码。
- 模块结构（`.module`）：`manifest.json`、`rules.db`、`templates/`。
- 所有规范数据从 SQLite 读取；左侧模板树由数据库动态生成。
- 使用 `ProjectDocument` 管理已创建资料，提供 load/save/versions/checkout 接口。
- 所有路径使用相对路径（POSIX 风格内部存储），日志写入 `Logs/`，并使用结构化 JSON 格式。
- 强制执行参数化 SQL、模板输入校验、文件备份与回滚策略，优先保证生成稳定性。
- Git 改动需在修改前检查 `git status`，默认不自动提交；可通过 `--auto-commit` 明确授权。

## Short Summary (English)
This prompt describes the repository-level automation for generating engineering documents based on modular `.module` packages and SQLite-driven rules (`rules.db`). Key points:

- Maintain WPS compatibility; do not use Office-specific APIs. Templates and rules must remain external to code.
- Module layout: `manifest.json`, `rules.db`, `templates/`.
- Rules and templates are read from SQLite; left-side template tree is generated dynamically from the DB.
- Use a `ProjectDocument` model to manage created documents (load/save/versions/checkout).
- Use relative paths (store internally as POSIX), write structured logs to `Logs/`.
- Enforce parameterized SQL, template input validation, file backup and rollback. Prioritize generation stability.
- Check `git status` before changing the repo; do not auto-commit unless explicitly authorized (`--auto-commit`).

## Example invocation
```json
{"project_root":".","module_name":"Modules/gd_building_2024_1.0.0","task":"generate-doc","options":{"template_id":"GD-C3-5182","output":"Export/Generated/"}}
```

## Where to find full spec
See root `.prompt.md` for complete instructions, schema recommendations, logging format and error-response schema.
