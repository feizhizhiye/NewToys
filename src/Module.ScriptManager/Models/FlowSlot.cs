using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 调试集合页的一段槽位（NT-086 批 C）：段名 + 下拉候选（按 <c># role:</c> 过滤）+ 当前选中脚本。
/// <para>
/// 🔴 <b>槽位一律可选、允许留空跳过</b>（<c>toc</c> 留空 = 链式模式），⛔ 不强制每段都有脚本。
/// <c>save</c> 是内置下载器，⛔ 不为下载写脚本（并发 / 限速 / 去重 / 续跑 / 取消 / 进度）。
/// </para>
/// </summary>
public partial class FlowSlot : ObservableObject
{
    /// <summary>段位键：<c>entry / pick / toc / content / save</c>。</summary>
    public string Key { get; set; } = "";

    /// <summary>中文标签。</summary>
    public string Label { get; set; } = "";

    /// <summary>工具提示（这段干什么 / 输出什么）。</summary>
    public string Hint { get; set; } = "";

    /// <summary>内置段（<c>save</c>）：⛔ 不可选脚本。</summary>
    public bool BuiltIn { get; set; }

    /// <summary>内置段的占位显示文本。</summary>
    public string BuiltInText { get; set; } = "";

    /// <summary>命中摘要（跑过之后填）：「12 条 · 3/3 字段」。</summary>
    [ObservableProperty] private string _hitText = "—";

    /// <summary>下拉候选（首项是「（留空 · 跳过）」= 空值，由 VM 按 <c>role</c> 填充）。</summary>
    public ObservableCollection<SlotOption> Options { get; } = new();

    /// <summary>当前选中的候选（<c>null</c> = 还没填）。</summary>
    [ObservableProperty] private SlotOption? _selected;

    /// <summary>选中的脚本名（下拉值的扁平形式，供服务层使用；空 = 留空 / 跳过）。</summary>
    public string ScriptName => Selected?.Value ?? "";

    public bool CanPick => !BuiltIn;
    public bool IsBuiltIn => BuiltIn;

    partial void OnSelectedChanged(SlotOption? value) => OnPropertyChanged(nameof(ScriptName));
}

/// <summary>
/// 槽位下拉的一项：<see cref="Value"/> = 脚本名（空 = 留空跳过），<see cref="Label"/> = 显示文本。
/// </summary>
public sealed class SlotOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>兜底显示（某些下拉渲染路径不走 ItemTemplate）。</summary>
    public override string ToString() => Label;
}
