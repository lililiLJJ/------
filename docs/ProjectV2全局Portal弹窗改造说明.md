# Project V2 全局 Portal 弹窗改造说明

更新日期：2026-06-03

## 1. 改动目标

本次改造将以下三个管理窗口统一迁移到应用级全局弹窗层：

- 检验批计划中心
- 材料台账管理
- 模板管理中心

改造后，三个窗口不再挂载在当前功能模块页面内部，而是统一移动到 `#global-modal-root`。这样可以避免被父级模块的 `overflow`、`height`、`position`、`transform` 或局部滚动容器裁剪。

本次只调整窗口挂载、显示、关闭、缩放、最大化/还原行为，不调整数据模型、后端接口、保存、导出、批量生成等业务逻辑。

## 2. 使用方式

### 2.1 检验批计划中心

入口：

- 主界面点击【检验批计划】
- 模板树中通过“加入检验批计划”打开

行为：

- 直接打开应用级 Portal 弹窗
- 不再优先尝试系统独立窗口
- 再次打开会复用已有弹窗和内部状态
- 最大化后铺满整个插件可视区域

### 2.2 材料台账管理

入口：

- 材料管理页面点击【查看全部】

行为：

- 材料台账窗口会被移动到 `#global-modal-root`
- 表格横向或纵向溢出时，只在弹窗内部滚动
- 关闭只隐藏窗口，不清空已加载台账数据
- 最大化/还原使用统一弹窗逻辑

### 2.3 模板管理中心

入口：

- 模板管理页面点击【打开模板管理中心】

行为：

- 模板管理旧工作区会挂入模板管理弹窗内容区
- 弹窗统一挂载到 `#global-modal-root`
- 不再从主入口调用 `window.open`
- 最小化、关闭会隐藏弹窗；再次点击入口可重新显示

## 3. 技术说明

### 3.1 全局挂载点

主页面新增：

```html
<div id="global-modal-root"></div>
```

该节点位于 `body` 末尾附近，与主业务容器同级，不属于任何功能模块、tab 或局部滚动区域。

### 3.2 统一弹窗服务

新增服务：

```text
src/WpsAddin/services/standaloneModalService.js
```

统一提供：

- `ensureGlobalModalRoot()`
- `openStandaloneModal(dialogId, options)`
- `closeStandaloneModal(dialogId)`
- `toggleStandaloneModalMaximize(dialogId, force)`

打开弹窗时，服务会把对应 dialog 移动到 `#global-modal-root`，并给窗口添加统一的 Portal 弹窗样式。

### 3.3 样式规则

Portal 弹窗使用：

- `position: fixed`
- 高层级 `z-index`
- 默认居中显示
- 默认尺寸约为 `90vw × 85vh`
- 支持最小宽高
- 内容区内部滚动
- 最大化时铺满整个应用可视区域

## 4. 验证记录

已执行：

```powershell
node --check "src/WpsAddin/services/standaloneModalService.js"
node --check "src/WpsAddin/services/windowHostService.js"
node --check "src/WpsAddin/services/legacyFeatureService.js"
npm run build
```

验证结果：

- 三个服务文件语法检查通过
- `src/WpsAddin/services/*.js` 中不再存在 `window.open`
- Vite 构建成功生成 `dist/app.bundle.js`
- 已通过 `tools/SyncWpsAddin.ps1` 同步到 WPS 加载项目录

## 5. 验收要点

在 WPS 中刷新任务窗格或重启 WPS 后验证：

1. 打开检验批计划中心，不再被当前功能模块区域裁剪。
2. 打开材料台账管理，不再被材料页面容器裁剪。
3. 打开模板管理中心，不再被模板管理入口页裁剪。
4. 三个窗口最大化后都铺满整个插件可视区域。
5. 关闭后再次打开，窗口和业务状态正常。
6. 表格内容过多时只在弹窗内部滚动。
