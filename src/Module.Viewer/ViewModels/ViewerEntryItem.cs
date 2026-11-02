namespace NewToys.Viewer.ViewModels;

/// <summary>图片条目条里的一条（= 一张图）。</summary>
/// <remarks><see cref="IsSectionStart"/> 只影响渲染（前面画节标），条目序号与视图索引仍然一一对应。</remarks>
public sealed class ViewerEntryItem
{
    /// <summary>显示名（文件名 / 包内路径）。</summary>
    public string Name { get; init; } = "";

    /// <summary>本条是否是某一节的第一张。</summary>
    public bool IsSectionStart { get; init; }

    /// <summary>节范围文案（如 <c>101–200</c>）；非节首为空串。</summary>
    public string SectionLabel { get; init; } = "";
}
