namespace NewToys.Shared.Services;

/// <summary>按模块分区的键值选项存储（ §3.5）。（WHY: SH-047）</summary>
public interface IOptionsStore
{
    /// <summary>读一个选项；不存在 / 文件损坏 → <c>null</c>。</summary>
    string? Get(string module, string key);

    /// <summary>写一个选项并立即落盘（<paramref name="value"/> 为 null 等价于删除）。（WHY: SH-048）</summary>
    void Set(string module, string key, string? value);

    /// <summary>删除一个选项；确实删掉了 → <c>true</c>（本来就不存在 → <c>false</c>，不写盘）。写失败同样抛出。</summary>
    bool Remove(string module, string key);
}
