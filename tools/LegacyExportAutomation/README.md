# 旧软件批量导出辅助工具

这个工具用于方案二：模拟人工点击旧软件，把一个个表格导出成 `.xls`。

## 1. 查看窗口

```powershell
python tools/LegacyExportAutomation/legacy_export_automation.py list-windows
```

如果没有列出旧软件窗口，请把旧软件窗口打开到前台，不要最小化。

## 2. 获取按钮坐标

```powershell
python tools/LegacyExportAutomation/legacy_export_automation.py mouse-pos
```

把鼠标移动到旧软件里的按钮、菜单、保存框位置，记录输出的 `x/y`。

## 3. 修改 recipe

复制 `sample-recipe.json`，把里面的点击坐标改成你实际导出一次 Excel 的步骤。

常用动作：

- `click`：点击坐标
- `double_click`：双击坐标
- `key`：按一个键，例如 `ENTER`、`DOWN`
- `hotkey`：组合键，例如 `["CTRL", "S"]`
- `clipboard` + `paste`：把保存路径粘贴进输入框
- `wait`：等待

## 4. 循环导出

```powershell
python tools/LegacyExportAutomation/legacy_export_automation.py run `
  --recipe tools/LegacyExportAutomation/sample-recipe.json `
  --count 10 `
  --output-dir "D:/桌面/广东土建2024导出"
```

如果有模板名称清单，可以加：

```powershell
--names-file "D:/桌面/广东土建2024导出/names.txt"
```

`names.txt` 每行一个模板名，脚本会生成类似：

```text
0001_地下室应急照明导管.xls
0002_一层应急照明导管.xls
```

## 建议

先只设置 `--count 2` 试跑，确认两个文件都能正确导出后，再扩大数量。
