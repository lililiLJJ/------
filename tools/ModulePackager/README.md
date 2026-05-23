# 从手动导出的表格生成模块包

当旧软件只能一个个导出 Excel 时，可以先把导出的 `.xls/.xlsx/.et/.ett` 放到一个目录，再用本工具生成我们自己的 `.module`。

## 示例

```powershell
python tools/ModulePackager/create_module_from_exports.py `
  --input-dir "D:/桌面/广东土建2024导出" `
  --output "D:/YY/编程/工程资料制作/Modules/广东土建2024.module" `
  --module-id "gd_building_2024" `
  --name "广东省房屋建筑工程竣工验收技术资料统一用表" `
  --province "广东" `
  --major "房建" `
  --year "2024"
```

生成内容：

- `manifest.json`
- `rules.db`
- `templates/`
- `previews/`
- `config/`

第一版会把所有表格放在一个“广东房建2024”分类下，规则表会写入“待补充规则”占位。后续可以继续增强为按文件夹结构生成多级分类。

## 注意

旧软件导出的 `.xls` 可以打包进模块并打开；但自动占位符填充只支持 `.xlsx`。如果需要自动填工程名称、部位等字段，建议后续批量转换成 `.xlsx`。
