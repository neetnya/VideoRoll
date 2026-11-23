using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VideoRoll;

public static class MpvLauncher
{
    private const string TempPrefix = "videoroll_";
    private const string TempSuffix = ".m3u8";

    /// <summary>
    /// 查找 mpv.exe：优先用配置里的路径，其次 exe 同目录 / mpv 子目录，最后搜索 PATH。
    /// </summary>
    public static string? FindMpv(string? configuredPath, string exeDir)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                if (File.Exists(configuredPath))
                    return configuredPath;
            }
            catch { /* 忽略非法路径字符 */ }
        }

        string[] candidates =
        {
            Path.Combine(exeDir, "mpv.exe"),
            Path.Combine(exeDir, "mpv", "mpv.exe"),
        };
        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(c))
                    return c;
            }
            catch { }
        }

        foreach (var rawDir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var dir = rawDir.Trim().Trim('"');
                if (dir.Length == 0)
                    continue;
                var p = Path.Combine(dir, "mpv.exe");
                if (File.Exists(p))
                    return p;
            }
            catch
            {
                // PATH 中存在非法字符路径时跳过
            }
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

        CleanupStaleTempPlaylistsAsync();

        string tmp = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("n") + TempSuffix);
        try
        {
            var lines = new List<string>(playlist.Count + 1) { "#EXTM3U" };
            lines.AddRange(playlist);
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = mpvPath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(mpvPath) ?? "",
            };
            psi.ArgumentList.Add("--playlist=" + tmp);
            psi.ArgumentList.Add("--playlist-start=" + Math.Max(0, startIndex).ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--script-opts=autoload-disabled=yes");

            foreach (var a in extraArgs)
            {
                if (!string.IsNullOrWhiteSpace(a))
                    psi.ArgumentList.Add(a.Trim());
            }

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                TryDelete(tmp);
                return "未能启动 mpv 进程。";
            }
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
            catch { /* 临时文件清理由后续启动或系统回收亦可 */ }
        });
        return null;
    }

    /// <summary>后台清理超过 1 小时的残留临时播放列表，避免进程提前退出导致 %TEMP% 堆积。</summary>
    private static void CleanupStaleTempPlaylistsAsync()
    {
        _ = Task.Run(() =>
        {
            try
            {
                string tempDir = Path.GetTempPath();
                DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                foreach (string file in Directory.EnumerateFiles(tempDir, TempPrefix + "*" + TempSuffix))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        });
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 仍被占用时稍后清理 */ }
    }
}
