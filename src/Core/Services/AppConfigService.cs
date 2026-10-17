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
        // v3.65 / NT-062：GpuAcceleration 已删除（没有任何消费方）。旧 app.json 里的该键由
        // System.Text.Json 默认忽略未知属性 ⇒ 反序列化不报错，下次保存即从文件里消失。
        public bool EncryptedDnsEnabled { get; set; } = false;
        public string EncryptedDnsProviderId { get; set; } = "dnspod";
        public string EncryptedDnsCustomUrl { get; set; } = "";
        // 运行环境（v1.33）：三个解释器路径，**应用级**（v3.65 / NT-062 回迁）；留空 = 用系统默认。
        // ⚠️ ScriptFolder 已定为**模块级**（「脚本管理器」设置），这里只为 NT-061 的一次性下沉保留读取。
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

    public AppConfigService(ConfigStoreService cfg) { _cfg = cfg; MigrateLegacyOnce(); Load(); }

    /// <summary>
    /// <c>app.json</c> 全路径（v3.65 / NT-062）：落在**应用根**，与 <c>profiles\</c> / <c>active-profile.txt</c> 同级。
    /// <para>理由：「运行环境」（外部程序路径 / 运行行为）是**应用级路由**，⛔ 不随配置档切换 —— 换配置只隔离
    /// <c>config / data / logs</c>，解释器与 ffmpeg 的路径不该跟着变。</para>
    /// </summary>
    private string ConfigFile => Path.Combine(_cfg.AppRootDirectory, "app.json");

    public string ConfigFilePath => ConfigFile;
    public string DataDirectory => _cfg.DataDir;

    /// <summary>v3.65 / NT-062：<c>app.json</c> 由旧位置上提到应用根时留下的说明（null = 没发生迁移）。供设置页 / 排障展示。</summary>
    public string? LegacyMigrationNote { get; private set; }

    /// <summary>
    /// 一次性迁移（v3.65 / NT-062）：把 <c>&lt;配置根&gt;\config\app.json</c> 复制到 <c>&lt;应用根&gt;\app.json</c>。
    /// <list type="bullet">
    ///   <item>⛔ 只在**新位置还没有**该文件时搬一次（已有 ⇒ 一律不覆盖，用户数据优先）；</item>
    ///   <item>⛔ **复制不移除**老文件（保守，留作回退），老文件不再被读写；</item>
    ///   <item>多配置档：只取**当前活动档**为准（其余档的老文件留在原地不动）；</item>
    ///   <item>失败不阻断启动 —— 本次按新位置默认值跑。</item>
    /// </list>
    /// </summary>
    private void MigrateLegacyOnce()
    {
        try
        {
            if (File.Exists(ConfigFile)) return;
            var legacy = Path.Combine(_cfg.ConfigDir, "app.json");
            if (!File.Exists(legacy)) return;

            File.Copy(legacy, ConfigFile, overwrite: false);
            LegacyMigrationNote = $"app.json 已上提到应用根（取自配置「{_cfg.ActiveProfileName}」）：{legacy} → {ConfigFile}";
            System.Diagnostics.Debug.WriteLine("[NewToys][迁移] " + LegacyMigrationNote);
        }
        catch (Exception ex)
        {
            LegacyMigrationNote = "app.json 上提失败（本次按新位置默认值运行）：" + ex.Message;
            System.Diagnostics.Debug.WriteLine("[NewToys][迁移] " + LegacyMigrationNote);
        }
    }

    public string FfmpegPath { get => _data.FfmpegPath; set { _data.FfmpegPath = value; Save(); } }
    public string PlayerPath { get => _data.PlayerPath; set { _data.PlayerPath = value; Save(); } }
    public int MaxConcurrency { get => _data.MaxConcurrency; set { _data.MaxConcurrency = value; Save(); } }
    public bool EncryptedDnsEnabled { get => _data.EncryptedDnsEnabled; set { _data.EncryptedDnsEnabled = value; Save(); } }
    public string EncryptedDnsProviderId { get => _data.EncryptedDnsProviderId; set { _data.EncryptedDnsProviderId = value; Save(); } }
    public string EncryptedDnsCustomUrl { get => _data.EncryptedDnsCustomUrl; set { _data.EncryptedDnsCustomUrl = value; Save(); } }

    // ==================== 运行环境（v1.33；v3.65 / NT-062 回迁为**应用级**正式读写） ====================
    // 三个解释器路径 = 「运行环境」（描述"本机怎么跑外部程序"），故归应用级、跨配置共享。
    // 唯一写入点 =「设置 → 路径与配置」页的运行环境行；模块只读。

    public string PythonPath { get => _data.PythonPath; set { _data.PythonPath = value; Save(); } }
    public string PwshPath { get => _data.PwshPath; set { _data.PwshPath = value; Save(); } }
    public string PowerShell51Path { get => _data.PowerShell51Path; set { _data.PowerShell51Path = value; Save(); } }

    /// <summary>⚠️ 已定为**模块级**（<c>config\script-manager.json</c>）：这里只为 NT-061 的一次性下沉保留读写，⛔ 不再有 UI 写入它。</summary>
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