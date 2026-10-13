using System;
using System.IO;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 媒体资产设置的读写（NT-021）：落 <c>&lt;应用根&gt;\config\media-assets.json</c>。
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

    public string FilePath => Path.Combine(_cfg.ConfigDir, "media-assets.json");

    /// <summary>缩略图目录（生效值）：配置为空时用 <c>&lt;应用根&gt;\data\thumbs</c>。</summary>
    public string ThumbsDirectory
    {
        get
        {
            var custom = (Settings.ThumbsDirectory ?? "").Trim();
            return custom.Length > 0 ? custom : Path.Combine(_cfg.DataDir, "thumbs");
        }
    }

    public event Action? Changed;

    private MediaAssetsSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<MediaAssetsSettings>(File.ReadAllText(FilePath));
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
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, JsonOpts));
        }
        catch { /* 保存失败不打断 UI；状态栏会给反馈 */ }
        Changed?.Invoke();
    }
}
