using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IPackageManagerService"/> 的实现：当前后端把 winget 当**命令行**用。
/// <para>
/// 设计要点（见规范 1.1「带 GUI 的脚本管理器」）：
/// ① 只读操作用「固定列宽表格解析」（<see cref="PackageOutputParser"/>），不走 PowerShell 中间层；
/// ② 一律以**非交互**方式调用（协议自动接受 + <c>--disable-interactivity</c>），否则会卡在 Y/N 提示上；
/// ③ 写操作（安装 / 卸载 / pin）**不做超时静默重试**，失败就把 winget 的原始错误原样抛给界面。
/// </para>
/// <para>
/// 后续若要在 Debian 等平台支持 apt，可在此类内按 <see cref="IsSupported"/> / 运行平台分支，
/// 或新增实现并替换 DI 注册 —— 接口层（<see cref="IPackageManagerService"/>）保持中立。
/// </para>
/// </summary>
public class PackageManagerService : IPackageManagerService
{
    private static string? _exePath;

    /// <summary>
    /// 包 Id 的形状，用来滤掉表格尾部的"统计行"。
    /// <para>
    /// ⚠️ 必须允许**反斜杠**：MSI/ARP 安装的条目 Id 形如
    /// <c>ARP\Machine\X86\AMD_Chipset_IODrivers</c>、<c>MSIX\Microsoft.AV1VideoExtension_…</c>，
    /// 旧正则不认 <c>\</c> 会把这类已安装条目整批丢掉（v1.33 修正，也是"列表少东西"的原因之一）。
    /// </para>
    /// </summary>
    private static readonly Regex IdLike = new(@"^[A-Za-z0-9][A-Za-z0-9._+\\-]*$", RegexOptions.Compiled);

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMinutes(30);

    public bool IsSupported => OperatingSystem.IsWindows();

    // ==================== 只读 ====================

