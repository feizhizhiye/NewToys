using System;
using System.IO;
using System.Text.Json;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class LaunchConfigService : ILaunchConfigService
{
    private readonly ConfigStoreService _cfg;
    private LaunchConfig? _config;

    public LaunchConfig Config => _config ??= Load();
    public string FilePath => Path.Combine(_cfg.ConfigDir, "launch.json");

    public LaunchConfigService(ConfigStoreService cfg) { _cfg = cfg; _config = Load(); }

    public LaunchConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var c = JsonSerializer.Deserialize<LaunchConfig>(json);
                if (c != null) return _config = c;
            }
        }
        catch { }
        return _config = new LaunchConfig();
    }

    public (bool Ok, string Message) Save(LaunchConfig config)
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            _config = config;
            return (true, "已保存");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}