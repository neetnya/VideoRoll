using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace VideoRoll;

public sealed class MainForm : Form
{
    private const string DefaultHint = "拖入文件夹或视频即可导入；双击播放；Del 移除；Ctrl+Z 撤销。";
    private const int UndoMax = 20;

    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripButton _btnShuffle = new("随机打乱");
    private readonly ToolStripButton _btnClear = new("清空列表");
    private readonly ToolStripButton _btnCleanInvalid = new("清理已失效");
    private readonly ToolStripButton _btnSettings = new("设置");
    private readonly TextBox _filter = new();

    private readonly ListView _list = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblHint = new();
    private readonly ToolStripStatusLabel _lblCount = new();
    private readonly ToolStripStatusLabel _lblMpv = new();

    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _miPlay = new("播放");
    private readonly ToolStripMenuItem _miOpenDir = new("打开文件所在目录");
    private readonly ToolStripMenuItem _miCopy = new("复制路径");
    private readonly ToolStripMenuItem _miRemove = new("从列表中移除");

    private readonly List<string> _items = new();
    private readonly List<List<string>> _undo = new();
    private readonly List<List<string>> _redo = new();
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);

    private AppConfig _config = new();
    private string? _rightClickedPath;
    private string? _hintOverride;
    private Color _hintColor = SystemColors.ControlText;
    private bool _importBusy;
    private bool _cleanBusy;
    private bool _adjustingCols;
    private bool _internalDrag;
    private ListViewItem? _dropHighlight;
    private int _scanGen;
    private bool _saveFailed;

    private string AppDir => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    private string ConfigPath => Path.Combine(AppDir, "config.json");
    private string PlaylistPath => Path.Combine(AppDir, "playlist.json");

    public MainForm()
    {
        _config = Store.LoadConfig(ConfigPath);
        var loaded = Store.LoadPlaylist(PlaylistPath);
        AbsorbPaths(loaded.Items);

        BuildUi();
        ApplyWindowBounds();
        RefreshList();
        ScheduleMissingScan();

        if (!string.IsNullOrEmpty(loaded.Warning))
        {
            Shown += (_, _) => MessageBox.Show(this, loaded.Warning, "VideoRoll",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------- UI 构建 ----------

    private void BuildUi()
    {
        Text = "VideoRoll 播放列表";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1032;
        Height = 672;
        MinimumSize = new Size(600, 380);
        AllowDrop = true;
        KeyPreview = true;
        Font = UiStyle.UiFont();
        Icon = UiStyle.CreateAppIcon();
        UiStyle.ApplyChrome(this);

        UiStyle.ApplyToolStrip(_toolbar);
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Font = Font;

        var btnAdd = new ToolStripDropDownButton("添加");
        btnAdd.DropDownItems.Add("添加文件...", null, (_, _) => AddFilesViaDialog());
        btnAdd.DropDownItems.Add("添加文件夹...", null, (_, _) => AddFolderViaDialog());

        _btnShuffle.ToolTipText = "随机打乱播放列表顺序（Ctrl+Z 可撤销）";
        _btnShuffle.Click += (_, _) => Shuffle();
        _btnClear.ToolTipText = "清空列表（不会删除实际文件）";
        _btnClear.Click += (_, _) => ClearAll();
        _btnCleanInvalid.ToolTipText = "移除列表中文件已不存在的条目";
        _btnCleanInvalid.Click += (_, _) => CleanInvalid();
        _btnSettings.ToolTipText = "设置 mpv 路径、扩展名等";
        _btnSettings.Click += (_, _) => OpenSettings();

        _toolbar.Items.Add(btnAdd);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(_btnShuffle);
        _toolbar.Items.Add(_btnClear);
        _toolbar.Items.Add(_btnCleanInvalid);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(_btnSettings);

        var filterHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(10, 4, 10, 6),
        };
        if (!SystemInformation.HighContrast)
            filterHost.BackColor = UiStyle.Chrome;
        var lblFilter = new Label
        {
            Text = "过滤",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _filter.BorderStyle = BorderStyle.FixedSingle;
        _filter.Dock = DockStyle.Fill;
        _filter.TextChanged += (_, _) => RefreshList();
        _filter.KeyDown += OnFilterKeyDown;
        UiStyle.SetCue(_filter, "输入文件名过滤列表…");
        var filterInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        if (!SystemInformation.HighContrast)
            filterInner.BackColor = UiStyle.Chrome;
        filterInner.Controls.Add(_filter);
        filterHost.Controls.Add(filterInner);
        filterHost.Controls.Add(lblFilter);
        lblFilter.Dock = DockStyle.Left;
        lblFilter.AutoSize = false;
        lblFilter.Width = 48;
        lblFilter.TextAlign = ContentAlignment.MiddleLeft;

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.AllowDrop = true;
        _list.LabelEdit = false;
        _list.ShowItemToolTips = true;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        if (!SystemInformation.HighContrast)
            _list.BackColor = UiStyle.ListBack;
        UiStyle.EnableDoubleBuffer(_list);
        int nameW = _config.ColNameWidth;
        _list.Columns.Add("文件名", nameW);
        _list.Columns.Add("路径", 120);
        _list.ItemActivate += (_, _) => OnItemActivate();
        _list.KeyDown += OnListKeyDown;
        _list.ItemDrag += OnItemDrag;
        _list.DragOver += OnListDragOver;
        _list.DragLeave += (_, _) => ClearDropHighlight();
        _list.DragEnter += OnDragEnter;
        _list.DragDrop += OnDragDrop;
        _list.Resize += (_, _) => StretchPathColumn();
        _list.ColumnWidthChanged += OnColumnWidthChanged;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        KeyDown += OnFormKeyDown;
        FormClosing += OnFormClosing;

        _miPlay.Click += (_, _) => PlaySelected();
        _miOpenDir.Click += (_, _) => OpenClickedItemFolder();
        _miCopy.Click += (_, _) => CopySelectedPaths();
        _miRemove.Click += (_, _) => RemoveSelected();
        var miAddFiles = new ToolStripMenuItem("添加文件...");
        miAddFiles.Click += (_, _) => AddFilesViaDialog();
        var miAddFolder = new ToolStripMenuItem("添加文件夹...");
        miAddFolder.Click += (_, _) => AddFolderViaDialog();
        var miExport = new ToolStripMenuItem("导出播放列表...");
        miExport.Click += (_, _) => ExportPlaylist();
        var miClear = new ToolStripMenuItem("清空列表");
        miClear.Click += (_, _) => ClearAll();

        _menu.Font = Font;
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _miPlay, _miOpenDir, _miCopy, _miRemove,
            new ToolStripSeparator(),
            miAddFiles, miAddFolder, miExport,
            new ToolStripSeparator(),
            miClear,
        });
        _menu.Opening += OnMenuOpening;
        _list.ContextMenuStrip = _menu;

        _lblHint.Spring = true;
        _lblHint.TextAlign = ContentAlignment.MiddleLeft;
        _lblHint.Text = DefaultHint;
        _lblCount.AutoSize = false;
        _lblCount.Width = 170;
        _lblCount.TextAlign = ContentAlignment.MiddleRight;
        _lblMpv.AutoSize = false;
        _lblMpv.Width = 110;
        _lblMpv.TextAlign = ContentAlignment.MiddleRight;
        _status.SizingGrip = true;
        if (!SystemInformation.HighContrast)
            _status.BackColor = UiStyle.Chrome;
        _status.Items.AddRange(new ToolStripItem[] { _lblHint, _lblCount, _lblMpv });

        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 0, 10, 8),
        };
        if (!SystemInformation.HighContrast)
            listHost.BackColor = UiStyle.Chrome;
        listHost.Controls.Add(_list);

        Controls.Add(listHost);
        Controls.Add(filterHost);
        Controls.Add(_toolbar);
        Controls.Add(_status);
    }

    // ---------- 列表刷新 ----------

    private void RefreshList()
    {
        var selected = SelectedPaths();
        string? focused = _list.FocusedItem?.Tag as string;
        string? top = _list.TopItem?.Tag as string;
        string filter = _filter.Text.Trim();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in _items)
        {
            var name = Path.GetFileName(p);
            if (filter.Length > 0 &&
                name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                p.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var li = new ListViewItem(name);
            li.SubItems.Add(p);
            li.Tag = p;
            li.ToolTipText = p;
            if (_missing.Contains(p))
                li.ForeColor = UiStyle.MissingText;
            _list.Items.Add(li);
        }
        _list.EndUpdate();

        foreach (ListViewItem it in _list.Items)
        {
            var p = (string)it.Tag!;
            if (selected.Contains(p))
                it.Selected = true;
            if (focused != null && string.Equals(p, focused, StringComparison.OrdinalIgnoreCase))
                it.Focused = true;
        }

        if (top != null)
        {
            foreach (ListViewItem it in _list.Items)
            {
                if (string.Equals((string)it.Tag!, top, StringComparison.OrdinalIgnoreCase))
                {
                    try { _list.TopItem = it; } catch { /* 尚未布局完时可能失败 */ }
                    break;
                }
            }
        }

        StretchPathColumn();
        UpdateStatus();
    }

    private HashSet<string> SelectedPaths() =>
        new(_list.SelectedItems.Cast<ListViewItem>().Select(x => (string)x.Tag!),
            StringComparer.OrdinalIgnoreCase);

    private void UpdateStatus()
    {
        int shown = _list.Items.Count;
        int missing = _items.Count(p => _missing.Contains(p));
        if (_items.Count == 0)
            _lblCount.Text = "列表为空";
        else if (shown != _items.Count)
            _lblCount.Text = $"显示 {shown} / 共 {_items.Count} 项";
        else if (missing > 0)
            _lblCount.Text = $"共 {_items.Count} 项 · {missing} 失效";
        else
            _lblCount.Text = $"共 {_items.Count} 项";

        var mpv = MpvLauncher.FindMpv(_config.MpvPath, AppDir);
        _lblMpv.Text = mpv != null ? "mpv 就绪" : "未找到 mpv";

        if (_saveFailed)
        {
            _lblHint.Text = "列表未能保存（目录只读或权限不足）。";
            _lblHint.ForeColor = UiStyle.HintWarn;
        }
        else
        {
            _lblHint.Text = _hintOverride ?? DefaultHint;
            _lblHint.ForeColor = _hintColor;
        }
    }

    private void SetHint(string? text, bool warn = false)
    {
        _hintOverride = text;
        _hintColor = warn ? UiStyle.HintWarn : SystemColors.ControlText;
        UpdateStatus();
    }

    private void StretchPathColumn()
    {
        if (_adjustingCols || _list.Columns.Count < 2)
            return;
        _adjustingCols = true;
        int w = _list.ClientSize.Width - _list.Columns[0].Width - 8;
        _list.Columns[1].Width = Math.Max(80, w);
        _adjustingCols = false;
    }

    private void OnColumnWidthChanged(object? sender, ColumnWidthChangedEventArgs e)
    {
        if (e.ColumnIndex == 0)
            StretchPathColumn();
    }

    // ---------- 路径 ----------

    private void AbsorbPaths(IEnumerable<string> stored)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in stored)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string full;
            try { full = ToFullPath(raw); }
            catch { full = raw; }
            if (seen.Add(full))
                _items.Add(full);
        }
    }

    private string ToFullPath(string stored)
    {
        if (!Path.IsPathFullyQualified(stored))
            return Path.GetFullPath(Path.Combine(AppDir, stored));

        var full = Path.GetFullPath(stored);

        // 绿色盘换盘符后，把原来的绝对路径接到当前程序所在盘再试一次
        if (full.Length >= 3 && full[1] == ':' && AppDir.Length >= 3 && AppDir[1] == ':' &&
            char.ToUpperInvariant(full[0]) != char.ToUpperInvariant(AppDir[0]))
        {
            var relocated = AppDir[0] + full[1..];
            if (File.Exists(relocated))
                return Path.GetFullPath(relocated);
        }
        return full;
    }

    private string ToStoragePath(string full)
    {
        try
        {
            full = Path.GetFullPath(full);
            var rel = Path.GetRelativePath(AppDir, full);
            if (!Path.IsPathFullyQualified(rel))
                return rel;
        }
        catch { /* 跨盘等情况下保持绝对路径 */ }
        return full;
    }

    // ---------- 导入 ----------

    private void ImportPaths(IEnumerable<string> paths)
    {
        var snapshot = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (snapshot.Length == 0)
            return;
        if (_importBusy)
        {
            SetHint("正在导入，请稍候…");
            return;
        }

        _importBusy = true;
        SetHint("正在扫描…");
        var exts = new HashSet<string>(
            _config.Extensions
                .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
                .Where(e => e.Length > 0));
        var existing = new HashSet<string>(_items, StringComparer.OrdinalIgnoreCase);
        bool recursive = _config.Recursive;

        Task.Run(() =>
        {
            var added = new List<string>();
            int duplicates = 0, skippedExt = 0, folderErrors = 0;
            foreach (var raw in snapshot)
            {
                try
                {
                    if (Directory.Exists(raw))
                    {
                        foreach (var f in EnumerateFilesSafe(raw, recursive, exts, ref folderErrors))
                        {
                            if (!existing.Add(f))
                                duplicates++;
                            else
                                added.Add(f);
                        }
                    }
                    else if (File.Exists(raw))
                    {
                        if (!MatchesExt(raw, exts))
                            skippedExt++;
                        else if (!existing.Add(raw))
                            duplicates++;
                        else
                            added.Add(raw);
                    }
                }
                catch
                {
                    folderErrors++;
                }
            }

            added.Sort(NaturalSort.Compare);
            Ui(() => FinishImport(added, duplicates, skippedExt, folderErrors));
        });
    }

    private void FinishImport(List<string> added, int duplicates, int skippedExt, int folderErrors)
    {
        _importBusy = false;
        if (added.Count > 0)
        {
            PushUndo();
            _items.AddRange(added);
            RefreshList();
            SavePlaylist();
            ScheduleMissingScan();
        }

        var bits = new List<string>();
        if (added.Count > 0)
            bits.Add($"已导入 {added.Count} 项");
        else
            bits.Add("未导入新视频");
        if (duplicates > 0)
            bits.Add($"重复 {duplicates}");
        if (skippedExt > 0)
            bits.Add($"扩展名不符 {skippedExt}");
        if (folderErrors > 0)
            bits.Add($"跳过 {folderErrors} 个无法访问的位置");
        SetHint(string.Join(" · ", bits), added.Count == 0);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, bool recursive, HashSet<string> exts, ref int folderErrors)
    {
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        int errors = 0;
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (MatchesExt(f, exts))
                        result.Add(f);
                }
            }
            catch
            {
                errors++;
                continue;
            }

            if (!recursive)
                continue;
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    stack.Push(sub);
            }
            catch
            {
                errors++;
            }
        }
        folderErrors += errors;
        return result;
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
            InitialDirectory = Directory.Exists(_config.LastDir) ? _config.LastDir : "",
        };
        if (ofd.ShowDialog(this) != DialogResult.OK)
            return;
        RememberDir(ofd.FileNames[0]);
        ImportPaths(ofd.FileNames);
    }

    private void AddFolderViaDialog()
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "选择要导入的视频文件夹",
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(_config.LastDir))
            fbd.SelectedPath = _config.LastDir;
        if (fbd.ShowDialog(this) != DialogResult.OK)
            return;
        RememberDir(fbd.SelectedPath);
        ImportPaths(new[] { fbd.SelectedPath });
    }

    private void RememberDir(string path)
    {
        try
        {
            var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            _config.LastDir = dir;
            Store.SaveConfig(ConfigPath, _config);
        }
        catch { /* 记住目录失败可忽略 */ }
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
        var item = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null;
        if (item != null)
            PlayItem(item);
    }

    private void PlayItem(ListViewItem item)
    {
        string? path = item.Tag as string;
        if (string.IsNullOrWhiteSpace(path))
            return;
        int index = _items.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        string? mpv = ResolveMpv();
        if (mpv == null)
            return;
        string? err = MpvLauncher.Play(mpv, _items, index, _config.ExtraArgs ?? []);
        if (err != null)
        {
            MessageBox.Show(this, "无法启动 mpv：\n" + err, "VideoRoll",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    // ---------- 移除 / 打乱 / 清空 / 移动 ----------

    private void RemoveSelected()
    {
        if (_list.SelectedItems.Count == 0)
            return;
        var toRemove = SelectedPaths();
        PushUndo();
        _items.RemoveAll(p => toRemove.Contains(p));
        RefreshList();
        SavePlaylist();
        SetHint($"已移除 {toRemove.Count} 项，Ctrl+Z 可撤销。");
    }

    private void Shuffle()
    {
        if (_items.Count < 2)
            return;
        PushUndo();
        for (int i = _items.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_items[i], _items[j]) = (_items[j], _items[i]);
        }
        RefreshList();
        SavePlaylist();
        SetHint("已打乱顺序，Ctrl+Z 可撤销。");
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
        PushUndo();
        _items.Clear();
        _missing.Clear();
        RefreshList();
        SavePlaylist();
        SetHint("列表已清空，Ctrl+Z 可撤销。");
    }

    private void CleanInvalid()
    {
        if (_cleanBusy || _items.Count == 0)
            return;
        _cleanBusy = true;
        _btnCleanInvalid.Enabled = false;
        SetHint("正在检查失效文件…");
        var snapshot = _items.ToList();
        Task.Run(() =>
        {
            var invalid = snapshot.Where(p => !File.Exists(p)).ToList();
            Ui(() => FinishCleanInvalid(invalid));
        });
    }

    private void FinishCleanInvalid(List<string> invalid)
    {
        _cleanBusy = false;
        _btnCleanInvalid.Enabled = true;

        _missing.Clear();
        foreach (var p in invalid)
            _missing.Add(p);
        RefreshList();

        if (invalid.Count == 0)
        {
            MessageBox.Show(this, "没有发现已失效的条目。", "VideoRoll",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetHint(null);
            return;
        }

        var r = MessageBox.Show(
            this,
            $"共发现 {invalid.Count} 个文件已不存在的条目，是否从列表中移除？",
            "清理已失效",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (r != DialogResult.Yes)
        {
            SetHint($"列表中有 {invalid.Count} 个失效条目（灰色显示）。");
            return;
        }

        var removed = new HashSet<string>(invalid, StringComparer.OrdinalIgnoreCase);
        PushUndo();
        _items.RemoveAll(p => removed.Contains(p));
        foreach (var p in invalid)
            _missing.Remove(p);
        RefreshList();
        SavePlaylist();
        SetHint($"已移除 {invalid.Count} 个失效条目，Ctrl+Z 可撤销。");
    }

    private void MoveSelected(int delta)
    {
        if (delta == 0 || _list.SelectedItems.Count == 0 || IsFilterOn())
            return;
        var selected = _list.SelectedItems.Cast<ListViewItem>()
            .Select(x => (string)x.Tag!)
            .ToList();
        var indexes = selected
            .Select(p => _items.FindIndex(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();
        if (indexes.Count == 0)
            return;
        if (delta < 0 && indexes[0] == 0)
            return;
        if (delta > 0 && indexes[^1] == _items.Count - 1)
            return;

        PushUndo();
        if (delta < 0)
        {
            foreach (int i in indexes)
                (_items[i - 1], _items[i]) = (_items[i], _items[i - 1]);
        }
        else
        {
            for (int k = indexes.Count - 1; k >= 0; k--)
            {
                int i = indexes[k];
                (_items[i + 1], _items[i]) = (_items[i], _items[i + 1]);
            }
        }
        RefreshList();
        SavePlaylist();
        SetHint("已调整顺序，Ctrl+Z 可撤销。");
    }

    // ---------- 拖拽排序 ----------

    private bool IsFilterOn() => _filter.Text.Trim().Length > 0;

    private void OnItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (IsFilterOn() || _list.SelectedItems.Count == 0)
            return;
        _internalDrag = true;
        var data = new DataObject();
        data.SetData("VideoRollItems", SelectedPaths().ToList());
        try
        {
            _list.DoDragDrop(data, DragDropEffects.Move);
        }
        finally
        {
            _internalDrag = false;
            ClearDropHighlight();
        }
    }

    private void OnListDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent("VideoRollItems") == true)
        {
            if (IsFilterOn())
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;
            HighlightDropTarget(e);
            return;
        }
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HighlightDropTarget(DragEventArgs e)
    {
        var pt = _list.PointToClient(new Point(e.X, e.Y));
        var hit = _list.GetItemAt(pt.X, pt.Y);
        if (hit == _dropHighlight)
            return;
        ClearDropHighlight();
        if (hit == null)
            return;
        hit.BackColor = SystemInformation.HighContrast
            ? SystemColors.Highlight
            : Color.FromArgb(226, 234, 244);
        _dropHighlight = hit;
    }

    private void ClearDropHighlight()
    {
        if (_dropHighlight != null)
        {
            _dropHighlight.BackColor = _list.BackColor;
            _dropHighlight = null;
        }
    }

    private void ReorderDropped(List<string> moving, DragEventArgs e)
    {
        var pt = _list.PointToClient(new Point(e.X, e.Y));
        var hit = _list.GetItemAt(pt.X, pt.Y);
        int insertAt;
        if (hit?.Tag is string target)
        {
            insertAt = _items.FindIndex(p => string.Equals(p, target, StringComparison.OrdinalIgnoreCase));
            if (insertAt < 0)
                insertAt = _items.Count;
            else if (pt.Y > hit.Bounds.Top + hit.Bounds.Height / 2)
                insertAt++;
        }
        else
        {
            insertAt = _items.Count;
        }

        var moveSet = new HashSet<string>(moving, StringComparer.OrdinalIgnoreCase);
        var orderedMove = _items.Where(moveSet.Contains).ToList();
        if (orderedMove.Count == 0)
            return;

        int removedBefore = _items.Take(insertAt).Count(moveSet.Contains);
        PushUndo();
        _items.RemoveAll(moveSet.Contains);
        insertAt = Math.Clamp(insertAt - removedBefore, 0, _items.Count);
        _items.InsertRange(insertAt, orderedMove);
        RefreshList();
        SavePlaylist();
        SetHint("已调整顺序，Ctrl+Z 可撤销。");
    }

    // ---------- 打开所在目录 / 复制 / 导出 ----------

    private void OpenClickedItemFolder()
    {
        string? p = _rightClickedPath;
        if (string.IsNullOrWhiteSpace(p) && _list.SelectedItems.Count > 0)
            p = _list.SelectedItems[0].Tag as string;
        if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
            return;
        OpenInExplorer(p);
    }

    private void OpenSelectedInExplorer()
    {
        var item = _list.SelectedItems.Count == 1
            ? _list.SelectedItems[0]
            : _list.FocusedItem;
        string? p = item?.Tag as string;
        if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
            return;
        OpenInExplorer(p);
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
            {
                UseShellExecute = true,
            });
        }
        catch { /* 忽略 */ }
    }

    private void CopySelectedPaths()
    {
        var paths = _list.SelectedItems.Count > 0
            ? _list.SelectedItems.Cast<ListViewItem>().Select(x => (string)x.Tag!).ToList()
            : (!string.IsNullOrWhiteSpace(_rightClickedPath) ? new List<string> { _rightClickedPath } : new());
        if (paths.Count == 0)
            return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
            SetHint($"已复制 {paths.Count} 条路径。");
        }
        catch
        {
            SetHint("复制到剪贴板失败。", warn: true);
        }
    }

    private void ExportPlaylist()
    {
        if (_items.Count == 0)
            return;
        using var sfd = new SaveFileDialog
        {
            Title = "导出播放列表",
            Filter = "播放列表 (*.m3u8)|*.m3u8|所有文件 (*.*)|*.*",
            FileName = "playlist.m3u8",
            InitialDirectory = Directory.Exists(_config.LastDir) ? _config.LastDir : "",
        };
        if (sfd.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            var lines = new List<string> { "#EXTM3U" };
            lines.AddRange(_items);
            File.WriteAllLines(sfd.FileName, lines, new System.Text.UTF8Encoding(false));
            RememberDir(sfd.FileName);
            SetHint("已导出播放列表。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：\n" + ex.Message, "VideoRoll",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        if (!Store.SaveConfig(ConfigPath, _config))
        {
            MessageBox.Show(this, "设置未能保存（目录只读或权限不足）。", "VideoRoll",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdateStatus();
    }

    // ---------- 事件 ----------

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent("VideoRollItems") == true)
        {
            e.Effect = IsFilterOn() ? DragDropEffects.None : DragDropEffects.Move;
            return;
        }
        if (_internalDrag)
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        ClearDropHighlight();
        if (e.Data?.GetDataPresent("VideoRollItems") == true)
        {
            if (ReferenceEquals(sender, _list) && e.Data.GetData("VideoRollItems") is List<string> moving)
                ReorderDropped(moving, e);
            return;
        }
        if (_internalDrag)
            return;
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
            return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            ImportPaths(files);
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (_filter.Focused)
            return;
        if (e.Control && e.KeyCode == Keys.Z)
        {
            Undo();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Y)
        {
            Redo();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _filter.Clear();
            e.Handled = true;
        }
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
        else if (e.Control && e.KeyCode == Keys.F)
        {
            OpenSelectedInExplorer();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.C)
        {
            CopySelectedPaths();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.Up)
        {
            MoveSelected(-1);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Down)
        {
            MoveSelected(1);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var pos = _list.PointToClient(Cursor.Position);
        var hit = _list.GetItemAt(pos.X, pos.Y);
        _rightClickedPath = hit?.Tag as string;
        if (hit != null && !hit.Selected)
        {
            _list.SelectedItems.Clear();
            hit.Selected = true;
            hit.Focused = true;
        }

        bool hasTarget = hit != null || _list.SelectedItems.Count > 0;
        _miPlay.Enabled = hasTarget;
        _miOpenDir.Enabled = hasTarget;
        _miCopy.Enabled = hasTarget;
        _miRemove.Enabled = _list.SelectedItems.Count > 0;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (WindowState == FormWindowState.Normal)
        {
            _config.WinX = Left;
            _config.WinY = Top;
            _config.WinW = Width;
            _config.WinH = Height;
        }
        _config.ColNameWidth = _list.Columns[0].Width;
        Store.SaveConfig(ConfigPath, _config);
        SavePlaylist();
    }

    private void ApplyWindowBounds()
    {
        if (_config.WinW < MinimumSize.Width || _config.WinH < MinimumSize.Height)
            return;
        var bounds = new Rectangle(_config.WinX, _config.WinY, _config.WinW, _config.WinH);
        bool visible = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds));
        if (!visible)
            return;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
    }

    // ---------- 撤销 ----------

    private void PushUndo()
    {
        _undo.Add(_items.ToList());
        if (_undo.Count > UndoMax)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Add(_items.ToList());
        _items.Clear();
        _items.AddRange(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        RefreshList();
        SavePlaylist();
        ScheduleMissingScan();
        SetHint("已撤销。");
    }

    private void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Add(_items.ToList());
        _items.Clear();
        _items.AddRange(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        RefreshList();
        SavePlaylist();
        ScheduleMissingScan();
        SetHint("已重做。");
    }

    // ---------- 失效扫描 ----------

    private void ScheduleMissingScan()
    {
        int gen = Interlocked.Increment(ref _scanGen);
        var snapshot = _items.ToList();
        Task.Run(() =>
        {
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in snapshot)
            {
                if (gen != _scanGen)
                    return;
                if (!File.Exists(p))
                    missing.Add(p);
            }
            Ui(() =>
            {
                if (gen != _scanGen)
                    return;
                _missing.Clear();
                foreach (var p in missing)
                    _missing.Add(p);
                foreach (ListViewItem it in _list.Items)
                {
                    var path = (string)it.Tag!;
                    it.ForeColor = _missing.Contains(path) ? UiStyle.MissingText : _list.ForeColor;
                }
                UpdateStatus();
            });
        });
    }

    // ---------- 持久化 ----------

    private void Ui(Action action)
    {
        if (IsDisposed)
            return;
        try
        {
            if (InvokeRequired)
                BeginInvoke(action);
            else
                action();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void SavePlaylist()
    {
        var stored = _items.Select(ToStoragePath);
        _saveFailed = !Store.SavePlaylist(PlaylistPath, stored);
        UpdateStatus();
    }
}
