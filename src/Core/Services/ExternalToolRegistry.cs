using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IExternalToolRegistry"/> 实现（v3.65 / NT-062，需求 §4.9.2 第 5 条）。
/// <para>
/// 判据：用户**显式配了**值 → 只认那个文件（<see cref="File.Exists(string)"/>，不解析相对名）；
/// **留空** → 走"系统默认"：先探常见安装位，再按 <c>PATH</c> 找可执行名。
/// 播放器 / Windows PowerShell 5.1 的"系统默认"= 系统关联程序 / 系统自带 ⇒ 视为总是可用。
/// </para>
/// <para>
/// ⛔ 不起进程跑 <c>--version</c>（规范：探测只判"文件存在 + 可执行"，避免拖启动 / 弹窗）。
/// 结果**缓存一次**；<see cref="IAppConfigService"/> 的值改了要重启或重新进「运行环境」节才复探（够用且便宜）。
/// </para>
/// </summary>
public class ExternalToolRegistry : IExternalToolRegistry
{
    /// <summary>一个外部程序的静态清单项（键 / 显示名 / 被谁用 / 是否硬依赖 / 留空时的候选可执行名）。</summary>
    private sealed record Spec(string Key, string DisplayName, string UsedBy, bool IsRequired,
                              string[] Candidates, string? KnownInstallPath = null);

    /// <summary>🔴 清单**唯一定义处**（需求 §4.9.2）：新增外部程序只改这里 + <c>PathViewModel.BuildPathRows</c>。</summary>
    private static readonly Spec[] Specs =
    {
        new("FfmpegPath", "ffmpeg", "媒体资产抽帧 / 视频工具媒体信息", true,
            new[] { "ffmpeg" }),
        new("PlayerPath", "外部播放器", "视频列表「▶ 播放」", false,
            Array.Empty<string>()),   // 留空 = 系统关联程序，恒可用
        new("PythonPath", "Python 解释器", "「设置与脚本」的 .py", false,
            new[] { "python", "python3" }),
        new("PwshPath", "PowerShell 7+（pwsh）", "「设置与脚本」的 .ps1", false,
            new[] { "pwsh" }, KnownPwsh()),
        new("PowerShell51Path", "Windows PowerShell 5.1", "「设置与脚本」的 .ps1（无 pwsh 时）", false,
            new[] { "powershell" }),
    };

    private static string? KnownPwsh()
    {
        try
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                 "PowerShell", "7", "pwsh.exe");
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    private readonly IAppConfigService _cfg;
    private List<ExternalToolStatus>? _cache;

    public ExternalToolRegistry(IAppConfigService cfg) => _cfg = cfg;

    public bool HasProbed => _cache != null;

    public IReadOnlyList<ExternalToolStatus> NeedsAttention
        => _cache?.Where(s => s.NeedsAttention).ToList() ?? (IReadOnlyList<ExternalToolStatus>)Array.Empty<ExternalToolStatus>();

    public IReadOnlyList<ExternalToolStatus> ProbeAll()
    {
        if (_cache != null) return _cache;

        var list = new List<ExternalToolStatus>(Specs.Length);
        foreach (var s in Specs)
        {
            var configured = (Configured(s.Key) ?? "").Trim();
            if (configured.Length > 0)
            {
                var exists = IsExecutable(configured);
                list.Add(new ExternalToolStatus
                {
                    Key = s.Key, DisplayName = s.DisplayName, UsedBy = s.UsedBy, IsRequired = s.IsRequired,
                    ConfiguredButMissing = !exists, EffectivePath = configured, Found = exists
                });
                continue;
            }

            // 留空 = 系统默认
            var hit = ResolveCandidate(s);
            var selfDefault = s.Candidates.Length == 0;   // 播放器 = 系统关联程序 ⇒ 恒可用
            list.Add(new ExternalToolStatus
            {
                Key = s.Key, DisplayName = s.DisplayName, UsedBy = s.UsedBy, IsRequired = s.IsRequired,
                EffectivePath = hit ?? (selfDefault ? "系统关联程序" : $"系统默认（{string.Join(" / ", s.Candidates)}）"),
                Found = selfDefault || hit != null
            });
        }

        _cache = list;
        return _cache;
    }

    /// <summary>读 <c>app.json</c> 里对应字段（⛔ 不缓存，避免"改了值探测结果还是旧的"假象）。</summary>
    private string? Configured(string key) => key switch
    {
        "FfmpegPath" => _cfg.FfmpegPath,
        "PlayerPath" => _cfg.PlayerPath,
        "PythonPath" => _cfg.PythonPath,
        "PwshPath" => _cfg.PwshPath,
        "PowerShell51Path" => _cfg.PowerShell51Path,
        _ => null
    };

    /// <summary>留空时：先探常见安装位，再按 PATH 找可执行名。</summary>
    private static string? ResolveCandidate(Spec s)
    {
        if (s.KnownInstallPath is { Length: > 0 } known && IsExecutable(known)) return known;

        foreach (var name in s.Candidates)
        {
            var hit = FindOnPath(name);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>文件存在且是文件（Windows 上再看扩展名 = .exe / .cmd / .bat / .com，其余平台只看可执行位）。</summary>
    private static bool IsExecutable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (!OperatingSystem.IsWindows()) return true;
            var ext = Path.GetExtension(path);
            return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".com", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>在 <c>PATH</c> 里找一个可执行名（Windows 补 <c>.exe</c>）；找不到返回 null。</summary>
    private static string? FindOnPath(string name)
    {
        try
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", ".com" } : new[] { "" };
            foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var ext in exts)
                {
                    try
                    {
                        var full = Path.Combine(dir.Trim(), name + ext);
                        if (File.Exists(full)) return full;
                    }
                    catch { /* 非法 PATH 项：跳过 */ }
                }
            }
        }
        catch { /* PATH 读不到就当没有 */ }
        return null;
    }
}
