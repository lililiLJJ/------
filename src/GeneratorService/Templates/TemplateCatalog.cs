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

    public TemplateLibraryNode GetTemplateLibraryTree()
    {
        var templatePath = GetTemplateRootPath();
        return BuildDirectoryNode(templatePath, templatePath);
    }

    public string GetTemplateRootPath()
    {
        var templatePath = _config.GetTemplatePath(_rootPath);
        Directory.CreateDirectory(templatePath);
        return Path.GetFullPath(templatePath);
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
        sheet.Cell("A1").Value = "工程资料生成记录";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 16;
        sheet.Range("A1:F1").Merge();

        sheet.Cell("A3").Value = "工程名称";
        sheet.Cell("B3").Value = "{{静态:工程名称}}";
        sheet.Cell("D3").Value = "模板类型";
        sheet.Cell("E3").Value = "{{静态:模板类型}}";
        sheet.Cell("A4").Value = "建设单位";
        sheet.Cell("B4").Value = "{{静态:建设单位}}";
        sheet.Cell("D4").Value = "建设单位项目负责人";
        sheet.Cell("E4").Value = "{{静态:建设单位项目负责人}}";
        sheet.Cell("A5").Value = "施工单位";
        sheet.Cell("B5").Value = "{{静态:施工单位}}";
        sheet.Cell("D5").Value = "施工单位项目负责人";
        sheet.Cell("E5").Value = "{{静态:施工单位项目负责人}}";
        sheet.Cell("A6").Value = "设计单位";
        sheet.Cell("B6").Value = "{{静态:设计单位}}";
        sheet.Cell("D6").Value = "设计单位技术负责人";
        sheet.Cell("E6").Value = "{{静态:设计单位技术负责人}}";
        sheet.Cell("A7").Value = "监理单位";
        sheet.Cell("B7").Value = "{{静态:监理单位}}";
        sheet.Cell("D7").Value = "总监理工程师";
        sheet.Cell("E7").Value = "{{静态:监理单位总监理工程师}}";
        sheet.Cell("A8").Value = "专业监理工程师";
        sheet.Cell("B8").Value = "{{静态:监理单位专业监理工程师}}";
        sheet.Cell("D8").Value = "检验批容量";
        sheet.Cell("E8").Value = "{{静态:检验批容量}}";
        sheet.Cell("A9").Value = "专业分包单位";
        sheet.Cell("B9").Value = "{{静态:专业分包单位}}";
        sheet.Cell("D9").Value = "第三方检测单位";
        sheet.Cell("E9").Value = "{{静态:第三方检测单位}}";
        sheet.Cell("A10").Value = "施工日期";
        sheet.Cell("B10").Value = "{{静态:施工日期}}";
        sheet.Cell("D10").Value = "验收日期";
        sheet.Cell("E10").Value = "{{静态:验收日期}}";

        sheet.Cell("A12").Value = "报验申请语";
        sheet.Cell("B12").Value = "{{AI:申请语}}";
        sheet.Cell("A14").Value = "验收意见";
        sheet.Cell("B14").Value = "{{AI:验收意见}}";
        sheet.Cell("A16").Value = "生成日期";
        sheet.Cell("B16").Value = "{{系统:当前日期}}";
        sheet.Cell("D16").Value = "编号";
        sheet.Cell("E16").Value = "{{系统:编号}}";

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

    private static TemplateLibraryNode BuildDirectoryNode(string directoryPath, string rootPath)
    {
        var children = Directory.EnumerateDirectories(directoryPath)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => BuildDirectoryNode(path, rootPath))
            .Concat(Directory.EnumerateFiles(directoryPath, "*.xlsx")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => BuildTemplateNode(path, rootPath)))
            .ToArray();

        return new TemplateLibraryNode(
            Path.GetFileName(directoryPath) ?? directoryPath,
            GetSafeRelativePath(directoryPath, rootPath),
            Path.GetFullPath(directoryPath),
            "folder",
            children);
    }

    private static TemplateLibraryNode BuildTemplateNode(string filePath, string rootPath)
    {
        return new TemplateLibraryNode(
            Path.GetFileName(filePath),
            GetSafeRelativePath(filePath, rootPath),
            Path.GetFullPath(filePath),
            "template",
            []);
    }

    private static string GetSafeRelativePath(string path, string rootPath)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(path);
        if (!IsWithinRoot(fullPath, fullRoot))
        {
            throw new InvalidOperationException("模板路径不合法。");
        }

        var relativePath = Path.GetRelativePath(fullRoot, fullPath);
        return relativePath == "." ? "" : relativePath;
    }

    private static bool IsWithinRoot(string fullPath, string fullRoot)
    {
        if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
