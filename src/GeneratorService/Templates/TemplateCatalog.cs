using ClosedXML.Excel;
using GeneratorService.Models;
using Serilog;

namespace GeneratorService.Templates;

public sealed class TemplateCatalog
{
    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;

    public TemplateCatalog(DirectoryInfo rootPath, AppConfig config)
    {
        _rootPath = rootPath;
        _config = config;
    }

    public IReadOnlyList<TemplateInfo> ListTemplates()
    {
        var templatePath = _config.GetTemplatePath(_rootPath);
        Directory.CreateDirectory(templatePath);

        return Directory.GetFiles(templatePath, "*.xlsx")
            .Select(path => new TemplateInfo(Path.GetFileName(path), path))
            .OrderBy(template => template.Name)
            .ToArray();
    }

    public string ResolveTemplate(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            templateName = "钢筋安装检验批.xlsx";
        }

        var templatePath = _config.GetTemplatePath(_rootPath);
        var fullPath = Path.GetFullPath(Path.Combine(templatePath, templateName));
        if (!fullPath.StartsWith(Path.GetFullPath(templatePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("模板名称不合法。");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"模板不存在：{templateName}", fullPath);
        }

        return fullPath;
    }

    public void EnsureSampleTemplates()
    {
        var templatePath = _config.GetTemplatePath(_rootPath);
        Directory.CreateDirectory(templatePath);

        var filePath = Path.Combine(templatePath, "钢筋安装检验批.xlsx");
        if (File.Exists(filePath))
        {
            return;
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("检验批");
        sheet.Cell("A1").Value = "钢筋安装检验批质量验收记录";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 16;
        sheet.Range("A1:F1").Merge();

        sheet.Cell("A3").Value = "工程名称";
        sheet.Cell("B3").Value = "{{静态:工程名称}}";
        sheet.Cell("D3").Value = "施工单位";
        sheet.Cell("E3").Value = "{{静态:施工单位}}";
        sheet.Cell("A4").Value = "监理单位";
        sheet.Cell("B4").Value = "{{静态:监理单位}}";
        sheet.Cell("D4").Value = "施工部位";
        sheet.Cell("E4").Value = "{{静态:施工部位}}";
        sheet.Cell("A5").Value = "分部工程";
        sheet.Cell("B5").Value = "{{静态:分部工程}}";
        sheet.Cell("D5").Value = "分项名称";
        sheet.Cell("E5").Value = "{{静态:分项名称}}";
        sheet.Cell("A6").Value = "施工日期";
        sheet.Cell("B6").Value = "{{静态:施工日期}}";
        sheet.Cell("D6").Value = "验收日期";
        sheet.Cell("E6").Value = "{{静态:验收日期}}";

        sheet.Cell("A8").Value = "主控项目";
        sheet.Cell("A9").Value = "{{规范:主控项目表}}";
        sheet.Cell("A13").Value = "一般项目";
        sheet.Cell("A14").Value = "{{规范:一般项目表}}";
        sheet.Cell("A18").Value = "允许偏差";
        sheet.Cell("A19").Value = "{{规范:允许偏差表}}";
        sheet.Cell("A23").Value = "报验申请语";
        sheet.Cell("B23").Value = "{{AI:申请语}}";
        sheet.Cell("A25").Value = "验收意见";
        sheet.Cell("B25").Value = "{{AI:验收意见}}";
        sheet.Cell("A27").Value = "生成日期";
        sheet.Cell("B27").Value = "{{系统:当前日期}}";
        sheet.Cell("D27").Value = "编号";
        sheet.Cell("E27").Value = "{{系统:编号}}";

        sheet.Column("A").Width = 16;
        sheet.Column("B").Width = 28;
        sheet.Column("C").Width = 12;
        sheet.Column("D").Width = 16;
        sheet.Column("E").Width = 28;
        sheet.Column("F").Width = 12;
        sheet.RangeUsed()!.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.RangeUsed()!.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        workbook.SaveAs(filePath);
        Log.Information("已创建示例模板：{TemplatePath}", filePath);
    }
}
