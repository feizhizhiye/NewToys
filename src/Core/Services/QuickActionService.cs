using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IQuickActionService"/> 实现：落到 <c>&lt;应用根&gt;\config\quickactions.json</c>。
/// <para>
/// 首次运行（文件不存在）灌入一组**内置的常用「系统设置 / 脚本」种子**，开箱即用；之后用户改动直接覆盖该文件。
/// 落点选在 <c>config\</c>：「设置 → 数据管理」的备份 / 导入（<see cref="ConfigStoreService"/>）天然覆盖它。
/// </para>
/// <para>
/// 「设置与脚本 / 快捷设置」两页共用这一份清单，只按 <see cref="QuickAction.Page"/> 分栏；
/// 运行命令经 <c>cmd.exe</c> 执行（<see cref="RunAsync"/>），提权走 ShellExecute + <c>runas</c>。
/// </para>
/// <para>
/// 删除的二次确认通过静态钩子 <see cref="ConfirmHandler"/> 外抛 —— <c>Core</c> 不引用 Avalonia，
/// 由宿主（<c>App.axaml.cs</c>）接到模态框上，与 <see cref="PackageManagerService"/> 同款做法。
/// </para>
/// </summary>
public class QuickActionService : IQuickActionService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>普通命令的运行上限：超时即连子进程一起结束（防 <c>sfc /scannow</c> 这类长命令把「运行中」挂死）。</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);

    private readonly ConfigStoreService _cfg;
    private readonly List<QuickAction> _items;

    /// <summary>删除前的二次确认钩子（宿主注入；为 null 时直接删除）。</summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    public QuickActionService(ConfigStoreService cfg)
    {
        _cfg = cfg;
        var (items, needPersist) = Load();
        _items = items;
        if (needPersist) Persist();   // 首次运行灌种子 / 老文件回填 Page：落盘，让文件与界面一致
    }

    public IReadOnlyList<QuickAction> Items => _items;
    public IReadOnlyList<QuickAction> Shortcuts => _items.Where(i => i.Page != QuickActionPage.Quick).ToList();
    public IReadOnlyList<QuickAction> QuickSettings => _items.Where(i => i.Page == QuickActionPage.Quick).ToList();
    public IReadOnlyList<QuickAction> Favorites => _items.Where(i => i.IsFavorite).ToList();
    public string FilePath => Path.Combine(_cfg.ConfigDir, "quickactions.json");

    public event Action? Changed;

    public QuickAction Add(string page)
    {
        var item = new QuickAction
        {
            Id = Guid.NewGuid().ToString("N"),
            Page = string.IsNullOrWhiteSpace(page) ? QuickActionPage.Shortcuts : page,
            Title = "新的设置项",
            Category = "未分类",
            Description = "",
            Command = "",
            NeedAdmin = false,
            IsFavorite = false
        };
        _items.Add(item);
        Persist();
        return item;
    }

    public void Save() => Persist();

    public async Task<bool> RemoveAsync(QuickAction item)
    {
        if (item == null || !_items.Contains(item)) return false;
        if (ConfirmHandler != null)
        {
            var ok = await ConfirmHandler($"确定删除「{item.Title}」吗？此操作不可撤销。").ConfigureAwait(false);
            if (!ok) return false;
        }
        _items.Remove(item);
        Persist();
        return true;
    }

    public void ToggleFavorite(QuickAction item)
    {
        if (item == null) return;
        item.IsFavorite = !item.IsFavorite;
        Persist();
    }

    // ==================== 运行 ====================

    public async Task<(bool Ok, string Message)> RunAsync(QuickAction item, CancellationToken ct = default)
    {
        if (item == null) return (false, "未选择项目。");

        var cmd = (item.Command ?? "").Trim();
        if (cmd.Length == 0) return (false, "这条还没有命令内容。");
        if (!OperatingSystem.IsWindows())
            return (false, "「运行」目前只在 Windows 上可用（经 cmd 执行）。");

        // 需要管理员：只能用 ShellExecute + runas（弹 UAC），此时**无法重定向输出** ——
        // 于是让 cmd 在新控制台窗口里跑，命令的原始输出 / 报错用户直接看得到（比在状态行里转述更可靠）。
        if (item.NeedAdmin)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + cmd,
                    Verb = "runas",
                    UseShellExecute = true
                });
                return (true, $"已请求以管理员身份运行「{item.Title}」：请在 UAC 弹窗确认，结果见新打开的命令行窗口。");
            }
            catch (Exception ex)
            {
                // 1223 = 用户在 UAC 弹窗里点了「否」
                var msg = ex is System.ComponentModel.Win32Exception w && w.NativeErrorCode == 1223
                    ? "已取消（未获得管理员权限）。"
                    : "无法提权运行：" + ex.Message;
                return (false, msg);
            }
        }

        // 普通命令：隐藏窗口 + 捕获输出，把结果 / 报错压成一行给页面状态行。
        // ⚠️ 前置 chcp 65001：重定向成管道时 cmd 及其子程序按控制台编码写字节，不切到 UTF-8 中文会读成乱码。
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /c chcp 65001>nul & " + cmd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = SafeWorkingDir()
        };

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) return (false, "无法启动 cmd.exe。");
        }
        catch (Exception ex)
        {
            return (false, "运行失败：" + ex.Message);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RunTimeout);

        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
            return (false, ct.IsCancellationRequested ? "已取消。" : $"命令执行超过 {RunTimeout.TotalMinutes:0} 分钟，已中止。");
        }

        var stdout = await outTask.ConfigureAwait(false);
        var stderr = await errTask.ConfigureAwait(false);
        var detail = Summarize(stderr, stdout);

        if (proc.ExitCode == 0)
            return (true, detail.Length == 0 ? $"已运行：{item.Title}" : $"已运行：{item.Title} —— {detail}");
        return (false, detail.Length == 0
            ? $"运行失败（退出码 {proc.ExitCode}）：{item.Title}"
            : $"运行失败（退出码 {proc.ExitCode}）：{detail}");
    }

    /// <summary>从命令输出里挑一行「最有信息量的」给状态行（优先 stderr 的最后一行，再退到 stdout）。</summary>
    private static string Summarize(string? stderr, string? stdout)
    {
        foreach (var src in new[] { stderr, stdout })
        {
            var lines = (src ?? "")
                .Replace("\r", "")
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
            if (lines.Count > 0) return Truncate(lines[^1]);
        }
        return "";
    }

    private static string Truncate(string s, int max = 160) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>命令的工作目录取用户主目录（比程序目录更"像"用户自己开 cmd），取不到就用默认。</summary>
    private static string SafeWorkingDir()
    {
        try
        {
            var p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Directory.Exists(p) ? p : "";
        }
        catch { return ""; }
    }

    // ==================== 落盘 / 加载 ====================

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items, JsonOpts));
        }
        catch { /* 保存失败不打断 UI；下一次改动会再试 */ }
        Changed?.Invoke();
    }

    private (List<QuickAction> items, bool needPersist) Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<QuickAction>>(File.ReadAllText(FilePath));
                if (list != null)
                {
                    var migrated = false;
                    foreach (var i in list)
                    {
                        if (string.IsNullOrEmpty(i.Id)) i.Id = Guid.NewGuid().ToString("N");   // 老文件兜底
                        if (string.IsNullOrEmpty(i.Page)) { i.Page = InferPage(i.Category); migrated = true; }
                    }
                    return (list, migrated);
                }
            }
        }
        catch { /* 配置损坏 → 回落到默认种子，不阻断启动 */ }
        return (BuildSeed(), true);
    }

    /// <summary>
    /// 旧文件（v1.32 之前）没有 <c>Page</c> 列，按分类回填：
    /// 「打开」类是系统自带入口 → 快捷设置，其余（reg / powercfg / 网络 / 维护等脚本）→ 设置与脚本。
    /// </summary>
    private static string InferPage(string? category)
        => string.Equals((category ?? "").Trim(), "打开", StringComparison.Ordinal)
            ? QuickActionPage.Quick
            : QuickActionPage.Shortcuts;

    /// <summary>
    /// 内置种子：常用「系统设置项 + 脚本」（→ 设置与脚本页）与「系统自带设置入口」（→ 快捷设置页）。
    /// 首次运行写入 <c>config\quickactions.json</c>，之后以用户文件为准。
    /// </summary>
    private static List<QuickAction> BuildSeed()
    {
        static QuickAction A(string title, string category, string desc, string cmd,
                             bool admin = false, string page = QuickActionPage.Shortcuts) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Page = page,
            Title = title,
            Category = category,
            Description = desc,
            Command = cmd,
            NeedAdmin = admin,
            IsFavorite = false
        };

        return new List<QuickAction>
        {
            // ==================== 设置与脚本页：较复杂的设置 / 脚本（靠命令执行） ====================
            // ---------- 系统设置（注册表 / 电源等） ----------
            A("显示文件扩展名", "系统设置", "在资源管理器里显示已知文件类型的扩展名",
              @"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /t REG_DWORD /d 0 /f"),
            A("隐藏文件扩展名", "系统设置", "恢复为不显示已知文件的扩展名",
              @"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /t REG_DWORD /d 1 /f"),
            A("显示隐藏文件", "系统设置", "资源管理器里显示隐藏的文件与文件夹",
              @"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v Hidden /t REG_DWORD /d 1 /f"),
            A("任务栏时钟显示秒", "系统设置", "在任务栏时钟里显示秒数",
              @"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ShowSecondsInSystemClock /t REG_DWORD /d 1 /f"),
            A("启用休眠", "系统设置", "开启系统休眠（会生成 hiberfil.sys）",
              "powercfg /hibernate on", admin: true),
            A("关闭休眠", "系统设置", "关闭系统休眠并删除休眠文件（释放磁盘空间）",
              "powercfg /hibernate off", admin: true),
            A("切换到高性能电源计划", "系统设置", "把当前电源计划切到「高性能」",
              "powercfg /setactive SCHEME_MIN"),
            A("切换到平衡电源计划", "系统设置", "把当前电源计划切回「平衡」",
              "powercfg /setactive SCHEME_BALANCED"),

            // ---------- 网络 ----------
            A("刷新 DNS 缓存", "网络", "清空本机 DNS 解析缓存（域名解析异常时常用）",
              "ipconfig /flushdns"),
            A("释放并重新获取 IP", "网络", "ipconfig /release 后再 /renew，重连 DHCP",
              "ipconfig /release & ipconfig /renew", admin: true),
            A("查看网络配置", "网络", "显示全部网卡的 IP / 网关 / DNS 明细",
              "ipconfig /all"),

            // ---------- 维护 ----------
            A("重启资源管理器", "维护", "结束并重启 explorer.exe（任务栏 / 桌面卡死时用）",
              "taskkill /f /im explorer.exe & start explorer.exe"),
            A("清理用户临时文件", "维护", "清空当前用户的 %TEMP% 目录",
              @"del /q /f ""%TEMP%\*"" & rd /s /q ""%TEMP%"""),
            A("系统文件检查", "维护", "sfc /scannow，扫描并修复系统文件（较慢）",
              "sfc /scannow", admin: true),
            A("检查磁盘（C:）", "维护", "chkdsk C: /scan，在线扫描磁盘错误",
              "chkdsk C: /scan", admin: true),

            // ==================== 快捷设置页：系统自带设置 / 管理工具入口（点一下即打开） ====================
            // ---------- 系统设置（Windows 设置面板） ----------
            A("显示设置", "系统设置", "打开「显示」设置（分辨率 / 缩放 / 多显示器）",
              "start ms-settings:display", page: QuickActionPage.Quick),
            A("个性化", "系统设置", "打开「个性化」设置（背景 / 颜色 / 主题）",
              "start ms-settings:personalization", page: QuickActionPage.Quick),
            A("网络和 Internet", "系统设置", "打开网络状态设置（WLAN / 以太网 / 代理）",
              "start ms-settings:network-status", page: QuickActionPage.Quick),
            A("蓝牙和其他设备", "系统设置", "打开蓝牙与设备设置",
              "start ms-settings:bluetooth", page: QuickActionPage.Quick),
            A("电源和睡眠", "系统设置", "打开电源与睡眠超时设置",
              "start ms-settings:powersleep", page: QuickActionPage.Quick),
            A("存储感知", "系统设置", "打开存储设置，清理临时文件",
              "start ms-settings:storagesense", page: QuickActionPage.Quick),
            A("应用和功能", "系统设置", "打开「应用和功能」（默认应用 / 启动项）",
              "start ms-settings:appsfeatures", page: QuickActionPage.Quick),
            A("Windows 更新", "系统设置", "打开 Windows 更新检查页",
              "start ms-settings:windowsupdate", page: QuickActionPage.Quick),
            A("日期和时间", "系统设置", "打开日期与时间设置（时区 / 自动校时）",
              "start ms-settings:dateandtime", page: QuickActionPage.Quick),
            A("声音", "系统设置", "打开声音设置（输出设备 / 音量）",
              "start ms-settings:sound", page: QuickActionPage.Quick),
            A("任务栏", "系统设置", "打开任务栏设置",
              "start ms-settings:taskbar", page: QuickActionPage.Quick),
            A("关于本机", "系统设置", "打开「关于」页（设备规格 / 重命名此电脑）",
              "start ms-settings:about", page: QuickActionPage.Quick),

            // ---------- 打开（经典管理工具入口） ----------
            A("打开控制面板", "打开", "启动经典控制面板",
              "control", page: QuickActionPage.Quick),
            A("打开系统属性", "打开", "系统属性（计算机名 / 高级 / 环境变量）",
              "sysdm.cpl", page: QuickActionPage.Quick),
            A("打开程序和功能", "打开", "卸载或更改已安装的程序",
              "appwiz.cpl", page: QuickActionPage.Quick),
            A("打开设备管理器", "打开", "查看与管理硬件设备",
              "devmgmt.msc", page: QuickActionPage.Quick),
            A("打开服务", "打开", "查看与管理 Windows 服务",
              "services.msc", page: QuickActionPage.Quick),
            A("打开磁盘清理", "打开", "清理系统盘临时文件与旧更新",
              "cleanmgr", page: QuickActionPage.Quick),
            A("打开任务管理器", "打开", "启动任务管理器",
              "taskmgr", page: QuickActionPage.Quick),
        };
    }
}
