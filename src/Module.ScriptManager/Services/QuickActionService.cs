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
using NewToys.ScriptManager.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <see cref="IQuickActionService"/> 实现：两个页面 = 两个配置文件。
/// <list type="bullet">
///   <item>「脚本库」→ <c>&lt;应用根&gt;\config\script\script.json</c>（有脚本文件的脚本）；</item>
///   <item>「命令库」→ <c>&lt;应用根&gt;\config\script\script.json</c>（命令行命令）。</item>
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
    private readonly IScriptManagerSettingsService? _pc;
    /// <summary>应用级配置（v3.65 / NT-062）：pwsh 路径已回迁为「运行环境」= 应用级。</summary>
    private readonly IAppConfigService? _appCfg;
    /// <summary>手工数据层（NT-068 S3）：脚本库 / 命令库的条目真身已切到 <c>config\script\script.json</c>。</summary>
    private readonly IScriptStoreService? _store;

    private readonly List<QuickAction> _shortcuts = new();
    private readonly List<QuickAction> _quick = new();

    /// <summary>删除前的二次确认钩子（宿主注入；为 null 时直接删除）。</summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    public QuickActionService(ConfigStoreService cfg, IScriptManagerSettingsService pc, IScriptStoreService store,
        IAppConfigService? app = null)
    {
        _cfg = cfg;
        _pc = pc;
        _store = store;
        _appCfg = app;
        var needPersist = Load();
        EnsureScriptsDirectory();
        // 首次运行 / 归一化：
        if (needPersist) Persist();
    }

    // ==================== 读取 ====================

    public IReadOnlyList<QuickAction> Items => _shortcuts.Concat(_quick).ToList();
    public IReadOnlyList<QuickAction> Shortcuts => _shortcuts;
    public IReadOnlyList<QuickAction> QuickSettings => _quick;
    public IReadOnlyList<QuickAction> Favorites => _shortcuts.Concat(_quick).Where(i => i.IsFavorite).ToList();

    /// <summary>手工数据文件全路径（<c>config\script\script.json</c>）：两个库页共用同一份（NT-068 S3 切库后）。</summary>
    public string ScriptsFilePath => _store?.FilePath ?? Path.Combine(_cfg.ConfigDir, "script", "script.json");

    /// <summary>与 <see cref="ScriptsFilePath"/> 相同 —— 命令库与脚本库已合并到同一个数据文件。</summary>
    public string QuickFilePath => ScriptsFilePath;

    /// <summary>脚本目录（生效值）：v3.64 起取自本模块设置（<c>config\script-manager.json</c>，NT-061）；配置为空时用 <c>&lt;应用根&gt;\scripts</c>。</summary>
    public string ScriptsDirectory => _pc?.EffectiveScriptsFolder ?? Path.Combine(AppContext.BaseDirectory, "scripts");

    public event Action? Changed;

    /// <summary>手动扫描目录（NT-068 §三.6）：只产出候选，⛔ 不自动入库。</summary>
    public IReadOnlyList<ScriptCandidate> Scan(string directory, bool includeSubFolders)
        => ScriptScanner.Scan(directory, includeSubFolders, _store?.Data.Items ?? Enumerable.Empty<ScriptItem>());

    // ==================== 增删改 ====================

    public QuickAction Add(string page)
    {
        var quick = page == QuickActionPage.Quick;
        var item = new QuickAction
        {
            Id = Guid.NewGuid().ToString("N"),
            Page = quick ? QuickActionPage.Quick : QuickActionPage.Shortcuts,
            // 页面默认类型：命令库页加的是命令行命令，脚本库页加的是脚本文件（可在详情里改回）。
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
    /// 目标页是「脚本库」时顺带把类型标成脚本（否则它下次又会被移回命令库）。
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
            item.Kind = QuickActionKind.Command;   // 没有脚本文件又放到命令库 → 就是一行命令
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

        // 「命令库 / 脚本库」的定位 = 把常用命令 / 脚本攒起来、点一下就跑、还能收藏。
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

    /// <summary>本机能不能拿到 pwsh（PowerShell 7+）：配置里填了用配置（存在才用），否则探常见安装位、再退回 PATH 里的 pwsh。
    /// <para>v3.65 / NT-062：读的值来自**应用级**「运行环境」（<c>app.json</c>，与「设置 → 路径与配置」同一份）。</para></summary>
    private bool TryGetPwsh(out string exe)
    {
        var cfg = (_appCfg?.PwshPath ?? "").Trim();
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

    /// <summary>把两个内存镜像回写到 <c>script.json</c>（NT-068 S3 起两个库页共用这一份数据文件）。</summary>
    private void Persist()
    {
        try
        {
            if (_store != null)
            {
                var root = _store.Data;
                var keep = new HashSet<string>(_shortcuts.Concat(_quick).Select(i => i.Id));

                // ① 内存里已没有的（只清本服务管的三种角色）⇒ 从数据层删掉；package 等其它角色不动。
                root.Items.RemoveAll(i =>
                    (i.Role is ScriptItemRole.Script or ScriptItemRole.Command or ScriptItemRole.Collector)
                    && !keep.Contains(i.Id));

                // ② 逐条 upsert（⛔ 不动数据层里 UI 改不到的字段：集合归属 / 列 schema / 保留策略 …）
                foreach (var q in _shortcuts.Concat(_quick))
                {
                    var si = root.Items.FirstOrDefault(i => i.Id == q.Id);
                    if (si == null)
                    {
                        si = new ScriptItem { Id = string.IsNullOrEmpty(q.Id) ? Guid.NewGuid().ToString("N") : q.Id };
                        q.Id = si.Id;
                        root.Items.Add(si);
                    }
                    ApplyTo(si, q);
                }

                // ③ 内置集合成员跟上（脚本库 / 命令库 = 全量清单，只读关系）
                _store.SyncBuiltinCollections();
                _store.Save();
            }
        }
        catch { /* 保存失败不打断 UI；下一次改动会再试 */ }
        Changed?.Invoke();
    }

    /// <summary>
    /// 从 <c>script.json</c> 投影出两个库页的内存镜像（NT-068 S3 切库）。
    /// <para>
    /// 🔴 旧的两份 JSON（<c>pc-scripts.json</c> / <c>pc-quick.json</c>）与更早的 <c>quickactions.json</c>
    /// <b>静默忽略</b>：不读、不写、不删（卡 §四「不做旧数据迁移」）。
    /// </para>
    /// </summary>
    /// <returns>恒为 <c>false</c>：判定结果的变化由 <see cref="Load"/> 自己落盘。</returns>
    private bool Load()
    {
        _shortcuts.Clear();
        _quick.Clear();
        if (_store == null) return false;

        var dirty = false;
        foreach (var si in _store.Data.Items)
        {
            // 可采集判定是派生值：每次加载重算（用户覆盖过的行由 Apply 自己跳过）。
            if (CollectableEvaluator.Apply(si, ScriptsDirectory)) dirty = true;

            switch (si.Role)
            {
                case ScriptItemRole.Command:
                    _quick.Add(From(si, QuickActionPage.Quick));
                    break;
                case ScriptItemRole.Script:
                case ScriptItemRole.Collector:
                    _shortcuts.Add(From(si, QuickActionPage.Shortcuts));
                    break;
            }
        }

        // 内置集合（脚本库 / 命令库）的成员 = 全量清单（只读关系，卡 §三.7）。
        if (_store.SyncBuiltinCollections()) dirty = true;

        if (dirty) _store.Save();
        return false;
    }

    /// <summary>数据层条目 → 库页条目（UI 模型）。</summary>
    private static QuickAction From(ScriptItem si, string page) => new()
    {
        Id = si.Id,
        Page = page,
        Kind = si.Role == ScriptItemRole.Command ? QuickActionKind.Command : QuickActionKind.Script,
        Title = si.Title,
        Category = si.Category,
        Description = si.Description,
        Command = si.Role == ScriptItemRole.Command ? si.Target : "",
        ScriptFile = si.Role == ScriptItemRole.Command ? "" : si.Target,
        Interpreter = si.Interpreter,
        NeedAdmin = si.NeedAdmin,
        IsFavorite = si.Favorite,
        RunHidden = si.RunHidden,
        Shell = QuickActionShell.Normalize(si.Shell),
        Collectable = si.Collectable,
        IneligibleReason = si.IneligibleReason,
    };

    /// <summary>
    /// 库页条目 → 数据层条目：<b>只覆盖 UI 能改的字段</b>；
    /// 集合归属 / 列 schema / 保留策略 / <c>Readonly</c> 等保持数据层原值（那些由集合页与信息面板维护）。
    /// </summary>
    private void ApplyTo(ScriptItem si, QuickAction q)
    {
        // 归属只由「所在页 + 执行类型」决定（⛔ 不看 ScriptFile 是否已填）：
        // ① Move() 把有脚本文件的条目移到命令库时 Kind 仍是 Script ⇒ 只看 Kind 会被打回脚本库；
        // ② 用户点「➕ 新增」刚建的脚本条目还没填文件 ⇒ 看 ScriptFile 会当场跳去命令库。
        var isScript = q.Page != QuickActionPage.Quick
                       && string.Equals(q.Kind, QuickActionKind.Script, StringComparison.OrdinalIgnoreCase);

        si.Role = isScript ? ScriptItemRole.Script : ScriptItemRole.Command;
        si.Title = q.Title ?? "";
        si.Category = q.Category ?? "";
        si.Description = q.Description ?? "";
        si.Target = isScript ? (q.ScriptFile ?? "") : (q.Command ?? "");
        si.Interpreter = q.Interpreter ?? "";
        si.NeedAdmin = q.NeedAdmin;
        si.Favorite = q.IsFavorite;
        si.RunHidden = q.RunHidden;
        si.Shell = QuickActionShell.Normalize(q.Shell);

        // 目标变了 ⇒ 可采集重新判定（用户覆盖过的行不动）。
        CollectableEvaluator.Apply(si, ScriptsDirectory);
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
