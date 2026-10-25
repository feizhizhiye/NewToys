using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NewToys.SystemTools.Models;
using NewToys.Shared.Services;

namespace NewToys.SystemTools.Services;

/// <summary>
/// <see cref="IPackageManagerService"/> 的 **apt/dpkg 后端**（结构验证适配器）。
/// <para>
/// 目的：证明「换后端 = 加一个适配器 + 命令模板，**页面 / VM 零改动**」——
/// 页面只认 <see cref="IPackageManagerService"/>，Shell 按平台二选一注册（见 <c>ScriptManagerModule</c>）。
/// </para>
/// <para>
/// 范围（刻意克制，YAGNI）：只读清单（dpkg-query 机读 TSV + <c>apt list --upgradable</c> +
/// sources.list 直读 + <c>apt-mark showhold</c>）。**写操作未实装** —— 按既定结论
/// （写操作⛔ 不脚本化：提权 sudo vs UAC、交互确认、失败原因都会退化），留给命令库条目。
/// </para>
/// </summary>
public sealed class AptPackageService : IPackageManagerService
{
    private readonly IProcessRunner _runner;

    public AptPackageService(IProcessRunner runner) => _runner = runner;

    public bool IsSupported => OperatingSystem.IsLinux();

    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(120);

    public async Task<(bool Ok, string Message)> ProbeAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, "apt 后端仅支持 Linux；当前平台不可用。");
        var r = await _runner.RunAsync(new ProcessRunRequest
        {
            FileName = "dpkg-query",
            ArgumentList = new[] { "--version" },
            StrictOutput = false
        }, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        if (!r.Started) return (false, r.Error ?? "未找到 dpkg-query");
        var first = (r.StdOut + r.StdErr).Split('\n').FirstOrDefault(x => x.Trim().Length > 0)?.Trim() ?? "";
        return r.ExitCode == 0 && first.Length > 0 ? (true, first) : (false, "未检测到 dpkg-query");
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageSourceInfo> Items)> ListSourcesAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, "apt 后端仅支持 Linux。", Array.Empty<PackageSourceInfo>());

        var list = new List<PackageSourceInfo>();
        foreach (var file in SourceFiles())
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { continue; }   // 权限 / 不存在：跳过这个文件

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[0] is not ("deb" or "deb-src")) continue;
                var uri = parts[1];
                list.Add(new PackageSourceInfo { Name = uri, Argument = uri, Type = parts[0] });
            }
        }
        return (true, $"共 {list.Count} 个源", list);
    }

    /// <summary>sources.list + sources.list.d/*.list（*.sources 的 deb822 格式⛔ 不解析，量级不配）。</summary>
    private static IEnumerable<string> SourceFiles()
    {
        const string main = "/etc/apt/sources.list";
        if (File.Exists(main)) yield return main;
        var dir = "/etc/apt/sources.list.d";
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, "*.list").OrderBy(x => x, StringComparer.Ordinal))
            yield return f;
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListInstalledAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, "apt 后端仅支持 Linux。", Array.Empty<PackageInfo>());

        // 机读输出：dpkg-query 自己吐 TSV（⛔ 不解析人读表格，见 架构结论 1）
        var r = await _runner.RunAsync(new ProcessRunRequest
        {
            FileName = "dpkg-query",
            ArgumentList = new[] { "-W", "-f", "${Package}\t${Version}\t${Maintainer}\n" },
            StrictOutput = false
        }, ListTimeout, ct).ConfigureAwait(false);

        if (!r.Started) return (false, r.Error ?? "无法启动 dpkg-query", Array.Empty<PackageInfo>());
        if (r.TimedOut || r.Canceled) return (false, r.Error ?? "dpkg-query 执行被中断", Array.Empty<PackageInfo>());

        var items = TsvRows.ParseTsv(r.StdOut, hasHeader: false, new[] { "Name", "Version", "Maintainer" })
            .Select(row => new PackageInfo
            {
                Name = row.GetValueOrDefault("Name", ""),
                Id = row.GetValueOrDefault("Name", ""),
                Version = row.GetValueOrDefault("Version", ""),
                Source = "dpkg",
                IsInstalled = true
            })
            .Where(p => p.Id.Length > 0)
            .ToList();

        return (true, $"已安装 {items.Count} 个包", items);
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListUpgradesAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, "apt 后端仅支持 Linux。", Array.Empty<PackageInfo>());

        var r = await _runner.RunAsync(new ProcessRunRequest
        {
            FileName = "apt",
            ArgumentList = new[] { "list", "--upgradable" },
            StrictOutput = false
        }, ListTimeout, ct).ConfigureAwait(false);

        if (!r.Started) return (false, r.Error ?? "无法启动 apt", Array.Empty<PackageInfo>());
        if (r.TimedOut || r.Canceled) return (false, r.Error ?? "apt 执行被中断", Array.Empty<PackageInfo>());

        // 行形态：<名称>/<仓库> <新版本> <架构> [upgradable from: <旧版本>]
        var items = new List<PackageInfo>();
        foreach (var raw in r.StdOut.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("Listing", StringComparison.OrdinalIgnoreCase)) continue;
            var m = Regex.Match(line, @"^([A-Za-z0-9][^\s/]+)/(\S+)\s+(\S+)\s+(\S+)(?:\s+\[upgradable from:\s*(\S+)\])?");
            if (!m.Success) continue;
            items.Add(new PackageInfo
            {
                Name = m.Groups[1].Value,
                Id = m.Groups[1].Value,
                Version = m.Groups[3].Value,
                Available = m.Groups[3].Value,
                Source = m.Groups[2].Value,
                IsInstalled = false
            });
        }
        return (true, items.Count == 0 ? "没有可升级的包" : $"可升级 {items.Count} 个包", items);
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListPinnedAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, "apt 后端仅支持 Linux。", Array.Empty<PackageInfo>());

        var r = await _runner.RunAsync(new ProcessRunRequest
        {
            FileName = "apt-mark",
            ArgumentList = new[] { "showhold" },
            StrictOutput = false
        }, ListTimeout, ct).ConfigureAwait(false);

        if (!r.Started) return (false, r.Error ?? "无法启动 apt-mark", Array.Empty<PackageInfo>());

        var items = r.StdOut.Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Select(id => new PackageInfo { Id = id, Name = id, IsPinned = true })
            .ToList();
        return (true, items.Count == 0 ? "没有已 Pin 的包" : $"已 Pin {items.Count} 个包", items);
    }

    // ==================== 未实装（结构验证适配器只做只读） ====================

    public Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> SearchAsync(string query, string? source, CancellationToken ct = default)
        => Task.FromResult<(bool, string, IReadOnlyList<PackageInfo>)>((false, "apt 后端未实装搜索（适配器只做只读清单）。", Array.Empty<PackageInfo>()));

    public Task<(bool Ok, string Message, PackageInfo? Detail)> ShowAsync(string id, string? source = null, CancellationToken ct = default)
        => Task.FromResult<(bool, string, PackageInfo?)>((false, "apt 后端未实装详情（适配器只做只读清单）。", null));

    /// <summary>写操作统一口径：需提权（sudo），按既定结论⛔ 不脚本化，走命令库条目。</summary>
    private static (bool Ok, string Message) WriteNotSupported(string what)
        => (false, $"apt {what}需要管理员权限（sudo），未内置 —— 请在命令库里执行。");

    public Task<(bool Ok, string Message)> InstallAsync(string id, string? source, bool silent, CancellationToken ct = default)
        => Task.FromResult(WriteNotSupported("安装"));

    public Task<(bool Ok, string Message)> UpgradeAsync(string id, bool silent, CancellationToken ct = default)
        => Task.FromResult(WriteNotSupported("升级"));

    public Task<(bool Ok, string Message)> UninstallAsync(string id, CancellationToken ct = default)
        => Task.FromResult(WriteNotSupported("卸载"));

    public Task<(bool Ok, string Message)> PinAsync(string id, CancellationToken ct = default)
        => Task.FromResult(WriteNotSupported("Pin"));

    public Task<(bool Ok, string Message)> UnpinAsync(string id, CancellationToken ct = default)
        => Task.FromResult(WriteNotSupported("取消 Pin"));
}
