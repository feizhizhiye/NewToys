namespace NewToys.WebTools.Models;

/// <summary>
/// 工具条「分类 ▾」里的一项（NT-086）：显示标签 + 文件里的原始分类值。
/// <para>
/// 下拉**按文件里实际出现的值动态生成**（⛔ 不硬编码五档）；已知五档显示中文标签，
/// 未知值原样显示。<c>Value</c> 为空串 = 「全部分类」。
/// </para>
/// </summary>
public sealed class CategoryOption
{
    /// <summary>下拉里显示的文案。</summary>
    public string Label { get; set; } = "";

    /// <summary>文件 <c># category:</c> 的原始值（空 = 全部分类）。</summary>
    public string Value { get; set; } = "";
}
