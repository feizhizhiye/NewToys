using System;
using System.IO;
using System.Text.Json;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IPackageSettingsService"/> 实现：落到 <c>&lt;应用根&gt;\config\package.json</c>。
/// <para>
/// 落点刻意选在 <c>config\</c>：这样「设置 → 数据管理」的备份 / 导入（<see cref="ConfigStoreService"/>）
/// 天然覆盖它 —— 这就是「设置里的数据可以备份」的落地方式；模块（PC 工具箱 → 软件包管理）
/// 只通过本服务**读**设置，不自己另存一份。
/// </para>
/// <para>
/// 兼容迁移：若 <c>package.json</c> 尚不存在但旧版 <c>winget.json</c> 存在，会先读旧文件
/// （升级前用户的源设置不丢），之后 <see cref="Save"/> 一律写新文件名。
/// </para>
/// </summary>
public class PackageSettingsService : IPackageSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ConfigStoreService _cfg;

    public PackageSettingsService(ConfigStoreService cfg)
    {
        _cfg = cfg;
        Settings = Load();
    }

    public PackageSettings Settings { get; }

    public string FilePath => Path.Combine(_cfg.ConfigDir, "package.json");

    public event Action? Changed;

    private PackageSettings Load()
    {
        // 优先新文件名；旧版 winget.json 兼容读取（仅当新文件不存在时，避免升级丢设置）
        var path = File.Exists(FilePath) ? FilePath : null;
        if (path == null)
        {
            var legacy = Path.Combine(_cfg.ConfigDir, "winget.json");
            if (File.Exists(legacy)) path = legacy;
        }
        if (path != null)
        {
            try
            {
                var s = JsonSerializer.Deserialize<PackageSettings>(File.ReadAllText(path));
                if (s != null) return s;
            }
            catch { /* 配置损坏时回落到默认值，不阻断启动 */ }
        }
        return new PackageSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, JsonOpts));
        }
        catch { /* 保存失败不打断 UI；界面的状态行会显示上一次成功时间 */ }
        Changed?.Invoke();
    }
}
