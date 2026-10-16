using System.Diagnostics;
using System.Text;

namespace VideoRoll;

public static class MpvLauncher
{
    /// <summary>
    /// 查找 mpv.exe：优先用配置里的路径，其次 exe 同目录 / mpv 子目录，最后搜索 PATH。
    /// </summary>
    public static string? FindMpv(string? configuredPath, string exeDir)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        string[] candidates =
        {
            Path.Combine(exeDir, "mpv.exe"),
            Path.Combine(exeDir, "mpv", "mpv.exe"),
        };
        foreach (var c in candidates)
            if (File.Exists(c))
                return c;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = Path.Combine(dir, "mpv.exe");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>
    /// 用软件当前的播放列表启动 mpv，并从 startIndex（0 基）处开始播放。
    /// 同时通过 --script-opts=autoload-disabled=yes 禁用 autoload.lua。
    /// 失败时返回错误说明，成功返回 null。
    /// </summary>
    public static string? Play(string mpvPath, IReadOnlyList<string> playlist, int startIndex,
        IReadOnlyList<string> extraArgs)
    {
        if (playlist.Count == 0)
            return "播放列表为空。";

        string tmp = Path.Combine(Path.GetTempPath(), "videoroll_" + Guid.NewGuid().ToString("n") + ".m3u8");
        try
        {
            var lines = new List<string> { "#EXTM3U" };
            lines.AddRange(playlist);
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));

            var parts = new List<string>
            {
                "--playlist=" + Quote(tmp),
                "--playlist-start=" + Math.Max(0, startIndex).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--script-opts=autoload-disabled=yes",
            };
            foreach (var a in extraArgs)
                if (!string.IsNullOrWhiteSpace(a))
                    parts.Add(a);

            var psi = new ProcessStartInfo
            {
                FileName = mpvPath,
                Arguments = string.Join(" ", parts),
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(mpvPath) ?? "",
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            TryDelete(tmp);
            return ex.Message;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                TryDelete(tmp);
            }
            catch { /* 临时文件清理由系统回收亦可 */ }
        });
        return null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 仍被占用时稍后由系统清理 */ }
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
}
