using System.Text;
using System.Text.Json;

var arguments = ArgumentMap.Parse(args);
if (arguments.ShowHelp)
{
    PrintHelp();
    return;
}

var request = BuildRequest(arguments);
var payload = new LicensePayload(
    request.MachineCodeHash,
    request.ExpireDate,
    request.LicenseType,
    request.Modules,
    request.Version);

var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
{
    WriteIndented = true
});
var activationCode = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

Console.WriteLine("激活码 JSON：");
Console.WriteLine(json);
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

  dotnet run --project "src/LicenseTool" -- --machine-code-hash "机器码Hash" --license-type year --expire-date 2027-12-31 --modules generate,batch,ai --version 3.0

参数说明：
  --machine-code-hash   必填，对方发来的机器码 Hash
  --license-type        授权类型，如 trial / month / year / permanent
  --expire-date         到期日期，格式 yyyy-MM-dd；永久版可省略
  --modules             授权模块，多个模块用逗号分隔
  --version             版本号，默认 3.0
  --help                显示帮助

如果不传参数，程序会进入交互式输入模式。
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

            if (string.Equals(current, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "-h", StringComparison.OrdinalIgnoreCase))
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

    private string? Get(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }
}
