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

    public static List<string> DefaultExtensions() => new()
    {
        "3g2", "3gp", "avi", "flv", "m2ts", "m4v", "mj2", "mkv", "mov", "mp4",
        "mpeg", "mpg", "ogv", "rmvb", "webm", "wmv", "y4m", "ts", "mts", "vob",
        "asf", "divx", "f4v"
    };
}

/// <summary>播放列表（保存在 exe 同目录的 playlist.json）。</summary>
public sealed class PlaylistData
{
    public List<string> Items { get; set; } = new();
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
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOpts);
                if (cfg != null)
                {
                    // 防止扩展名被写成空列表导致无法导入
                    if (cfg.Extensions == null || cfg.Extensions.Count == 0)
                        cfg.Extensions = AppConfig.DefaultExtensions();
                    return cfg;
                }
            }
        }
        catch
        {
            // 配置损坏时回退到默认值
        }
        return new AppConfig();
    }

    public static void SaveConfig(string path, AppConfig cfg)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOpts)); }
        catch { /* 只读目录等情况下静默失败 */ }
    }

    public static PlaylistData LoadPlaylist(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var d = JsonSerializer.Deserialize<PlaylistData>(File.ReadAllText(path), JsonOpts);
                if (d?.Items != null)
                    return d;
            }
        }
        catch
        {
            // 损坏时回退为空列表
        }
        return new PlaylistData();
    }

    public static void SavePlaylist(string path, IEnumerable<string> items)
    {
        try
        {
            var d = new PlaylistData { Items = items.ToList() };
            File.WriteAllText(path, JsonSerializer.Serialize(d, JsonOpts));
        }
        catch { /* 静默失败 */ }
    }
}
