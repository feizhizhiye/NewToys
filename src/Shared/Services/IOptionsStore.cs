namespace NewToys.Shared.Services;

/// <summary>
/// 按模块分区的键值选项存储（v3.60 §3.5 / NT-061）。
/// <para>
/// 模块只调本接口 —— ⛔ 不碰文件、不知道底下是 json 还是 SQLite。默认实现落
/// <c>&lt;配置根&gt;\config\&lt;module&gt;.json</c>（可读 / 可手改 / 随 profile 目录天然隔离）；
/// 将来整体换成 SQLite 的 <c>options(module, key, value, updated_at)</c> 表，调用方零改动。
/// </para>
/// <para>
/// <b>module</b> = 配置文件基名（kebab-case，如 <c>media-assets</c> / <c>script-manager</c> / <c>settings</c>），
/// 决定落到哪个 <c>config\&lt;module&gt;.json</c>；<b>key</b> = 该模块内的选项名（建议 camelCase）。
/// </para>
/// <para>
/// <b>值一律是 string</b>（调用方自己序列化简单类型）—— 这样换 SQLite 时表结构就是
/// <c>module / key / value / updated_at</c>，⛔ 不必为每种类型单独建列。
/// </para>
/// </summary>
public interface IOptionsStore
{
    /// <summary>读一个选项；不存在 / 文件损坏 → <c>null</c>。</summary>
    string? Get(string module, string key);

    /// <summary>
    /// 写一个选项并**立即落盘**（<paramref name="value"/> 为 <c>null</c> 等价于删除）。
    /// <para>
    /// ⚠️ 写失败（无权限 / 路径非法 / 磁盘满）**不吞异常** —— 抛出后由调用方（模块设置服务）
    /// 转成状态栏红字提示，且本选项**保持上一个有效值**（内存与磁盘都不改）。
    /// </para>
    /// </summary>
    void Set(string module, string key, string? value);

    /// <summary>删除一个选项；确实删掉了 → <c>true</c>（本来就不存在 → <c>false</c>，不写盘）。写失败同样抛出。</summary>
    bool Remove(string module, string key);
}
