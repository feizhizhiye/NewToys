namespace NewToys.MediaAssets.Models;

/// <summary>
/// 资产关联（`relations`，NT-018）：字幕之于正片 / 封面之于作品 / 同一作品的不同版本…
/// <para>关系类型取 <see cref="RelationTypes"/>（定义在 `AssetConstants.cs`）。</para>
/// </summary>
public class Relation
{
    public long Id { get; set; }
    public long SourceAssetId { get; set; }
    public long TargetAssetId { get; set; }
    public string RelationType { get; set; } = RelationTypes.RelatedTo;
    public string? MetadataJson { get; set; }
    public string CreatedAt { get; set; } = "";

    /// <summary>另一端的显示名（列表用；由服务查询时填）。</summary>
    public string TargetName { get; set; } = "";

    /// <summary>关系的中文名。</summary>
    public string TypeText => RelationType switch
    {
        RelationTypes.SubtitleOf => "字幕属于",
        RelationTypes.CoverOf => "封面属于",
        RelationTypes.SameWork => "同一作品",
        RelationTypes.VersionOf => "版本",
        RelationTypes.FormatOf => "格式变体",
        RelationTypes.Reference => "引用",
        _ => "相关"
    };
}
