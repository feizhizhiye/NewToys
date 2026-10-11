using System;
using System.IO;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class AppConfigService : IAppConfigService
{
    private class ConfigData
    {
        // 主题（深色/浅色/强调色）不在这里 —— 统一由 ThemeService 落到 theme.json，
        // 避免出现两份"当前是不是深色"的真相（v1.18 移除历史字段 IsDarkTheme）。
        // 注：v1.34 曾移除"视频库默认目录"；v3.15 重新引入为**全局导入/导出默认目录**（VideoDbFolder），
        // 仅用于导入/导出对话框定位，不影响"每次手动选目录"的扫描逻辑。
        public string FfmpegPath { get; set; } = "ffmpeg";
        public string PlayerPath { get; set; } = "";
        public int MaxConcurrency { get; set; } = 2;
        public bool GpuAcceleration { get; set; } = false;
        public bool EncryptedDnsEnabled { get; set; } = false;
        public string EncryptedDnsProviderId { get; set; } = "dnspod";
        public string EncryptedDnsCustomUrl { get; set; } = "";
        // 运行环境（脚本解释器 + 脚本目录，v1.33）：留空 = 用系统默认
        public string PythonPath { get; set; } = "";
        public string PwshPath { get; set; } = "";
        public string PowerShell51Path { get; set; } = "";
        public string ScriptFolder { get; set; } = "";
        // 更新检查：留空 = 不检查（「设置 → 通用」如实标注）
        public string UpdateUrl { get; set; } = "";
        // v3.15：全局导入/导出默认目录（字段名 VideoDbFolder 仅为向后兼容，语义已是全局）
        public string VideoDbFolder { get; set; } = "";
        // 注：浏览器 1/2 的程序路径与参数**不在这里**，见 IAppConfigService 的注释（v1.19 收敛到 launch.json）。
        // 旧版本写过 Browser1Path / Browser1Args / Browser2Path / Browser2Args 四个字段，
        // 反序列化时会被忽略、下次保存即从 app.json 里消失，无需迁移。
    }

    private ConfigData _data = new();
    private readonly ConfigStoreService _cfg;

    public AppConfigService(ConfigStoreService cfg) { _cfg = cfg; Load(); }

    private string ConfigFile => Path.Combine(_cfg.ConfigDir, "app.json");

    public string ConfigFilePath => ConfigFile;
    public string DataDirectory => _cfg.DataDir;

    public string FfmpegPath { get => _data.FfmpegPath; set { _data.FfmpegPath = value; Save(); } }
    public string PlayerPath { get => _data.PlayerPath; set { _data.PlayerPath = value; Save(); } }
    public int MaxConcurrency { get => _data.MaxConcurrency; set { _data.MaxConcurrency = value; Save(); } }
    public bool GpuAcceleration { get => _data.GpuAcceleration; set { _data.GpuAcceleration = value; Save(); } }
    public bool EncryptedDnsEnabled { get => _data.EncryptedDnsEnabled; set { _data.EncryptedDnsEnabled = value; Save(); } }
    public string EncryptedDnsProviderId { get => _data.EncryptedDnsProviderId; set { _data.EncryptedDnsProviderId = value; Save(); } }
    public string EncryptedDnsCustomUrl { get => _data.EncryptedDnsCustomUrl; set { _data.EncryptedDnsCustomUrl = value; Save(); } }

    // ==================== 运行环境（v1.33） ====================

    public string PythonPath { get => _data.PythonPath; set { _data.PythonPath = value; Save(); } }
    public string PwshPath { get => _data.PwshPath; set { _data.PwshPath = value; Save(); } }
    public string PowerShell51Path { get => _data.PowerShell51Path; set { _data.PowerShell51Path = value; Save(); } }
    public string ScriptFolder { get => _data.ScriptFolder; set { _data.ScriptFolder = value; Save(); } }
    public string UpdateUrl { get => _data.UpdateUrl; set { _data.UpdateUrl = value; Save(); } }
    public string VideoDbFolder { get => _data.VideoDbFolder; set { _data.VideoDbFolder = value; Save(); } }

    /// <summary>
    /// 脚本目录的默认值：<c>&lt;应用根&gt;\scripts</c>。
    /// <para>⚠️ 用**应用根**而不是配置根：脚本是用户自己的文件，v3.5 起多配置**共享同一份脚本目录**
    /// （「配置」只隔离 config / data / logs）。</para>
    /// </summary>
    public string DefaultScriptFolder => Path.Combine(_cfg.AppRootDirectory, "scripts");

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                var d = JsonSerializer.Deserialize<ConfigData>(json);
                if (d != null) _data = d;
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}