    public async Task<(bool Ok, string Message)> ProbeAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, "winget 仅支持 Windows；当前平台不可用。");
        var r = await RunAsync(new[] { "--version" }, ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        var ver = PackageOutputParser.Clean(r.Out).Trim();
        if (r.Code == 0 && ver.Length > 0) return (true, $"winget {ver.Split('\n')[0].Trim()}");
        return (false, Explain(r, "未检测到 winget"));
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageSourceInfo> Items)> ListSourcesAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg, Array.Empty<PackageSourceInfo>());
        var r = await RunAsync(new[] { "source", "list" }, ct, ReadTimeout).ConfigureAwait(false);

        var rows = PackageOutputParser.ParseTable(r.Out, "Name", "Argument");
        var list = new List<PackageSourceInfo>();
        foreach (var row in rows)
        {
            var name = row.GetValueOrDefault("Name", "").Trim();
            if (name.Length == 0) continue;
            list.Add(new PackageSourceInfo
            {
                Name = name,
                Argument = row.GetValueOrDefault("Argument", "").Trim(),
                Type = row.GetValueOrDefault("Type", row.GetValueOrDefault("Explicit", "")).Trim()
            });
        }

        // 兜底：极少数 winget 版本的 source list 没有可识别的表头 → 按「首列=名 / 次列=地址」硬切
        if (list.Count == 0)
        {
            foreach (var line in PackageOutputParser.Clean(r.Out).Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.All(c => c == '-' || c == ' ')) continue;
                var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[0] is "Name" or "名称") continue;
                if (!parts[0].All(c => char.IsLetterOrDigit(c) || c is '_' or '-')) continue;
                list.Add(new PackageSourceInfo { Name = parts[0], Argument = parts[1], Type = "" });
            }
        }

        if (list.Count == 0 && r.Code != 0) return (false, Explain(r, "读取源列表失败"), list);
        return (true, $"共 {list.Count} 个源", list);
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListInstalledAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg, Array.Empty<PackageInfo>());
        var r = await RunAsync(new[] { "list", "--accept-source-agreements" }, ct, ReadTimeout).ConfigureAwait(false);
        var items = Map(r.Out, installed: true);
        if (items.Count == 0 && r.Code != 0) return (false, Explain(r, "读取已安装列表失败"), items);
        return (true, $"已安装 {items.Count} 个包", items);
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListUpgradesAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg, Array.Empty<PackageInfo>());
        var r = await RunAsync(new[] { "upgrade", "--accept-source-agreements" }, ct, ReadTimeout).ConfigureAwait(false);
        var items = Map(r.Out, installed: true);
        if (items.Count == 0 && r.Code != 0) return (false, Explain(r, "读取可升级列表失败"), items);

        // winget upgrade 的统计行（"3 个升级可用"）会被 Id 形状过滤掉；此处把这句话捞出来当提示。
        var hint = FirstLineContaining(r.Out, "升级") ?? $"可升级 {items.Count} 个包";
        return (true, hint, items);
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> SearchAsync(string query, string? source, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg, Array.Empty<PackageInfo>());
        if (string.IsNullOrWhiteSpace(query)) return (false, "请输入搜索关键字。", Array.Empty<PackageInfo>());

        var args = new List<string> { "search", query.Trim(), "--accept-source-agreements" };
        if (!string.IsNullOrWhiteSpace(source)) { args.Add("--source"); args.Add(source.Trim()); }

        var r = await RunAsync(args, ct, ReadTimeout).ConfigureAwait(false);
        var items = Map(r.Out, installed: false);
        if (items.Count == 0)
        {
            var note = LastMeaningful(r);
            return (r.Code == 0, string.IsNullOrEmpty(note) ? "没有匹配的包" : note, items);
        }
        var pinHint = source == null ? "" : $"（源：{source}）";
        return (true, $"找到 {items.Count} 个包{pinHint}", items);
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListPinnedAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg, Array.Empty<PackageInfo>());
        var r = await RunAsync(new[] { "pin", "list" }, ct, ReadTimeout).ConfigureAwait(false);

        var list = new List<PackageInfo>();
        foreach (var id in PackageOutputParser.ParsePackageIds(r.Out))
            list.Add(new PackageInfo { Id = id, IsPinned = true });
        return (true, list.Count == 0 ? "没有已 Pin 的包" : $"已 Pin {list.Count} 个包", list);
    }

    public async Task<(bool Ok, string Message, PackageInfo? Detail)> ShowAsync(string id, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg, null);
        if (string.IsNullOrWhiteSpace(id)) return (false, "未选择包。", null);

        var r = await RunAsync(new[] { "show", "--id", id.Trim(), "--exact", "--accept-source-agreements" }, ct, ReadTimeout)
            .ConfigureAwait(false);
        var kv = PackageOutputParser.ParseKeyValues(r.Out);
        if (kv.Count == 0 && r.Code != 0) return (false, Explain(r, "读取详情失败"), null);

        var detail = new PackageInfo
        {
            Id = id.Trim(),
            Version = kv.GetValueOrDefault("Version", ""),
            Publisher = kv.GetValueOrDefault("Publisher", ""),
            Description = kv.GetValueOrDefault("Description", ""),
            Homepage = kv.GetValueOrDefault("Homepage", ""),
            License = kv.GetValueOrDefault("License", ""),
            InstallLocation = kv.GetValueOrDefault("InstallLocation", "")
        };
        // 首行 "Found Xxx [Id]" 里有显示名
        var found = FirstLineContaining(r.Out, "Found") ?? FirstLineContaining(r.Out, "找到");
        if (found != null)
        {
            var m = Regex.Match(found, @"\[(.+?)\]");
            if (m.Success && m.Groups[1].Value.Replace(" ", "") == id.Replace(" ", ""))
                detail.Name = found[..found.IndexOf('[')].Replace("Found", "").Replace("找到", "").Trim();
        }
        return (true, "详情已更新", detail);
    }

    // ==================== 源的增删（直接改系统 winget，与它共用一套数据） ====================

    public async Task<(bool Ok, string Message)> AddSourceAsync(string name, string argument, string? type = null, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(name)) return (false, "请先填写源名称。");
        if (string.IsNullOrWhiteSpace(argument)) return (false, "请先填写源地址（URL 或 UNC 路径）。");

        var args = new List<string> { "source", "add", "--name", name.Trim(), "--arg", argument.Trim() };
        if (!string.IsNullOrWhiteSpace(type)) { args.Add("--type"); args.Add(type.Trim()); }

        var r = await RunAsync(args, ct, ReadTimeout).ConfigureAwait(false);
        return r.Code == 0
            ? (true, $"已添加源 {name}（写到系统 winget 的配置里）")
            : (false, Explain(r, $"添加源 {name} 失败"));
    }

    public async Task<(bool Ok, string Message)> RemoveSourceAsync(string name, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(name)) return (false, "未指定源。");

        // ⚠️ 破坏性操作：动的是**整台机器**的包管理器源（等同于用户在终端敲 winget source remove）
        if (!await ConfirmAsync(
                $"确定要从系统 winget 里删除源「{name}」吗？\n" +
                "这会改掉整台机器的包管理器源配置（等价于 winget source remove），之后搜索与安装都会少一个来源。")
            .ConfigureAwait(false))
            return (false, "已取消。");

        var r = await RunAsync(new[] { "source", "remove", "--name", name.Trim() }, ct, ReadTimeout).ConfigureAwait(false);
        return r.Code == 0
            ? (true, $"已从系统 winget 删除源 {name}")
            : (false, Explain(r, $"删除源 {name} 失败"));
    }

    public async Task<(bool Ok, string Message)> ResetSourcesAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);

        if (!await ConfirmAsync(
                "确定把系统 winget 的源恢复为默认吗？\n" +
                "自定义添加的源会被移除，默认源（winget / msstore）会被重新加回来。")
            .ConfigureAwait(false))
            return (false, "已取消。");

        var r = await RunAsync(new[] { "source", "reset", "--force" }, ct, ReadTimeout).ConfigureAwait(false);
        return r.Code == 0
            ? (true, "已把系统 winget 的源恢复为默认")
            : (false, Explain(r, "恢复默认源失败"));
    }

    // ==================== 写操作 ====================

    public async Task<(bool Ok, string Message)> InstallAsync(string id, string? source, bool silent, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(id)) return (false, "未选择包。");

        var args = new List<string>
        {
            "install", "--id", id.Trim(), "--exact",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"
        };
        if (!string.IsNullOrWhiteSpace(source)) { args.Add("--source"); args.Add(source.Trim()); }
        if (silent) args.Add("--silent");

        var r = await RunAsync(args, ct, WriteTimeout).ConfigureAwait(false);
        return r.Code == 0
            ? (true, $"已安装 {id}")
            : (false, Explain(r, $"安装 {id} 失败"));
    }

    public async Task<(bool Ok, string Message)> UpgradeAsync(string id, bool silent, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(id)) return (false, "未选择包。");

        var args = new List<string>
        {
            "upgrade", "--id", id.Trim(), "--exact",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"
        };
        if (silent) args.Add("--silent");

        var r = await RunAsync(args, ct, WriteTimeout).ConfigureAwait(false);
        return r.Code == 0
            ? (true, $"已升级 {id}")
            : (false, Explain(r, $"升级 {id} 失败"));
    }

    public async Task<(bool Ok, string Message)> UninstallAsync(string id, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(id)) return (false, "未选择包。");
        if (!await ConfirmAsync($"确定要卸载 {id} 吗？\n这会调用 winget 真正卸载该软件，不可撤销。").ConfigureAwait(false))
            return (false, "已取消。");

        var r = await RunAsync(new[]
        {
            "uninstall", "--id", id.Trim(), "--exact", "--disable-interactivity"
        }, ct, WriteTimeout).ConfigureAwait(false);
        return r.Code == 0 ? (true, $"已卸载 {id}") : (false, Explain(r, $"卸载 {id} 失败"));
    }

    public async Task<(bool Ok, string Message)> PinAsync(string id, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(id)) return (false, "未选择包。");

        var r = await RunAsync(new[] { "pin", "add", "--id", id.Trim(), "--exact" }, ct, ReadTimeout).ConfigureAwait(false);
        return r.Code == 0
            ? (true, $"已 Pin {id}（升级会被跳过）")
            : (false, Explain(r, $"Pin {id} 失败"));
    }

    public async Task<(bool Ok, string Message)> UnpinAsync(string id, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(id)) return (false, "未选择包。");

        var r = await RunAsync(new[] { "pin", "remove", "--id", id.Trim(), "--exact" }, ct, ReadTimeout).ConfigureAwait(false);
        return r.Code == 0
            ? (true, $"已取消 Pin {id}")
            : (false, Explain(r, $"取消 Pin {id} 失败"));
    }

    /// <summary>
    /// 不可逆操作（卸载）前的二次确认钩子 —— 规范 3.7 的第三个分界。
    /// <para>
    /// Core 不引用 Avalonia，所以这里留一个可替换的异步钩子：Shell 启动时把它接到
    /// 真正的模态对话框上（见 <c>App.axaml.cs</c>）。
    /// **没接上时一律返回 false** —— 宁可什么都不做，也不能在用户没确认的情况下卸载软件。
    /// </para>
    /// </summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    private static async Task<bool> ConfirmAsync(string message)
        => ConfirmHandler != null && await ConfirmHandler(message).ConfigureAwait(false);

    // ==================== 内部 ====================

    private static class Fallback
    {
        public const string Msg = "winget 仅支持 Windows；当前平台不可用。";
    }

    private static List<PackageInfo> Map(string stdout, bool installed)
    {
        var rows = PackageOutputParser.ParseTable(stdout, "Name", "Id");
        var list = new List<PackageInfo>();
        foreach (var row in rows)
        {
            var id = row.GetValueOrDefault("Id", "").Trim();
            if (id.Length == 0 || !IdLike.IsMatch(id)) continue;   // 滤掉统计行 / 换行残行
            list.Add(new PackageInfo
            {
                Name = row.GetValueOrDefault("Name", "").Trim(),
                Id = id,
                Version = row.GetValueOrDefault("Version", "").Trim(),
                Available = row.GetValueOrDefault("Available", "").Trim(),
                Source = row.GetValueOrDefault("Source", "").Trim(),
                IsInstalled = installed
            });
        }
        return list;
    }

    private static string Explain((int Code, string Out, string Err) r, string prefix)
    {
        var detail = LastMeaningful(r);
        var hint = r.Code == -1 ? "（未找到 winget.exe：请确认已安装「应用安装程序 / App Installer」）" : "";
        return string.IsNullOrEmpty(detail) ? $"{prefix}{hint}" : $"{prefix}：{detail}";
    }

    private static string LastMeaningful((int Code, string Out, string Err) r)
    {
        foreach (var src in new[] { r.Err, r.Out })
        {
            var lines = PackageOutputParser.Clean(src)
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0 && !x.All(c => c == '-' || c == ' '))
                .ToList();
            if (lines.Count > 0) return lines[^1];
        }
        return "";
    }

    private static string? FirstLineContaining(string? raw, string token)
        => PackageOutputParser.Clean(raw)
            .Split('\n')
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.Length > 0 && x.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static async Task<(int Code, string Out, string Err)> RunAsync(
        IReadOnlyList<string> args, CancellationToken ct, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveExe(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // winget 在重定向输出时按 UTF-8 写；不用 UTF-8 读会把中文源名 / 包名解成乱码。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["WINGET_DISABLE_INTERACTIVITY"] = "1";
        psi.Environment["WINGET_ACCEPT_SOURCE_AGREEMENTS"] = "1";

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) return (-1, "", "无法启动 winget 进程。");
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
            return (-2, "", ct.IsCancellationRequested ? "已取消。" : $"winget 执行超过 {timeout.TotalMinutes:0} 分钟，已中止。");
        }

        return (proc.ExitCode,
                await outTask.ConfigureAwait(false),
                await errTask.ConfigureAwait(false));
    }

    /// <summary>
    /// 定位 winget.exe。先扫 PATH，再退到 WindowsApps 的应用执行别名 ——
    /// 该别名是 MSIX 注册的重解析点，某些环境下 <c>CreateProcess</c> 直接按名字找不到。
    /// </summary>
    private static string ResolveExe()
    {
        if (_exePath != null) return _exePath;

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var full = Path.Combine(dir.Trim(), "winget.exe");
                if (File.Exists(full)) return _exePath = full;
            }
            catch { /* 非法路径段，跳过 */ }
        }

        try
        {
            var alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(alias)) return _exePath = alias;
        }
        catch { }

        return _exePath = "winget";
    }
}
