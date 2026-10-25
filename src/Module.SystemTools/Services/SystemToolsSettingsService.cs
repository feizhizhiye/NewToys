using System;
using System.Globalization;
using System.IO;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.SystemTools.Services;

/// <summary>
/// <see cref="ISystemToolsSettingsService"/> 实现。
/// <para>🔴 与 ScriptManager 的设置服务同口径：<b>默认值不落盘</b>、读不到就用默认值、写失败由调用方转成状态栏红字。</para>
/// </summary>
public sealed class SystemToolsSettingsService : ISystemToolsSettingsService
{
    private const string KeyTimeout = "commandTimeoutSeconds";
    private const int DefaultTimeout = 30;
    private const int MinTimeout = 5;
    private const int MaxTimeout = 300;

    private readonly IOptionsStore _store;
    private readonly ConfigStoreService _cfg;

    public SystemToolsSettingsService(IOptionsStore store, ConfigStoreService cfg)
    {
        _store = store;
        _cfg = cfg;
    }

    public string Section => "system";

    public string FilePath => Path.Combine(_cfg.ConfigDir, "system.json");

    public int CommandTimeoutSeconds
    {
        get
        {
            var raw = _store.Get(Section, KeyTimeout);
            if (string.IsNullOrWhiteSpace(raw)) return DefaultTimeout;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return DefaultTimeout;
            return Math.Clamp(v, MinTimeout, MaxTimeout);
        }
        set => _store.Set(Section, KeyTimeout, Math.Clamp(value, MinTimeout, MaxTimeout).ToString(CultureInfo.InvariantCulture));
    }
}
