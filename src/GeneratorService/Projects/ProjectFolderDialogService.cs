using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GeneratorService.Projects;

public sealed class ProjectFolderDialogService
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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
                var dialogTitle = string.IsNullOrWhiteSpace(description) ? "选择工程目录" : description;
                using var owner = CreateTopMostOwner(dialogTitle);
                using var dialog = new FolderBrowserDialog
                {
                    Description = dialogTitle,
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    dialog.SelectedPath = initialDirectory;
                }

                ShowOwnerInForeground(owner);

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

    private static Form CreateTopMostOwner(string title)
    {
        var owner = new Form
        {
            Text = title,
            TopMost = true,
            ShowInTaskbar = true,
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(360, 96),
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            MinimizeBox = false,
            MaximizeBox = false
        };

        owner.Controls.Add(new Label
        {
            Text = "请选择工程目录，目录选择窗口即将打开。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular)
        });

        return owner;
    }

    private static void ShowOwnerInForeground(Form owner)
    {
        owner.Show();
        owner.Activate();
        owner.BringToFront();
        SetForegroundWindow(owner.Handle);
        Application.DoEvents();
    }
}
