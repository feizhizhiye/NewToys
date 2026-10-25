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

    /// <summary>重建整张表：**路径行**（app.json）+ **配置行**（profiles\）。</summary>
    private void BuildItems()
    {
        Items.Clear();
        BuildPathRows();
        BuildProfileRows();
    }

    private void BuildPathRows()
    {
        if (_cfg == null) return;

        // v3.64：下面两者是**跨模块共用的全局项** ⇒ 保持应用级、⛔ 不下沉。
        //   ffmpeg：媒体资产（缩略图抽帧）与视频工具都用同一份；外部播放器：视频列表「▶ 播放」用。
        Items.Add(new PathSettingItem
        {
            Key = "FfmpegPath", Name = "ffmpeg 路径", Kind = PathItemKind.File, DefaultValue = "ffmpeg",
            Description = "视频的媒体信息（时长 / 分辨率 / 编码 / 码率）用它旁边的 ffprobe 读取；留空或写 ffmpeg 则走 PATH",
            Value = _cfg.FfmpegPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PlayerPath", Name = "外部播放器路径", Kind = PathItemKind.File,
            Description = "视频「▶ 播放」用它打开；留空 = 系统默认关联程序",
            Value = _cfg.PlayerPath
        });
        // v3.65：三个解释器路径**回迁应用级**（判据 = 语义：「怎么跑外部程序」= 运行环境 ⇒ 应用级路由，
        // ⛔ 不随配置档切换）。它们曾于 下沉到「脚本管理器设置」，现回到本页（唯一写入点），落 app.json。
        // ⚠️ 脚本目录仍归「脚本管理器设置」（模块级，config\script-manager.json）。
        Items.Add(new PathSettingItem
        {
            Key = "PythonPath", Name = "Python 解释器", Kind = PathItemKind.File,
            Description = "「设置与脚本」里 .py 脚本的解释器；留空 = 用 PATH 里的 python",
            Value = _cfg.PythonPath
        });
        // Python **共享环境**（venv）状态行 —— 只读展示，↗ 打开工具根目录。
        // 🔴 「当前值」= 目录（供 ↗ 使用），环境状态写进「说明」列；进本节时由 ProbeExternalTools() 刷新。
        Items.Add(new PathSettingItem
        {
            Key = "PythonRuntime", Name = "Python 共享环境（venv）", Kind = PathItemKind.ReadOnly,
            Description = PythonEnvText(),
            Value = _py?.ToolsRoot ?? ""
        });
        Items.Add(new PathSettingItem
        {
            Key = "PwshPath", Name = "PowerShell 7+（pwsh）", Kind = PathItemKind.File,
            Description = ".ps1 优先用它；留空 = 优先探常见安装位、再退回 PATH 里的 pwsh",
            Value = _cfg.PwshPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PowerShell51Path", Name = "Windows PowerShell 5.1", Kind = PathItemKind.File,
            Description = "没配 pwsh 时 .ps1 用它；留空 = 用系统自带的 powershell.exe",
            Value = _cfg.PowerShell51Path
        });
        // 「导入导出默认目录」可编辑项**已删除** —— 它原来把某个 profile 的绝对路径写进
        // 跨配置共享的 app.json（切配置后指向别人的目录）。现在一律由「数据目录」推导，
        // 也就没有再让用户手填的必要。
        Items.Add(new PathSettingItem
        {
            Key = "DataDir", Name = "数据目录", Kind = PathItemKind.ReadOnly,
            Description = "本地数据库 newtoys.db（视频媒体信息缓存）等数据放这里；由应用决定，可用环境变量 NEWTOYS_HOME 整根切换",
            Value = _cfg.DataDirectory
        });
        // v3.65：app.json 已上提到**应用根**（与 profiles\ 同级）—— 它是应用级「运行环境」，
        // ⛔ 不再随配置档切换，也不再位于 <配置根>\config。这里如实展示新位置（只读）。
        Items.Add(new PathSettingItem
        {
            Key = "AppConfigFile", Name = "app.json（应用级配置）", Kind = PathItemKind.ReadOnly,
            Description = "本页这些行的落盘文件；v3.65 起放在应用根（profiles 的同级），多配置共享同一份",
            Value = _cfg.ConfigFilePath
        });
        Items.Add(new PathSettingItem
        {
            Key = "ConfigDir", Name = "配置目录", Kind = PathItemKind.ReadOnly,
            Description = "本配置档的 config 目录：launch.json / theme.json / script-manager.json / pc-quick.json / pc-scripts.json / columnlayout.json 等都在这里，可整体备份",
            Value = _store?.ConfigDir ?? ""
        });
    }

    /// <summary>
    /// 配置行（v3.53：由「设置 → 配置」页并入）：`Kind` 借只读展示位（它的「当前值」= 数据目录，
    /// 只读展示），⛔ 不新增行类型。
    /// </summary>
    private void BuildProfileRows()
    {
        if (_profiles == null) return;

        var active = _profiles.ActiveProfile;
        foreach (var name in _profiles.Profiles)
        {
            var isActive = string.Equals(name, active, StringComparison.OrdinalIgnoreCase);
            Items.Add(new PathSettingItem
            {
                Key = "profile:" + name,
                Category = SettingCategory.Profile,
                Kind = PathItemKind.ReadOnly,
                Name = name,
                Value = _profiles.GetDirectory(name),
                IsActiveProfile = isActive,
                Description = isActive
                    ? "当前窗口正在使用的配置（切换 = 重启本窗口；⭐ 设为默认 = 以后不带参数启动用它）"
                    : "一套独立的 config / data / logs，可切换 / 新开窗口 / 设为默认 / 重命名 / 删除"
            });
        }
    }

    /// <summary>默认选中「正在使用」的配置行（与并入前的「设置 → 配置」页一致）；没有就选第一行。</summary>
    private void SelectActiveProfile()
    {
        Selected = Items.FirstOrDefault(i => i.IsProfileRow && i.IsActiveProfile)
            ?? (Items.Count > 0 ? Items[0] : null);
    }

    /// <summary>配置侧动作（新建 / 重命名 / 删除 / 切换 / 设为默认）之后重建配置行并保持"选中当前配置"。</summary>
    private void RefreshProfiles()
    {
        BuildItems();
        RebuildQuickFilters();
        ApplyView();
        SelectActiveProfile();
        OnPropertyChanged(nameof(HasItems));
    }

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    /// <summary>筛选取值（多项筛选用）。</summary>
    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Category" => ((PathSettingItem)row).CategoryText,
        "Name" => ((PathSettingItem)row).Name,
        "Value" => ((PathSettingItem)row).ValueDisplay,
        "Kind" => ((PathSettingItem)row).KindText,
        "Description" => ((PathSettingItem)row).Description,
        _ => null
    };

    private void ApplyView()
    {
        IEnumerable<PathSettingItem> q = Items;

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            // v3.53：分类是"分组列"—— 同分类聚在一起，组内再按名称（否则同类行会被名称打散）
            "Category" => SortDescending
                ? q.OrderByDescending(i => i.CategoryText, byText).ThenByDescending(i => i.Name, byText)
                : q.OrderBy(i => i.CategoryText, byText).ThenBy(i => i.Name, byText),
            "Value" => SortDescending ? q.OrderByDescending(i => i.Value, byText) : q.OrderBy(i => i.Value, byText),
            "Kind" => SortDescending ? q.OrderByDescending(i => i.KindText, byText) : q.OrderBy(i => i.KindText, byText),
            _ => SortDescending ? q.OrderByDescending(i => i.Name, byText) : q.OrderBy(i => i.Name, byText)
        };

        var ordered = q.ToList();
        Items.Clear();
        foreach (var i in ordered) Items.Add(i);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders()
    {
        CategoryHeader = "分类" + SortArrow("Category");
        NameHeader = "名称" + SortArrow("Name");
        ValueHeader = "当前值" + SortArrow("Value");
        KindHeader = "类型" + SortArrow("Kind");
    }

    // ==================== 路径侧增删改查 ====================

}
