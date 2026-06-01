using GeneratorService.Modules;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateService
{
    private readonly ModuleManager _moduleManager;

    public TemplateService(ModuleManager moduleManager)
    {
        _moduleManager = moduleManager;
    }

    public TemplateResolution ResolveTemplate(string templateNodeId)
    {
        var moduleTemplate = _moduleManager.ResolveTemplate(templateNodeId)
            ?? throw new FileNotFoundException("模板节点不存在或未绑定有效模块模板。", templateNodeId);
        if (!File.Exists(moduleTemplate.TemplatePath))
        {
            throw new FileNotFoundException($"模板文件不存在：{moduleTemplate.TemplatePath}", moduleTemplate.TemplatePath);
        }

        return new TemplateResolution(
            templateNodeId,
            moduleTemplate.ModuleId,
            moduleTemplate.ModuleVersion,
            moduleTemplate.TemplateItemId,
            moduleTemplate.TemplateName,
            string.IsNullOrWhiteSpace(moduleTemplate.TemplateCode) ? "template" : moduleTemplate.TemplateCode,
            moduleTemplate.TemplateType,
            moduleTemplate.TemplateFile,
            moduleTemplate.TemplatePath);
    }
}

public sealed record TemplateResolution(
    string TemplateNodeId,
    string ModuleId,
    string ModuleVersion,
    long TemplateItemId,
    string TemplateName,
    string TemplateCode,
    string TemplateType,
    string TemplateFile,
    string TemplatePath);
