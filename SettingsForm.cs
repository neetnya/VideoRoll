using System.Drawing;
using System.Windows.Forms;

namespace VideoRoll;

public sealed class SettingsForm : Form
{
    public string MpvPathValue { get; private set; }
    public string ExtensionsValue { get; private set; }
    public bool RecursiveValue { get; private set; }

    private readonly TextBox _mpv = new();
    private readonly TextBox _ext = new();
    private readonly CheckBox _rec = new();

    public SettingsForm(string mpvPath, IEnumerable<string> extensions, bool recursive)
    {
        MpvPathValue = mpvPath;
        ExtensionsValue = string.Join(", ", extensions);
        RecursiveValue = recursive;

        Text = "设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 240);
        Font = new Font("Microsoft YaHei UI", 9F);

        var lblMpv = new Label
        {
            Text = "mpv 播放器路径 (mpv.exe)：",
            Location = new Point(16, 20),
            AutoSize = true,
        };
        _mpv.Location = new Point(16, 44);
        _mpv.Size = new Size(446, 25);
        _mpv.Text = mpvPath;
        var btnBrowse = new Button
        {
            Text = "浏览...",
            Location = new Point(470, 43),
            Size = new Size(92, 27),
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
            Location = new Point(16, 86),
            AutoSize = true,
        };
        _ext.Location = new Point(16, 110);
        _ext.Size = new Size(546, 25);
        _ext.Text = ExtensionsValue;

        _rec.Location = new Point(16, 148);
        _rec.AutoSize = true;
        _rec.Text = "导入文件夹时递归搜索子文件夹";
        _rec.Checked = recursive;

        var btnOk = new Button
        {
            Text = "确定",
            Location = new Point(384, 192),
            Size = new Size(84, 32),
            DialogResult = DialogResult.OK,
        };
        btnOk.Click += (_, _) => Apply();

        var btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(478, 192),
            Size = new Size(84, 32),
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.AddRange(new Control[]
        {
            lblMpv, _mpv, btnBrowse, lblExt, _ext, _rec, btnOk, btnCancel,
        });
    }

    private void Apply()
    {
        MpvPathValue = _mpv.Text.Trim();
        ExtensionsValue = _ext.Text.Trim();
        RecursiveValue = _rec.Checked;
    }
}
