# Project V2 资料核心前端改动与使用说明

更新时间：2026-06-02

## 1. 本次改动目标

本次改动用于完成 Project V2 资料核心前端的主链路收口，并解决 WPS WebView 对 ES Module 兼容性不稳定的问题。

当前前端主链路为：

`TemplateTreeSnapshot -> InspectionBatchPlan -> InspectionBatchPlanRow -> GeneratedDocumentIndex -> Excel 文件 -> SummaryData`

本轮重点不是继续在旧页面上叠加逻辑，而是：

- 保留 Project V2 后端模型和 API
- 清理旧检验批计划页面残留
- 将检验批计划改为独立工作台
- 保持前端模块化结构
- 增加 Vite 生产打包，保证 WPS 生产环境可加载

## 2. 本次功能改动总结

### 2.1 前端结构重构

`src/WpsAddin/app.js` 已收口为纯启动器，只负责：

- 根据 `view` 选择工作区
- 启动主界面工作区
- 启动检验批计划中心工作区
- 兜底显示启动错误

当前模块结构保持为：

```text
src/WpsAddin/
├── app.js
├── api/
│   ├── projectApi.js
│   ├── templateApi.js
│   └── inspectionApi.js
├── components/
│   ├── TemplateTree.js
│   ├── DocumentViewer.js
│   ├── InspectionBatchPlanCenter.js
│   ├── InspectionBatchPlanList.js
│   └── InspectionBatchPlanEditor.js
├── services/
│   ├── eventBus.js
│   ├── syncService.js
│   └── windowHostService.js
├── state/
│   └── projectState.js
└── workspaces/
    ├── mainWorkspace.js
    └── inspectionPlanCenterWorkspace.js
```

### 2.2 主界面职责收口

主界面现在只负责：

- 查看模板树
- 查看已生成资料
- 打开资料
- 查看右侧预览
- 查看汇总
- 打开检验批计划中心

主界面不再直接承载完整检验批计划编辑区。

`index.html` 中旧检验批计划 DOM、旧批量生成 DOM、旧生成按钮和旧入口残留已清理。

### 2.3 检验批计划中心独立化

新增独立工作台：

- `inspection-batch-plan-center.html`
- `InspectionBatchPlanCenter`
- `InspectionBatchPlanList`
- `InspectionBatchPlanEditor`

计划中心支持：

- 单独窗口打开
- 已有窗口激活
- 计划列表切换
- 计划行新增
- 模板树追加计划行
- 容量详情编辑
- 批量生成
- 统一刷新

### 2.4 模板树与计划中心联动

模板树继续使用统一数据源：

- `TemplateTreeSnapshot`
- `GeneratedDocumentIndex`

主界面模板树支持把当前模板节点加入检验批计划中心。

流程为：

1. 在主界面选择模板节点
2. 点击“加入检验批计划”
3. 打开或激活 `InspectionBatchPlanCenter`
4. 把 `templateNodeId` 带入计划中心
5. 在当前计划中追加一条计划行

### 2.5 统一刷新机制

统一刷新协议固定为 `formsChanged`。

所有以下操作完成后，统一触发该事件：

- 新增计划行
- 删除计划行
- 修改部位
- 修改日期
- 修改容量
- 批量生成
- 删除资料
- 恢复资料

主界面收到后刷新：

- 模板树
- 右侧预览
- 汇总状态

### 2.6 Vite 打包支持

为兼容 WPS WebView，新增了 Vite 打包链路：

- `package.json`
- `vite.config.js`
- `boot-loader.js`
- `package-lock.json`

规则如下：

- 开发模式：允许 `type="module"` 直接加载源码 `app.js`
- 生产模式：加载 `dist/app.bundle.js`
- 保持源码目录模块化，不回退成单文件源码维护

Vite 构建产物目录为：

```text
src/WpsAddin/dist/
├── app.bundle.js
├── boot-loader.js
├── index.html
├── inspection-batch-plan-center.html
├── material-ledger.html
├── template-management.html
├── styles.css
├── main.js
└── js/
```

### 2.7 WPS 最终加载入口调整

WPS 任务窗格入口已调整为优先使用：

`dist/index.html`

只有在显式指定开发模式时，才会回到源码 `index.html`。

这样可以保证：

- 开发时仍可保留模块化源码调试
- 生产安装包中使用稳定的 bundle 入口
- 不需要把业务逻辑重新塞回 `app.js`

## 3. 新功能使用说明

### 3.1 启动服务

先确保本地生成服务已启动：

```powershell
dotnet run --project "src/GeneratorService"
```

如果首页顶部仍显示服务未启动，请先启动后再重新打开加载项。

### 3.2 主界面使用方式

进入主界面后，主要操作为：

1. 打开或切换工程
2. 选择当前单位工程
3. 查看左侧模板树
4. 点击已生成资料进行预览或打开
5. 在汇总区查看汇总结构

主界面中的“检验批计划”页现在只是入口页，不再直接编辑计划。

### 3.3 打开检验批计划中心

有两种常见进入方式：

#### 方式一：从主界面入口按钮进入

1. 打开主界面
2. 进入“检验批计划”入口页
3. 点击【检验批计划】
4. 系统打开或激活 `InspectionBatchPlanCenter`

#### 方式二：从模板树追加模板进入

