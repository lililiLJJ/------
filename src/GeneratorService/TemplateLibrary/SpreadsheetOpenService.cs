using System.Diagnostics;

namespace GeneratorService.TemplateLibrary;

public sealed class SpreadsheetOpenService
{
    public void OpenSpreadsheet(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"资料表文件不存在：{filePath}", filePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        });
    }
}
