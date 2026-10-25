using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.SystemTools.ViewModels;

public partial class PackageViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    // ==================== 右键菜单 / 详情列的动作 ====================

    [RelayCommand]
    private async Task InstallAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;

        // 🔴 装完刷新会整表重建，`p` 就成了**游离实例**（已不在 Packages 里）⇒ 详情卡要手动回写。
        //    这里记住它在 _all 里的新实例，刷新后把「已安装」抄回 p 并发通知（否则按钮仍显示「⬇ 安装」）。
        var wantedId = p.Id;
        try
        {
            IsBusy = true;
            StatusText = $"正在安装 {p.Id} ……（较大的包会久一点，请勿关闭窗口）";
            var (ok, msg) = await _pkg.InstallAsync(p.Id, p.Source, SilentInstall);
            StatusText = msg;
            if (ok)
            {
                _installedIds = null;
                _installedList = null;
                await MarkInstalledAsync();
                ApplyView();
                SyncSelectedInstalledState(wantedId);
            }
        }
        finally { IsBusy = false; }
    }

    /// <summary>安装 / 卸载后用最新列表回写「详情卡当前引用的那个实例」的安装状态（见 <see cref="InstallAsync"/>）。</summary>
    private void SyncSelectedInstalledState(string id)
    {
        var fresh = _all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        var current = Selected;
        if (fresh == null || current == null) return;
        if (ReferenceEquals(fresh, current)) { NotifyActionVisibility(); return; }

        current.IsInstalled = fresh.IsInstalled;
        current.Available = fresh.Available;
        if (!string.IsNullOrWhiteSpace(fresh.Version) && !string.Equals(fresh.Version, current.Version, StringComparison.Ordinal))
            current.Version = fresh.Version;

        // ⚠️ 这些派生属性在**另一个实例**上变了 ⇒ 详情卡的渲染器订阅的是 VM（不是 PackageInfo），
        //    所以必须从 VM 侧补一次通知才能整体重解析。
        DetailChangedFromItem();
    }

    /// <summary>让详情卡整体重解析（渲染器订阅 VM 的 PropertyChanged）。</summary>
    private void DetailChangedFromItem() => OnPropertyChanged(nameof(DetailItem));

    [RelayCommand]
    private async Task UpgradeAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        if (!p.CanUpgrade)
        {
            StatusText = $"{p.Name} 已是最新版（当前 {p.Version}），无需升级。";
            return;
        }
        try
        {
            IsBusy = true;
            StatusText = $"正在升级 {p.Id} ……";
            var (ok, msg) = await _pkg.UpgradeAsync(p.Id, SilentInstall);
            StatusText = msg;
            if (ok && _mode == ListMode.Upgrades) await LoadUpgradesAsync();
            else
            {
                if (ok) { _installedIds = null; _installedList = null; await MarkInstalledAsync(); }
                ApplyView();
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在卸载 {p.Id} ……";
            // ⚠️ 二次确认在 IPackageManagerService 内部完成（宿主把钩子接到了模态对话框上，见 App.axaml.cs）
            var (ok, msg) = await _pkg.UninstallAsync(p.Id);
            StatusText = msg;
            if (ok) { _installedIds = null; _installedList = null; await RefreshAsync(); }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task PinAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.PinAsync(p.Id);
            StatusText = msg;
            if (ok) { await ApplyPinnedAsync(); ApplyView(); }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UnpinAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.UnpinAsync(p.Id);
            StatusText = msg;
            if (ok) { await ApplyPinnedAsync(); ApplyView(); }
        }
        finally { IsBusy = false; }
    }

    // ==================== 动词执行底座（模块内，⛔ 不改 Shared 的 IPackageManagerService） ====================

    /// <summary>
    /// 跑一条后端命令（命令型条目走 <c>cmd.exe /c</c>，⛔ 不经 pwsh ⇒ 无冷启动开销）。
    /// <para>用 Core 的 <see cref="ScriptRunner"/>而非给 <see cref="IPackageManagerService"/> 加方法 —— 后者在 Shared（框架层冻结）。</para>
    /// </summary>
    private async Task<ScriptRunResult> RunBackendAsync(string command, int timeoutSeconds = ScriptRunner.MaxTimeoutSeconds)
    {
        var runner = new ScriptRunner(_app);
        return await runner.RunCommandAsync(new CommandRunRequest
        {
            Command = command,
            Shell = QuickActionShell.Cmd,
            TimeoutSeconds = Math.Min(timeoutSeconds, ScriptRunner.MaxTimeoutSeconds),
        });
    }

    /// <summary>失败原因：超时 &gt; 进程错误 &gt; stderr 末行 &gt; 退出码。</summary>
    private static string ErrorOf(ScriptRunResult r)
    {
        if (r.TimedOut) return "执行超时";
        if (!string.IsNullOrWhiteSpace(r.Error)) return r.Error!;
        if (!string.IsNullOrWhiteSpace(r.StdErr)) return r.StdErr.Trim();
        return $"退出码 {r.ExitCode}";
    }

    /// <summary>命令参数加引号（包 Id 允许含空格 / 括号 —— 见 <c>IdLike</c>）；顺手去掉双引号防拼接被破。</summary>
    private static string QuoteArg(string s) => "\"" + (s ?? "").Replace("\"", "") + "\"";

    /// <summary>
    /// 后端可执行文件的**绝对路径**。
    /// <para>🔴 不能只写 <c>winget</c>：它是 **App Execution Alias**（<c>WindowsApps\winget.exe</c> 是 0 字节重解析点），
    /// <c>UseShellExecute=false</c> 的进程创建不解析别名 ⇒ 必须自己按 PATH 找出真实路径（<c>PackageManagerService.ResolveExe</c>
    /// 就是为这个存在的，它 private ⇒ 这里在模块内重一份）。</para>
    /// </summary>
    private string? _backendExe;

    private string BackendExe
    {
        get
        {
            if (_backendExe != null) return _backendExe;

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var full = Path.Combine(dir.Trim(), BackendExeName);
                    if (File.Exists(full)) return _backendExe = full;
                }
                catch { /* 非法路径段，跳过 */ }
            }

            var alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", BackendExeName);

            return _backendExe = File.Exists(alias) ? alias : BackendExeName;   // 兜底：交给宿主解析
        }
    }

    /// <summary>把命令模板里的占位符替换掉：<c>{exe}</c> → 后端绝对路径，<c>{id}</c> → 包 Id（都加引号）。</summary>
    private string ResolveCommand(string template, string? id = null)
    {
        var s = template.Replace("{exe}", QuoteArg(BackendExe));
        return id is null ? s : s.Replace("{id}", QuoteArg(id));
    }

    /// <summary>破坏性动作的二次确认（走 Core 的公共钩子 <see cref="DialogHooks.ConfirmAsync"/>，Shell 已注入模态框）。</summary>
    private static Task<bool> ConfirmAsync(string message) => DialogHooks.ConfirmAsync(message);

    // ==================== 新增动词：刷新索引 / 全部升级 / 卸载选中 ====================

    /// <summary>🔃 刷新索引（<see cref="CmdRefreshIndex"/>）：源元数据拉新，之后「可升级」才准。</summary>
    [RelayCommand]
    private async Task RefreshIndexAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = "正在刷新软件源索引……（首次会比较慢）";
            var r = await RunBackendAsync(ResolveCommand(CmdRefreshIndex));
            StatusText = r.Ok ? "软件源索引已刷新。" : "刷新索引失败：" + ErrorOf(r);
            if (r.Ok) await LoadExplicitSourcesAsync();      // 索引变了，显式源名单可能也变
        }
        finally { IsBusy = false; }
    }

    /// <summary>⬆⬆ 全部升级（<see cref="CmdUpgradeAll"/>）：**先整体确认一次**，再跑后端命令。</summary>
    [RelayCommand]
    private async Task UpgradeAllAsync()
    {
        if (IsBusy) return;
        if (!await ConfirmAsync("确定把全部可升级的包升到最新版吗？\n\n· 已锁定（暂停更新）的包会被跳过\n· 过程可能持续很久，请勿关闭窗口"))
        {
            StatusText = "已取消「全部升级」。";
            return;
        }
        try
        {
            IsBusy = true;
            StatusText = "正在升级全部包……（可能持续很久，请勿关闭窗口）";
            var cmd = ResolveCommand(SilentInstall ? CmdUpgradeAll + ArgSilent : CmdUpgradeAll);
            var r = await RunBackendAsync(cmd);
            StatusText = r.Ok ? "全部升级已执行完成。" : "全部升级失败：" + ErrorOf(r);

            _installedIds = null;
            _installedList = null;
            await MarkInstalledAsync();
            ApplyView();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 🗑 卸载选中（<see cref="CmdUninstall"/> 逐条跑）。
    /// <para>⚠️ 刻意<b>不走</b> <see cref="IPackageManagerService.UninstallAsync"/>：那个方法内部每次都弹二次确认，
    /// 批量 N 个就要弹 N 次。这里整体确认一次，再逐条跑命令。</para>
    /// </summary>
    [RelayCommand]
    private async Task UninstallCheckedAsync()
    {
        var targets = _all.Where(p => p.IsChecked).ToList();
        if (targets.Count == 0) { StatusText = "先勾选要卸载的包（行首勾选框）。"; return; }
        if (IsBusy) return;
        if (!await ConfirmAsync($"确定卸载勾选的 {targets.Count} 个包吗？此操作不可撤销。"))
        {
            StatusText = "已取消卸载。";
            return;
        }
        try
        {
            IsBusy = true;
            var fails = new List<string>();
            for (var i = 0; i < targets.Count; i++)
            {
                var p = targets[i];
                StatusText = $"正在卸载 {i + 1}/{targets.Count}：{p.Id} ……";
                var r = await RunBackendAsync(ResolveCommand(CmdUninstall, p.Id));
                if (!r.Ok) fails.Add($"{p.Id}（{ErrorOf(r)}）");
            }

            StatusText = fails.Count == 0
                ? $"已卸载 {targets.Count} 个包。"
                : $"卸载完成：{targets.Count - fails.Count} 成功 / {fails.Count} 失败 —— " + string.Join("、", fails);

            _installedIds = null;
            _installedList = null;
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

}
