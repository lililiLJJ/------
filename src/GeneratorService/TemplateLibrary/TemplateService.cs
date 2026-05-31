using GeneratorService.Modules;

namespace GeneratorService.TemplateLibrary;

public sealed class TemplateService
{
    private readonly ModuleManager _moduleManager;
    private readonly TemplateTreeRepository _repository;

    public TemplateService(ModuleManager moduleManager, TemplateTreeRepository repository)
    {
        _moduleManager = moduleManager;
        _repository = repository;
    }

    public TemplateResolution ResolveTemplate(string templateNodeId)
    {
        var moduleTemplate = _moduleManager.ResolveTemplate(templateNodeId);
        if (moduleTemplate is not null)
        {
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

        var legacyNode = _repository.GetNode(templateNodeId)
            ?? throw new FileNotFoundException("模板节点不存在。", templateNodeId);
        if (legacyNode.NodeType != "template" || string.IsNullOrWhiteSpace(legacyNode.TemplateFilePath))
        {
            throw new InvalidOperationException("请选择检验批模板节点后再新建资料。");
        }

        var templatePath = _repository.ResolveStoredPath(legacyNode.TemplateFilePath);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"模板文件不存在：{templatePath}", templatePath);
        }

        return new TemplateResolution(
            templateNodeId,
            legacyNode.ModuleId ?? "legacy",
            "",
            legacyNode.TemplateItemId ?? 0,
            legacyNode.Name,
            string.IsNullOrWhiteSpace(legacyNode.TemplateCode) ? "template" : legacyNode.TemplateCode,
            legacyNode.FolderLevel ?? "检验批",
            legacyNode.TemplateFilePath ?? "",
            templatePath);
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
