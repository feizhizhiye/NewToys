using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Settings.Models;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 「设置 → 路径配置」页 —— v1.34 起是一张**列表**（布局仿「电脑工具 → 快捷设置」，规范 3.6）：
/// 左边可排序表头 + 列表 + 行内「📂 浏览 / ↗ 打开」，右边选中项的详情卡（改值 → 💾 保存），
/// 表头最右侧还有一个无文字小按钮，点一下就展开详情（并自动选中第一项）。
/// <para>
/// 本页**保留「💾 保存」**（规范 3.5 的分界）：路径是自由文本的复杂输入，边打边落盘没有意义，
/// 也可能把半截路径写进配置。例外是「📂 浏览…」选出来的路径 —— 那是明确的动作，**选完立即生效**。
/// </para>
/// <para>
    /// ⚠️ v1.34 曾去掉"视频库默认目录"；v3.15 重新引入为**「导入导出默认目录」**（全局，覆盖视频库 .db 与批量任务 .json 的导入/导出对话框定位），
    /// 不回写扫描目录，不影响"每次手动选目录"的扫描逻辑。
    /// </para>
/// </summary>
public partial class PathViewModel : ListPageViewModelBase
{
    private readonly IAppConfigService? _cfg;

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _cfg?.VideoDbFolder ?? "";
    private readonly ConfigStoreService? _store;

    private bool _syncing;

    public ObservableCollection<PathSettingItem> Items { get; } = new();

    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private PathSettingItem? _selected;

    public bool IsNotBusy => !IsBusy;

    // ==================== 表头 ====================

    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _valueHeader = "当前值";
    [ObservableProperty] private string _kindHeader = "类型";
    [ObservableProperty] private string _descriptionHeader = "说明";

    // ==================== 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase（v1.35 收口） ====================
    //
    // XAML 只能绑具名属性 → 每列给一个直通属性（真正的状态在基类里，落 config\columnlayout.json）。

    public bool IsNameVisible => Vis("Name");
    public bool IsValueVisible => Vis("Value");
    public bool IsKindVisible => Vis("Kind");
    public bool IsDescriptionVisible => Vis("Description");
    public bool IsActionVisible => Vis("Action");

