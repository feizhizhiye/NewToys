using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.PcTools.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.Services;

/// <summary>
/// <see cref="IQuickActionService"/> 实现：两个页面 = 两个配置文件。
/// <list type="bullet">
///   <item>「设置与脚本」→ <c>&lt;应用根&gt;\config\pc-scripts.json</c>（有脚本文件的脚本）；</item>
///   <item>「快捷设置」→ <c>&lt;应用根&gt;\config\pc-quick.json</c>（命令行命令）。</item>
/// </list>
/// <para>
/// <b>命令内容不写死在代码里，也不随程序预置</b>（v3.60 §3.6 / NT-060）：<b>干净启动</b> —— 两个配置文件
/// 首次运行落盘成**空清单**，条目全部由用户自己导入 / 新建。软件只提供**默认路径**
/// （<see cref="ScriptsDirectory"/> 这类常量），⛔ 不再读 <c>defaults\*.default.json</c> 播种。
/// </para>
/// <para>
/// 旧版（v1.33 及以前）只有一个 <c>quickactions.json</c>：首次运行本服务会把它**按 <see cref="QuickAction.Page"/>
/// 拆成两个新文件**，然后把旧文件改名成 <c>quickactions.json.migrated</c>（改名而不是删除 —— 不丢数据，
/// 且 <c>*.json</c> 的备份扫描不会再带上它）。
/// </para>
/// <para>
/// 删除等破坏性操作的二次确认通过静态钩子 <see cref="ConfirmHandler"/> 外抛 —— <c>Core</c> 不引用 Avalonia，
/// 由宿主（<c>App.axaml.cs</c>）接到模态框上，与 <see cref="PackageManagerService"/> 同款做法。
/// </para>
/// </summary>
public class QuickActionService : IQuickActionService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ConfigStoreService _cfg;
    private readonly IAppConfigService? _app;

    private readonly List<QuickAction> _shortcuts = new();
    private readonly List<QuickAction> _quick = new();

    /// <summary>删除前的二次确认钩子（宿主注入；为 null 时直接删除）。</summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    public QuickActionService(ConfigStoreService cfg, IAppConfigService app)
    {
        _cfg = cfg;
        _app = app;
        var needPersist = Load();
        EnsureScriptsDirectory();
        // 首次运行播种新文件 / 从旧单文件迁移 / 归一化归属：都落盘，让磁盘上的文件与界面完全一致
        if (needPersist) Persist();
    }

    // ==================== 读取 ====================

    public IReadOnlyList<QuickAction> Items => _shortcuts.Concat(_quick).ToList();
    public IReadOnlyList<QuickAction> Shortcuts => _shortcuts;
    public IReadOnlyList<QuickAction> QuickSettings => _quick;
    public IReadOnlyList<QuickAction> Favorites => _shortcuts.Concat(_quick).Where(i => i.IsFavorite).ToList();

    public string ScriptsFilePath => Path.Combine(_cfg.ConfigDir, "pc-scripts.json");
    public string QuickFilePath => Path.Combine(_cfg.ConfigDir, "pc-quick.json");

    /// <summary>旧版单文件（v1.33 及以前）；只用于一次性迁移。</summary>
    private string LegacyFilePath => Path.Combine(_cfg.ConfigDir, "quickactions.json");

    /// <summary>脚本目录（生效值）：配置为空时用 <c>&lt;应用根&gt;\scripts</c>。</summary>
    public string ScriptsDirectory
    {
        get
        {
            var custom = (_app?.ScriptFolder ?? "").Trim();
            if (custom.Length > 0) return custom;
            var def = _app?.DefaultScriptFolder ?? "";
            return def.Length > 0 ? def : Path.Combine(AppContext.BaseDirectory, "scripts");
        }
    }

    public event Action? Changed;

    // ==================== 增删改 ====================

    public QuickAction Add(string page)
    {
        var quick = page == QuickActionPage.Quick;
        var item = new QuickAction
        {
            Id = Guid.NewGuid().ToString("N"),
            Page = quick ? QuickActionPage.Quick : QuickActionPage.Shortcuts,
            // 页面默认类型：快捷设置页加的是命令行命令，设置与脚本页加的是脚本文件（可在详情里改回）。
            Kind = quick ? QuickActionKind.Command : QuickActionKind.Script,
            Title = quick ? "新的命令" : "新的脚本",
            Category = "未分类",
            Description = "",
            Command = "",
            ScriptFile = "",
            Interpreter = ScriptInterpreter.Auto,
            NeedAdmin = false,
            IsFavorite = false
        };
        (quick ? _quick : _shortcuts).Add(item);
        Persist();
        return item;
    }

    public void Save() => Persist();

    public async Task<bool> RemoveAsync(QuickAction item)
    {
        if (item == null) return false;
        if (!_shortcuts.Contains(item) && !_quick.Contains(item)) return false;

        if (ConfirmHandler != null)
        {
            var ok = await ConfirmHandler($"确定删除「{item.Title}」吗？此操作不可撤销。").ConfigureAwait(false);
            if (!ok) return false;
        }
        _shortcuts.Remove(item);
        _quick.Remove(item);
        Persist();
        return true;
    }

    public bool RemoveSilent(QuickAction item)
    {
        if (item == null) return false;
        if (!_shortcuts.Contains(item) && !_quick.Contains(item)) return false;

        _shortcuts.Remove(item);
        _quick.Remove(item);
        Persist();
        return true;
    }

    public void ToggleFavorite(QuickAction item)
    {
        if (item == null) return;
        item.IsFavorite = !item.IsFavorite;
        Persist();
    }

    /// <summary>
    /// 在两页之间移动一条（改 <see cref="QuickAction.Page"/> + 挪到另一个列表 + 落盘）。
    /// 目标页是「设置与脚本」时顺带把类型标成脚本（否则它下次又会被移回快捷设置）。
    /// </summary>
    public void Move(QuickAction item, string page)
    {
        if (item == null) return;

        var toQuick = page != QuickActionPage.Shortcuts;
        _shortcuts.Remove(item);
        _quick.Remove(item);

        item.Page = toQuick ? QuickActionPage.Quick : QuickActionPage.Shortcuts;
        if (!toQuick)
        {
            item.Kind = QuickActionKind.Script;
        }
        else if (item.Kind == QuickActionKind.Script && string.IsNullOrWhiteSpace(item.ScriptFile))
        {
            item.Kind = QuickActionKind.Command;   // 没有脚本文件又放到快捷设置 → 就是一行命令
        }

        (toQuick ? _quick : _shortcuts).Add(item);
        Persist();
    }

    // ==================== 导入 / 导出（v3.19 / NT-006、NT-007） ====================

    /// <summary>导出某页**全条目**为 json 文本（<see cref="QuickActionPage.Quick"/> / <see cref="QuickActionPage.Shortcuts"/>）。</summary>
    public string ExportPage(string page)
    {
        var list = page == QuickActionPage.Quick ? _quick : _shortcuts;
        return JsonSerializer.Serialize(list, JsonOpts);
    }

    /// <summary>
    /// 导入某页条目（v3.19）：<paramref name="replace"/>=true 整页替换、false 合并
    /// （按 <see cref="QuickAction.Id"/> 去重，同 Id 覆盖字段、保留列表顺序）。
    /// </summary>
    /// <returns>(新增, 更新, 跳过/无效)；<c>Skipped=-1</c> 表示 json 解析失败。</returns>
    public (int Added, int Updated, int Skipped) ImportPage(string page, string json, bool replace)
    {
        List<QuickAction>? incoming;
        try { incoming = JsonSerializer.Deserialize<List<QuickAction>>(json); }
        catch { return (0, 0, -1); }
        if (incoming == null) return (0, 0, -1);

        var isQuick = page == QuickActionPage.Quick;
        var target = isQuick ? _quick : _shortcuts;
        if (replace) target.Clear();

        var added = 0; var updated = 0; var skipped = 0;
        foreach (var it in incoming)
        {
            if (it == null) { skipped++; continue; }
            it.Page = page;
            if (string.IsNullOrWhiteSpace(it.Id)) it.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(it.Kind))
                it.Kind = isQuick ? QuickActionKind.Command : QuickActionKind.Script;
            it.Shell = QuickActionShell.Normalize(it.Shell);

            var exist = target.FirstOrDefault(x => string.Equals(x.Id, it.Id, StringComparison.OrdinalIgnoreCase));
            if (exist != null)
            {
                exist.Title = it.Title; exist.Category = it.Category; exist.Description = it.Description;
                exist.Command = it.Command; exist.ScriptFile = it.ScriptFile; exist.Interpreter = it.Interpreter;
                exist.NeedAdmin = it.NeedAdmin; exist.IsFavorite = it.IsFavorite;
                exist.RunHidden = it.RunHidden; exist.Shell = it.Shell; exist.Kind = it.Kind;
                updated++;
            }
            else { target.Add(it); added++; }
        }
        Persist();
        return (added, updated, skipped);
    }

    /// <summary>清空某页**全部条目**并落盘（v3.19 / NT-006「🧹 清空本页」= 真删除，会写空列表进 json）。</summary>
    public void ClearPage(string page)
    {
        if (page == QuickActionPage.Quick) _quick.Clear();
        else _shortcuts.Clear();
        Persist();
    }

    // ==================== 运行 ====================

    public Task<(bool Ok, string Message)> RunAsync(QuickAction item, CancellationToken ct = default)
    {
        if (item == null) return Task.FromResult((false, "未选择项目。"));
        if (!OperatingSystem.IsWindows())
            return Task.FromResult((false, "「运行」目前只在 Windows 上可用。"));

        var isCommand = !string.Equals(item.Kind, QuickActionKind.Script, StringComparison.OrdinalIgnoreCase);
        var verb = item.NeedAdmin ? "runas" : "";

        // 「隐藏终端窗口」（RunHidden，v3.19 / NT-006）：GUI 启动类命令用 cmd /c 静默跑、不弹终端，
        // 目标 GUI 正常打开。仅对命令行命令生效；**需要管理员时忽略本开关**（仍走下面 runas + 可见 UAC）。
        if (item.RunHidden && !item.NeedAdmin && isCommand && !string.IsNullOrWhiteSpace(item.Command))
        {
            if (StartHidden("cmd.exe", "/c " + item.Command.Trim(), SafeWorkingDir(), out var hideErr))
                return Task.FromResult((true, $"已静默启动「{item.Title}」：目标程序应已打开（未弹终端窗口）。"));
            return Task.FromResult((false, hideErr ?? "无法静默启动。"));
        }

        var (ok, exe, args, workDir, err) = BuildRunPlan(item);
        if (!ok) return Task.FromResult((false, err));

        // 「快捷设置 / 设置与脚本」的定位 = 把常用命令 / 脚本攒起来、点一下就跑、还能收藏。
        // 普通命令**开一个可见终端窗口**直接跑（不隐藏、不捕获，输出用户直接看），窗口跑完留着方便读结果；
        // 优先用 pwsh（中文 / UTF-8 支持好，且先回显命令本身，见 <see cref="BuildEchoScript"/>），本机没 pwsh 才退回 cmd。
        // 需要管理员：同样的可见窗口 + <c>runas</c> 弹 UAC，窗口也保留（不闪退）。
        if (StartVisible(exe, args, workDir, verb, out var launchError))
            return Task.FromResult((true, SuccessMessage(item)));

        // 选了 pwsh 却起不来（本机没装 / PATH 没有）→ 退化成 cmd /k 跑同一条命令，至少还能看到窗口。
        if (IsPwshExe(exe) && isCommand && !string.IsNullOrWhiteSpace(item.Command))
        {
            if (StartVisible("cmd.exe", "/k " + item.Command, workDir, verb, out _))
                return Task.FromResult((true, $"已用 cmd 打开窗口运行「{item.Title}」（本机未找到可用的 pwsh）：结果见弹出的命令行窗口。"));
        }
        return Task.FromResult((false, launchError ?? "无法启动进程。"));
    }

    /// <summary>成功时的状态行文案（管理员 / 普通两种口径）。</summary>
    private static string SuccessMessage(QuickAction item)
        => item.NeedAdmin
            ? $"已请求以管理员身份打开窗口运行「{item.Title}」：请在 UAC 弹窗确认，结果见新打开的窗口。"
            : $"已打开终端窗口运行「{item.Title}」：结果见弹出的窗口（关闭即可结束）。";

    /// <summary>
    /// 把一条项目翻译成"要启动的宿主 exe + 它的参数 + 工作目录"。
    /// <list type="bullet">
    /// <item><see cref="QuickActionKind.Command"/>：优先 pwsh（<c>-NoExit -EncodedCommand</c> 跑原始命令、窗口留着），
    /// 拿不到 pwsh 就 <c>cmd /k</c>；命令本身原样透传，不做引号转义（用 base64 避开特殊字符）。</item>
    /// <item><see cref="QuickActionKind.Script"/>：**运行如双击**（v3.19 / NT-007，需求 §6.2.2）—— 直接用系统文件关联
    /// 启动脚本文件（<c>UseShellExecute</c>），像在资源管理器里双击它；工作目录取脚本所在目录（相对路径才好使）。</item>
    /// </list>
    /// </summary>
    private (bool Ok, string Exe, string Args, string WorkDir, string Error) BuildRunPlan(QuickAction item)
    {
        var kind = string.IsNullOrWhiteSpace(item.Kind) ? QuickActionKind.Command : item.Kind;

        if (kind != QuickActionKind.Script)
        {
            var cmd = (item.Command ?? "").Trim();
            if (cmd.Length == 0) return (false, "", "", "", "这条还没有命令内容。");

            // 宿主选择（Shell，v3.19 / NT-006）：Auto=优先 pwsh、Cmd=强制 cmd /k、Pwsh=强制 pwsh。
            var shell = QuickActionShell.Normalize(item.Shell);
            var hasPwsh = TryGetPwsh(out var pwsh);
            if (shell == QuickActionShell.Pwsh || (shell == QuickActionShell.Auto && hasPwsh))
            {
                if (!hasPwsh)
                    return (false, "", "", "", "本机未找到可用的 pwsh，无法按「Pwsh」运行（可把 Shell 改回「自动」或「cmd」）。");
                // -EncodedCommand：先回显命令本身（PS> <命令>）再执行，像用户手动在 pwsh 里输入；
                // 整段脚本按 UTF-16LE 转 base64，避开命令里的引号 / & / | 破坏参数解析。
                var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildEchoScript(cmd)));
                return (true, pwsh, $"-NoExit -NoProfile -EncodedCommand {b64}", SafeWorkingDir(), "");
            }
            return (true, "cmd.exe", $"/k {cmd}", SafeWorkingDir(), "");
        }

        // ---------- 脚本文件 ----------
        var file = (item.ScriptFile ?? "").Trim();
        if (file.Length == 0) return (false, "", "", "", "这条还没有指定脚本文件。");

        var path = Path.IsPathRooted(file) ? file : Path.Combine(ScriptsDirectory, file);
        if (!File.Exists(path))
            return (false, "", "", "", $"找不到脚本文件：{path}（脚本目录可在「设置 → 路径配置」里改）");

        // 运行如双击（v3.19 / NT-007，需求 §6.2.2）：直接用**系统文件关联**启动该脚本文件（UseShellExecute），
        // 像在资源管理器里双击它一样；不再用 cmd /k 包解释器。.py 若无关联会弹"如何打开"，与双击行为一致。
        // 工作目录取脚本所在目录（脚本里的相对路径才好使）。
        var scriptDir = Path.GetDirectoryName(path);
        var workDir = string.IsNullOrEmpty(scriptDir) ? SafeWorkingDir() : scriptDir;
        return (true, path, "", workDir, "");
    }

    /// <summary>本机能不能拿到 pwsh（PowerShell 7+）：配置里填了用配置（存在才用），否则探常见安装位、再退回 PATH 里的 pwsh。</summary>
    private bool TryGetPwsh(out string exe)
    {
        var cfg = (_app?.PwshPath ?? "").Trim();
        if (cfg.Length > 0)
        {
            if (File.Exists(cfg)) { exe = cfg; return true; }
            exe = ""; return false;   // 配了却不存在 → 交给调用方退回 cmd
        }
        // 没配置：先探常见安装位，再退回 PATH 里的 pwsh（PATH 没有则由 StartVisible 兜底成 cmd /k）。
        foreach (var cand in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PowerShell", "7", "pwsh.exe"),
        })
        {
            if (File.Exists(cand)) { exe = cand; return true; }
        }
        exe = "pwsh";
        return true;
    }

    private static bool IsPwshExe(string exe)
        => exe.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
        || exe.EndsWith("pwsh.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>开一个可见终端窗口跑命令（不隐藏、不重定向、窗口留着）；<paramref name="verb"/>=<c>runas</c> 即提权。</summary>
    private static bool StartVisible(string exe, string args, string workDir, string verb, out string? error)
    {
        error = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = workDir,
                Verb = verb
            };
            return Process.Start(psi) != null;
        }
        catch (System.ComponentModel.Win32Exception w) when (w.NativeErrorCode is 2 or 3 or 5)
        {
            error = w.Message;
            return false;   // 文件不存在 / 路径非法 / 拒绝访问 → 交给调用方兜底
        }
    }

    /// <summary>
    /// 包一层 pwsh 脚本：先回显命令本身（形如 <c>PS> &lt;命令&gt;</c>）再执行，
    /// 让可见终端里的显示像用户手动输入命令一样完整（v3.19 / NT-006，需求 §6.2.1）。
    /// <para>用户命令用 here-string 字面量（<c>@' … '@</c>）装，避开引号 / <c>$</c> / 反引杠被 pwsh 二次解析；
    /// 唯一禁忌是命令里出现**单独一行且内容恰为 <c>'@</c>**（会提前终结 here-string），真实命令不会触发。</para>
    /// </summary>
    private static string BuildEchoScript(string cmd)
    {
        var sb = new StringBuilder();
        sb.Append("$__c = @'\n");
        sb.Append(cmd);
        sb.Append("\n'@\n");
        sb.Append("Write-Host \"PS> $__c\"\n");
        sb.Append("Invoke-Expression $__c\n");
        return sb.ToString();
    }

    /// <summary>
    /// 静默启动（不弹终端窗口）：给「隐藏终端窗口」的 GUI 启动类命令用（v3.19 / NT-006）。
    /// <para>用 <c>UseShellExecute=true</c>（协议 / 文件关联才解析得动，如 <c>ms-settings:</c>、<c>explorer</c>）
    /// + <c>WindowStyle=Hidden</c> 藏掉 cmd 控制台窗口；目标 GUI 仍正常显示。</para>
    /// </summary>
    private static bool StartHidden(string exe, string args, string workDir, out string? error)
    {
        error = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workDir,
            };
            return Process.Start(psi) != null;
        }
        catch (System.ComponentModel.Win32Exception w) when (w.NativeErrorCode is 2 or 3 or 5)
        {
            error = w.Message;
            return false;
        }
    }

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

    /// <summary>两个文件各写各的（顺序稳定 = 界面顺序稳定）。</summary>
    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(ScriptsFilePath, JsonSerializer.Serialize(_shortcuts, JsonOpts));
            File.WriteAllText(QuickFilePath, JsonSerializer.Serialize(_quick, JsonOpts));
        }
        catch { /* 保存失败不打断 UI；下一次改动会再试 */ }
        Changed?.Invoke();
    }

    /// <summary>
    /// 加载顺序：
    /// ① 两个用户文件（<c>pc-scripts.json</c> / <c>pc-quick.json</c>）各自读；
    /// ② 缺哪个 → 用随程序发布的同名默认清单播种；
    /// ③ 两个都不存在、但旧的单文件 <c>quickactions.json</c> 在 → 按 <c>Page</c> 拆开迁移，旧文件改名 <c>.migrated</c>。
    /// 最后做一次**归属归一化**（没脚本文件的一律进「快捷设置」）。
    /// </summary>
    /// <returns>是否需要立即回写磁盘（有播种 / 迁移 / 归一化发生）。</returns>
    private bool Load()
    {
        var dirty = false;

        var scriptItems = TryRead(ScriptsFilePath);
        var quickItems = TryRead(QuickFilePath);

        // ---- ③ 从旧单文件迁移（仅当两个新文件都还没有时） ----
        if (scriptItems == null && quickItems == null && File.Exists(LegacyFilePath))
        {
            var legacy = TryRead(LegacyFilePath) ?? new List<QuickAction>();
            // 先按旧文件里的 Page 分栏（缺失的按分类推断），随后统一走下面的归属归一化 ——
            // 这样"设置与脚本里没有脚本文件的老条目"也会被挪到「快捷设置」，不会既跑不起来又看不见。
            scriptItems = legacy.Where(i => InferPage(i.Page, i.Category) != QuickActionPage.Quick).ToList();
            quickItems = legacy.Where(i => InferPage(i.Page, i.Category) == QuickActionPage.Quick).ToList();
            TryRetireLegacyFile();
            dirty = true;
        }

        // ---- ② 干净启动（v3.60 §3.6 / NT-060）：⛔ 不播种任何默认清单 ----
        // 两个文件不存在 ⇒ 落一份**空清单**（让磁盘上看得见"这里由我自己填"），⛔ 不再读 defaults\*.default.json。
        if (scriptItems == null)
        {
            scriptItems = new List<QuickAction>();
            dirty = true;
        }
        if (quickItems == null)
        {
            quickItems = new List<QuickAction>();
            dirty = true;
        }

        // ---- 字段回填（老文件缺 Id / Page / Kind） ----
        if (Migrate(scriptItems, QuickActionPage.Shortcuts)) dirty = true;
        if (Migrate(quickItems, QuickActionPage.Quick)) dirty = true;

        _shortcuts.Clear();
        _quick.Clear();
        _shortcuts.AddRange(scriptItems);
        _quick.AddRange(quickItems);

        // ---- ④ 归属归一化：没有脚本文件的，不属于「设置与脚本」 ----
        var stray = _shortcuts.Where(i => !QualifiesForShortcuts(i)).ToList();
        foreach (var item in stray)
        {
            _shortcuts.Remove(item);
            item.Page = QuickActionPage.Quick;
            if (string.IsNullOrWhiteSpace(item.Kind) || item.Kind == QuickActionKind.Script)
                item.Kind = item.Command.Trim().Length > 0 ? QuickActionKind.Command : item.Kind;
            _quick.Add(item);
            dirty = true;
        }

        return dirty;
    }

    /// <summary>一条是否够格待在「设置与脚本」：必须是脚本类型**且**填了脚本文件。</summary>
    private static bool QualifiesForShortcuts(QuickAction item)
        => string.Equals(item.Kind, QuickActionKind.Script, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(item.ScriptFile);

    /// <summary>
    /// 推断一条旧数据该属于哪一页：<c>Page</c> 有值就用它；没有（v1.32 及更早的单文件）按分类判断 ——
    /// 「打开」类是系统自带入口 → 快捷设置，其余（reg / powercfg / 网络 / 维护脚本）→ 设置与脚本。
    /// </summary>
    private static string InferPage(string? page, string? category)
    {
        if (string.Equals(page, QuickActionPage.Quick, StringComparison.OrdinalIgnoreCase)) return QuickActionPage.Quick;
        if (string.Equals(page, QuickActionPage.Shortcuts, StringComparison.OrdinalIgnoreCase)) return QuickActionPage.Shortcuts;
        return string.Equals((category ?? "").Trim(), "打开", StringComparison.Ordinal)
            ? QuickActionPage.Quick
            : QuickActionPage.Shortcuts;
    }

    private static List<QuickAction>? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var list = JsonSerializer.Deserialize<List<QuickAction>>(File.ReadAllText(path));
            return list ?? new List<QuickAction>();
        }
        catch { return null; }
    }

    /// <summary>旧单文件改名成 <c>quickactions.json.migrated</c>（不删除；改名后不会再进备份 / 加载）。</summary>
    private void TryRetireLegacyFile()
    {
        try
        {
            var target = LegacyFilePath + ".migrated";
            if (File.Exists(target)) File.Delete(target);
            File.Move(LegacyFilePath, target);
        }
        catch { /* 改不动就留着：它已经不参与加载，最多是多占几 KB */ }
    }

    /// <summary>
    /// 老文件 / 默认清单的字段回填：
    /// <c>Id</c> 缺失 → 生成；<c>Kind</c> 缺失 → 按页推断（快捷设置默认命令行命令、设置与脚本默认脚本）；
    /// <c>Page</c> 统一按它所在的文件写死（文件才是归属的真相）。
    /// </summary>
    private static bool Migrate(List<QuickAction> list, string page)
    {
        var migrated = false;
        foreach (var i in list)
        {
            if (string.IsNullOrEmpty(i.Id)) { i.Id = Guid.NewGuid().ToString("N"); migrated = true; }
            if (string.IsNullOrEmpty(i.Kind))
            {
                i.Kind = page == QuickActionPage.Quick ? QuickActionKind.Command : QuickActionKind.Script;
                migrated = true;
            }
            if (!string.Equals(i.Page, page, StringComparison.Ordinal)) { i.Page = page; migrated = true; }
        }
        return migrated;
    }

    /// <summary>
    /// 确保脚本目录存在（<see cref="ScriptsDirectory"/>）。
    /// <para>
    /// v3.60 §一 / NT-060：⛔ <b>不再复制示例脚本</b> —— 软件只提供默认路径，脚本内容由用户自己放 / 导入。
    /// </para>
    /// </summary>
    private void EnsureScriptsDirectory()
    {
        try
        {
            var dir = ScriptsDirectory;
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);
        }
        catch { /* 建目录失败不阻断启动：运行时会给出"找不到脚本文件"的明确提示 */ }
    }
}
