using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var arguments = ArgumentMap.Parse(args);
if (arguments.ShowHelp)
{
    PrintHelp();
    return;
}

var paths = KeyPaths.Create(arguments.PrivateKeyPath, arguments.PublicKeyPath);
if (arguments.GenerateKeyPair)
{
    GenerateKeyPair(paths, arguments.Force);
    return;
}

var request = BuildRequest(arguments);
var payload = new LicensePayload(
    request.MachineCodeHash,
    request.ExpireDate,
    request.LicenseType,
    request.Modules,
    request.Version);

var payloadJsonCompact = JsonSerializer.Serialize(payload);
var payloadJsonPretty = JsonSerializer.Serialize(payload, new JsonSerializerOptions
{
    WriteIndented = true
});

if (!File.Exists(paths.PrivateKeyPath))
{
    Console.WriteLine($"未找到私钥文件：{paths.PrivateKeyPath}");
    Console.WriteLine("请先运行：dotnet run --project \"src/LicenseTool\" -- --generate-keypair");
    Environment.ExitCode = 2;
    return;
}

var privateKeyPem = File.ReadAllText(paths.PrivateKeyPath);
using var rsa = RSA.Create();
rsa.ImportFromPem(privateKeyPem);

var signatureBytes = rsa.SignData(
    Encoding.UTF8.GetBytes(payloadJsonCompact),
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);
var signatureBase64 = Convert.ToBase64String(signatureBytes);

var envelope = new LicenseEnvelope(payloadJsonCompact, signatureBase64, "RSA-SHA256");
var envelopeJsonCompact = JsonSerializer.Serialize(envelope);
var envelopeJsonPretty = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
{
    WriteIndented = true
});
var activationCode = Convert.ToBase64String(Encoding.UTF8.GetBytes(envelopeJsonCompact));

Console.WriteLine("授权载荷 JSON：");
Console.WriteLine(payloadJsonPretty);
Console.WriteLine();
Console.WriteLine("签名信封 JSON：");
Console.WriteLine(envelopeJsonPretty);
Console.WriteLine();
Console.WriteLine("激活码 Base64：");
Console.WriteLine(activationCode);

static LicenseRequest BuildRequest(ArgumentMap arguments)
{
    var machineCodeHash = ReadRequired(arguments.MachineCodeHash, "请输入对方发来的机器码 Hash");
    var licenseType = ReadWithDefault(arguments.LicenseType, "请输入授权类型", "year").Trim();
    var expireDate = ReadExpireDate(arguments.ExpireDate, licenseType);
    var modules = ReadModules(arguments.Modules);
    var version = ReadWithDefault(arguments.Version, "请输入版本号", "3.0").Trim();

    return new LicenseRequest(machineCodeHash.Trim(), licenseType, expireDate, modules, version);
}

