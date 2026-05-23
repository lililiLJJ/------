using System.IO.Compression;
using System.Text.Json;

namespace GeneratorService.Modules;

public sealed class ModulePackageReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ModuleManifest ReadManifest(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("模块包缺少 manifest.json。");

        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(stream, JsonOptions);
        return manifest ?? throw new InvalidOperationException("manifest.json 内容无效。");
    }

    public string EnsureExtracted(string packagePath, string cacheRoot, ModuleManifest manifest, bool forceRefresh = false)
    {
        Directory.CreateDirectory(cacheRoot);
        var cachePath = Path.Combine(cacheRoot, BuildCacheFolderName(manifest, packagePath));
        var markerPath = Path.Combine(cachePath, ".module-source");
        var sourceStamp = $"{Path.GetFullPath(packagePath)}|{File.GetLastWriteTimeUtc(packagePath):O}|{new FileInfo(packagePath).Length}";
        var hasCurrentMarker = Directory.Exists(cachePath) &&
            File.Exists(markerPath) &&
            File.ReadAllText(markerPath) == sourceStamp;

        if (hasCurrentMarker && HasRequiredContent(cachePath, manifest) && !forceRefresh)
        {
            return cachePath;
        }

        if (!hasCurrentMarker && Directory.Exists(cachePath))
        {
            Directory.Delete(cachePath, recursive: true);
        }

        Directory.CreateDirectory(cachePath);
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(cachePath, entry.FullName));
            if (!IsWithinRoot(destinationPath, cachePath))
            {
                throw new InvalidOperationException($"模块包包含非法路径：{entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (hasCurrentMarker && File.Exists(destinationPath))
            {
                continue;
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        File.WriteAllText(markerPath, sourceStamp);
        return cachePath;
    }

    private static bool HasRequiredContent(string cachePath, ModuleManifest manifest)
    {
        return File.Exists(Path.Combine(cachePath, manifest.Database)) &&
            Directory.Exists(Path.Combine(cachePath, manifest.TemplateRoot));
    }

    private static string BuildCacheFolderName(ModuleManifest manifest, string packagePath)
    {
        var raw = string.IsNullOrWhiteSpace(manifest.ModuleId)
            ? Path.GetFileNameWithoutExtension(packagePath)
            : $"{manifest.ModuleId}_{manifest.Version}";

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            raw = raw.Replace(invalid, '_');
        }

        return raw;
    }

    private static bool IsWithinRoot(string fullPath, string rootPath)
    {
        var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
