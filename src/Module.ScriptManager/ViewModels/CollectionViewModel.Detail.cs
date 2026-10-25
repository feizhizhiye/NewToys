using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class CollectionViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(HasSelectedCollection),
        EmptyText = "在左侧选中一个集合，可在这里改名称 / 图标 / 类别 / 描述 / 刷新策略 / 标签。集合只是「一组采集器的容器」，本身不跑任何东西。",
        Sections =
        {
            new DetailSection
            {
                Header = "集合",
                Fields =
                {
                    new DetailField
                    {
                        Label = "名称", ValuePath = nameof(EditName), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "图标", ValuePath = nameof(EditIcon), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur, Placeholder = "一个 emoji 或字符"
                    },
                    new DetailField
                    {
                        Label = "类别", Kind = FieldKind.Select,
                        OptionsPath = nameof(KindOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        OptionValuePath = nameof(CollectionFilterOption.Value), ValuePath = nameof(EditKind),
                        Hint = "决定这个集合归哪个页面（信息面板 / 脚本库 / 命令库 / 软件包）；改内置库的类别后，下次启动会自动补回一个默认库"
                    },
                    new DetailField
                    {
                        Label = "置顶", ValuePath = nameof(EditPinned), Kind = FieldKind.Check,
                        Hint = "★ 标记：左栏分组下拉选「置顶」时可单独筛出，信息面板的集合下拉里也排最前"
                    },
                    new DetailField
                    {
                        Label = "描述", ValuePath = nameof(EditDescription), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur, MultiLine = true, Rows = 52
                    },
                    new DetailField
                    {
                        Label = "脚本根目录", ValuePath = nameof(EditRootPath), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        VisibleWhen = nameof(IsScriptCollection),
                        Placeholder = "留空 = 用设置里的全局脚本目录",
                        Hint = "这个库的脚本文件放哪；相对路径按它解析"
                    },
                    new DetailField
                    {
                        Label = "默认视图", Kind = FieldKind.Select,
                        OptionsPath = nameof(ViewOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        OptionValuePath = nameof(CollectionFilterOption.Value), ValuePath = nameof(EditDefaultViewId),
                        VisibleWhen = nameof(IsInfoCollection), EnabledWhen = nameof(IsInfoCollection)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint, ValuePath = nameof(ViewCountText),
                        VisibleWhen = nameof(IsInfoCollection)
                    },
                    new DetailField
                    {
                        Label = "刷新策略", Kind = FieldKind.Select,
                        OptionsPath = nameof(RefreshOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        OptionValuePath = nameof(CollectionFilterOption.Value), ValuePath = nameof(EditRefreshMode),
                        EnabledWhen = nameof(IsInfoCollection),
                        Hint = "只有信息面板集合会刷新（脚本库 / 命令库只是索引）"
                    },
                    new DetailField
                    {
                        Label = "刷新间隔(秒)", ValuePath = nameof(EditRefreshSeconds), Kind = FieldKind.Text,
                        EnabledWhen = nameof(IsInfoCollection),
                        Hint = "只对「定时」有效；0 = 用最小间隔"
                    },
                    new DetailField
                    {
                        Label = "标签（逗号分隔）", ValuePath = nameof(EditTags), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(CollectionHint) },
                }
            }
        }
    };

    public bool HasSelectedCollection => SelectedCollection != null;

    /// <summary>只有「信息面板」类集合才有节 / 视图（其它集合不显示默认视图与视图提示）。</summary>
    public bool IsInfoCollection => SelectedCollection == null
        || ScriptCollectionKind.Normalize(SelectedCollection.Kind) == ScriptCollectionKind.Info;

    /// <summary>当前是不是脚本库类集合（决定「脚本根目录」字段显隐）。</summary>
    public bool IsScriptCollection => SelectedCollection != null
        && ScriptCollectionKind.Normalize(SelectedCollection.Kind) == ScriptCollectionKind.Script;

    /// <summary>视图数 + 一句话解释（集合卡「视图」段提示）。</summary>
    public string ViewCountText
    {
        get
        {
            if (_store == null || SelectedCollection == null) return "";

            var n = _store.Data.Views.Count(v => v.CollectionId == SelectedCollection.Id);
            return n == 0
                ? "还没有视图（打开信息面板时自动建一个「默认」）。"
                : $"共 {n} 个视图 · 视图决定每节显示哪些列、节怎么排；同一个脚本在不同集合可以显示不同的列。";
        }
    }

    public string CollectionHint => SelectedCollection == null
        ? ""
        : $"成员 {SelectedCollection.MemberCount} 个"
          + (SelectedCollection.Builtin ? " · 默认库（兜底接收无归属条目）" : "")
          + (SelectedCollection.IsInUse ? " · 在用，不可删除（可改名 / 归档）" : " · 可删除")
          + $" · 落盘 {_store?.FilePath ?? ""}";

    // ==================== 工具条 ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "➕ 新建集合", Classes = "ghost", CommandPath = nameof(NewCollectionCommand), ToolTip = "新建一个空集合，然后在右栏勾选成员" },
        new() { Text = "⧉ 复制", Classes = "ghost", CommandPath = nameof(DuplicateCollectionCommand) },
        new() { Text = "🗑 删除", Classes = "danger-btn", CommandPath = nameof(DeleteCollectionCommand), ToolTip = "只有「在用」的集合不能删（默认库一直算在用）；其余可删" },
        new() { Text = "📦 归档", Classes = "ghost", CommandPath = nameof(ArchiveCollectionCommand), ToolTip = "归档后默认不显示，可从「全部」里找回" },
        new() { Text = "🔍 扫描目录", Classes = "ghost", CommandPath = nameof(ScanIntoCollectionCommand), ToolTip = "扫描脚本目录，把新脚本入库并加入当前集合" },
        new() { Text = "📤 发送到对应页面", Classes = "ghost", CommandPath = nameof(SendToPageCommand), ToolTip = "按集合类别跳到对应页面并选中这个集合（信息面板 / 脚本库 / 命令库 / 软件包）" },
        new() { Text = "📤 导出", Classes = "ghost", CommandPath = nameof(ExportCommand), ToolTip = "把集合定义导出为 json" },
        new()
        {
            Kind = ToolbarItemKind.TextField, Width = 96, Placeholder = "搜索成员…",
            ValuePath = nameof(MemberSearch)
        },
        new()
        {
            Kind = ToolbarItemKind.Toggle, Text = "显示不可采集",
            ValuePath = nameof(ShowNonCollectable),
            ToolTip = "默认只列可采集条目；打开后不可采集项灰显（⛔ 勾不上）"
        },
        new()
        {
            Kind = ToolbarItemKind.Toggle, Text = "显示全部类型",
            ValuePath = nameof(ShowAllKinds),
            ToolTip = "默认候选池按当前集合的类别过滤（脚本库集合只列脚本）；打开后可跨类勾选"
        },
    };

    // ==================== 数据加载 ====================

}
