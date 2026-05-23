# WPS 加载项 MVP

这个目录提供 WPS 表格加载项 MVP，页面通过 `http://127.0.0.1:5188` 调用本地 C# 生成服务。

## 当前功能

- 工程信息
- 资料生成
- 授权状态与激活
- 最近日志查看

## 浏览器调试

1. 先启动本地服务：

```powershell
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
dotnet run --project "src/GeneratorService"
```

2. 用浏览器打开：

```text
src/WpsAddin/index.html
```

## WPS 调试

正式安装包不需要执行下面的调试命令。客户首次使用时运行发布包 `Client/1-Install-Client.cmd`，之后直接打开 WPS 表格即可使用。

```powershell
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
cd "D:\YY\编程\工程资料制作\src\WpsAddin"
& "C:/Users/ljj/AppData/Roaming/npm/wpsjs.cmd" debug
```

调试服务常见地址是 `http://127.0.0.1:3889`，以命令输出为准。

## 为什么先这样做

WPS 脚手架的 `join` 命令需要交互选择，在非交互终端中会失败。因此本目录手动补齐了与 `et` 模板等价的核心文件。
