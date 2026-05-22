using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeneratorService.Models;
using Serilog;

namespace GeneratorService.Licensing;

public sealed class LicenseService
{
    private const int TrialLimit = 20;
    private const string PublicKeyRelativePath = "Keys/license-public.pem";
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
            var decoded = DecodeActivationCode(activationCode);
            if (decoded.IsSigned &&
                !VerifySignature(decoded.PayloadJson, decoded.SignatureBase64))
            {
                return new ActivationResult(false, "激活码签名验证失败", GetStatus());
            }

            var payloadJson = decoded.PayloadJson;
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
            return new ActivationResult(false, "激活码无效。当前支持 Base64(JSON) 和 RSA 签名激活码。", GetStatus());
        }
    }

    private ActivationCodeContent DecodeActivationCode(string activationCode)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(activationCode));
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.TryGetProperty("payload", out var payloadElement) &&
            document.RootElement.TryGetProperty("signature", out var signatureElement))
        {
            return new ActivationCodeContent(
                payloadElement.GetString() ?? "",
                signatureElement.GetString(),
                true);
        }

        return new ActivationCodeContent(json, null, false);
    }

    private bool VerifySignature(string payloadJson, string? signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64))
        {
            return false;
        }

        var publicKeyPath = Path.Combine(_rootPath.FullName, PublicKeyRelativePath);
        if (!File.Exists(publicKeyPath))
        {
            Log.Warning("未找到授权公钥文件：{PublicKeyPath}", publicKeyPath);
            return false;
        }

        var publicKeyPem = File.ReadAllText(publicKeyPath);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);

        var data = Encoding.UTF8.GetBytes(payloadJson);
        var signature = Convert.FromBase64String(signatureBase64);
        return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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

    private sealed record ActivationCodeContent(
        string PayloadJson,
        string? SignatureBase64,
        bool IsSigned);
}
