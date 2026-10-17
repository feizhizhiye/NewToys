using System;
using System.IO;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <see cref="IScriptManagerSettingsService"/> 的实现（v3.64 / NT-061）：把本模块的选项存进
/// <c>config\script-manager.json</c>（经 Core 的 <see cref="IOptionsStore"/>，KV + 按模块分区）。
/// <para>
/// <b>一次性迁移</b>（NT-061）：脚本目录原来在 <c>app.json</c> 里，首次运行只要 <c>script-manager.json</c>
/// 还没该键就搬过来（⛔ 不丢用户配置）。
/// </para>
/// <para>
/// 🔴 <b>反向迁移</b>（v3.65 / NT-062）：三个解释器路径在 NT-061 曾下沉到本模块，NT-062 判定它们属
/// 「运行环境」= 应用级 ⇒ **回迁 <c>app.json</c>**。首次运行把 <c>script-manager.json</c> 里的旧值搬回去
/// （⛔ 只在 <c>app.json</c> 对应字段为空时搬，用户在新页改过的一律不覆盖）；本文件里遗留的三个键
/// <b>不再被读写</b>，下次写盘时会自然保留（无害）。
/// </para>
/// </summary>
public class ScriptManagerSettingsService : IScriptManagerSettingsService
{
    /// <summary>模块分区名 = 配置文件名基名（<c>config\script-manager.json</c>）。</summary>
    public const string Module = "script-manager";

    // 键名：camelCase，与 KV 里其它模块同一个口径（换 SQLite 时就是 key 列）。
    private const string KeyScriptsFolder = "scriptsFolder";
    // ⚠️ 下面三个 key ⛔ 已不是本模块设置项（v3.65 / NT-062 回迁应用级），仅用于反向迁移时**读旧值**。
    private const string KeyPwshPath = "pwshPath";
    private const string KeyPythonPath = "pythonPath";
    private const string KeyPowerShell51Path = "powershell51Path";

    private readonly IOptionsStore _store;
    private readonly ConfigStoreService _cfg;

    public ScriptManagerSettingsService(IOptionsStore store, ConfigStoreService cfg, IAppConfigService? legacy = null)
    {
        _store = store;
        _cfg = cfg;
        MigrateLegacyOnce(legacy);
    }

    public string ModuleKey => Module;

    public string FilePath => Path.Combine(_cfg.ConfigDir, Module + ".json");

    public string ScriptsFolder => Read(KeyScriptsFolder);

    /// <summary>默认脚本目录：<c>&lt;应用根&gt;\scripts</c> —— 用**应用根**而非配置根，脚本是多配置共享的用户文件。</summary>
    public string DefaultScriptsFolder => Path.Combine(_cfg.AppRootDirectory, "scripts");

    public string EffectiveScriptsFolder
    {
        get
        {
            var custom = ScriptsFolder;
            return custom.Length > 0 ? custom : DefaultScriptsFolder;
        }
    }

    public event Action? Changed;

    // ==================== 写（返回状态文案；失败 ⇒ ⚠ 开头 + 不写盘） ====================

    public string SetScriptsFolder(string value)
        => Write(KeyScriptsFolder, value, "脚本目录", PathKind.Folder, "用默认目录");

    public string ResetScriptsFolder() => SetScriptsFolder("");

    // ==================== 内部 ====================

    private enum PathKind { Folder, File }

    private string Read(string key)
    {
        try { return (_store.Get(Module, key) ?? "").Trim(); }
        catch { return ""; }
    }

    /// <summary>
    /// 收口写入：空值 = 恢复默认（允许）；非空 ⇒ **必须先存在**，否则返回 <c>⚠ …</c> 且**不写盘**
    /// （需求 §3.5：路径无效 / 无权限 ⇒ 红字警示 + 保持上一个有效值）。
    /// </summary>
    private string Write(string key, string? value, string label, PathKind kind, string emptyHint)
    {
        var v = (value ?? "").Trim();

        if (v.Length > 0)
        {
            var bad = kind == PathKind.Folder
                ? (Directory.Exists(v) ? null : $"目录不存在：{v}")
                : (File.Exists(v) ? null : $"文件不存在：{v}");
            if (bad != null) return $"⚠ {bad} —— {label}未写盘，仍用上一个有效值。";
        }

        try
        {
            _store.Set(Module, key, v);
        }
        catch (Exception ex)
        {
            return $"⚠ {label}保存失败：{ex.Message} —— 未写盘，仍用上一个有效值。";
        }

        Changed?.Invoke();
        return v.Length == 0
            ? $"{label}已恢复默认（{emptyHint}）· 写入 {Module}.json，立即生效。"
            : $"{label}已保存：{v} · 写入 {Module}.json，立即生效。";
    }

    /// <summary>
    /// 双向一次性迁移（构造函数里跑一次，失败不阻断启动）：
    /// <list type="number">
    ///   <item>NT-061 **正向**：<c>app.json</c> 的脚本目录 → <c>script-manager.json</c>（脚本目录仍归本模块）；</item>
    ///   <item>NT-062 **反向**：<c>script-manager.json</c> 的三个解释器路径 → <c>app.json</c>（回迁应用级）。</item>
    /// </list>
    /// </summary>
    private void MigrateLegacyOnce(IAppConfigService? legacy)
    {
        if (legacy == null) return;
        Migrate(KeyScriptsFolder, legacy.ScriptFolder);
        MigrateBackToApp(legacy);
    }

    /// <summary>反向迁移（v3.65 / NT-062）：本模块遗留的三个解释器路径 → <c>app.json</c>，⛔ 只在应用级该字段为空时写（不覆盖用户新改的值）。</summary>
    private void MigrateBackToApp(IAppConfigService legacy)
    {
        try
        {
            if ((legacy.PwshPath ?? "").Trim().Length == 0)
            {
                var v = Read(KeyPwshPath);
                if (v.Length > 0) legacy.PwshPath = v;
            }
            if ((legacy.PythonPath ?? "").Trim().Length == 0)
            {
                var v = Read(KeyPythonPath);
                if (v.Length > 0) legacy.PythonPath = v;
            }
            if ((legacy.PowerShell51Path ?? "").Trim().Length == 0)
            {
                var v = Read(KeyPowerShell51Path);
                if (v.Length > 0) legacy.PowerShell51Path = v;
            }
        }
        catch { /* 迁移失败不阻断启动：本次按"没配"跑，用户可在设置页重填 */ }
    }

    private void Migrate(string key, string? legacyValue)
    {
        try
        {
            if (_store.Get(Module, key) != null) return;
            _store.Set(Module, key, (legacyValue ?? "").Trim());
        }
        catch { /* 迁移失败不阻断启动：本次按"没配"跑，用户可在设置页重填 */ }
    }
}
