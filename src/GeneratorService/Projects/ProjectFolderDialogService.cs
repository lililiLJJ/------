using System.Windows.Forms;

namespace GeneratorService.Projects;

public sealed class ProjectFolderDialogService
{
    public ProjectFolderSelectResult SelectFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ProjectFolderSelectResult(false, null, "当前系统不支持文件夹弹窗，请手动输入工程目录。");
        }

        string? selectedPath = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "选择工程目录",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    selectedPath = dialog.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            return new ProjectFolderSelectResult(false, null, $"文件夹选择失败：{error.Message}");
        }

        return string.IsNullOrWhiteSpace(selectedPath)
            ? new ProjectFolderSelectResult(false, null, "已取消选择。")
            : new ProjectFolderSelectResult(true, selectedPath, "已选择工程目录。");
    }
}
