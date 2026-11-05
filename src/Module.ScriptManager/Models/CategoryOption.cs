namespace NewToys.ScriptManager.Models;

/// <summary>工具条「分类 ▾」里的一项：显示标签 + 文件里的原始分类值。（WHY: SM-002）</summary>
public sealed class CategoryOption
{
    /// <summary>下拉里显示的文案。</summary>
    public string Label { get; set; } = "";

    /// <summary>文件 <c># category:</c> 的原始值（空 = 全部分类）。</summary>
    public string Value { get; set; } = "";
}