1. 在模板树中选择某个检验批模板节点
2. 点击“加入检验批计划”
3. 系统打开或激活计划中心
4. 自动把当前模板节点追加到计划中

### 3.4 检验批计划中心使用方式

计划中心分为左右两部分：

- 左侧：计划列表
- 右侧：计划编辑器

#### 左侧计划列表

可查看：

- 计划名称
- 单位工程
- 检验批数量
- 已生成数量
- 最后修改时间

可执行：

- 新建计划
- 重命名计划
- 复制计划
- 删除计划
- 切换计划

#### 右侧计划编辑器

支持：

- 新增计划行
- 插入行
- 删除行
- 多行复制
- 多行粘贴
- 批量填充
- 修改部位
- 修改日期
- 编辑容量详情
- 预检
- 批量生成
- 保存计划

### 3.5 容量详情使用方式

容量不再使用单一文本字段作为真实数据源。

实际流程为：

`CapacityFieldConfig -> PlanRowCapacity -> capacitySummary`

使用方法：

1. 在计划行点击“容量详情”
2. 系统根据当前模板加载容量配置
3. 填写容量项
4. 保存后自动生成容量摘要

注意：

- `capacitySummary` 只用于页面显示和表头填写
- 容量唯一事实来源仍然是 `PlanRowCapacity`

### 3.6 批量生成使用方式

步骤如下：

1. 在计划中心选择当前计划
2. 勾选需要生成的计划行
3. 点击批量生成
4. 系统先执行预检
5. 预检通过后创建 Excel 文件并写入 `GeneratedDocumentIndex`
6. 生成成功后自动触发 `formsChanged`

生成成功后，主界面会同步刷新：

- 模板树
- 右侧资料预览
- 汇总状态

### 3.7 删除同步使用方式

当删除已生成资料时：

- 索引状态更新为删除
- 文件移动到 `.trash/forms/`
- 计划行状态同步刷新
- 主界面模板树同步移除该资料
- 汇总同步排除该资料

## 4. 开发与构建说明

### 4.1 开发模式

源码入口页面：

- `src/WpsAddin/index.html`
- `src/WpsAddin/inspection-batch-plan-center.html`

开发态通过 `boot-loader.js` 加载：

- `type="module"`
- `src/WpsAddin/app.js`

适用于本地调试和模块化开发。

### 4.2 生产模式

先安装依赖：

```powershell
npm install
```

再执行构建：

```powershell
npm run build
```

构建后产物位于：

- `src/WpsAddin/dist/index.html`
- `src/WpsAddin/dist/app.bundle.js`
- `src/WpsAddin/dist/inspection-batch-plan-center.html`

WPS 插件生产环境应加载：

`dist/index.html`

而不是直接加载源码 `index.html`。

### 4.3 开发/生产切换规则

当前 `js/ribbon.js` 中的任务窗格入口规则为：

- 默认：生产模式，打开 `dist/index.html`
- 显式设置开发模式时：打开源码 `index.html`

适合后续安装包和本地调试共存。

## 5. 本次涉及的关键文件

### 前端工作区与组件

- `src/WpsAddin/app.js`
- `src/WpsAddin/workspaces/mainWorkspace.js`
- `src/WpsAddin/workspaces/inspectionPlanCenterWorkspace.js`
- `src/WpsAddin/components/InspectionBatchPlanCenter.js`
- `src/WpsAddin/components/InspectionBatchPlanList.js`
- `src/WpsAddin/components/InspectionBatchPlanEditor.js`
- `src/WpsAddin/components/TemplateTree.js`

### 服务与状态

- `src/WpsAddin/services/syncService.js`
- `src/WpsAddin/services/windowHostService.js`
- `src/WpsAddin/services/eventBus.js`
- `src/WpsAddin/state/projectState.js`

### 打包与入口

- `src/WpsAddin/index.html`
- `src/WpsAddin/inspection-batch-plan-center.html`
- `src/WpsAddin/boot-loader.js`
- `src/WpsAddin/package.json`
- `src/WpsAddin/vite.config.js`
- `src/WpsAddin/js/ribbon.js`

## 6. 验证记录

本轮已执行验证：

```powershell
node --check "src/WpsAddin/boot-loader.js"
node --check "src/WpsAddin/vite.config.js"
node --check "src/WpsAddin/js/ribbon.js"
npm install
npm run build
dotnet build "工程资料制作.sln"
```

验证结果：

- 前端关键入口脚本语法通过
- Vite 构建成功
- `dist/index.html` 与 `dist/app.bundle.js` 已成功生成
- `inspection-batch-plan-center.html` 已纳入构建产物
- 后端构建通过
- 当前仅剩 `NU1900` 的 NuGet 网络警告，不影响本次功能构建

## 7. 当前边界说明

本轮已完成的是 Project V2 前端基础设施和主链路收口，重点是：

- 主界面收口
- 独立计划中心
- 统一刷新
- Vite 打包
- 生产入口切换

本轮没有处理的内容：

- WPS 宿主内的真实点击联调
- 材料管理的 Project V2 迁移
- 模板管理中心的完全重构
- 工程信息页的进一步组件化

建议下一步优先做：

1. 在 WPS WebView 中实测 `dist/index.html`
2. 验证独立计划中心的打开、激活、关闭与刷新
3. 验证模板树到计划中心的追加链路
4. 验证批量生成后主界面同步刷新
