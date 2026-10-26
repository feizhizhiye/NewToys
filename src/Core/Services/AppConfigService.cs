using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class AppConfigService : IAppConfigService
{
    private class ConfigData
    {
        // 主题（深色/浅色/强调色）不在这里 —— 统一由 ThemeService 落到 shell.json 的 theme 节，
        // 避免出现两份"当前是不是深色"的真相（v1.18 移除历史字段 IsDarkTheme）。

        // ==================== 迁移遗留（机器级项已下沉 %LOCALAPPDATA%\NewToys\env.json） ====================
        // 🔴 这些字段**只为一次性下沉保留读取**：搬进 env.json 之后立刻清空 + 立即 Save，
        //    配合 [JsonIgnore(WhenWritingDefault)] ⇒ 下一次保存就从 app.json 里消失，⛔ 不会长期挂着两份真相。
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string FfmpegPath { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string PlayerPath { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string PythonPath { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string PwshPath { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string PowerShell51Path { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string UpdateUrl { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool EncryptedDnsEnabled { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string EncryptedDnsProviderId { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string EncryptedDnsCustomUrl { get; set; } = "";
        // 🔴 v3.66：ScriptFolder 已下沉到模块设置。仍然能读（旧模块设置的自愈迁移要拿），
        // 但值为空时不写盘 ⇒ 下一次保存就把键从 app.json 里抹掉。
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string ScriptFolder { get; set; } = "";

        // ==================== 仍归应用级的项 ====================
        public int MaxConcurrency { get; set; } = 2;
        // 原 config\launch.json 并入 app.json（应用级行为，⛔ 不随配置档隔离）
        public LaunchConfig Launch { get; set; } = new();
        // 注：浏览器 1/2 的程序路径与参数**不在这里**，见 IAppConfigService 的注释（v1.19 收敛到 launch.json）。
        // 旧版本写过 Browser1Path / Browser1Args / Browser2Path / Browser2Args 四个字段，
        // 反序列化时会被忽略、下次保存即从 app.json 里消失，无需迁移。
        // 🔴 VideoDbFolder 已删除（写死某个 profile 绝对路径的缺陷）——导入/导出默认目录
        //    一律用 ConfigStoreService.StoreDir 推导（见 IAppConfigService.StoreDirectory）。
    }

    private ConfigData _data = new();
    private readonly ConfigStoreService _cfg;
    private readonly EnvConfigService _env;

    public AppConfigService(ConfigStoreService cfg, EnvConfigService env)
    {
        _cfg = cfg;
        _env = env;
        MigrateLegacyOnce();
        Load();
        MigrateLaunchOnce();
        MigrateMachineFieldsOnce();
        CleanupMovedFieldsOnce();
    }

    /// <summary>
    /// 一次性下沉：<c>app.json</c> 里的**机器级**项（解释器 / ffmpeg / 播放器 / 更新地址 / DNS）
    /// → <c>%LOCALAPPDATA%\NewToys\env.json</c>。
    /// <list type="bullet">
    ///   <item>只填空字段（env.json 已有值 ⇒ 用户数据优先，⛔ 不覆盖）；</item>
    ///   <item>搬完把 <c>app.json</c> 里的旧键清空并立即保存 ⇒ 文件里不再挂第二份真相；</item>
    ///   <item>失败不阻断启动 —— 本次按默认值跑，旧值留在 app.json 里下次重试。</item>
    /// </list>
    /// </summary>
    private void MigrateMachineFieldsOnce()
    {
        try
        {
            var moved = _env.ImportLegacy(
                _data.FfmpegPath, _data.PlayerPath, _data.PythonPath, _data.PwshPath, _data.PowerShell51Path,
                _data.UpdateUrl, _data.EncryptedDnsEnabled, _data.EncryptedDnsProviderId, _data.EncryptedDnsCustomUrl);

            var anyLegacy = (_data.FfmpegPath ?? "").Length > 0 || (_data.PlayerPath ?? "").Length > 0
                            || (_data.PythonPath ?? "").Length > 0 || (_data.PwshPath ?? "").Length > 0
                            || (_data.PowerShell51Path ?? "").Length > 0 || (_data.UpdateUrl ?? "").Length > 0
                            || (_data.EncryptedDnsProviderId ?? "").Length > 0 || (_data.EncryptedDnsCustomUrl ?? "").Length > 0
                            || _data.EncryptedDnsEnabled;
            if (!anyLegacy) return;

            // 清空旧键（[JsonIgnore(WhenWritingDefault)] 保证它们从文件里消失）
            _data.FfmpegPath = "";
            _data.PlayerPath = "";
            _data.PythonPath = "";
            _data.PwshPath = "";
            _data.PowerShell51Path = "";
            _data.UpdateUrl = "";
            _data.EncryptedDnsEnabled = false;
            _data.EncryptedDnsProviderId = "";
            _data.EncryptedDnsCustomUrl = "";
            Save();

            System.Diagnostics.Debug.WriteLine(
                $"[NewToys][迁移] 机器级配置已下沉 env.json（{(moved ? "有值搬入" : "env.json 已有值，跳过")}）：{_env.FilePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][迁移] 机器级配置下沉失败（旧值留在 app.json，下次重试）：" + ex.Message);
        }
    }

    /// <summary>
    /// 一次性收尾（v3.66）：<c>ScriptFolder</c> 已下沉到 <c>config\script-manager.json</c>。
    /// 只要模块 json 里已经有 <c>scriptsFolder</c>，就把 app.json 里的旧值清空 ⇒ 下次保存即从文件里消失
    /// （配合 <c>[JsonIgnore(WhenWritingDefault)]</c>，app.json 不再挂废弃键）。
    /// <para>⛔ 模块 json 还没有这个键时**不动** —— 那是迁移还没发生，清了就真丢路径。</para>
    /// </summary>
    private void CleanupMovedFieldsOnce()
    {
        try
        {
            if ((_data.ScriptFolder ?? "").Length == 0) return;
            // 模块设置已归位 config\modules\（旧位置兜底：搬文件被占用时还能判到）
            var moduleFile = Path.Combine(_cfg.ModulesConfigDir, "script-manager.json");
            if (!File.Exists(moduleFile)) moduleFile = Path.Combine(_cfg.ConfigDir, "script-manager.json");
            if (!File.Exists(moduleFile)) return;
            if (!File.ReadAllText(moduleFile).Contains("\"scriptsFolder\"", StringComparison.Ordinal)) return;

            _data.ScriptFolder = "";
            Save();
        }
        catch { /* 收尾失败无害：旧键留在 app.json 里而已 */ }
    }

    /// <summary>
    /// <c>app.json</c> 全路径（v3.65）：落在**应用根**，与 <c>profiles\</c> / <c>active-profile.txt</c> 同级。
    /// <para>理由：「运行环境」（外部程序路径 / 运行行为）是**应用级路由**，⛔ 不随配置档切换 —— 换配置只隔离
    /// <c>config / data / logs</c>，解释器与 ffmpeg 的路径不该跟着变。</para>
    /// </summary>
    private string ConfigFile => Path.Combine(_cfg.AppRootDirectory, "app.json");

    public string ConfigFilePath => ConfigFile;
    public string StoreDirectory => _cfg.StoreDir;

    /// <summary>v3.65：<c>app.json</c> 由旧位置上提到应用根时留下的说明（null = 没发生迁移）。供设置页 / 排障展示。</summary>
    public string? LegacyMigrationNote { get; private set; }

    /// <summary>
    /// 一次性迁移（v3.65）：把 <c>&lt;配置根&gt;\config\app.json</c> 复制到 <c>&lt;应用根&gt;\app.json</c>。
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

    // ==================== 机器级项：⛔ 不再存在 app.json，读写都走 env.json ====================
    // 唯一写入点仍是「设置 → 路径与配置」页；模块只读。命令行 / 环境变量覆盖只影响读取，不写进文件。

    /// <summary>ffmpeg 可执行文件（覆盖链见 <see cref="EnvConfigService"/>）。</summary>
    public string FfmpegPath { get => _env.FfmpegPath; set => _env.SetFfmpegPath(value); }

    /// <summary>外部播放器路径（空 = 系统默认关联）。</summary>
    public string PlayerPath { get => _env.PlayerPath; set => _env.SetPlayerPath(value); }

    public int MaxConcurrency { get => _data.MaxConcurrency; set { _data.MaxConcurrency = value; Save(); } }

    public bool EncryptedDnsEnabled { get => _env.EncryptedDnsEnabled; set => _env.SetEncryptedDnsEnabled(value); }
    public string EncryptedDnsProviderId { get => _env.EncryptedDnsProviderId; set => _env.SetEncryptedDnsProviderId(value); }
    public string EncryptedDnsCustomUrl { get => _env.EncryptedDnsCustomUrl; set => _env.SetEncryptedDnsCustomUrl(value); }

    // ==================== 运行环境（v1.33；现为机器级 env.json） ====================
    // 三个解释器路径 = 「运行环境」（描述"本机怎么跑外部程序"），故跟着机器、跨配置共享。

    public string PythonPath { get => _env.PythonPath; set => _env.SetPythonPath(value); }
    public string PwshPath { get => _env.PwshPath; set => _env.SetPwshPath(value); }
    public string PowerShell51Path { get => _env.PowerShell51Path; set => _env.SetPowerShell51Path(value); }

    /// <summary>⚠️ 已定为**模块级**（<c>config\modules\script-manager.json</c>）：这里只为一次性下沉迁移保留读取，⛔ 不再有 UI 写入它。</summary>
    public string ScriptFolder { get => _data.ScriptFolder; set { _data.ScriptFolder = value; Save(); } }

    /// <summary>更新检查地址（空 = 不检查）。</summary>
    public string UpdateUrl { get => _env.UpdateUrl; set => _env.SetUpdateUrl(value); }

    /// <summary>
    /// 一键按钮 + 行为开关（v3.66）：原 <c>config\launch.json</c> 的内容，现在住在 <c>app.json</c> 的 <c>Launch</c> 节点。
    /// <para>⚠️ 唯一写入点 =「设置 → 启动与行为」页（经 <see cref="Core.Services.LaunchConfigService"/> 转手）。</para>
    /// </summary>
    public LaunchConfig Launch { get => _data.Launch; set { _data.Launch = value ?? new LaunchConfig(); Save(); } }

    /// <summary>
    /// 一次性迁移（v3.66）：<c>&lt;配置根&gt;\config\launch.json</c> → <c>app.json</c> 的 <c>Launch</c> 节点。
    /// <list type="bullet">
    ///   <item>⛔ 只在 <c>app.json</c> 里**还没有** <c>Launch</c> 时才搬（已有 ⇒ 用户数据优先）；</item>
    ///   <item>搬完**删除旧文件**（内容已并入；整目录备份在 <c>NewToys_backup_*.zip</c>）；</item>
    ///   <item>失败不阻断启动 —— 本次按默认值跑，旧文件留着下次重试。</item>
    /// </list>
    /// </summary>
    private void MigrateLaunchOnce()
    {
        try
        {
            var legacy = Path.Combine(_cfg.ConfigDir, "launch.json");
            if (!File.Exists(legacy)) return;

            var hasLaunch = false;
            if (File.Exists(ConfigFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigFile));
                hasLaunch = doc.RootElement.ValueKind == JsonValueKind.Object
                            && doc.RootElement.TryGetProperty(nameof(LaunchConfig), out _);
            }

            if (!hasLaunch)
            {
                var c = JsonSerializer.Deserialize<LaunchConfig>(File.ReadAllText(legacy));
                if (c != null) { _data.Launch = c; Save(); }
            }

            File.Delete(legacy);
            LegacyMigrationNote = $"launch.json 已并入 app.json（配置「{_cfg.ActiveProfileName}」）：{legacy}";
            System.Diagnostics.Debug.WriteLine("[NewToys][迁移] " + LegacyMigrationNote);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][迁移] launch.json 合并失败（保留旧文件）：" + ex.Message);
        }
    }

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
            // 原子写（目录由 AtomicJsonFile 负责创建）
            AtomicJsonFile.Write(ConfigFile, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}