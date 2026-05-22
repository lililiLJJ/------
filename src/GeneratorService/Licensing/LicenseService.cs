using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeneratorService.Models;
using Serilog;

namespace GeneratorService.Licensing;

public sealed class LicenseService
{
    private const int TrialLimit = 20;
    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;

    public LicenseService(DirectoryInfo rootPath, AppConfig config)
    {
        _rootPath = rootPath;
        _config = config;
    }

    public LicenseStatus GetStatus()
    {
        var state = LoadState();
        if (state.Activated && (state.ExpireDate is null || state.ExpireDate >= DateOnly.FromDateTime(DateTime.Today)))
        {
            return new LicenseStatus(true, state.LicenseType, GetMachineCodeHash(), state.ExpireDate, state.Modules, TrialLimit, state.TrialUsed, true, "授权有效");
        }

        var canGenerate = state.TrialUsed < TrialLimit;
        return new LicenseStatus(false, "trial", GetMachineCodeHash(), null, ["generate"], TrialLimit, state.TrialUsed, canGenerate, canGenerate ? "试用授权" : "试用次数已用完");
    }

    public void RecordGeneration()
    {
        var state = LoadState();
        if (!state.Activated)
        {
            state.TrialUsed += 1;
            SaveState(state);
        }
    }

    public ActivationResult Activate(string activationCode)
    {
        try
        {
            var payloadJson = DecodeActivationPayload(activationCode);
            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (payload is null)
            {
                return new ActivationResult(false, "激活码内容无效", GetStatus());
            }

            if (!string.Equals(payload.MachineCodeHash, GetMachineCodeHash(), StringComparison.OrdinalIgnoreCase))
            {
                return new ActivationResult(false, "激活码不属于当前电脑", GetStatus());
            }

            if (payload.ExpireDate is not null && payload.ExpireDate < DateOnly.FromDateTime(DateTime.Today))
            {
                return new ActivationResult(false, "激活码已过期", GetStatus());
            }

            var state = new LicenseState
            {
                Activated = true,
                LicenseType = payload.LicenseType,
                ExpireDate = payload.ExpireDate,
                Modules = payload.Modules.Count == 0 ? ["generate", "batch", "ai"] : payload.Modules,
                TrialUsed = 0
            };
            SaveState(state);
            Log.Information("软件激活成功。LicenseType={LicenseType}", payload.LicenseType);
            return new ActivationResult(true, "激活成功", GetStatus());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "激活失败");
            return new ActivationResult(false, "激活码无效。MVP版本支持Base64(JSON)格式，正式版可替换为RSA签名信封。", GetStatus());
        }
    }

    private static string DecodeActivationPayload(string activationCode)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(activationCode));
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("payload", out var payloadElement))
        {
            return payloadElement.GetString() ?? "";
        }

        return json;
    }

    private string GetMachineCodeHash()
    {
        var raw = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion.VersionString}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private LicenseState LoadState()
    {
        var path = GetStatePath();
        if (!File.Exists(path))
        {
            return new LicenseState();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LicenseState>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new LicenseState();
    }

    private void SaveState(LicenseState state)
    {
        var path = GetStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private string GetStatePath()
    {
        return Path.Combine(_config.GetLogPath(_rootPath), "license-state.json");
    }

    private sealed class LicensePayload
    {
        public string MachineCodeHash { get; set; } = "";
        public DateOnly? ExpireDate { get; set; }
        public string LicenseType { get; set; } = "trial";
        public List<string> Modules { get; set; } = [];
        public string Version { get; set; } = "3.0";
    }

    private sealed class LicenseState
    {
        public bool Activated { get; set; }
        public string LicenseType { get; set; } = "trial";
        public DateOnly? ExpireDate { get; set; }
        public List<string> Modules { get; set; } = ["generate"];
        public int TrialUsed { get; set; }
    }
}
