namespace NewToys.Shared.Formatting;

/// <summary>
/// 媒体数值的**显示串**（码率 / 时长一类的单位换算）。
/// <para>🔴 同一套数字口径分散写会在各模块慢慢长歪（同一个 8.5 Mbps 曾有三份实现），
/// 新增或修改单位换算一律加到这里，⛔ 不在模块里再写一份。</para>
/// </summary>
public static class MediaText
{
    /// <summary>
    /// 码率可读串：<c>8.5 Mbps</c> / <c>640 kbps</c>。
    /// <paramref name="bitRateBps"/> ≤ 0（没探测到）⇒ 空串，调用方自己决定用「—」还是留白。
    /// </summary>
    public static string BitRate(long bitRateBps)
        => bitRateBps <= 0
            ? ""
            : bitRateBps >= 1_000_000
                ? $"{bitRateBps / 1_000_000.0:0.0} Mbps"
                : $"{bitRateBps / 1000.0:0} kbps";
}
