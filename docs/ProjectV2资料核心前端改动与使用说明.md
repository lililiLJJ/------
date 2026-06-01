# Project V2 资料核心前端改动与使用说明

更新时间：2026-06-02

## 1. 本次改动目标

本次改动用于落地 Project V2 资料核心统一方案，目标是让以下链路在同一套数据源上联动：

`TemplateTreeSnapshot -> InspectionBatchPlan -> InspectionBatchPlanRow -> GeneratedDocumentIndex -> Excel 文件 -> SummaryData`

本轮重点不是继续在旧前端大文件上叠加逻辑，而是先清理污染入口，再用模块化结构重新接入 Project V2。

## 2. 本次功能改动

### 2.1 后端核心能力

- 完成 Project V2 数据模型落地：
  - `TemplateTreeSnapshot`
  - `InspectionBatchPlan`
  - `InspectionBatchPlanRow`
  - `PlanRowCapacity`
  - `GeneratedDocumentIndex`
- 检验批计划行状态拆分为：
  - `status` 生命周期状态
  - `generateStatus` 生成状态
- 已生成资料统一改为从 `GeneratedDocumentIndex` 管理，不再允许模板树、计划区、预览区各自维护一套已生成列表。
- 删除改为软删除，文件进入 `.trash/forms/`。
- 汇总改为从统一资料索引读取活动检验批资料。

### 2.2 Project V2 API

已接入并使用的核心路由包括：

- `GET /api/v2/projects/{projectId}/template-tree`
- `GET /api/v2/projects/{projectId}/documents`
- `POST /api/v2/projects/{projectId}/documents`
- `POST /api/v2/projects/{projectId}/documents/{documentId}/open`
- `DELETE /api/v2/projects/{projectId}/documents/{documentId}`
- `GET /api/v2/projects/{projectId}/inspection-plans`
- `POST /api/v2/projects/{projectId}/inspection-plans`
- `PUT /api/v2/projects/{projectId}/inspection-plans/{planId}`
- `DELETE /api/v2/projects/{projectId}/inspection-plans/{planId}`
- `POST /api/v2/projects/{projectId}/inspection-plans/{planId}/preview`
- `POST /api/v2/projects/{projectId}/inspection-plans/{planId}/generate`
- `GET /api/v2/projects/{projectId}/capacity-configs`
- `GET /api/v2/projects/{projectId}/summary/tree`
- `GET /api/v2/projects/{projectId}/summary/preview`
- `POST /api/v2/projects/{projectId}/summary/generate`

### 2.3 前端入口重构

`src/WpsAddin/app.js` 不再承载整份历史页面逻辑，现已改为“干净启动入口”。

新增前端模块结构：

```text
src/WpsAddin/
├── app.js
├── api/
│   ├── projectApi.js
│   ├── templateApi.js
│   └── inspectionApi.js
├── components/
│   ├── TemplateTree.js
│   ├── PlanEditor.js
│   └── DocumentViewer.js
├── services/
│   ├── syncService.js
│   └── eventBus.js
└── state/
    └── projectState.js
```

本次拆分的目的：

- 避免继续在污染的旧 `app.js` 上修补
- 将 Project V2 API、页面状态、组件渲染、刷新事件拆开
- 为后续继续拆分剩余页面提供稳定基础

### 2.4 模板树与资料预览

- 模板树改为从 Project V2 `template-tree` 读取
- 模板节点与已生成资料节点由统一组件渲染
- 选中模板后，可：
  - 新建资料
  - 直接加入检验批计划
- 选中已生成资料后，可：
  - 查看资料状态
  - 打开资料表
  - 删除资料

### 2.5 检验批计划

已重建的检验批计划前端能力：

- 计划列表
- 当前计划切换
- 草稿计划创建
- 计划行新增
- 从模板树选中模板后加入计划
- 工程层级列折叠/展开
- 容量详情弹窗
- 计划保存
- 计划预览
- 批量生成
- 删除资料后统一刷新

### 2.6 汇总

- 汇总树改为从 Project V2 `summary/tree` 读取
- 汇总预览改为从 `summary/preview` 读取
- 汇总生成改为走 `summary/generate`
- 与模板树、计划区共用统一 `formsChanged` 刷新事件

## 3. 新功能使用说明

### 3.1 启动服务

先确保本地生成服务已启动：

```powershell
dotnet run --project "src/GeneratorService"
```

若首页顶部仍显示“本地生成服务未启动”，请先启动服务后再刷新加载项。

### 3.2 进入 Project V2 工作区

1. 打开 WPS 加载项
2. 选择或打开工程
3. 选择当前单位工程
4. 系统会自动加载：
   - 模板树
   - 检验批计划
   - 汇总树

### 3.3 模板树使用方式

在模板树中：

- 点击模板节点：
  - 右侧显示模板信息
  - 可点击“新建资料”
  - 可点击“加入计划”
- 点击已生成资料节点：
  - 右侧显示资料信息
  - 可点击“打开表格”
  - 可点击“删除资料”

### 3.4 检验批计划使用方式

#### 新建计划

1. 进入“检验批批量生成”
2. 点击“新建计划”
3. 填写计划名称和备注

#### 添加计划行

支持两种方式：

1. 先在模板树选择模板，再点击“加入计划”
2. 在计划区点击“新增行”，再选择检验批模板

模板选定后，系统会自动带出：

- 单位工程
- 分部工程
- 子分部工程
- 分项工程
- 检验批名称

用户补充：

- 部位
- 施工日期
- 容量详情
- 备注

#### 编辑容量

1. 在计划行点击“容量详情”
2. 系统会根据 `templateNodeId` 加载容量字段配置
3. 填写容量项
4. 保存后自动生成容量摘要

#### 保存与预览

1. 点击“保存计划”
2. 点击“预览”
3. 检查：
   - 可生成数量
   - 阻止数量
   - 预检错误或警告

#### 批量生成

1. 勾选需要生成的计划行
2. 点击“批量创建”
3. 生成成功后会自动刷新：
   - 模板树
   - 检验批计划
   - 汇总树
   - 右侧资料预览

### 3.5 删除同步

删除已生成资料时：

- 资料索引会改为删除状态
- Excel 文件会移入 `.trash/forms/`
- 计划行状态会同步刷新
- 汇总树会自动排除已删除资料

### 3.6 汇总使用方式

1. 进入“分部分项汇总”
2. 选择左侧汇总节点
3. 查看右侧汇总预览
4. 点击“生成汇总表”

## 4. 当前实现边界

当前已完成的是 Project V2 资料核心前端主链路，不包含所有历史页面的完全重构。

本轮优先完成：

- 干净入口
- 模块化 API
- 模板树
- 资料预览
- 检验批计划
- 汇总
- 统一刷新机制

后续建议继续拆分的内容：

- 工程管理页面进一步组件化
- 模板管理中心与主页面彻底分层
- 材料管理独立模块化
- 旧页面遗留逻辑逐步迁出

## 5. 验证记录

已执行验证：

```powershell
node --check "src/WpsAddin/app.js"
dotnet build "工程资料制作.sln"
```

验证结果：

- 前端模块语法通过
- 后端构建通过
- 剩余 `NU1900` 为 NuGet 漏洞源访问警告，不影响本次功能构建

