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

    /// <summary>本页详情卡描述符（C# 对象字面量，⛔ 不做运行时 JSON / XAML 加载）。（WHY: ST-032）</summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一个包。选中后这里显示版本、源、状态、发布者、主页、协议与描述；右键列表还有安装 / 升级 / 卸载 / Pin 等操作。",
        Sections =
        {
            new DetailSection
            {
                Header = "选中包",
                Fields =
                {
                    // '：11 个只读字段一律 Selectable=true（SelectableTextBlock = 只读可选可复制）。
                    // ⛔ 包装成可编辑 TextBox 会让「详情卡」变成第二个写入点，与「设置页是唯一写入点」冲突。
                    new DetailField
                    {
                        Label = "名称",
                        ValuePath = "DetailItem.Name",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "Id",
                        ValuePath = "DetailItem.Id",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    // 「版本 / 可升级到」按已拍板口径拆成两行（版本 → 可升级）
                    new DetailField
                    {
                        Label = "版本",
                        ValuePath = "DetailItem.Version",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "可升级到",
                        ValuePath = "DetailItem.Available",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "状态",
                        ValuePath = "DetailItem.StatusText",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "源",
                        ValuePath = "DetailItem.Source",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "发布者",
                        ValuePath = "DetailItem.Publisher",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "协议 / 许可证",
                        ValuePath = "DetailItem.License",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "安装位置",
                        ValuePath = "DetailItem.InstallLocation",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "主页",
                        ValuePath = "DetailItem.Homepage",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "描述",
                        ValuePath = "DetailItem.Description",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（描述等信息来自「🔎 详情」，点一下才去源里查）"
                    }
                }
            }
        },
        Actions =
        {
            // —— 一条动作组合：安装 / 升级按「是否已安装」二选一，Pin 按「是否已 Pin」二选一 ——
            new DetailAction
            {
                Text = "安装", IconKey = "IconArrowDown",
                Classes = "primary",
                ToolTip = "安装这个包（用当前包管理器后端）",
                IsVisiblePath = nameof(ShowInstallAction),
                IsEnabledPath = "!" + nameof(IsBusy),
                Command = InstallCommand
            },
            new DetailAction
            {
                Text = "升级", IconKey = "IconArrowUp",
                Classes = "primary",
                ToolTip = "升级到最新版（已是最新版时置灰）",
                IsVisiblePath = nameof(ShowUpgradeAction),
                IsEnabledPath = nameof(CanUpgradeSelected),
                Command = UpgradeCommand
            },
            new DetailAction
            {
                Text = "详情", IconKey = "IconSearch",
                Classes = "ghost",
                ToolTip = "向源查询这个包的详细信息（发布者 / 协议 / 主页 / 描述）",
                IsVisiblePath = nameof(HasSelection),
                Command = LoadDetailCommand
            },
            new DetailAction
            {
                Text = "🔒 锁定版本",
                Classes = "ghost",
                ToolTip = "锁定到当前版本 = 暂停更新，「全部升级 / 升级」都会跳过它",
                IsVisiblePath = nameof(ShowPinAction),
                IsEnabledPath = "!" + nameof(IsBusy),
                Command = PinCommand
            },
            new DetailAction
            {
                Text = "🔒 解除锁定",
                Classes = "ghost",
                ToolTip = "解除锁定，恢复参与升级",
                IsVisiblePath = nameof(ShowUnpinAction),
                IsEnabledPath = "!" + nameof(IsBusy),
                Command = UnpinCommand
            },
            new DetailAction
            {
                Text = "卸载", IconKey = "IconTrash",
                Classes = "ghost",
                ToolTip = "卸载这个包（不可撤销，会再确认一次）",
                IsVisiblePath = nameof(ShowUninstallAction),
                IsEnabledPath = "!" + nameof(IsBusy),
                Command = UninstallCommand
            },
            // 2026-10-17：「复制信息」删除 —— 详情卡内所有文字本身就可复制（SelectableTextBlock），
            //              再给一个"复制整卡"按钮是重复入口。
            new DetailAction
            {
                Text = "🌐 打开主页",
                Classes = "ghost",
                ToolTip = "在默认浏览器里打开主页（先点「🔎 详情」才有地址）",
                IsVisiblePath = nameof(HasSelection),
                Command = OpenHomepageCommand
            }
        }
    };

    // —— 动作可见性 / 可用性（路径绑定的具名属性；派生自 Selected，选中变化时统一通知） ——

    /// <summary>⬇ 安装：选中项**未安装**时才出现（🔴 已安装的包不该再给「安装」）。</summary>
    public bool ShowInstallAction => Selected is { IsInstalled: false };

    /// <summary>⬆ 升级：选中项**已安装**时出现（是否可点另看 <see cref="CanUpgradeSelected"/>）。</summary>
    public bool ShowUpgradeAction => Selected is { IsInstalled: true };

    /// <summary>升级按钮可用态：已安装且确有新版本（否则置灰 —— 悬浮提示说明已是最新版）。</summary>
    public bool CanUpgradeSelected => Selected is { CanUpgrade: true } && !IsBusy;

    /// <summary>📌 Pin：未 Pin 时显示。</summary>
    public bool ShowPinAction => Selected is { IsPinned: false };

    /// <summary>📌 取消 Pin：已 Pin 时显示（三态单按钮的另一半）。</summary>
    public bool ShowUnpinAction => Selected is { IsPinned: true };

    /// <summary>🗑 卸载：只有已安装的包才有意义。</summary>
    public bool ShowUninstallAction => Selected is { IsInstalled: true };

}
