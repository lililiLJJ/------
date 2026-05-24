using System.Drawing;
using System.Windows.Forms;

namespace GeneratorService.Projects;

public sealed class ProjectFolderDialogService
{
    public ProjectFolderSelectResult SelectFolder(string? description = null, string? initialDirectory = null)
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
                using var owner = new Form
                {
                    TopMost = true,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.CenterScreen,
                    Size = new Size(1, 1),
                    Opacity = 0
                };

                using var dialog = new FolderBrowserDialog
                {
                    Description = string.IsNullOrWhiteSpace(description) ? "选择工程目录" : description,
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    dialog.SelectedPath = initialDirectory;
                }

                owner.Show();
                owner.Activate();
                owner.BringToFront();

                if (dialog.ShowDialog(owner) == DialogResult.OK)
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
