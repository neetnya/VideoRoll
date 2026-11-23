using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace VideoRoll;

internal static class Program
{
    private static string AppDir =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogCrash(e.Exception, "UI");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LogCrash(ex, "AppDomain");
        };

        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppDir.ToLowerInvariant())))[..16];
        Mutex? mutex = null;
        bool created = false;
        try
        {
            mutex = new Mutex(true, @"Local\VideoRoll." + id, out created);
        }
        catch (AbandonedMutexException ex)
        {
            created = true;
            mutex = ex.Mutex;
        }

        using (mutex)
        {
            if (!created)
            {
                Native.ActivateOtherInstance();
                return;
            }

            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                try { mutex?.ReleaseMutex(); } catch (ApplicationException) { }
            }
        }
    }

    private static void LogCrash(Exception ex, string source)
    {
        try
        {
            string logPath = Path.Combine(AppDir, "error.log");
            string text = $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] ==={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, text, Encoding.UTF8);
        }
        catch
        {
            // 日志写入失败忽略
        }
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
            Process[] procs = Process.GetProcessesByName("VideoRoll");
            bool activated = false;

            foreach (var p in procs)
            {
                using (p)
                {
                    if (activated || p.Id == mine)
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
                    activated = true;
                }
            }

            if (!activated)
            {
                MessageBox.Show("VideoRoll 已经在运行。", "VideoRoll",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
