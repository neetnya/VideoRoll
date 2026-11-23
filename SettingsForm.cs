using System.Drawing;
using System.Windows.Forms;

namespace VideoRoll;

public sealed class SettingsForm : Form
{
    public string MpvPathValue { get; private set; }
    public string ExtensionsValue { get; private set; }
    public string ExtraArgsValue { get; private set; }
    public bool RecursiveValue { get; private set; }

    private readonly TextBox _mpv = new();
    private readonly TextBox _ext = new();
    private readonly TextBox _extraArgs = new();
    private readonly CheckBox _rec = new();

    public SettingsForm(string mpvPath, IEnumerable<string> extensions, bool recursive, IEnumerable<string> extraArgs)
    {
        MpvPathValue = mpvPath;
        ExtensionsValue = string.Join(", ", extensions);
        ExtraArgsValue = string.Join(" ", extraArgs);
        RecursiveValue = recursive;

        Text = "设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(580, 316);
        Font = UiStyle.UiFont();
        UiStyle.ApplyChrome(this);
        ShowIcon = false;

        var lblMpv = new Label
        {
            Text = "mpv 播放器路径 (mpv.exe)：",
            Location = new Point(16, 18),
            AutoSize = true,
        };
        _mpv.Location = new Point(16, 42);
        _mpv.Size = new Size(446, 25);
        _mpv.Text = mpvPath;
        var btnBrowse = new Button
        {
            Text = "浏览...",
            Location = new Point(470, 40),
            Size = new Size(92, 28),
            FlatStyle = FlatStyle.System,
        };
        btnBrowse.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "mpv 播放器 (mpv.exe)|mpv.exe|所有文件 (*.*)|*.*",
                Title = "选择 mpv.exe",
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
                _mpv.Text = ofd.FileName;
        };

        var lblExt = new Label
        {
            Text = "视频文件扩展名（逗号分隔，不含点）：",
            Location = new Point(16, 82),
            AutoSize = true,
        };
        var btnResetExt = new Button
        {
            Text = "恢复默认",
            Location = new Point(470, 104),
            Size = new Size(92, 28),
            FlatStyle = FlatStyle.System,
        };
        btnResetExt.Click += (_, _) =>
        {
            _ext.Text = string.Join(", ", AppConfig.DefaultExtensions());
        };
        _ext.Location = new Point(16, 106);
        _ext.Size = new Size(446, 25);
        _ext.Text = ExtensionsValue;

        var lblExtra = new Label
        {
            Text = "额外 mpv 启动参数（空格分隔，例如 --fs --volume=80）：",
            Location = new Point(16, 146),
            AutoSize = true,
        };
        _extraArgs.Location = new Point(16, 170);
        _extraArgs.Size = new Size(546, 25);
        _extraArgs.Text = ExtraArgsValue;

        _rec.Location = new Point(16, 212);
        _rec.AutoSize = true;
        _rec.Text = "导入文件夹时递归搜索子文件夹";
        _rec.Checked = recursive;
        _rec.BackColor = BackColor;

        var btnOk = new Button
        {
            Text = "确定",
            Location = new Point(384, 266),
            Size = new Size(84, 32),
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.System,
        };
        btnOk.Click += (_, _) => Apply();

        var btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(478, 266),
            Size = new Size(84, 32),
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.System,
        };

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.AddRange(new Control[]
        {
            lblMpv, _mpv, btnBrowse,
            lblExt, _ext, btnResetExt,
            lblExtra, _extraArgs,
            _rec, btnOk, btnCancel,
        });
    }

    private void Apply()
    {
        MpvPathValue = _mpv.Text.Trim();
        ExtensionsValue = _ext.Text.Trim();
        ExtraArgsValue = _extraArgs.Text.Trim();
        RecursiveValue = _rec.Checked;
    }
}
