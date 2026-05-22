# WPS 加载项接入说明

## 当前接入方式

当前目录 `src/WpsAddin` 已经从普通网页 MVP 补齐为 WPS 表格加载项结构：

```text
package.json   告诉 wpsjs 这是 et / 电子表格加载项
manifest.xml   告诉 WPS 加载项名称和描述
ribbon.xml     定义 WPS 顶部“工程资料”菜单
main.js        WPS 加载项入口脚本
js/ribbon.js   Ribbon 按钮回调
js/util.js     WPS 工具函数
index.html     任务窗格页面
app.js         页面业务逻辑
styles.css     页面样式
```

## 启动调试

先启动本地生成服务：

```powershell
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
cd "D:\YY\编程\工程资料制作"
dotnet run --project "src/GeneratorService"
```

再启动 WPS 加载项调试：

```powershell
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
cd "D:\YY\编程\工程资料制作\src\WpsAddin"
& "C:/Users/ljj/AppData/Roaming/npm/wpsjs.cmd" debug
```

正常情况下会启动一个本地调试服务，常见地址是：

```text
http://127.0.0.1:3889
```

如果端口冲突，再尝试：

```powershell
& "C:/Users/ljj/AppData/Roaming/npm/wpsjs.cmd" debug -p 3890
```

## 代码作用说明

`package.json` 的作用是告诉 `wpsjs` 当前工程是什么类型：

```json
{
  "name": "engineering-docs-wps-addin",
  "addonType": "et"
}
```

其中 `addonType: "et"` 表示这是 WPS 表格加载项。

`manifest.xml` 的作用是告诉 WPS 加载项的名称和描述：

```xml
<Name>工程资料</Name>
<Description>工程资料智能生成系统 WPS 表格加载项</Description>
```

`ribbon.xml` 的作用是定义顶部菜单：

```text
工程资料
├── 控制面板
├── 生成当前资料
├── 批量生成
├── 软件激活
└── 查看日志
```

`main.js` 是 WPS 加载项入口脚本：

```js
document.write("<script src='./js/util.js'></script>");
document.write("<script src='./js/ribbon.js'></script>");
```

它的作用是把工具函数和 Ribbon 回调函数加载到 WPS 能访问的全局环境里。

`js/ribbon.js` 负责接收 Ribbon 按钮点击事件。比如点击“批量生成”时，会打开：

```text
index.html#batch
```

`app.js` 看到 URL 里的 `#batch` 后，会自动切换到“批量生成”页签。

`index.html` 是任务窗格界面，里面的按钮和输入框给 `app.js` 使用。

`app.js` 负责调用本地 C# 服务：

```js
const serviceBaseUrl = "http://127.0.0.1:5188";
```

所以调试 WPS 加载项前，必须先启动 `GeneratorService`。

## 验收标准

```text
WPS 表格顶部出现“工程资料”
点击按钮能打开任务窗格
任务窗格顶部显示“服务正常”
点击生成当前资料后 Export 目录出现 xlsx
查看日志和授权状态正常
```

## 服务未启动提示

如果没有启动 `GeneratorService`，任务窗格会显示：

```text
本地生成服务未启动
复制启动命令
重新检测服务
```

其中“复制启动命令”会复制：

```powershell
cd "D:\YY\编程\工程资料制作"; dotnet run --project "src/GeneratorService"
```

复制后打开 PowerShell 粘贴执行。看到服务监听 `http://127.0.0.1:5188` 后，再回到 WPS 任务窗格点击“重新检测服务”。