    protected override string PageKey => "settings-path";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；最后一列 Detail 是「▤」按钮的固定窄列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // v3.32 §4.8.3：每列给一条示例值（空态水印行 + 列宽判定）；「操作」是按钮列 → 不给
        new("Name", "名称", "2.2*", sample: "ffmpeg 路径"),
        new("Value", "当前值", "5*", sample: @"C:\ffmpeg\bin\ffmpeg.exe"),
        new("Kind", "类型", "1*", sample: "文件"),
        new("Description", "说明", "4*", sortable: false, sample: "视频的媒体信息用它旁边的 ffprobe 读取"),
        new("Action", "操作", "1.8*", sortable: false),
    };

    // ==================== 详情栏开合（基类管状态；这里只补"没选中就选第一项"） ====================

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    // ==================== v3.32 §4.8.3：列表示例行 + 详情卡示例态 ====================

    /// <summary>详情卡 / 列表示例行用的**示例项**。</summary>
    private static readonly PathSettingItem SampleItem = new()
    {
        Key = "FfmpegPath",
        Name = "ffmpeg 路径",
        Kind = PathItemKind.File,
        Description = "视频的媒体信息（时长 / 分辨率 / 编码 / 码率）用它旁边的 ffprobe 读取（示例数据）",
        Value = @"C:\ffmpeg\bin\ffmpeg.exe"
    };

    /// <summary>列表为空时显示**示例行**（⛔ 不进 <c>Items</c>、不参与统计 / 筛选 / 排序）。</summary>
    public bool ShowSampleRow => HasSampleRow && Items.Count == 0;

    /// <summary>详情卡的数据源：有选中项用真值，没选中用**示例项**。</summary>
    public PathSettingItem DetailItem => Selected ?? SampleItem;

    /// <summary>详情卡正文是否显示（有选中项，或处于示例态）。</summary>
    public bool ShowDetailBody => HasSelection || IsDetailSample;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    // ==================== 编辑缓冲 ====================

    /// <summary>详情卡里正在编辑的值（点「💾 保存」才写回对象并落盘）。</summary>
    [ObservableProperty] private string _editValue = "";

    partial void OnSelectedChanged(PathSettingItem? value)
    {
        UpdateDetailSample(value != null);   // v3.32 §4.8.3：没选中项 → 详情卡改用示例项
        OnPropertyChanged(nameof(DetailItem));
        _syncing = true;
        EditValue = (value ?? (HasSampleRow ? SampleItem : null))?.Value ?? "";
        _syncing = false;
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnEditValueChanged(string value)
    {
        // 绑定到 TextBox 时不要反过来打扰输入；这里只是把"是否改过"反映到提示上
        if (!_syncing) StatusText = "改完点「💾 保存」写入 app.json。";
    }

    // ==================== 构造 ====================

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PathViewModel() { }

    public PathViewModel(IAppConfigService cfg, ConfigStoreService store, IColumnLayoutService cols)
        : base(cols)
    {
        _cfg = cfg;
        _store = store;

        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        BuildItems();
        RebuildQuickFilters();            // v3.21 §4.8.2：类型（文件 / 目录 / 只读）进表头右键「筛选」
        ApplyView();
        UpdateDetailSample(false);        // v3.32 §4.8.3：初始没有选中项 → 详情卡进"示例态"
        EditValue = SampleItem.Value;
    }

    private void RebuildQuickFilters()
        => SetQuickFilters(Items.Select(i => i.KindText)
                                .Where(k => k.Length > 0)
                                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                                .OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)
                                .Select(k => (Label: "类型：" + k, ColumnKey: "Kind", Value: k)));

    private void BuildItems()
    {
        Items.Clear();
        if (_cfg == null) return;

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
        Items.Add(new PathSettingItem
        {
            Key = "PythonPath", Name = "Python 路径", Kind = PathItemKind.File,
            Description = "「设置与脚本」里 .py 的解释器；留空 = 用 PATH 里的 python",
            Value = _cfg.PythonPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PwshPath", Name = "PowerShell 7+（pwsh）路径", Kind = PathItemKind.File,
            Description = ".ps1 优先用它；留空 = 用 PATH 里的 pwsh，找不到则退回系统自带的 5.1",
            Value = _cfg.PwshPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PowerShell51Path", Name = "Windows PowerShell 5.1 路径", Kind = PathItemKind.File,
            Description = "没配 pwsh 时 .ps1 用它；留空 = 系统自带的 powershell.exe",
            Value = _cfg.PowerShell51Path
        });
        Items.Add(new PathSettingItem
        {
            Key = "ScriptFolder", Name = "脚本目录", Kind = PathItemKind.Folder,
            DefaultValue = _cfg.DefaultScriptFolder,
            Description = "「设置与脚本」页引用的 .py / .ps1 / .bat 都放这里；首次运行会把示例脚本复制进去",
            Value = _cfg.ScriptFolder
        });
        Items.Add(new PathSettingItem
        {
            Key = "VideoDbFolder", Name = "导入导出默认目录", Kind = PathItemKind.Folder,
            Description = "批量任务 .json（及媒体资产库文件）的导入/导出对话框打开时直接定位到这里；留空 = 用系统默认",
            Value = _cfg.VideoDbFolder
        });
        Items.Add(new PathSettingItem
        {
            Key = "DataDir", Name = "数据目录", Kind = PathItemKind.ReadOnly,
            Description = "本地数据库 newtoys.db（视频媒体信息缓存）等数据放这里；由应用决定，可用环境变量 NEWTOYS_HOME 整根切换",
            Value = _cfg.DataDirectory
        });
        Items.Add(new PathSettingItem
        {
            Key = "ConfigDir", Name = "配置目录", Kind = PathItemKind.ReadOnly,
            Description = "app.json / launch.json / theme.json / pc-quick.json / pc-scripts.json / columnlayout.json 等都在这里，可整体备份",
            Value = _store?.ConfigDir ?? ""
        });
    }

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    /// <summary>筛选取值（多项筛选用）。</summary>
    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
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
        OnPropertyChanged(nameof(ShowSampleRow));   // v3.32：有真实数据后示例行消失
        UpdateDetailSample(Selected != null);
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        ValueHeader = "当前值" + SortArrow("Value");
        KindHeader = "类型" + SortArrow("Kind");
    }

    // ==================== 增删改查 ====================

    /// <summary>💾 保存：把当前选中项的值写回 <c>app.json</c>。</summary>
    [RelayCommand]
    private void Save()
    {
        var item = Selected;
        if (item == null || _cfg == null) { StatusText = "先在左侧选中一项。"; return; }

        if (item.IsReadOnly) { StatusText = $"「{item.Name}」是只读项，不能改。"; return; }

        item.Value = (EditValue ?? "").Trim();
        WriteBack();
        StatusText = $"已保存「{item.Name}」→ {item.ValueDisplay}（下一轮启动 / 调用即生效）";
    }

    /// <summary>↩ 还原默认：只**预填**默认值，还要点「💾 保存」才生效（规范 3.5）。</summary>
    [RelayCommand]
    private void RestoreDefault()
    {
        var item = Selected;
        if (item == null) { StatusText = "先在左侧选中一项。"; return; }
        if (item.IsReadOnly) { StatusText = "只读项没有默认值可还原。"; return; }

        EditValue = item.DefaultValue;
        StatusText = item.DefaultValue.Length > 0
            ? $"已预填默认值「{item.DefaultValue}」，点「💾 保存」生效。"
            : "默认值就是留空（用系统默认），点「💾 保存」生效。";
    }

    /// <summary>
    /// 「📂 浏览…」选完之后由 View 的 code-behind 调用：选路径是**明确的动作** → 立即生效并落盘
    /// （不用再点保存；这也避免了"选了却没生效"的困惑）。
    /// </summary>
    public void ApplyPickedValue(PathSettingItem item, string path)
    {
        if (item == null || item.IsReadOnly || string.IsNullOrWhiteSpace(path)) return;

        item.Value = path.Trim();
        if (ReferenceEquals(item, Selected)) EditValue = item.Value;
        WriteBack();
        StatusText = $"已设置「{item.Name}」→ {item.Value}";
    }

    /// <summary>↗ 打开：目录项打开目录；文件项在资源管理器里定位（文件不存在就退到它的上级目录）。
    /// 不传参就用当前选中项（行内按钮 / 详情卡按钮都可用）。</summary>
    [RelayCommand]
    private void Open(PathSettingItem? item)
    {
        var target = item ?? Selected;
        if (target == null) { StatusText = "先在左侧选中一项。"; return; }

        var value = (target.Value.Length > 0 ? target.Value : target.DefaultValue).Trim();
        if (value.Length == 0) { StatusText = "这一项还是空的，没有可打开的位置。"; return; }

        try
        {
            if (target.Kind == PathItemKind.Folder || Directory.Exists(value))
            {
                Directory.CreateDirectory(value);
                Process.Start(new ProcessStartInfo("explorer.exe", value) { UseShellExecute = true });
            }
            else if (File.Exists(value))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{value}\"") { UseShellExecute = true });
            }
            else
            {
                var dir = Path.GetDirectoryName(value) ?? "";
                if (dir.Length == 0 || !Directory.Exists(dir)) { StatusText = $"找不到这个位置：{value}"; return; }
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            StatusText = "已打开：" + value;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenConfigDir()
    {
        try
        {
            var dir = _store?.ConfigDir ?? Path.Combine(AppContext.BaseDirectory, "config");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch { StatusText = "无法打开配置目录。"; }
    }

    /// <summary>把列表里每个可编辑项的值写回 <see cref="IAppConfigService"/>（每个 setter 各自落盘，这里收口存储根）。</summary>
    private void WriteBack()
    {
        if (_cfg == null) return;
        foreach (var it in Items)
        {
            var v = it.Value.Trim();
            switch (it.Key)
            {
                case "FfmpegPath": _cfg.FfmpegPath = v; break;
                case "PlayerPath": _cfg.PlayerPath = v; break;
                case "PythonPath": _cfg.PythonPath = v; break;
                case "PwshPath": _cfg.PwshPath = v; break;
                case "PowerShell51Path": _cfg.PowerShell51Path = v; break;
                case "ScriptFolder": _cfg.ScriptFolder = v; break;
                case "VideoDbFolder": _cfg.VideoDbFolder = v; break;
                // DataDir / ConfigDir 是只读展示项，不写
            }
        }
        _cfg.Save();
    }

    /// <summary>脚本目录的默认值（详情卡提示用）。</summary>
    public string DefaultScriptFolder => _cfg?.DefaultScriptFolder ?? "";
}
