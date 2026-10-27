using System;
using System.IO;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 媒体资产设置的读写：落 <c>&lt;应用根&gt;\config\media-assets.json</c>。
/// <para>
/// 单例；<see cref="Settings"/> 是**活实例**（消费者每次读最新值，⛔ 不各存一份快照）。
/// 设置页 <c>Save</c> 后由调用方把新值应用到 <see cref="ScanService"/>（忽略规则 / 采样大小）。
/// </para>
/// </summary>
public class MediaAssetsSettingsIO
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ConfigStoreService _cfg;

    public MediaAssetsSettingsIO(ConfigStoreService cfg)
    {
        _cfg = cfg;
        Settings = Load();
    }

    /// <summary>活实例（属性可被设置页直接改；改完调 <see cref="Save"/> 落盘）。</summary>
    public MediaAssetsSettings Settings { get; }

    /// <summary>本模块设置文件（起落 <c>config\modules\media-assets.json</c>）。</summary>
    public string FilePath => Path.Combine(_cfg.ModulesConfigDir, "media-assets.json");

    /// <summary>迁移前的旧位置（<c>config\media-assets.json</c>）；搬文件被占用时按它兜底读。</summary>
    private string LegacyPath => Path.Combine(_cfg.ConfigDir, "media-assets.json");

    /// <summary>缩略图目录（生效值）：配置为空时用 <c>&lt;配置根&gt;\cache\assets\thumbs</c>（可重建，不进备份）。</summary>
    public string ThumbsDirectory
    {
        get
        {
            var custom = (Settings.ThumbsDirectory ?? "").Trim();
            if (custom.Length == 0) return Path.Combine(_cfg.CacheDir, "assets", "thumbs");
            if (Directory.Exists(custom)) return custom;

            // 存储分层前的绝对路径（<配置根>\data\thumbs）已随迁移搬到 cache\ / store\：目录在就改回配置。
            var healed = MapLegacyDataPath(custom);
            if (healed == null) return custom;

            Settings.ThumbsDirectory = healed;
            Save();
            return healed;
        }
    }

    /// <summary>把旧的 <c>&lt;配置根&gt;\data\&lt;rest&gt;</c> 映射到 <c>store\&lt;rest&gt;</c> / <c>cache\&lt;rest&gt;</c>（存在才认）。</summary>
    private string? MapLegacyDataPath(string path)
    {
        try
        {
            var prefix = Path.Combine(_cfg.ProfileRootDirectory, "data") + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var rest = path[prefix.Length..];
            foreach (var candidate in new[]
                     {
                         Path.Combine(_cfg.CacheDir, rest),
                         Path.Combine(_cfg.StoreDir, rest),
                     })
            {
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        catch { /* 路径非法 ⇒ 不认，按原值返回 */ }
        return null;
    }

    public event Action? Changed;

    private MediaAssetsSettings Load()
    {
        try
        {
            var path = File.Exists(FilePath) ? FilePath : (File.Exists(LegacyPath) ? LegacyPath : null);
            if (path != null)
            {
                var s = JsonSerializer.Deserialize<MediaAssetsSettings>(File.ReadAllText(path));
                if (s != null) return s;
            }
        }
        catch { /* 配置损坏时回落到默认值，不阻断启动 */ }
        return new MediaAssetsSettings();
    }

    public void Save()
    {
        try
        {
            // 原子写（目录由 AtomicJsonFile 负责创建）
            AtomicJsonFile.Write(FilePath, JsonSerializer.Serialize(Settings, JsonOpts));
        }
        catch { /* 保存失败不打断 UI；状态栏会给反馈 */ }
        Changed?.Invoke();
    }
}
