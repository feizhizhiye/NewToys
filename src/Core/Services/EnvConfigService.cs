using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewToys.Core.Services;

/// <summary>
/// <b>L0 机器环境配置</b>：<c>%LOCALAPPDATA%\NewToys\env.json</c> 的**唯一读写入口**。
/// <para>
/// 语义：这里是「本机怎么跑外部程序」，**跟随机器、⛔ 不随配置档切换**
/// （换配置只隔离 <c>config / data / logs</c>，ffmpeg / 解释器路径不该跟着变）。
/// ⛔ 这些键不放进 <c>app.json</c>：那是配置档内参数，混在一起会被切配置带走。
/// </para>
/// <para>
/// 🔴 覆盖链（读）：命令行 <c>--python</c> / <c>--pwsh</c> / <c>--ffmpeg</c> ＞ 环境变量
/// <c>NEWTOYS_PYTHON</c> / <c>NEWTOYS_PWSH</c> / <c>NEWTOYS_FFMPEG</c> / <c>NEWTOYS_PLAYER</c> ＞
/// <c>env.json</c> ＞ 内置默认值（<c>ffmpeg</c> 走 PATH）。
/// <b>写</b>只写 <c>env.json</c>（覆盖值不进文件 —— 否则"临时覆盖"会变成永久设置）。
/// </para>
/// <para>
/// ⚠️ 只有 Core 读写本文件；模块一律经 <c>IAppConfigService</c>，⛔ 模块不该知道文件名。
/// </para>
/// </summary>
public sealed class EnvConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>env.json 的内容（键名用小写短名，手改友好）。</summary>
    private sealed class EnvData
    {
        public string Ffmpeg { get; set; } = "";
        public string Player { get; set; } = "";
        public string Python { get; set; } = "";
        public string Pwsh { get; set; } = "";
        public string Pwsh51 { get; set; } = "";
        public string SevenZip { get; set; } = "";
        public string UpdateUrl { get; set; } = "";
        public bool DnsEnabled { get; set; }
        public string DnsProviderId { get; set; } = "";
        public string DnsCustomUrl { get; set; } = "";
    }

    private readonly object _gate = new();
    private EnvData _data = new();

    public EnvConfigService()
    {
        FilePath = ComputePath();
        _override = ReadCommandLineOverrides();
        Load();
    }

    /// <summary><c>%LOCALAPPDATA%\NewToys\env.json</c>。</summary>
    public string FilePath { get; }

    /// <summary>是否读到过现有文件（false = 首次运行 / 读失败，界面可提示）。</summary>
    public bool Exists { get; private set; }

    private static string ComputePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(AppContext.BaseDirectory, "machine");
        return Path.Combine(root, "NewToys", "env.json");
    }

    // ==================== 读（带覆盖链） ====================

    /// <summary>ffmpeg 可执行文件（默认 <c>ffmpeg</c> = 走 PATH）。</summary>
    public string FfmpegPath => Resolve(_override.Ffmpeg, "NEWTOYS_FFMPEG", _data.Ffmpeg, "ffmpeg");

    /// <summary>外部播放器路径（空 = 用系统默认关联）。</summary>
    public string PlayerPath => Resolve(_override.Player, "NEWTOYS_PLAYER", _data.Player, "");

    /// <summary>Python 解释器路径（空 = 用 PATH 里的 python）。</summary>
    public string PythonPath => Resolve(_override.Python, "NEWTOYS_PYTHON", _data.Python, "");

    /// <summary>PowerShell 7 路径（空 = 用 PATH 里的 pwsh）。</summary>
    public string PwshPath => Resolve(_override.Pwsh, "NEWTOYS_PWSH", _data.Pwsh, "");

    /// <summary>Windows PowerShell 5.1 路径（空 = 用系统内置）。</summary>
    public string PowerShell51Path => Resolve(_override.Pwsh51, "NEWTOYS_PWSH51", _data.Pwsh51, "");

    /// <summary>7z.exe 路径（空 = 自动探测常见安装位 → PATH）。</summary>
    public string SevenZipPath => Resolve(_override.SevenZip, "NEWTOYS_7Z", _data.SevenZip, "");

    /// <summary>更新检查地址（空 = 不检查）。</summary>
    public string UpdateUrl => Resolve(_override.UpdateUrl, "NEWTOYS_UPDATE_URL", _data.UpdateUrl, "");

    /// <summary>是否启用加密 DNS（机器级偏好：跟着网络环境走）。</summary>
    public bool EncryptedDnsEnabled => _data.DnsEnabled;

    /// <summary>加密 DNS 服务商标识。</summary>
    public string EncryptedDnsProviderId => string.IsNullOrWhiteSpace(_data.DnsProviderId) ? "dnspod" : _data.DnsProviderId;

    /// <summary>自定义加密 DNS 地址。</summary>
    public string EncryptedDnsCustomUrl => _data.DnsCustomUrl;

    private static string Resolve(string? cli, string envName, string? file, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(cli)) return cli.Trim();
        var env = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        if (!string.IsNullOrWhiteSpace(file)) return file.Trim();
        return fallback;
    }

    // ==================== 写（只写文件） ====================

    public void SetFfmpegPath(string? value) => Set(d => d.Ffmpeg = value ?? "");
    public void SetPlayerPath(string? value) => Set(d => d.Player = value ?? "");
    public void SetPythonPath(string? value) => Set(d => d.Python = value ?? "");
    public void SetPwshPath(string? value) => Set(d => d.Pwsh = value ?? "");
    public void SetPowerShell51Path(string? value) => Set(d => d.Pwsh51 = value ?? "");
    public void SetSevenZipPath(string? value) => Set(d => d.SevenZip = value ?? "");
    public void SetUpdateUrl(string? value) => Set(d => d.UpdateUrl = value ?? "");

    public void SetEncryptedDnsEnabled(bool value) => Set(d => d.DnsEnabled = value);
    public void SetEncryptedDnsProviderId(string? value) => Set(d => d.DnsProviderId = value ?? "");
    public void SetEncryptedDnsCustomUrl(string? value) => Set(d => d.DnsCustomUrl = value ?? "");

    /// <summary>
    /// 写入一份**原样搬运**的旧值（<c>app.json</c> 一次性下沉用）：只填空字段，⛔ 不覆盖已有值。
    /// </summary>
    /// <returns>是否有字段被填入（调用方据此决定是否回写 app.json）。</returns>
    public bool ImportLegacy(string? ffmpeg, string? player, string? python, string? pwsh, string? pwsh51,
                             string? updateUrl, bool dnsEnabled, string? dnsProviderId, string? dnsCustomUrl)
    {
        var changed = false;
        lock (_gate)
        {
            Fill(_data.Ffmpeg, ffmpeg, v => _data.Ffmpeg = v, ref changed);
            Fill(_data.Player, player, v => _data.Player = v, ref changed);
            Fill(_data.Python, python, v => _data.Python = v, ref changed);
            Fill(_data.Pwsh, pwsh, v => _data.Pwsh = v, ref changed);
            Fill(_data.Pwsh51, pwsh51, v => _data.Pwsh51 = v, ref changed);
            Fill(_data.UpdateUrl, updateUrl, v => _data.UpdateUrl = v, ref changed);
            Fill(_data.DnsProviderId, dnsProviderId, v => _data.DnsProviderId = v, ref changed);
            Fill(_data.DnsCustomUrl, dnsCustomUrl, v => _data.DnsCustomUrl = v, ref changed);
            if (dnsEnabled && !_data.DnsEnabled) { _data.DnsEnabled = true; changed = true; }
        }
        if (changed) Save();
        return changed;
    }

    private static bool Fill(string current, string? legacy, Action<string> set, ref bool changed)
    {
        if (!string.IsNullOrWhiteSpace(current)) return false;   // 已有值 ⇒ ⛔ 不覆盖
        if (string.IsNullOrWhiteSpace(legacy)) return false;
        set(legacy!.Trim());
        changed = true;
        return true;
    }

    private void Set(Action<EnvData> apply)
    {
        lock (_gate)
        {
            apply(_data);
        }
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<EnvData>(File.ReadAllText(FilePath), JsonOpts);
            if (d != null) { _data = d; Exists = true; }
        }
        catch (Exception ex)
        {
            // 坏文件：先归档再按默认值跑（⛔ 不静默丢内容）
            try { File.Move(FilePath, $"{FilePath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true); } catch { }
            System.Diagnostics.Debug.WriteLine("[NewToys][env] env.json 解析失败，已归档：" + ex.Message);
        }
    }

    private void Save()
    {
        try
        {
            AtomicJsonFile.Write(FilePath, JsonSerializer.Serialize(_data, JsonOpts));
            Exists = File.Exists(FilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][env] env.json 写入失败：" + ex.Message);
        }
    }

    // ==================== 命令行覆盖 ====================

    private readonly Overrides _override;

    private sealed class Overrides
    {
        public string Ffmpeg = "";
        public string Player = "";
        public string Python = "";
        public string Pwsh = "";
        public string Pwsh51 = "";
        public string SevenZip = "";
        public string UpdateUrl = "";
    }

    /// <summary>
    /// 解析 <c>--python &lt;路径&gt;</c> / <c>--python=&lt;路径&gt;</c> 这类开关（大小写不敏感）。
    /// 解析失败 / 没给值 ⇒ 当没传（⛔ 不抛：启动参数不该让程序起不来）。
    /// </summary>
    private static Overrides ReadCommandLineOverrides()
    {
        var o = new Overrides();
        try
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                var raw = args[i];
                if (!raw.StartsWith("--", StringComparison.Ordinal)) continue;

                var name = raw[2..];
                string? value = null;
                var eq = name.IndexOf('=');
                if (eq >= 0) { value = name[(eq + 1)..]; name = name[..eq]; }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    i++;
                }

                value = (value ?? "").Trim();
                switch (name.ToLowerInvariant())
                {
                    case "ffmpeg": o.Ffmpeg = value; break;
                    case "player": o.Player = value; break;
                    case "python": o.Python = value; break;
                    case "pwsh": o.Pwsh = value; break;
                    case "pwsh51": o.Pwsh51 = value; break;
                    case "7z": o.SevenZip = value; break;
                    case "update-url": o.UpdateUrl = value; break;
                }
            }
        }
        catch { /* 参数畸形不影响启动 */ }
        return o;
    }

    /// <summary>当前生效的覆盖来源（给「设置 → 路径」页标注「命令行 / 环境变量覆盖中」）。</summary>
    public string OverrideNote
    {
        get
        {
            var list = new List<string>();
            if (_override.Ffmpeg.Length > 0 || Env("NEWTOYS_FFMPEG").Length > 0) list.Add("ffmpeg");
            if (_override.Player.Length > 0 || Env("NEWTOYS_PLAYER").Length > 0) list.Add("播放器");
            if (_override.Python.Length > 0 || Env("NEWTOYS_PYTHON").Length > 0) list.Add("Python");
            if (_override.Pwsh.Length > 0 || Env("NEWTOYS_PWSH").Length > 0) list.Add("PowerShell");
            if (_override.Pwsh51.Length > 0 || Env("NEWTOYS_PWSH51").Length > 0) list.Add("PowerShell 5.1");
            if (_override.SevenZip.Length > 0 || Env("NEWTOYS_7Z").Length > 0) list.Add("7-Zip");
            if (_override.UpdateUrl.Length > 0 || Env("NEWTOYS_UPDATE_URL").Length > 0) list.Add("更新地址");
            return list.Count == 0 ? "" : string.Join(" / ", list);
        }
    }

    private static string Env(string name) => (Environment.GetEnvironmentVariable(name) ?? "").Trim();
}
