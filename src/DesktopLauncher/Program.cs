using System.Diagnostics;
using System.Net.Http.Json;
using System.Windows.Forms;

namespace DesktopLauncher;

internal static class Program
{
    private const string ServiceUrl = "http://127.0.0.1:5188";
    private static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(25);

    [STAThread]
    private static async Task Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var packageRoot = ResolvePackageRoot(AppContext.BaseDirectory);
            await EnsureServiceStartedAsync(packageRoot);
            OpenFrontend(packageRoot);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "工程资料制作软件启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string ResolvePackageRoot(string baseDirectory)
    {
        var candidates = new[]
        {
            baseDirectory,
            Directory.GetParent(baseDirectory)?.FullName ?? baseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var candidate in candidates)
        {
            if (HasPackageShape(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException("未找到桌面版运行目录。请确认 GeneratorService 和 WpsAddin 目录与启动器放在同一发布包内。");
    }

    private static bool HasPackageShape(string root)
    {
        return File.Exists(GetServiceExePath(root)) &&
               (File.Exists(GetProductionFrontendPath(root)) || File.Exists(GetDevelopmentFrontendPath(root)));
    }

    private static async Task EnsureServiceStartedAsync(string packageRoot)
    {
        if (await IsServiceHealthyAsync())
        {
            return;
        }

        var serviceExe = GetServiceExePath(packageRoot);
        if (!File.Exists(serviceExe))
        {
            throw new FileNotFoundException("未找到本地生成服务。", serviceExe);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = serviceExe,
            WorkingDirectory = packageRoot,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        var deadline = DateTimeOffset.Now + ServiceStartTimeout;
        while (DateTimeOffset.Now < deadline)
        {
            if (await IsServiceHealthyAsync())
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("本地生成服务启动超时。请检查 Logs/startup 日志，或确认端口 5188 没有被其他程序占用。");
    }

    private static async Task<bool> IsServiceHealthyAsync()
    {
        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
            var result = await http.GetFromJsonAsync<HealthResult>($"{ServiceUrl}/api/health");
            return result?.Success == true;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenFrontend(string packageRoot)
    {
        var frontendPath = FindFrontendPath(packageRoot);

        if (!File.Exists(frontendPath))
        {
            throw new FileNotFoundException("未找到桌面版前端入口。", frontendPath);
        }

        var url = new Uri(frontendPath).AbsoluteUri + "?desktop=1";
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static string GetServiceExePath(string packageRoot)
    {
        return Path.Combine(packageRoot, "GeneratorService", "GeneratorService.exe");
    }

    private static string FindFrontendPath(string packageRoot)
    {
        foreach (var candidate in GetFrontendCandidates(packageRoot))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(packageRoot, "WebClient", "dist", "index.html");
    }

    private static IEnumerable<string> GetFrontendCandidates(string packageRoot)
    {
        yield return Path.Combine(packageRoot, "WebClient", "dist", "index.html");
        yield return Path.Combine(packageRoot, "WpsAddin", "dist", "index.html");
        yield return Path.Combine(packageRoot, "WpsAddin", "index.html");
    }

    private static string GetProductionFrontendPath(string packageRoot)
    {
        return Path.Combine(packageRoot, "WebClient", "dist", "index.html");
    }

    private static string GetDevelopmentFrontendPath(string packageRoot)
    {
        return Path.Combine(packageRoot, "WpsAddin", "index.html");
    }

    private sealed record HealthResult(bool Success);
}
