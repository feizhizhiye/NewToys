using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class ScriptInfoViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "点左边任意一节（或右键它）把这里切到那一节；右键「⚙ 显示这一节的详情 / 设置」可重新打开这张卡。",
        Sections =
        {
            new DetailSection
            {
                Header = "节",
                Fields =
                {
                    new DetailField { Label = "采集器", ValuePath = nameof(SectionTitleText), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "目标", ValuePath = nameof(SectionTargetText), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "状态", ValuePath = nameof(SectionStateText), Kind = FieldKind.Text, Selectable = true },
                }
            },
            new DetailSection
            {
                Header = "采集参数（所有集合共用）",
                Fields =
                {
                    new DetailField
                    {
                        Label = "解析器", Kind = FieldKind.Select,
                        OptionsPath = nameof(ParserOptions), OptionDisplayPath = nameof(InfoOption.Label),
                        ValuePath = nameof(SettingsParser)
                    },
                    new DetailField { Label = "超时(秒)", ValuePath = nameof(SettingsTimeout), Kind = FieldKind.Text },
                    new DetailField
                    {
                        Label = "保留", Kind = FieldKind.Select,
                        OptionsPath = nameof(RetentionOptions), OptionDisplayPath = nameof(InfoOption.Label),
                        ValuePath = nameof(SettingsRetention)
                    },
                    new DetailField
                    {
                        Label = "保留数量", ValuePath = nameof(SettingsRetentionN), Kind = FieldKind.Text,
                        Hint = "count = 保留次数；days = 保留天数"
                    },
                    new DetailField
                    {
                        Label = "自动刷新", ValuePath = nameof(SettingsAuto), Kind = FieldKind.Check,
                        Commit = EditCommitMode.Instant, EnabledWhen = nameof(CanAuto)
                    },
                    new DetailField
                    {
                        Label = "间隔(秒)", ValuePath = nameof(SettingsInterval), Kind = FieldKind.Text,
                        EnabledWhen = nameof(CanAuto), Hint = "2~3600；0 = 只手动"
                    },
                }
            },
            new DetailSection
            {
                Header = "本视图的显示",
                Fields =
                {
                    new DetailField
                    {
                        Kind = FieldKind.Chips,
                        OptionsPath = nameof(SectionColumnChips),
                        ChipTextPath = nameof(SectionColumnChip.Title),
                        ChipSelectedPath = nameof(SectionColumnChip.Visible),
                        ChipCommandPath = nameof(ToggleSectionColumnCommand)
                    },
                    new DetailField
                    {
                        Label = "表头名称",
                        Kind = FieldKind.EditableList,
                        OptionsPath = nameof(SectionColumnChips),
                        ItemLabelPath = nameof(SectionColumnChip.Key),
                        ItemTextPath = nameof(SectionColumnChip.Title),
                        ItemCommandPath = nameof(CommitColumnTitleCommand),
                        Hint = "改完点别处即生效；只改当前视图的表头，⛔ 不改脚本本体、不影响其它集合。"
                    },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(SectionColumnsHint) },
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "保存参数", IconKey = "IconSave",
                Classes = "primary",
                ToolTip = "把上面的采集参数与本视图的列宽写进 script.json",
                Command = SaveSectionSettingsCommand
            },
            new DetailAction
            {
                Text = "上移", IconKey = "IconArrowUp", Classes = "ghost",
                ToolTip = "在当前视图里把这一节往前挪",
                Command = MoveSectionUpCommand
            },
            new DetailAction
            {
                Text = "下移", IconKey = "IconArrowDown", Classes = "ghost",
                ToolTip = "在当前视图里把这一节往后挪",
                Command = MoveSectionDownCommand
            },
            new DetailAction
            {
                Text = "脚本目录", IconKey = "IconOpen", Classes = "ghost",
                ToolTip = "打开脚本目录",
                Command = OpenScriptDirCommand
            }
        }
    };

}
