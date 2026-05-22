# WPS 加载项 MVP

这个目录先提供可直接运行的任务窗格页面，页面通过 `http://127.0.0.1:5188` 调用本地 C# 生成服务。

## 当前功能

- 控制面板
- 单份生成
- 批量生成
- 授权状态与激活
- 最近日志查看

## 调试方式

1. 先启动本地服务：

```powershell
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
dotnet run --project "src/GeneratorService"
```

2. 用浏览器打开：

```text
src/WpsAddin/index.html
```

3. 后续正式接入 WPS 时，可用 `wpsjs create` 创建加载项工程，再把本目录的 `index.html`、`styles.css`、`app.js` 迁入任务窗格页面。

## 为什么先这样做

WPS 脚手架需要交互选择模板，不适合自动化地稳定生成。先把业务界面做成普通页面，可以更快验证本地服务和生成链路。
