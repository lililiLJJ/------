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

## 正式安装

发布包中的客户目录 `Client/` 已经提供正式安装脚本：

```text
1-Install-Client.cmd
```

客户机器上首次使用时双击该脚本即可。安装完成后会自动完成两件事：

- WPS 加载项安装到 `%APPDATA%/kingsoft/wps/jsaddons`
- `GeneratorService.exe` 注册为当前用户登录时静默启动

后续不需要再从终端执行 `wpsjs debug` 或手动启动服务，直接打开 WPS 表格即可使用顶部“工程资料”加载项。

如果安装时 WPS 已经打开，请完全退出 WPS 后重新打开。

## 开发调试

推荐直接双击：

```text
tools/1-启动开发模式.cmd
```

该脚本会自动停止旧的 `GeneratorService`，从源码启动 `src/GeneratorService`，再从 `src/WpsAddin` 启动 `wpsjs debug`。以后查看前端新功能一般只需要刷新或重开 WPS 任务窗格；后端接口变更只需要重新运行开发模式脚本，不需要重新安装发布包。

如果使用的是已经安装到 WPS 的加载项目录，而不是 `wpsjs debug` 调试窗口，修改 `src/WpsAddin` 后需要同步一次插件文件。推荐直接双击：

```text
tools/2-同步WPS加载项.cmd
```

该脚本会把 `src/WpsAddin` 同步到 `%APPDATA%/kingsoft/wps/jsaddons/engineering-docs-wps-addin_0.1.0`，并输出插件版本和关键文件哈希。脚本不会自动关闭 WPS，避免未保存表格丢失；同步完成后请刷新任务窗格，或完全退出 WPS 后重新打开。

注意：`dotnet build` 只构建本地 C# 服务，不会自动更新 WPS 已安装的加载项目录。WPS 已经打开时也可能缓存旧的 JavaScript，所以前端改动同步后仍需要刷新或重启 WPS 加载项页面。

开发时先启动本地生成服务：

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
├── 模板管理
├── 知识库管理
├── AI文本
├── 软件激活
├── 设置
├── 环境自检
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

所以开发调试 WPS 加载项前，必须先启动 `GeneratorService`。正式安装包会通过计划任务自动启动该服务。

## 模板管理

“模板管理”页签调用已有接口：

```text
GET /api/templates
```

它会展示当前 `Templates` 目录里的 `.xlsx` 模板数量、模板名称和完整路径。

这个功能的作用是先让用户确认：

```text
模板服务能连接
模板文件能被后端识别
控制面板和批量生成使用的是同一份模板列表
```

当前阶段只做模板查看和刷新，不直接在 WPS 插件里删除、覆盖模板。

原因是模板是工程资料系统的核心资产，删除和覆盖操作风险较高，后续需要增加备份、校验和权限确认后再做。

## 知识库管理

“知识库管理”页签调用接口：

```text
GET /api/knowledge/items
```

可选查询参数：

```text
division  分部工程
subItem   分项名称
itemType  项目类型
```

它会展示 SQLite 知识库中的规范数据，包括：

```text
专业、分部、分项、项目类型、项目名称、合格标准、允许偏差、检查方法、引用标准、版本
```

当前阶段只做只读查看，不在界面里新增、修改、删除规范数据。

原因是规范数据必须可追溯、可审核，后续写入功能需要增加导入校验、版本记录和审核流程。

## AI文本

“AI文本”页签调用接口：

```text
POST /api/ai/preview
```

它只允许生成这些自由文本：

```text
申请语、验收意见、备注说明、试验过程
```

接口会拒绝主控项目、一般项目、允许偏差、检查方法、规范条文和实测值等内容。

如果 `config.json` 或 `config.example.json` 中 `EnableAI` 为 `false`，或者没有配置 DeepSeek `ApiKey`，服务会返回兜底文本，保证资料生成流程不被 AI 接口阻塞。

## 设置

“设置”页签调用接口：

```text
GET /api/settings
```

当前只做只读查看，展示：

```text
服务端口、AI启用状态、模板目录、输出目录、知识库路径、日志目录、DeepSeek地址、DeepSeek模型、API Key是否已配置
```

接口不会返回 DeepSeek `ApiKey` 原文，只返回是否已配置。

如果需要修改配置，仍然编辑 `config.json`，然后重启 `GeneratorService`。

## 软件激活

授权页基于以下接口：

```text
GET /api/license/status
POST /api/license/activate
```

当前界面会展示：

```text
机器码 Hash
授权类型
授权模块
到期时间
```

并提供“复制机器码”按钮，方便把当前电脑的机器码发送给授权方生成离线激活码。

## 环境自检

“环境自检”页签调用接口：

```text
GET /api/environment/check
```

当前会检查：

```text
本地服务
模板目录
知识库文件
日志目录
输出目录
AI 配置
授权状态
```

每一项会显示：

```text
正常 / 警告 / 错误
```

用于装机、调试、售后时快速判断环境是否完整。

## 验收标准

```text
WPS 表格顶部出现“工程资料”
点击按钮能打开任务窗格
任务窗格顶部显示“服务正常”
模板管理能列出 Templates 目录下的 xlsx 模板
知识库管理能查询到钢筋安装规范数据
AI文本能预览申请语或验收意见
设置能显示服务端口、模板目录、输出目录、知识库路径和AI状态
软件激活能显示机器码、授权类型、授权模块和到期时间
环境自检能显示正常、警告、错误统计和每项检查结果
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

其中“复制启动命令”会复制开发调试命令：

```powershell
cd "D:\YY\编程\工程资料制作"; dotnet run --project "src/GeneratorService"
```

正式安装包场景下，如果服务未启动，优先重新运行 `Client/1-Install-Client.cmd`；开发调试场景下，复制后打开 PowerShell 粘贴执行。看到服务监听 `http://127.0.0.1:5188` 后，再回到 WPS 任务窗格点击“重新检测服务”。