static void GenerateKeyPair(KeyPaths paths, bool force)
{
    Directory.CreateDirectory(Path.GetDirectoryName(paths.PrivateKeyPath)!);

    if (!force && (File.Exists(paths.PrivateKeyPath) || File.Exists(paths.PublicKeyPath)))
    {
        Console.WriteLine("密钥文件已存在。如需覆盖，请追加 --force。");
        Console.WriteLine($"私钥：{paths.PrivateKeyPath}");
        Console.WriteLine($"公钥：{paths.PublicKeyPath}");
        Environment.ExitCode = 3;
        return;
    }

    using var rsa = RSA.Create(2048);
    File.WriteAllText(paths.PrivateKeyPath, rsa.ExportRSAPrivateKeyPem());
    File.WriteAllText(paths.PublicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

    Console.WriteLine("RSA 密钥对已生成。");
    Console.WriteLine($"私钥：{paths.PrivateKeyPath}");
    Console.WriteLine($"公钥：{paths.PublicKeyPath}");
    Console.WriteLine("注意：私钥只保留在你自己的电脑，不要发给客户。");
}

static string ReadRequired(string? currentValue, string prompt)
{
    if (!string.IsNullOrWhiteSpace(currentValue))
    {
        return currentValue;
    }

    while (true)
    {
        Console.Write($"{prompt}: ");
        var value = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        Console.WriteLine("不能为空，请重新输入。");
    }
}

static string ReadWithDefault(string? currentValue, string prompt, string defaultValue)
{
    if (!string.IsNullOrWhiteSpace(currentValue))
    {
        return currentValue;
    }

    Console.Write($"{prompt}（默认 {defaultValue}）: ");
    var value = Console.ReadLine();
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}

static DateOnly? ReadExpireDate(string? currentValue, string licenseType)
{
    if (!string.IsNullOrWhiteSpace(currentValue))
    {
        return ParseExpireDate(currentValue);
    }

    if (string.Equals(licenseType, "permanent", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    while (true)
    {
        Console.Write("请输入到期日期（yyyy-MM-dd，永久版可留空）: ");
        var value = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ParseExpireDate(value);
        }
        catch (FormatException)
        {
            Console.WriteLine("日期格式不正确，请按 yyyy-MM-dd 输入。");
        }
    }
}

static DateOnly? ParseExpireDate(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (DateOnly.TryParse(value, out var date))
    {
        return date;
    }

    throw new FormatException("ExpireDate 格式不正确。");
}

static IReadOnlyList<string> ReadModules(string? currentValue)
{
    var raw = currentValue;
    if (string.IsNullOrWhiteSpace(raw))
    {
        Console.Write("请输入授权模块，多个模块用逗号分隔（默认 generate,batch,ai）: ");
        raw = Console.ReadLine();
    }

    if (string.IsNullOrWhiteSpace(raw))
    {
        raw = "generate,batch,ai";
    }

    return raw
        .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static void PrintHelp()
{
    Console.WriteLine("""
LicenseTool 用法：

  生成 RSA 密钥对：
    dotnet run --project "src/LicenseTool" -- --generate-keypair

  生成激活码：
    dotnet run --project "src/LicenseTool" -- --machine-code-hash "机器码Hash" --license-type year --expire-date 2027-12-31 --modules generate,batch,ai --version 3.0

参数说明：
  --machine-code-hash   对方发来的机器码 Hash
  --license-type        授权类型，如 trial / month / year / permanent
  --expire-date         到期日期，格式 yyyy-MM-dd；永久版可省略
  --modules             授权模块，多个模块用逗号分隔
  --version             版本号，默认 3.0
  --generate-keypair    生成 RSA 私钥和公钥
  --private-key-path    私钥文件路径，默认 Keys/license-private.pem
  --public-key-path     公钥文件路径，默认 Keys/license-public.pem
  --force               覆盖已存在的密钥文件
  --help                显示帮助

如果不传生成激活码参数，程序会进入交互式输入模式。
""");
}

sealed record LicenseRequest(
    string MachineCodeHash,
    string LicenseType,
    DateOnly? ExpireDate,
    IReadOnlyList<string> Modules,
    string Version);

sealed record LicensePayload(
    string MachineCodeHash,
    DateOnly? ExpireDate,
    string LicenseType,
    IReadOnlyList<string> Modules,
    string Version);

sealed record LicenseEnvelope(
    [property: JsonPropertyName("payload")] string Payload,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("algorithm")] string Algorithm);

sealed record KeyPaths(
    string PrivateKeyPath,
    string PublicKeyPath)
{
    public static KeyPaths Create(string? privateKeyPath, string? publicKeyPath)
    {
        var root = FindRoot(AppContext.BaseDirectory, Directory.GetCurrentDirectory());
        var privatePath = string.IsNullOrWhiteSpace(privateKeyPath)
            ? Path.Combine(root, "Keys", "license-private.pem")
            : NormalizePath(root, privateKeyPath);
        var publicPath = string.IsNullOrWhiteSpace(publicKeyPath)
            ? Path.Combine(root, "Keys", "license-public.pem")
            : NormalizePath(root, publicKeyPath);

        return new KeyPaths(privatePath, publicPath);
    }

    private static string NormalizePath(string root, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root, path));
    }

    private static string FindRoot(params string[] startPaths)
    {
        foreach (var startPath in startPaths)
        {
            var current = new DirectoryInfo(startPath);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                    File.Exists(Path.Combine(current.FullName, "README.md")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}

sealed class ArgumentMap
{
    private readonly Dictionary<string, string?> _values;

    private ArgumentMap(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public string? MachineCodeHash => Get("--machine-code-hash");
    public string? LicenseType => Get("--license-type");
    public string? ExpireDate => Get("--expire-date");
    public string? Modules => Get("--modules");
    public string? Version => Get("--version");
    public string? PrivateKeyPath => Get("--private-key-path");
    public string? PublicKeyPath => Get("--public-key-path");
    public bool GenerateKeyPair => _values.ContainsKey("--generate-keypair");
    public bool Force => _values.ContainsKey("--force");
    public bool ShowHelp => _values.ContainsKey("--help") || _values.ContainsKey("-h");

    public static ArgumentMap Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            if (!current.StartsWith('-'))
            {
                continue;
            }

            if (IsFlag(current))
            {
                values[current] = "true";
                continue;
            }

            var nextIndex = index + 1;
            if (nextIndex >= args.Length || args[nextIndex].StartsWith('-'))
            {
                values[current] = null;
                continue;
            }

            values[current] = args[nextIndex];
            index = nextIndex;
        }

        return new ArgumentMap(values);
    }

    private static bool IsFlag(string current)
    {
        return string.Equals(current, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(current, "-h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(current, "--generate-keypair", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(current, "--force", StringComparison.OrdinalIgnoreCase);
    }

    private string? Get(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }
}
