using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shell.Models;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

public partial class PathViewModel : ListPageViewModelBase, IStatusActionProvider
{

    /// <summary>
    /// 惰性探测外部程序（需求 §4.9.2 第 5 条）：**进入「运行环境」节时**由设置页触发 ——
    /// ⛔ 不在启动时全量探测（拖启动）。缺硬依赖（ffmpeg）或"配了但文件不在" ⇒ 底栏 `⚠`，
    /// 且这一行**可点击**（<see cref="StatusActionKey"/> 指回本节）方便就地改路径。
    /// </summary>
    public void ProbeExternalTools()
    {
        RefreshPythonRow();   // Python 共享环境与外部程序一起惰性探测（重建行文案，不动其它行）

        if (_ext == null) return;

        var all = _ext.ProbeAll();
        var attention = all.Where(s => s.NeedsAttention).ToList();

        // ⚠️ 先定好"可点击目标"，再改 StatusText（StatusChanged 触发时 Shell 会立刻读它）
        _externalWarn = attention.Count == 0
            ? null
            : $"⚠ 未找到 {string.Join(" / ", attention.Select(s => s.WarnText))}（点这里到「运行环境」填路径）";

        OnPropertyChanged(nameof(StatusActionKey));
        OnPropertyChanged(nameof(StatusActionTip));

        StatusText = _externalWarn ?? $"运行环境已探测：{all.Count} 个外部程序均可解析（留空 = 用系统默认）。";
    }

    /// <summary>
    /// 「Python 共享环境」行的说明文案：一句话说清现在用的是哪个解释器、共享 venv 建没建。
    /// <para>
    /// 🔴 未装 Python ⛔ 不阻断任何功能 —— 只有"跑 .py 脚本 / 抓取规则"才需要它，本行只是**如实告知**。
    /// </para>
    /// </summary>
    private string PythonEnvText()
    {
        if (_py == null) return "运行时服务未注册（配置错误）";

        try
        {
            var s = _py.Probe();
            if (!s.Ready)
                return "未找到 Python：装 3.10+ 或在本页填「Python 解释器」；建共享环境见「脚本管理器 → 环境维护」集合";

            var ver = s.Version.Length > 0 ? " · Python " + s.Version : "";
            return s.UsingVenv
                ? "已就绪（venv）" + ver + "；规则集 / .py 脚本用它跑"
                : "未建 venv，现用系统解释器" + ver + "：跑一次「环境维护 → 环境：准备 Python」建共享环境并装依赖";
        }
        catch { return "探测失败（见日志）"; }
    }

    /// <summary>刷新「Python 共享环境」行（**就地改值**，⛔ 不重建集合 —— 重建会丢选中行）。</summary>
    private void RefreshPythonRow()
    {
        var row = Items.FirstOrDefault(i => i.Key == "PythonRuntime");
        if (row == null) return;
        row.Value = _py?.ToolsRoot ?? "";
        row.Description = PythonEnvText();
    }

    /// <summary>状态栏 `⚠` 的跳转目标 = 本页（「运行环境」节）；没问题时为 null（不可点）。</summary>
    public string? StatusActionKey => _externalWarn == null ? null : "settings-path";

    public string StatusActionTip => _externalWarn == null
        ? ""
        : "打开「运行环境」节，填好缺失的外部程序路径（留空 = 用系统默认）";

    /// <summary>筛选快捷项：**分类**（路径 / 配置）+ **类型**（文件 / 目录 / 只读 / 配置档）。</summary>
    private void RebuildQuickFilters()
    {
        var items = new List<(string Label, string ColumnKey, string Value)>();

        items.AddRange(Items.Select(i => i.CategoryText)
                            .Where(c => c.Length > 0)
                            .Distinct(StringComparer.CurrentCultureIgnoreCase)
                            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
                            .Select(c => ("分类：" + c, "Category", c)));

        items.AddRange(Items.Select(i => i.KindText)
                            .Where(k => k.Length > 0)
                            .Distinct(StringComparer.CurrentCultureIgnoreCase)
                            .OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)
                            .Select(k => ("类型：" + k, "Kind", k)));

        SetQuickFilters(items);
    }

    // ==================== 行数据 ====================

}
