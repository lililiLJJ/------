using GeneratorService.Modules;
using Microsoft.Data.Sqlite;

namespace GeneratorService.TemplateLibrary;

public sealed class RuleService
{
    private readonly ModuleManager _moduleManager;

    public RuleService(ModuleManager moduleManager)
    {
        _moduleManager = moduleManager;
    }

    public TemplateRulesResult GetRules(string templateNodeId)
    {
        if (!_moduleManager.TryParseTemplateNodeId(templateNodeId, out var moduleId, out var templateItemId))
        {
            throw new InvalidOperationException("当前模板不是模块包模板，暂无模块规则。");
        }

        var module = _moduleManager.FindModule(moduleId)
            ?? throw new FileNotFoundException("模块不存在。", moduleId);
        if (module is not { IsValid: true, RulesDbPath: not null, Manifest: not null })
        {
            throw new InvalidOperationException("模块未通过校验，无法读取规则。");
        }

        var template = _moduleManager.ResolveTemplate(templateNodeId)
            ?? throw new FileNotFoundException("模板不存在。", templateNodeId);

        using var connection = new SqliteConnection($"Data Source={module.RulesDbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RuleType, ItemName, Requirement, CheckMethod, AllowedDeviation, SortOrder, Source
            FROM InspectionRule
            WHERE TemplateItemId = $templateItemId
            ORDER BY SortOrder, Id;
            """;
        command.Parameters.AddWithValue("$templateItemId", templateItemId);

        using var reader = command.ExecuteReader();
        var rules = new List<TemplateRuleInfo>();
        while (reader.Read())
        {
            rules.Add(new TemplateRuleInfo(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return new TemplateRulesResult(
            true,
            templateNodeId,
            moduleId,
            module.Manifest.Name,
            template.TemplateName,
            template.TemplateCode,
            rules);
    }
}
