using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>
    /// 工具条末位的详情开关 → 走本命令。
    /// <para>⛔ 基类 <c>ToggleDetailCommand</c> 只翻 <c>IsDetailVisible</c>、**不通知 Shell**，
    /// Shell 的右列（由 <c>DetailCardHost</c> 承载）就不会跟着出现 / 消失。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);   // 基类逻辑：翻转 + 落盘 + 展开时没选中就选第一项
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);     // 基类逻辑：确保展开并定位到当前选中项
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的页内自绘卡片对应：单分区（只读信息 + 可改文件名），无卡内动作区
    /// （v3.19 起详情卡不放操作按钮，动作全在工具条 / 右键菜单）。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "扫描一个目录后，在左侧列表里选中视频：这里会显示完整路径、大小、修改时间，以及分辨率 / 编码 / 码率 / 时长 —— 足够判断要不要剪辑或转码。勾上几行还能批量「发送到转码 / 剪切」。",
        Sections =
        {
            new DetailSection
            {
                // 原页内卡的「选中视频」小标题挪成分区标题（Shell 渲染器统一给标题行样式）
                Header = "选中视频",
                Fields =
                {
                    new DetailField
                    {
                        Label = "文件名（可改 · 失焦即提交；也可用下方「✏ 应用新文件名」）",
                        ValuePath = nameof(EditName),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "完整路径",
                        ValuePath = "DetailItem.FullPath",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    // 「类型 / 大小」按已拍板口径拆成两行（⛔ 不并排：并排会让用户以为是两个字段）
                    new DetailField
                    {
                        Label = "类型",
                        ValuePath = "DetailItem.Extension",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "大小",
                        ValuePath = "DetailItem.SizeDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "时长",
                        ValuePath = "DetailItem.DurationDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "分辨率",
                        ValuePath = "DetailItem.ResolutionDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "编码格式",
                        ValuePath = "DetailItem.CodecDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "码率",
                        ValuePath = "DetailItem.BitRateDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "修改时间",
                        ValuePath = "DetailItem.ModifiedDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "媒体信息",
                        ValuePath = "DetailItem.MediaStatus",
                        Kind = FieldKind.Text,
                        Selectable = true
                    }
                }
            }
        },
        Actions =
        {
            // 改名：声明式卡的输入框在 Shell 侧，页面 code-behind 够不着 ⇒ 收进 Actions 区补一个显式按钮
            //（回车 / 失焦自动提交仍保留，见 OnEditNameChanged）。
            new DetailAction
            {
                Text = "✏ 应用名称",
                Classes = "ghost",
                ToolTip = "把「文件名」框里的内容提交到磁盘（回车场景：失焦已自动提交，这里手动补一次）",
                Command = SaveNameCommand
            }
        }
    };

    /// <summary>导入 / 导出默认目录：本模块的 <c>store\video</c>（未建出 ⇒ 退回 <c>store\</c>）。</summary>
    public string ImportExportDefaultDir
    {
        get
        {
            var root = _cfg?.StoreDirectory;
            if (string.IsNullOrEmpty(root)) return "";
            var dir = Path.Combine(root, VideoDb.Key);
            return Directory.Exists(dir) ? dir : root;
        }
    }

}
