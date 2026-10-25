using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public abstract partial class QuickActionListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>工具条末位的详情开关 → 走本命令（⛔ 基类 <c>ToggleDetailCommand</c> 不通知 Shell，右列不会跟着开合）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>本页详情卡描述符（**C# 对象字面量**；由两个子类各自实现，因为两页字段不同）。</summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    /// <summary>构造本页的详情卡描述符（子类实现）。</summary>
    protected abstract DetailCardDescriptor BuildCard();

    /// <summary>
    /// 详情卡的**图标动作组**（/ <see cref="FieldKind.IconBar"/>）：运行 / 收藏 / 保存 / 删除
    /// 合成**一行 32×32 小图标按钮**（⛔ 不要四个撑满宽度的文字按钮）。
    /// <para>
    /// 两页**完全一致** ⇒ 基类给一份，子类把它插进字段区末尾。
    /// 收藏 = 「未收藏 / 已收藏」两个**互斥**图标（靠 <c>IsActivePath</c> 走强调色表达状态，⛔ 无文字）。
    /// </para>
    /// </summary>
    protected DetailField BuildActionBar() => new()
    {
        Kind = FieldKind.IconBar,
        IconActions =
        {
            new DetailIconAction
            {
                IconKey = "IconPlay",
                ToolTip = "运行这条（命令用 cmd 执行；勾了「需要管理员权限」会弹 UAC）",
                CommandPath = nameof(RunCommand),
                IsVisiblePath = nameof(HasSelection),
                IsEnabledPath = nameof(IsNotBusy)
            },
            new DetailIconAction
            {
                IconKey = "IconStar",
                ToolTip = "收藏 / 取消收藏（图标亮 = 已收藏）",
                CommandPath = nameof(ToggleFavoriteCommand),
                IsVisiblePath = nameof(HasSelection),
                IsActivePath = nameof(IsSelectedFavorite)
            },
            new DetailIconAction
            {
                IconKey = "IconSave",
                ToolTip = "把当前内容写回本页数据文件",
                CommandPath = nameof(SaveCommand),
                IsVisiblePath = nameof(HasSelection)
            },
            new DetailIconAction { IsSeparator = true },
            new DetailIconAction
            {
                IconKey = "IconTrash",
                ToolTip = "从列表移除这条（⛔ 只动清单、不删磁盘文件；删文件用列表右键菜单）",
                CommandPath = nameof(DeleteCommand),
                IsVisiblePath = nameof(HasSelection),
                IsEnabledPath = nameof(IsNotBusy),
                IsDanger = true
            }
        }
    };

    // ==================== 分类 = 功能标签（v3.62：详情卡 chips） ====================

    /// <summary>分类标签（**功能**维度，⛔ 不再是来源）：设置 / 脚本 / 定时任务。</summary>
    public static readonly string[] CategoryPresets = { "设置", "脚本", "定时任务" };

    /// <summary>
    /// 详情卡「分类」chips 的数据源。
    /// <para>🔴 **集合实例必须稳定**：只改每项的 <c>IsSelected</c>，⛔ 不要 Clear / 重建
    /// （重建会把 chip 的选中态清掉并回写空值）。</para>
    /// </summary>
    public ObservableCollection<CategoryPick> CategoryPicks { get; } =
        new(CategoryPresets.Select(p => new CategoryPick(p)));

    /// <summary>
    /// 详情卡「分类」：与表头「分类」列**同源**（<see cref="QuickAction.Classify"/>）
    /// —— 脚本条目 = 脚本后缀（.py / .ps1 / .bat / .cmd），命令行命令 = Shell 宿主（cmd / pwsh / 自动）。
    /// </summary>
    public string ClassifyText => DetailItem?.Classify ?? "";

    /// <summary>详情卡「分类」（旧）：脚本条目 = **脚本后缀**（.py / .ps1 / .bat / .cmd），没有脚本文件 = 「—」。</summary>
    public string ScriptExtText
    {
        get
        {
            var file = (DetailItem?.ScriptFile ?? "").Trim();
            var ext = Path.GetExtension(file).ToLowerInvariant();
            return ext.Length > 0 ? ext : "—";
        }
    }

    /// <summary>点分类 chip：**单选**语义（点已选中的那个 = 取消），改完立即落盘。</summary>
    [RelayCommand]
    private void ToggleCategory(CategoryPick? pick)
    {
        if (pick == null || Selected == null || _svc == null) return;

        var next = pick.IsSelected ? "" : pick.Name;
        Selected.Category = next;
        EditCategory = next;
        _svc.Save();

        SyncCategoryPicks();
        RebuildCategories();      // 分类值变了 ⇒ 分类候选 / 筛选菜单要重算
        ApplyViewKeepSelection();

        StatusText = next.Length > 0
            ? $"已把「{Selected.Title}」归到「{next}」。"
            : $"已清掉「{Selected.Title}」的分类。";
    }

    /// <summary>把 chips 的选中态同步到**当前卡片条目**（集合实例不变，只改 <c>IsSelected</c>）。</summary>
    private void SyncCategoryPicks()
    {
        var cur = (DetailItem?.Category ?? "").Trim();
        foreach (var p in CategoryPicks)
            p.IsSelected = string.Equals(p.Name, cur, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// 刷新列表但**保住选中项**：<see cref="ApplyView"/> 会 <c>Items.Clear()</c> 再重填，
    /// ListBox 的 <c>SelectedItem</c> 双向绑定在清空这一瞬被回写成 null ⇒ 详情卡掉回「未选中」空态
    /// （"点收藏 / 点分类后详情卡变空白"就是这个）⇒ 改完列表内容后把选中项原样放回去。
    /// </summary>
    protected void ApplyViewKeepSelection()
    {
        var keep = Selected;
        ApplyView();
        if (keep != null && Selected == null && Items.Contains(keep)) Selected = keep;
    }

    /// <summary>选中项是否已收藏（没选中 → false）—— 收藏图标的高亮。</summary>
    public bool IsSelectedFavorite => DetailItem?.IsFavorite == true;

    /// <summary>选中项尚未收藏（没选中 → false）。</summary>
    public bool IsSelectedNotFavorite => DetailItem?.IsFavorite != true;

    /// <summary>属性卡里收藏按钮的文案。</summary>
    public string FavoriteButtonText => DetailItem?.IsFavorite == true ? "★ 取消收藏" : "☆ 收藏";

    /// <summary>底部提示：数据落在哪、会不会被备份（规范 §9）。
    /// 🔴 起脚本库 / 命令库**共用** <c>config\script\script.json</c>（按条目角色区分归属）。</summary>
    public string DataFileHint =>
        $"本页数据文件：{FilePath}\n（两个页面的文件都在 config\\ 下，会被「设置 → 数据管理」的备份 / 导入一起覆盖）";

    /// <summary>本页的配置文件全路径（两页同一个文件，按 <c>role</c> 区分）。</summary>
    public string FilePath => _pageKind == QuickActionPage.Quick
        ? _svc?.QuickFilePath ?? ""
        : _svc?.ScriptsFilePath ?? "";

    /// <summary>另一个页面的名字（右键「移动到…」的提示文案用）。</summary>
    public string OtherPageName => _pageKind == QuickActionPage.Quick ? "脚本库" : "命令库";

    /// <summary>右键菜单里「移到另一页」的动态文案。</summary>
    public string MoveMenuLabel => _pageKind == QuickActionPage.Quick
        ? "➡ 移到「脚本库」"
        : "➡ 移到「命令库」（本页只放有脚本文件的脚本）";

}
