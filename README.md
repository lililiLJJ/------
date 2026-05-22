# 工程资料智能生成系统（WPS版）

这是一个工程资料智能生成系统 MVP，采用 WPS JS 加载项 + 本地 C# 生成服务的结构。

## MVP 能力

- WPS 插件控制面板
- 本地 HTTP 生成服务
- Excel 模板占位符替换
- SQLite 规范知识库查询
- AI 自由文本生成接口预留
- 离线授权校验 MVP
- 单份生成与批量生成
- 运行日志、生成日志、授权日志、AI 日志

## 快速启动

1. 启动本地服务：

```powershell
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
dotnet run --project "src/GeneratorService"
```

2. 打开浏览器验证：

```text
http://127.0.0.1:5188/api/health
```

3. 打开 `src/WpsAddin/index.html`，填写控制面板并点击生成。

详细操作见 `docs/使用说明.md`。

## 目录说明

```text
src/WpsAddin          WPS 加载项 MVP 页面
src/GeneratorService 本地生成服务
Templates            Excel 模板库
KnowledgeBase        SQLite 知识库
Export               生成输出目录
Logs                 日志目录
docs                 开发与使用文档
```

## 安全约定

- `config.json` 不提交 Git。
- 私钥、激活码生成器、授权文件不提交 Git。
- `Export/` 和 `Logs/` 不提交 Git。
- 规范数据只来自 SQLite，AI 只生成自然语言。
