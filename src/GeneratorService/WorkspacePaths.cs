namespace GeneratorService;

public static class WorkspacePaths
{
    public static DirectoryInfo FindRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "config.example.json")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Templates")))
            {
                return current;
            }

            current = current.Parent;
        }

        return new DirectoryInfo(Directory.GetCurrentDirectory());
    }
}
