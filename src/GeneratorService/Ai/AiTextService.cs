using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeneratorService.Models;
using Serilog;

namespace GeneratorService.Ai;

public sealed class AiTextService
{
    private readonly AppConfig _config;
    private readonly HttpClient _httpClient = new();

    public AiTextService(AppConfig config)
    {
        _config = config;
    }

    public async Task<string> GenerateTextAsync(string fieldName, GenerateRequest request)
    {
        if (!_config.EnableAI || string.IsNullOrWhiteSpace(_config.DeepSeek.ApiKey))
        {
            return GetFallbackText(fieldName, request);
        }

        try
        {
            var payload = new
            {
                model = _config.DeepSeek.Model,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "你只能生成工程资料中的自然语言说明。禁止生成规范条文、技术参数、验收标准、允许偏差、检查方法、实测值。输出必须简短、正式、符合工程资料语气。"
                    },
                    new
                    {
                        role = "user",
                        content = $"字段：{fieldName}\n工程：{request.ProjectName}\n施工单位：{request.ConstructorUnit.Name}\n监理单位：{request.SupervisorUnit.Name}\n施工日期：{request.ConstructionDate:yyyy-MM-dd}\n验收日期：{request.AcceptanceDate:yyyy-MM-dd}"
                    }
                },
                temperature = 0.2
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_config.DeepSeek.BaseUrl.TrimEnd('/')}/chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.DeepSeek.ApiKey);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var text = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(text) ? GetFallbackText(fieldName, request) : text.Trim();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI调用失败，已使用兜底文本。Field={FieldName}", fieldName);
            return GetFallbackText(fieldName, request);
        }
    }

    private static string GetFallbackText(string fieldName, GenerateRequest request)
    {
        return fieldName switch
        {
            "申请语" => $"{request.ConstructorUnit.Name}已完成本次资料相关内容，资料自检合格，现申请验收。",
            "验收意见" => "经检查，资料齐全，现场质量满足验收要求，同意进入下一道工序。",
            "试验过程" => "按现场取样及试验要求完成相关试验过程记录。",
            _ => "见现场记录。"
        };
    }
}
