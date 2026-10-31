using System;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Details;
using NewToys.Shared.Services;
using NewToys.VideoTools.Details;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoBatchViewModel
{
    /// <summary>
    /// 工具条末位的详情开关 → 走本命令。
    /// <para>⛔ 不能直接用基类的 <c>ToggleDetailCommand</c>：它只翻 <c>IsDetailVisible</c>，**不通知 Shell**，
    /// Shell 的右列（由 <c>DetailCardHost</c> 承载）就不会跟着出现 / 消失。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);   // 基类逻辑：翻转 + 落盘 + 展开时没选中就选第一项
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>右键「▤ 显示详情栏」→ 走本命令（同上：基类逻辑 + 通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);     // 基类逻辑：确保展开并定位到当前选中项
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡：结构 / 字段 / 文案全部来自 <see cref="VideoTaskCard"/>（与「视频预览」页同一份），
    /// 这里只声明本页的差异 —— 显式提交（工具条「💾 保存修改」生效）、多一节「📄 任务状态」。
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= VideoTaskCard.Build(
        new VideoCardCommands
        {
            PickOutput = PickOutputCommand,
            PickSubtitle = PickSubtitleCommand,
            PickAudio = PickAudioCommand,
            DetectCrop = DetectCropCommand,
            Trial = TrialSelectedCommand,
            SaveCommandLine = SaveCommandLineCommand
        },
        explicitCommit: true,
        emptyText: "在左侧选中一个任务：这里可以改它的参数（剪辑 = 区间 / 预设；转码 = 预设 / 编码器 / 码率 / 画面 / 音轨），改完点工具条上的「💾 保存修改」。",
        includeTaskState: true);

    /// <summary>台头「类型 / 文件」那一行（两页共用同一张卡，属性名也必须一致）。</summary>
    public string DetailTitle => DetailTask?.Name ?? "—";

    /// <summary>批量页不探测源分辨率（只有预览页会载入并探测）→ 隐藏卡里的「原视频宽高」提示行。</summary>
    public bool HasSourceResolution => false;

    /// <summary>与预览页同名的占位（<see cref="HasSourceResolution"/> 恒 false ⇒ 这一行永不渲染）。</summary>
    public string SourceResolutionDisplay => "—";

    // ==================== 收益预估（转码前静态判定） ====================

    /// <summary>选中任务的收益预估一行（建任务 / 改参数时算好，存在任务上）。</summary>
    public string DetailAdvice => (DetailTask as Models.TranscodeTask)?.AdviceText ?? "";

    /// <summary>没有预估（剪辑任务 / 源信息不全 / 判定关闭）⇒ 整行不渲染。</summary>
    public bool HasDetailAdvice => DetailTask is Models.TranscodeTask { HasAdvice: true };
}
