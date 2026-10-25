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

    /// <summary>选中行变化 / 列表重建后，把上面这批"可用性 + 提示词"整体通知一遍（工具条按钮绑的就是它们）。</summary>
    private void RaiseActionStates()
    {
        OnPropertyChanged(nameof(IsPathRowSelected));
        OnPropertyChanged(nameof(IsProfileRowSelected));
        OnPropertyChanged(nameof(CanSavePath));
        OnPropertyChanged(nameof(CanSwitchProfile));
        OnPropertyChanged(nameof(CanDeleteProfile));

        OnPropertyChanged(nameof(SaveTip));
        OnPropertyChanged(nameof(BrowseTip));
        OnPropertyChanged(nameof(OpenTip));
        OnPropertyChanged(nameof(RestoreTip));
        OnPropertyChanged(nameof(SwitchTip));
        OnPropertyChanged(nameof(LaunchTip));
        OnPropertyChanged(nameof(DefaultTip));
        OnPropertyChanged(nameof(RenameTip));
        OnPropertyChanged(nameof(DeleteTip));
    }

    // ==================== 工具条（v3.66 第五轮起由**设置页「运行环境」节**的节点内嵌 ToolbarHost 渲染） ====================

    /// <summary>
    /// 工具条描述符（按优先级顺序）：前四个是**路径**侧工具，中间是共用的目录 / 新建，后面是**配置**侧工具。
    /// 🔴 启停全部落在 <c>IsEnabledPath</c> 上（选中另一分类 ⇒ 置灰 + <c>ToolTipPath</c> 说明原因）。
    /// <para>⚠️ 渲染位置 = 宿主节点（节点抬头下方、正文之上），⛔ 不再上抛到 Shell 的页面顶部工具条行。</para>
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📂 打开配置目录", CommandPath = nameof(OpenConfigDirCommand),
                ToolTip = "打开本配置档的 config 目录（launch.json / theme.json / 模块设置等；可整体备份）" },
        new() { Text = "💾 保存", Classes = "ghost", CommandPath = nameof(SaveCommand),
                IsEnabledPath = nameof(CanSavePath), ToolTipPath = nameof(SaveTip), ShowToolTipOnDisabled = true },
        new() { Text = "📂 浏览…", CommandPath = nameof(PickPathCommand),
                IsEnabledPath = nameof(CanSavePath), ToolTipPath = nameof(BrowseTip), ShowToolTipOnDisabled = true },
        new() { Text = "↗ 打开位置", CommandPath = nameof(OpenCommand),
                IsEnabledPath = nameof(IsPathRowSelected), ToolTipPath = nameof(OpenTip), ShowToolTipOnDisabled = true },
        new() { Text = "↩ 还原默认", CommandPath = nameof(RestoreDefaultCommand),
                IsEnabledPath = nameof(CanSavePath), ToolTipPath = nameof(RestoreTip), ShowToolTipOnDisabled = true },

        new() { Kind = ToolbarItemKind.Separator },

        new() { Text = "＋ 新建配置", CommandPath = nameof(NewProfileCommand),
                ToolTip = "新建一套独立的 config / data / logs（切换过去后从空白开始）" },
        new() { Text = "⇄ 切换", CommandPath = nameof(SwitchProfileCommand),
                IsEnabledPath = nameof(CanSwitchProfile), ToolTipPath = nameof(SwitchTip), ShowToolTipOnDisabled = true },
        new() { Text = "🚀 新开", CommandPath = nameof(LaunchProfileNewCommand),
                IsEnabledPath = nameof(CanSwitchProfile), ToolTipPath = nameof(LaunchTip), ShowToolTipOnDisabled = true },
        new() { Text = "⭐ 设为默认", CommandPath = nameof(SetProfileDefaultCommand),
                IsEnabledPath = nameof(IsProfileRowSelected), ToolTipPath = nameof(DefaultTip), ShowToolTipOnDisabled = true },
        new() { Text = "✏ 重命名", CommandPath = nameof(RenameProfileCommand),
                IsEnabledPath = nameof(IsProfileRowSelected), ToolTipPath = nameof(RenameTip), ShowToolTipOnDisabled = true },
        new() { Text = "🗑 删除", Classes = "danger-btn", CommandPath = nameof(DeleteProfileCommand),
                IsEnabledPath = nameof(CanDeleteProfile), ToolTipPath = nameof(DeleteTip), ShowToolTipOnDisabled = true },
        // v3.60：原 Label「「路径」行改默认路径；「配置」行管多套数据」删除（工具条⛔ 不放说明文字）——
        //        表头「分类」列已经把两类行分开，各行自带说明（StringFormat 那条坑见工程规范）。
    };

    // ==================== 编辑缓冲 ====================

    /// <summary>「当前值」编辑缓冲里正在编辑的值（点「💾 保存」才写回对象并落盘）。</summary>
    [ObservableProperty] private string _editValue = "";

    partial void OnSelectedChanged(PathSettingItem? value)
    {
        _syncing = true;
        // 只有路径行才进「当前值」编辑缓冲（配置行只读展示）
        EditValue = value is { IsPathRow: true } ? value.Value : "";        _syncing = false;
        OnPropertyChanged(nameof(HasSelection));

        RaiseActionStates();   // v3.53：工具条按分类启停，靠这批通知刷新
    }

    partial void OnEditValueChanged(string value)
    {
        // 绑定到 TextBox 时不要反过来打扰输入；这里只是把"是否改过"反映到提示上
        if (!_syncing) StatusText = "改完点「💾 保存」写入 app.json。";
    }

    // ==================== 构造 ====================

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PathViewModel() { }

    public PathViewModel(IAppConfigService cfg, ConfigStoreService store, IColumnLayoutService cols, IProfileService profiles,
                         IExternalToolRegistry? ext = null, IPythonRuntime? py = null)
        : base(cols)
    {
        _cfg = cfg;
        _store = store;
        _profiles = profiles;
        _ext = ext;
        _py = py;

        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        SetSort("Category", false);       // v3.53：默认按「分类」升序（同类聚在一起），组内再按名称
        BuildItems();
        RebuildQuickFilters();            // v3.21 §4.8.2：分类 / 类型进表头右键「筛选」
        ApplyView();
        SelectActiveProfile();            // v3.53：默认选中「正在使用」的配置行（一眼看到我在哪）
    }

    // ==================== 外部程序「惰性探测」 + 状态栏 ⚠ 可跳转（v3.65） ====================

}
