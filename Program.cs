using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace VideoRoll;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        string appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(appDir.ToLowerInvariant())))[..16];
        using var mutex = new Mutex(true, @"Local\VideoRoll." + id, out bool created);
        if (!created)
        {
            Native.ActivateOtherInstance();
            return;
        }

        Application.Run(new MainForm());
    }

    private static class Native
    {
        private const int SwRestore = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        public static void ActivateOtherInstance()
        {
            string? me = Environment.ProcessPath;
            int mine = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("VideoRoll"))
            {
                if (p.Id == mine)
                    continue;
                try
                {
                    if (!string.Equals(p.MainModule?.FileName, me, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch
                {
                    continue;
                }

                IntPtr h = p.MainWindowHandle;
                if (h == IntPtr.Zero)
                    continue;
                if (IsIconic(h))
                    ShowWindow(h, SwRestore);
                SetForegroundWindow(h);
                return;
            }

            MessageBox.Show("VideoRoll 已经在运行。", "VideoRoll",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
