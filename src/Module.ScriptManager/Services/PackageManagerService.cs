using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;   // DialogHooks（二次确认钩子，由 Shell 注入）
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

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
    /// <para>
    /// 🔴 v3.37 / NT-002 实测再修正（winget 1.29 真实 <c>winget list</c> 输出）：合法 Id 远不止"点分命名"——
    /// 实测存在 <b>带空格</b>的 <c>ARP\Machine\X64\Steam App 570</c>、<b>带花括号</b>的
    /// <c>ARP\Machine\X86\{{GUID}}</c>、乃至 <b>带中文</b>的 <c>ARP\Machine\X86\天翼云盘</c>，
    /// 旧白名单把它们全误杀了。现将空格 / 花括号 / 方括号 / 圆括号 / 中文块加入白名单；
    /// 统计行 / 提示行（如 <c>winget upgrade</c> 尾部的"1 程序包拥有阻止升级的包钉。使用 'winget pin'…"）
    /// 都是**短行或含句子标点**（<c>'</c> <c>。</c> 等不在白名单）→ 仍被滤掉。
    /// </para>
    /// </summary>
    private static readonly Regex IdLike = new(@"^[A-Za-z0-9\u4e00-\u9fff][A-Za-z0-9._+\\\-{}\[\]() \u4e00-\u9fff]*$", RegexOptions.Compiled);

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
            var explicitText = row.GetValueOrDefault("Explicit", "").Trim();
            list.Add(new PackageSourceInfo
            {
                Name = name,
                Argument = row.GetValueOrDefault("Argument", "").Trim(),
                Type = row.GetValueOrDefault("Type", "").Trim(),
                // 中文表头这一列叫「显式」；英文是 Explicit。认不出就按 false（= 参与默认搜索）。
                Explicit = explicitText.Equals("true", StringComparison.OrdinalIgnoreCase)
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

        // 类型 / 显式标记不在 source list 里 ⇒ 从 source export 补齐（跑一次，忽略失败）
        if (list.Count > 0)
        {
            var (defOk, _, defs) = await ExportSourceDefinitionsAsync(ct).ConfigureAwait(false);
            if (defOk)
            {
                foreach (var s in list)
                {
                    if (!defs.TryGetValue(s.Name, out var def)) continue;
                    if (s.Type.Length == 0) s.Type = def.Type;
                    s.Explicit = def.Explicit;
                }
            }
        }

        if (list.Count == 0 && r.Code != 0) return (false, Explain(r, "读取源列表失败"), list);
        return (true, $"共 {list.Count} 个源", list);
    }

    /// <summary>
    /// <c>winget source export</c>（每行一个 JSON）：把源的完整定义读出来，供「关掉还能加回来」用。
    /// <para>⚠️ 这是只读操作，**不需要管理员权限**。</para>
    /// </summary>
    public async Task<(bool Ok, string Message, IReadOnlyDictionary<string, PackageSourceDefinition> Items)> ExportSourceDefinitionsAsync(CancellationToken ct = default)
    {
        var dict = new Dictionary<string, PackageSourceDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!IsSupported) return (false, Fallback.Msg, dict);

        var r = await RunAsync(new[] { "source", "export" }, ct, ReadTimeout).ConfigureAwait(false);
        foreach (var line in PackageOutputParser.Clean(r.Out).Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] != '{') continue;
            try
            {
                using var doc = JsonDocument.Parse(t);
                var root = doc.RootElement;
                var name = root.TryGetProperty("Name", out var n) ? (n.GetString() ?? "").Trim() : "";
                if (name.Length == 0) continue;
                dict[name] = new PackageSourceDefinition
                {
                    Name = name,
                    Argument = root.TryGetProperty("Arg", out var a) ? (a.GetString() ?? "") : "",
                    Type = root.TryGetProperty("Type", out var ty) ? (ty.GetString() ?? "") : "",
                    Explicit = root.TryGetProperty("Explicit", out var ex) && ex.ValueKind == JsonValueKind.True
                };
            }
            catch (JsonException) { /* 单行解析不了就跳过，不影响其它源 */ }
        }

        return dict.Count > 0
            ? (true, $"读到 {dict.Count} 个源定义", dict)
            : (r.Code == 0, "没有读到源定义（winget 版本可能不支持 source export）", dict);
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
        var r = await RunAsync(new[] { "upgrade", "--accept-source-agreements", "--include-unknown" }, ct, ReadTimeout).ConfigureAwait(false);
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
        // NT-075：走表头切片拿「Id + 被 pin 住的版本」（⛔ 老实现会把版本号当成 Id 混进来）。
        foreach (var (id, ver) in PackageOutputParser.ParsePins(r.Out))
            list.Add(new PackageInfo { Id = id, Version = ver, PinnedVersion = ver, IsPinned = true });
        return (true, list.Count == 0 ? "没有已 Pin 的包" : $"已 Pin {list.Count} 个包", list);
    }

    public async Task<(bool Ok, string Message, PackageInfo? Detail)> ShowAsync(string id, string? source = null, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg, null);
        if (string.IsNullOrWhiteSpace(id)) return (false, "未选择包。", null);

        // 🔴 v3.78：商店包（msstore）不在社区源里 ⇒ 必须带上 --source，否则 show 查不到、详情一片空白。
        var args = new List<string> { "show", "--id", id.Trim(), "--exact", "--accept-source-agreements" };
        if (!string.IsNullOrWhiteSpace(source)) { args.Add("--source"); args.Add(source.Trim()); }

        var r = await RunAsync(args, ct, ReadTimeout)
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
        // 首行 "已找到 <名称> [<Id>]"（中文）/ "Found <名称> [<Id>]"（英文）里有显示名
        var found = FirstLineContaining(r.Out, "Found") ?? FirstLineContaining(r.Out, "找到");
        if (found != null)
        {
            var m = Regex.Match(found, @"\[(.+?)\]");
            if (m.Success && m.Groups[1].Value.Replace(" ", "") == id.Replace(" ", ""))
            {
                // 取 '[' 前部分，再去掉开头提示词（长的在前：⛔ 不能只 Replace"找到"——"已找到"会残留"已"，v3.19 修的包名多字 bug）
                var namePart = found[..found.IndexOf('[')].Trim();
                foreach (var prefix in new[] { "已找到", "找到", "Found" })
                    if (namePart.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    { namePart = namePart[prefix.Length..].Trim(); break; }
                detail.Name = namePart;
            }
        }
        return (true, "详情已更新", detail);
    }

    // ==================== 源的增删（直接改系统 winget，与它共用一套数据） ====================

    public async Task<(bool Ok, string Message)> AddSourceAsync(string name, string argument, string? type = null, bool explicitSource = false, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(name)) return (false, "请先填写源名称。");
        if (string.IsNullOrWhiteSpace(argument)) return (false, "请先填写源地址（URL 或 UNC 路径）。");

        var args = new List<string> { "source", "add", "--name", name.Trim(), "--arg", argument.Trim() };
        if (!string.IsNullOrWhiteSpace(type)) { args.Add("--type"); args.Add(type.Trim()); }
        // 🔴 显式源（如 winget-font）重新添加时必须带 --explicit，否则会被卷进每次默认搜索。
        if (explicitSource) args.Add("--explicit");

        var r = await RunAsync(args, ct, ReadTimeout, elevate: true).ConfigureAwait(false);
        return r.Code == 0
            ? (true, $"已添加源 {name}（写到系统 winget 的配置里）")
            : (false, Explain(r, $"添加源 {name} 失败"));
    }

    public async Task<(bool Ok, string Message)> RemoveSourceAsync(string name, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        if (string.IsNullOrWhiteSpace(name)) return (false, "未指定源。");

        // ⚠️ 破坏性操作：动的是**整台机器**的包管理器源（等同于用户在终端敲 winget source remove）
        return await RemoveCoreAsync(name.Trim(),
            $"确定要从系统 winget 里删除源「{name}」吗？\n" +
            "这会改掉整台机器的包管理器源配置（等价于 winget source remove），之后搜索与安装都会少一个来源。",
            ct).ConfigureAwait(false);
    }

    /// <summary>删除源的统一落点（确认 → 提权 remove）。</summary>
    private static async Task<(bool Ok, string Message)> RemoveCoreAsync(string name, string confirmMessage, CancellationToken ct)
    {
        if (!await ConfirmAsync(confirmMessage).ConfigureAwait(false))
            return (false, "已取消。");

        var r = await RunAsync(new[] { "source", "remove", "--name", name }, ct, ReadTimeout, elevate: true).ConfigureAwait(false);
        return r.Code == 0
            ? (true, $"已从系统 winget 移除源 {name}")
            : (false, Explain(r, $"移除源 {name} 失败"));
    }

    public async Task<(bool Ok, string Message)> ResetSourcesAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);

        if (!await ConfirmAsync(
                "确定把系统 winget 的源恢复为默认吗？\n" +
                "自定义添加的源会被移除，默认源（winget / msstore / winget-font）会被重新加回来。")
            .ConfigureAwait(false))
            return (false, "已取消。");

        var r = await RunAsync(new[] { "source", "reset", "--force" }, ct, ReadTimeout, elevate: true).ConfigureAwait(false);
        return r.Code == 0
            ? (true, "已把系统 winget 的源恢复为默认")
            : (false, Explain(r, "恢复默认源失败"));
    }

    /// <summary>
    /// 启用 / 停用源（v3.79）。
    /// <para>
    /// 🔴 实测 winget 1.29 的 <c>source</c> 子命令只有 add / list / update / remove / edit / reset / export
    /// —— **没有 enable / disable**（旧实现先试 enable 再回退，等于每次多弹一次 UAC 才失败）。
    /// 现在的做法：开 = 按记下的定义 <c>source add</c>；关 = <c>source remove</c>（带确认）。
    /// </para>
    /// </summary>
    public async Task<(bool Ok, string Message)> SetSourceEnabledAsync(string name, bool enable, PackageSourceDefinition? definition = null, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, Fallback.Msg);
        var n = (name ?? "").Trim();
        if (n.Length == 0) return (false, "未指定源。");

        if (!PackageSources.CanToggle(n))
            return (false, $"商店源 {n} 由 winget 统一管理，不支持启用 / 停用（丢了点「↺ 恢复默认源」）。");

        if (enable)
        {
            var def = definition ?? PackageSources.DefaultDefinition(n);
            if (def == null || string.IsNullOrWhiteSpace(def.Argument))
                return (false, $"没有记下源 {n} 的地址，无法加回来 —— 请用「添加源」重新添加（或点「↺ 恢复默认源」）。");

            var (ok, msg) = await AddSourceAsync(n, def.Argument, def.Type, def.Explicit, ct).ConfigureAwait(false);
            return ok ? (true, $"已启用源 {n}（winget source add）") : (false, msg);
        }

        return await RemoveCoreAsync(n,
            $"停用源「{n}」= 从系统 winget 里移除它（winget 1.29 没有 source enable/disable，只能 remove）。\n" +
            "它的地址 / 类型已经记在本应用配置里，之后把开关拨回来就会按原样加回去。\n\n" +
            "这会改掉整台机器的包管理器源配置，继续吗？",
            ct).ConfigureAwait(false);
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
        // 🔴 v3.78：商店源（msstore）走的是 Microsoft Store 自己的安装通道，⛔ 不吃 --silent
        //    （硬传会被 winget 判成不支持的参数）⇒ 静默开关只对社区源 / 自定义源生效。
        var skipSilent = silent && PackageSources.IsStore(source);
        if (silent && !skipSilent) args.Add("--silent");

        var r = await RunAsync(args, ct, WriteTimeout).ConfigureAwait(false);
        var note = skipSilent ? "（商店源不支持 --silent，已按默认方式安装）" : "";
        return r.Code == 0
            ? (true, $"已安装 {id}{note}")
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
    /// 不可逆操作（卸载 / 删源）前的二次确认 —— 规范 3.7 的第三个分界。
    /// <para>
    /// NT-074：本服务从 Core 下放到模块后，**Shell 不能再直接给它接钩子**（Release 下 Shell 不引模块）
    /// ⇒ 改走 Core 的公共钩子 <see cref="DialogHooks.ConfirmAsync"/>（Shell 已注入同一套模态框）。
    /// **钩子没接上时返回 false** —— 宁可什么都不做，也不能在用户没确认的情况下卸载软件。
    /// </para>
    /// </summary>
    private static Task<bool> ConfirmAsync(string message)
        => DialogHooks.ConfirmAsync(message);

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
        IReadOnlyList<string> args, CancellationToken ct, TimeSpan timeout, bool elevate = false)
    {
        if (elevate) return await RunElevatedAsync(args, ct, timeout).ConfigureAwait(false);

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
    /// **提权**执行（NT-075）：<c>winget source enable|disable|add|remove|reset</c> 都要管理员权限，
    /// 不提权会直接失败（表现："启用了商店源但没生效"）。
    /// <para>
    /// 🔴 难点：<c>Verb="runas"</c> 必须配 <c>UseShellExecute=true</c>，而那样**不能重定向标准流** ⇒
    /// 借 <c>cmd /c … &gt; 临时文件 2&gt;&amp;1</c> 把 winget 的输出落盘，再读回来解析（退出码仍是 winget 的）。
    /// </para>
    /// <para>用户在 UAC 上点「否」⇒ 抛 <see cref="System.ComponentModel.Win32Exception"/>（1223）⇒ 按"已取消"处理。</para>
    /// </summary>
    private static async Task<(int Code, string Out, string Err)> RunElevatedAsync(
        IReadOnlyList<string> args, CancellationToken ct, TimeSpan timeout)
    {
        var outFile = Path.Combine(Path.GetTempPath(), "newtoys-winget-" + Guid.NewGuid().ToString("N") + ".log");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = true,            // 🔴 runas 必需
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        // 环境变量在 ShellExecute 下不能经 psi.Environment 传 ⇒ 拼进 cmd 命令行
        var inner = "set WINGET_DISABLE_INTERACTIVITY=1 && set WINGET_ACCEPT_SOURCE_AGREEMENTS=1 && "
                    + Quote(ResolveExe()) + " " + string.Join(" ", args.Select(Quote))
                    + " > " + Quote(outFile) + " 2>&1";

        // 🔴🔴 NT-076 修复（"弹了 UAC 却什么都没发生"的根因）：cmd 的 /c 有两层坑 ——
        //   ① `ArgumentList` 会按 MSVCRT 规则把引号转义成 `\"`，而 **cmd 不认反斜杠转义** ⇒
        //      winget 路径 / 日志路径的引号被吃掉一半，命令变成非法路径，整条直接失败；
        //   ② 不给 `/s` 时 cmd 会"剥掉首尾引号"，把内层引号配对彻底打乱。
        //   解法：**用 psi.Arguments 原样传命令行**（⛔ 不用 ArgumentList 的转义）
        //   + `/d`（跳过 AutoRun）+ `/s`（只剥首尾那一对引号）+ 外面再套一层引号
        //   ⇒ cmd 拿到的就是 inner 原文，内层引号保持成对。
        psi.Arguments = "/d /s /c \"" + inner + "\"";

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) return (-1, "", "无法以管理员身份启动 winget。");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, "", "已取消管理员授权（UAC）—— 改源 / 改商店源需要管理员权限。");
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return (-2, "", ct.IsCancellationRequested ? "已取消。" : $"winget 执行超过 {timeout.TotalMinutes:0} 分钟，已中止。");
        }

        var hasLog = File.Exists(outFile);
        string text = "";
        try
        {
            if (hasLog) text = File.ReadAllText(outFile);
        }
        catch { /* 读不到就当没输出 */ }
        finally
        {
            try { if (hasLog) File.Delete(outFile); } catch { /* 临时文件删不掉无所谓 */ }
        }

        // NT-076：连日志文件都没生成 ⇒ cmd 那一步就没跑起来（命令行解析失败 / 起不来进程）
        // ⇒ 给一句看得懂的原因，⛔ 别让状态栏只显示一个干巴巴的退出码。
        if (!hasLog && proc.ExitCode != 0)
            return (-1, text, "提权后的命令没能执行（cmd 未产生输出，多半是命令行解析失败）");

        return (proc.ExitCode, text, "");
    }

    /// <summary>给 <c>cmd /c</c> 用的引号（winget 路径 / 包 Id / 临时文件路径都可能带空格）。</summary>
    private static string Quote(string s) => "\"" + s + "\"";

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
