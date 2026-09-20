using System.Diagnostics;
using System.Windows.Forms;

namespace VideoRoll;

public sealed class MainForm : Form
{
    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripButton _btnShuffle = new("随机打乱");
    private readonly ToolStripButton _btnClear = new("清空列表");
    private readonly ToolStripButton _btnSettings = new("设置");

    private readonly ListView _list = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblHint = new();
    private readonly ToolStripStatusLabel _lblCount = new();
    private readonly ToolStripStatusLabel _lblMpv = new();

    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _miPlay = new("播放");
    private readonly ToolStripMenuItem _miOpenDir = new("打开文件所在目录");
    private readonly ToolStripMenuItem _miRemove = new("从列表中移除");

    private readonly List<string> _items = new();
    private AppConfig _config = new();
    private string? _rightClickedPath;

    private string AppDir => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    private string ConfigPath => Path.Combine(AppDir, "config.json");
    private string PlaylistPath => Path.Combine(AppDir, "playlist.json");

    public MainForm()
    {
        _config = Store.LoadConfig(ConfigPath);
        _items.AddRange(Store.LoadPlaylist(PlaylistPath).Items);

        BuildUi();
        RefreshList();
    }

    // ---------- UI 构建 ----------

    private void BuildUi()
    {
        Text = "VideoRoll 播放列表";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1032;
        Height = 672;
        MinimumSize = new System.Drawing.Size(520, 360);
        AllowDrop = true;

        // 工具栏
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Dock = DockStyle.Top;

        var btnAdd = new ToolStripDropDownButton("添加");
        btnAdd.DropDownItems.Add("添加文件...", null, (_, _) => AddFilesViaDialog());
        btnAdd.DropDownItems.Add("添加文件夹...", null, (_, _) => AddFolderViaDialog());

        _btnShuffle.ToolTipText = "随机打乱播放列表顺序";
        _btnShuffle.Click += (_, _) => Shuffle();
        _btnClear.ToolTipText = "清空列表（不会删除实际文件）";
        _btnClear.Click += (_, _) => ClearAll();
        _btnSettings.ToolTipText = "设置 mpv 路径、扩展名等";
        _btnSettings.Click += (_, _) => OpenSettings();

        _toolbar.Items.Add(btnAdd);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(_btnShuffle);
        _toolbar.Items.Add(_btnClear);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(_btnSettings);

        // 列表
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.AllowDrop = true;
        _list.LabelEdit = false;
        _list.Columns.Add("文件名", 360);
        _list.Columns.Add("路径", 380);
        _list.ItemActivate += (_, _) => OnItemActivate();
        _list.KeyDown += OnListKeyDown;
        _list.MouseClick += OnListMouseClick;
        _list.DragEnter += OnDragEnter;
        _list.DragDrop += OnDragDrop;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        // 右键菜单
        _miPlay.Click += (_, _) => PlaySelected();
        _miOpenDir.Click += (_, _) => OpenClickedItemFolder();
        _miRemove.Click += (_, _) => RemoveSelected();
        var miAddFiles = new ToolStripMenuItem("添加文件...");
        miAddFiles.Click += (_, _) => AddFilesViaDialog();
        var miAddFolder = new ToolStripMenuItem("添加文件夹...");
        miAddFolder.Click += (_, _) => AddFolderViaDialog();
        var miClear = new ToolStripMenuItem("清空列表");
        miClear.Click += (_, _) => ClearAll();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _miPlay, _miOpenDir, _miRemove,
            new ToolStripSeparator(),
            miAddFiles, miAddFolder,
            new ToolStripSeparator(),
            miClear,
        });
        _menu.Opening += OnMenuOpening;
        _list.ContextMenuStrip = _menu;

        // 状态栏
        _lblHint.Spring = true;
        _lblHint.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        _lblHint.Text = "将文件夹或视频文件拖入窗口即可导入；双击播放；Del 移除；右键更多操作。";
        _lblCount.AutoSize = false;
        _lblCount.Width = 140;
        _lblCount.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
        _lblMpv.AutoSize = false;
        _lblMpv.Width = 300;
        _lblMpv.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
        _status.Items.AddRange(new ToolStripItem[] { _lblHint, _lblCount, _lblMpv });

        Controls.Add(_list);
        Controls.Add(_toolbar);
        Controls.Add(_status);
    }

    // ---------- 列表刷新 ----------

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in _items)
        {
            var li = new ListViewItem(Path.GetFileName(p));
            li.SubItems.Add(p);
            li.Tag = p;
            li.ToolTipText = p;
            _list.Items.Add(li);
        }
        _list.EndUpdate();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _lblCount.Text = _items.Count == 0 ? "列表为空" : $"共 {_items.Count} 项";
        var mpv = MpvLauncher.FindMpv(_config.MpvPath, AppDir);
        _lblMpv.Text = mpv != null ? "mpv：已就绪" : "mpv：未找到（点“设置”选择 mpv.exe）";
    }

    // ---------- 导入 ----------

    private void ImportPaths(IEnumerable<string> paths)
    {
        var exts = new HashSet<string>(
            _config.Extensions
                .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
                .Where(e => e.Length > 0));
        var existing = new HashSet<string>(_items, StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            if (Directory.Exists(raw))
            {
                var opt = _config.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(raw, "*", opt);
                }
                catch
                {
                    continue;
                }
                foreach (var f in files)
                    if (MatchesExt(f, exts) && existing.Add(f))
                        added.Add(f);
            }
            else if (File.Exists(raw) && MatchesExt(raw, exts) && existing.Add(raw))
            {
                added.Add(raw);
            }
        }

        if (added.Count == 0)
            return;

        added.Sort(NaturalSort.Compare);
        _items.AddRange(added);
        RefreshList();
        SavePlaylist();
    }

    private static bool MatchesExt(string file, HashSet<string> exts)
    {
        var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        return exts.Contains(ext);
    }

    private void AddFilesViaDialog()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "选择视频文件",
            Filter = BuildFileFilter(),
            Multiselect = true,
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
            ImportPaths(ofd.FileNames);
    }

    private void AddFolderViaDialog()
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "选择要导入的视频文件夹",
            UseDescriptionForTitle = true,
        };
        if (fbd.ShowDialog(this) == DialogResult.OK)
            ImportPaths(new[] { fbd.SelectedPath });
    }

    private string BuildFileFilter()
    {
        var exts = _config.Extensions
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();
        var joined = string.Join(";", exts.Select(e => "*." + e));
        return $"视频文件 ({joined})|{joined}|所有文件 (*.*)|*.*";
    }

    // ---------- 播放 ----------

    private void OnItemActivate()
    {
        var item = _list.FocusedItem
            ?? (_list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null);
        if (item != null)
            PlayItem(item);
    }

    private void PlaySelected()
    {
        var item = _list.SelectedItems.Count > 0
            ? _list.SelectedItems[0]
            : (_list.Items.Count > 0 ? _list.Items[0] : null);
        if (item != null)
            PlayItem(item);
    }

    private void PlayItem(ListViewItem item)
    {
        string? mpv = ResolveMpv();
        if (mpv == null)
            return;
        MpvLauncher.Play(mpv, _items, item.Index, _config.ExtraArgs);
    }

    private string? ResolveMpv()
    {
        var found = MpvLauncher.FindMpv(_config.MpvPath, AppDir);
        if (found != null)
        {
            if (!string.Equals(found, _config.MpvPath, StringComparison.OrdinalIgnoreCase))
            {
                _config.MpvPath = found;
                Store.SaveConfig(ConfigPath, _config);
                UpdateStatus();
            }
            return found;
        }

        var r = MessageBox.Show(
            this,
            "未找到 mpv.exe。是否现在选择 mpv 播放器程序？",
            "VideoRoll",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (r != DialogResult.Yes)
            return null;

        using var ofd = new OpenFileDialog
        {
            Filter = "mpv 播放器 (mpv.exe)|mpv.exe|所有文件 (*.*)|*.*",
            Title = "选择 mpv.exe",
        };
        if (ofd.ShowDialog(this) != DialogResult.OK)
            return null;

        _config.MpvPath = ofd.FileName;
        Store.SaveConfig(ConfigPath, _config);
        UpdateStatus();
        return ofd.FileName;
    }

    // ---------- 移除 / 打乱 / 清空 ----------

    private void RemoveSelected()
    {
        if (_list.SelectedItems.Count == 0)
            return;
        var toRemove = new HashSet<string>(
            _list.SelectedItems.Cast<ListViewItem>().Select(x => (string)x.Tag!),
            StringComparer.OrdinalIgnoreCase);
        _items.RemoveAll(p => toRemove.Contains(p));
        RefreshList();
        SavePlaylist();
    }

    private void Shuffle()
    {
        if (_items.Count < 2)
            return;
        for (int i = _items.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_items[i], _items[j]) = (_items[j], _items[i]);
        }
        RefreshList();
        SavePlaylist();
    }

    private void ClearAll()
    {
        if (_items.Count == 0)
            return;
        var r = MessageBox.Show(
            this,
            $"确定清空列表吗？（共 {_items.Count} 项，不会删除实际文件）",
            "VideoRoll",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (r != DialogResult.Yes)
            return;
        _items.Clear();
        RefreshList();
        SavePlaylist();
    }

    // ---------- 打开所在目录 ----------

    private void OpenClickedItemFolder()
    {
        string? p = _rightClickedPath;
        if (string.IsNullOrWhiteSpace(p) && _list.SelectedItems.Count > 0)
            p = _list.SelectedItems[0].Tag as string;
        if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
            return;
        OpenInExplorer(p);
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            // /select 打开所在目录并选中该文件
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // 忽略
        }
    }

    // ---------- 设置 ----------

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_config.MpvPath, _config.Extensions, _config.Recursive);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        _config.MpvPath = dlg.MpvPathValue;
        _config.Extensions = dlg.ExtensionsValue
            .Split(new[] { ',', ' ', ';', '，', '；', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('.'))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_config.Extensions.Count == 0)
            _config.Extensions = AppConfig.DefaultExtensions();
        _config.Recursive = dlg.RecursiveValue;

        Store.SaveConfig(ConfigPath, _config);
        UpdateStatus();
    }

    // ---------- 事件 ----------

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
            return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ImportPaths(files);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.A)
        {
            foreach (ListViewItem it in _list.Items)
                it.Selected = true;
            e.Handled = true;
        }
    }

    private void OnListMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;
        var item = _list.GetItemAt(e.X, e.Y);
        _rightClickedPath = item?.Tag as string;
        if (item != null && !item.Selected)
        {
            item.Selected = true;
            item.Focused = true;
        }
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        bool hasTarget = !string.IsNullOrWhiteSpace(_rightClickedPath) ||
                         _list.SelectedItems.Count > 0;
        _miPlay.Enabled = _list.Items.Count > 0;
        _miOpenDir.Enabled = hasTarget;
        _miRemove.Enabled = _list.SelectedItems.Count > 0;
    }

    // ---------- 持久化 ----------

    private void SavePlaylist()
    {
        Store.SavePlaylist(PlaylistPath, _items);
    }
}
