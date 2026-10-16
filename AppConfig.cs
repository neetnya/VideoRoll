using System.Text.Json;

namespace VideoRoll;

/// <summary>应用配置（保存在 exe 同目录的 config.json）。</summary>
public sealed class AppConfig
{
    /// <summary>mpv.exe 的完整路径。为空时自动探测。</summary>
    public string MpvPath { get; set; } = "";

    /// <summary>导入时识别为视频的扩展名（不含点）。</summary>
    public List<string> Extensions { get; set; } = DefaultExtensions();

    /// <summary>拖入文件夹时是否递归搜索子文件夹。</summary>
    public bool Recursive { get; set; } = true;

    /// <summary>额外的 mpv 启动参数（原样追加）。</summary>
    public List<string> ExtraArgs { get; set; } = new();

    public int WinX { get; set; } = int.MinValue;
    public int WinY { get; set; } = int.MinValue;
    public int WinW { get; set; }
    public int WinH { get; set; }
    public int ColNameWidth { get; set; } = 360;
    public string LastDir { get; set; } = "";

    public static List<string> DefaultExtensions() => new()
    {
        "3g2", "3gp", "avi", "flv", "m2ts", "m4v", "mj2", "mkv", "mov", "mp4",
        "mpeg", "mpg", "ogv", "rmvb", "webm", "wmv", "y4m", "ts", "mts", "vob",
        "asf", "divx", "f4v"
    };

    public void Normalize()
    {
        Extensions ??= DefaultExtensions();
        if (Extensions.Count == 0)
            Extensions = DefaultExtensions();
        ExtraArgs ??= new();
        LastDir ??= "";
        if (ColNameWidth < 80)
            ColNameWidth = 360;
    }
}

/// <summary>播放列表（保存在 exe 同目录的 playlist.json）。</summary>
public sealed class PlaylistData
{
    public List<string> Items { get; set; } = new();
}

public sealed class PlaylistLoadResult
{
    public List<string> Items { get; init; } = new();
    public string? Warning { get; init; }
}

/// <summary>读写 config.json / playlist.json。</summary>
public static class Store
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public static AppConfig LoadConfig(string path)
    {
        if (TryRead(path, out AppConfig? cfg) && cfg != null)
        {
            cfg.Normalize();
            return cfg;
        }
        if (TryRead(path + ".bak", out cfg) && cfg != null)
        {
            cfg.Normalize();
            return cfg;
        }
        return new AppConfig();
    }

    public static bool SaveConfig(string path, AppConfig cfg) =>
        TryWrite(path, cfg);

    public static PlaylistLoadResult LoadPlaylist(string path)
    {
        if (TryRead(path, out PlaylistData? data) && data?.Items != null)
            return new PlaylistLoadResult { Items = data.Items };

        bool mainExists = File.Exists(path);
        if (TryRead(path + ".bak", out data) && data?.Items != null)
        {
            return new PlaylistLoadResult
            {
                Items = data.Items,
                Warning = mainExists
                    ? "播放列表文件损坏，已从备份恢复。"
                    : null,
            };
        }

        if (mainExists)
        {
            return new PlaylistLoadResult
            {
                Warning = "播放列表文件损坏，已从空列表启动。",
            };
        }

        return new PlaylistLoadResult();
    }

    public static bool SavePlaylist(string path, IEnumerable<string> items)
    {
        var d = new PlaylistData { Items = items.ToList() };
        return TryWrite(path, d);
    }

    private static bool TryRead<T>(string path, out T? value) where T : class
    {
        value = null;
        try
        {
            if (!File.Exists(path))
                return false;
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts);
            return value != null;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static bool TryWrite<T>(string path, T value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOpts);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, path + ".bak");
                }
                catch
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
            else
            {
                File.Move(tmp, path, overwrite: true);
            }
            return true;
        }
        catch
        {
            try
            {
                var tmp = path + ".tmp";
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch { /* 清理失败可忽略 */ }
            return false;
        }
    }
}
