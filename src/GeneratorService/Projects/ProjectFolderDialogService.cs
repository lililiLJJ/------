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
                Application.EnableVisualStyles();
                var dialogTitle = string.IsNullOrWhiteSpace(description) ? "选择工程目录" : description;
                using var dialog = new TopMostFolderPickerForm(dialogTitle, initialDirectory);
                ShowInForeground(dialog);

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

    private static void ShowInForeground(Form dialog)
    {
        dialog.Shown += (_, _) =>
        {
            dialog.TopMost = true;
            dialog.Activate();
            dialog.BringToFront();
            SetForegroundWindow(dialog.Handle);
        };
    }

    private sealed class TopMostFolderPickerForm : Form
    {
        private readonly TreeView _tree = new();
        private readonly TextBox _pathText = new();
        private readonly Button _okButton = new();

        public TopMostFolderPickerForm(string title, string? initialDirectory)
        {
            Text = title;
            TopMost = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(720, 520);
            MinimumSize = new Size(560, 420);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            KeyPreview = true;

            SelectedPath = Directory.Exists(initialDirectory) ? initialDirectory! : "";
            BuildLayout();
            LoadDriveNodes();
            SelectInitialDirectory();
        }

        public string SelectedPath { get; private set; }

        private void BuildLayout()
        {
            var header = new Label
            {
                Text = "请选择工程目录",
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 12, 0),
                Font = new Font(Font, FontStyle.Bold)
            };

            _tree.Dock = DockStyle.Fill;
            _tree.HideSelection = false;
            _tree.BeforeExpand += (_, eventArgs) =>
            {
                if (eventArgs.Node is not null)
                {
                    LoadChildDirectories(eventArgs.Node);
                }
            };
            _tree.AfterSelect += (_, eventArgs) => SetSelectedPath(eventArgs.Node?.Tag as string ?? "");
            _tree.NodeMouseDoubleClick += (_, _) => ConfirmSelection();

            _pathText.Dock = DockStyle.Fill;
            _pathText.PlaceholderText = "也可以直接输入或粘贴工程目录路径";
            _pathText.Text = SelectedPath;
            _pathText.TextChanged += (_, _) =>
            {
                SelectedPath = _pathText.Text.Trim();
                _okButton.Enabled = Directory.Exists(SelectedPath);
            };

            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(12, 8, 12, 4),
                ColumnCount = 2
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathPanel.Controls.Add(new Label
            {
                Text = "目录",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            pathPanel.Controls.Add(_pathText, 1, 0);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 8, 12, 8)
            };

            _okButton.Text = "确定";
            _okButton.Width = 96;
            _okButton.Enabled = Directory.Exists(SelectedPath);
            _okButton.Click += (_, _) => ConfirmSelection();

            var cancelButton = new Button
            {
                Text = "取消",
                Width = 96,
                DialogResult = DialogResult.Cancel
            };

            var newFolderButton = new Button
            {
                Text = "新建文件夹",
                Width = 112
            };
            newFolderButton.Click += (_, _) => CreateFolderUnderSelection();

            footer.Controls.Add(_okButton);
            footer.Controls.Add(cancelButton);
            footer.Controls.Add(newFolderButton);

            Controls.Add(_tree);
            Controls.Add(pathPanel);
            Controls.Add(footer);
            Controls.Add(header);

            AcceptButton = _okButton;
            CancelButton = cancelButton;
        }

        private void LoadDriveNodes()
        {
            _tree.Nodes.Clear();
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
            {
                var node = CreateDirectoryNode(drive.RootDirectory.FullName);
                _tree.Nodes.Add(node);
            }
        }

        private static TreeNode CreateDirectoryNode(string path)
        {
            var label = Path.GetPathRoot(path)?.Equals(path, StringComparison.OrdinalIgnoreCase) == true
                ? path
                : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var node = new TreeNode(string.IsNullOrWhiteSpace(label) ? path : label)
            {
                Tag = path
            };

            if (HasChildDirectories(path))
            {
                node.Nodes.Add(new TreeNode("正在加载..."));
            }

            return node;
        }

        private static bool HasChildDirectories(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path).Any();
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path)
                    .OrderBy(directory => directory, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }

        private void LoadChildDirectories(TreeNode node)
        {
            if (node.Tag is not string path || node.Nodes.Count == 0 || node.Nodes[0].Tag is string)
            {
                return;
            }

            node.Nodes.Clear();
            foreach (var directory in EnumerateDirectoriesSafe(path))
            {
                node.Nodes.Add(CreateDirectoryNode(directory));
            }
        }

        private void SelectInitialDirectory()
        {
            if (string.IsNullOrWhiteSpace(SelectedPath))
            {
                _tree.SelectedNode = _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;
                return;
            }

            foreach (TreeNode driveNode in _tree.Nodes)
            {
                if (driveNode.Tag is not string drivePath ||
                    !SelectedPath.StartsWith(drivePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _tree.SelectedNode = ExpandToPath(driveNode, SelectedPath);
                _tree.SelectedNode?.EnsureVisible();
                return;
            }
        }

        private TreeNode ExpandToPath(TreeNode startNode, string targetPath)
        {
            var current = startNode;
            var currentPath = current.Tag as string ?? "";
            while (!string.Equals(currentPath.TrimEnd('\\'), targetPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                LoadChildDirectories(current);
                var next = current.Nodes
                    .Cast<TreeNode>()
                    .FirstOrDefault(node => node.Tag is string path &&
                                            targetPath.StartsWith(path, StringComparison.OrdinalIgnoreCase));
                if (next is null)
                {
                    break;
                }

                current.Expand();
                current = next;
                currentPath = current.Tag as string ?? "";
            }

            return current;
        }

        private void SetSelectedPath(string path)
        {
            SelectedPath = path;
            if (_pathText.Text != path)
            {
                _pathText.Text = path;
            }
        }

        private void ConfirmSelection()
        {
            if (!Directory.Exists(SelectedPath))
            {
                MessageBox.Show(this, "请选择一个存在的目录。", "工程目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CreateFolderUnderSelection()
        {
            var parent = Directory.Exists(SelectedPath) ? SelectedPath : Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(parent))
            {
                return;
            }

            var folderPath = GetAvailableFolderPath(parent, "新建工程");
            Directory.CreateDirectory(folderPath);
            SetSelectedPath(folderPath);
            LoadDriveNodes();
            SelectInitialDirectory();
        }

        private static string GetAvailableFolderPath(string parent, string name)
        {
            var path = Path.Combine(parent, name);
            var index = 2;
            while (Directory.Exists(path))
            {
                path = Path.Combine(parent, $"{name}{index}");
                index++;
            }

            return path;
        }
    }
}
