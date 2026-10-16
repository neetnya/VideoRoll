using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VideoRoll;

/// <summary>克制的视觉：统一字体、平工具栏、简单图标。不引入额外交互。</summary>
internal static class UiStyle
{
    public static readonly Color Chrome = Color.FromArgb(243, 243, 243);
    public static readonly Color ListBack = Color.White;
    public static Color MissingText =>
        SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(150, 150, 150);
    public static readonly Color HintWarn = Color.FromArgb(180, 80, 0);

    private const int EmSetCueBanner = 0x1501;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Font UiFont() => new("Microsoft YaHei UI", 9F);

    public static void ApplyChrome(Form form)
    {
        if (SystemInformation.HighContrast)
            return;
        form.BackColor = Chrome;
    }

    public static void ApplyToolStrip(ToolStrip strip)
    {
        strip.GripStyle = ToolStripGripStyle.Hidden;
        strip.Padding = new Padding(6, 3, 6, 3);
        strip.RenderMode = ToolStripRenderMode.Professional;
        if (!SystemInformation.HighContrast)
        {
            strip.Renderer = new FlatRenderer();
            strip.BackColor = Chrome;
        }
    }

    public static void SetCue(TextBox box, string cue)
    {
        void apply() => SendMessage(box.Handle, EmSetCueBanner, (IntPtr)1, cue);
        if (box.IsHandleCreated)
            apply();
        else
            box.HandleCreated += (_, _) => apply();
    }

    public static void EnableDoubleBuffer(ListView list)
    {
        typeof(ListView)
            .GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(list, true);
    }

    public static Icon CreateAppIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var bg = new SolidBrush(Color.FromArgb(47, 108, 176)))
                g.FillEllipse(bg, 1, 1, 30, 30);
            Point[] tri = { new(12, 9), new(12, 23), new(24, 16) };
            g.FillPolygon(Brushes.White, tri);
        }

        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }

    private sealed class FlatRenderer : ToolStripProfessionalRenderer
    {
        public FlatRenderer() : base(new FlatColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // 去掉工具栏底边线，让界面更干净
        }
    }

    private sealed class FlatColors : ProfessionalColorTable
    {
        private static readonly Color Hover = Color.FromArgb(226, 234, 244);
        private static readonly Color Press = Color.FromArgb(204, 220, 238);
        private static readonly Color Line = Color.FromArgb(220, 220, 220);

        public override Color ToolStripGradientBegin => Chrome;
        public override Color ToolStripGradientMiddle => Chrome;
        public override Color ToolStripGradientEnd => Chrome;
        public override Color ImageMarginGradientBegin => Chrome;
        public override Color ImageMarginGradientMiddle => Chrome;
        public override Color ImageMarginGradientEnd => Chrome;
        public override Color OverflowButtonGradientBegin => Chrome;
        public override Color OverflowButtonGradientMiddle => Chrome;
        public override Color OverflowButtonGradientEnd => Chrome;
        public override Color ToolStripBorder => Chrome;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
        public override Color MenuBorder => Line;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color ButtonSelectedBorder => Color.FromArgb(201, 214, 230);
        public override Color ButtonPressedGradientBegin => Press;
        public override Color ButtonPressedGradientMiddle => Press;
        public override Color ButtonPressedGradientEnd => Press;
        public override Color ButtonPressedBorder => Color.FromArgb(201, 214, 230);
        public override Color StatusStripGradientBegin => Chrome;
        public override Color StatusStripGradientEnd => Chrome;
    }
}
