using System;
using System.IO;

namespace NewToys.Core.Services;

/// <summary>
/// 配置文件写盘工具（NT-075）：**原子写 + 默认值不落盘**。
/// <list type="bullet">
///   <item><b>原子写</b>：同目录 <c>.&lt;名&gt;.&lt;guid&gt;.tmp</c> → <c>File.Move(overwrite)</c>（同卷 rename 是原子操作）。
///     进程崩溃 / 断电只会留下临时文件，⛔ 不会得到半截正式文件（旧的 <c>File.WriteAllText</c> 会）。</item>
///   <item><b>懒创建 / 空文件清理</b>：内容为 <c>""</c> / <c>"{}"</c> / <c>"[]"</c> / <c>"null"</c> 时
///     **删除目标文件**（读方一律把「文件不存在」当默认值）。这样配置目录只留下用户真正改过的东西，
///     不会攒出一堆 2 字节的 <c>[]</c>。</item>
///   <item><b>目录自动创建</b>：调用方不必再自己 <c>Directory.CreateDirectory</c>。</item>
/// </list>
/// <para>
/// 🔴 只给「配置 / 小型状态」用，⛔ 不要用它写导出文件（用户选了路径，删空文件会让人以为导出失败）。
/// </para>
/// </summary>
public static class AtomicJsonFile
{
    /// <summary>原子写入；内容为空（空串 / <c>{}</c> / <c>[]</c> / <c>null</c>）时删除目标文件。</summary>
    public static void Write(string path, string content)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("path 不能为空。", nameof(path));
        if (content is null) throw new ArgumentNullException(nameof(content));

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (IsEmpty(content))
        {
            TryDelete(path);
            return;
        }

        var tmp = Path.Combine(dir ?? ".", $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            // 失败路径（写盘异常 / Move 被占用）清掉临时文件；残留不影响主流程，但别攒垃圾。
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 忽略 */ }
        }
    }

    /// <summary>内容是否等于「没有值」——这类文件不该存在（懒创建的反面：写空 = 删文件）。</summary>
    public static bool IsEmpty(string content)
    {
        var t = content.AsSpan().Trim();
        return t.IsEmpty
               || t.SequenceEqual("{}".AsSpan())
               || t.SequenceEqual("[]".AsSpan())
               || t.SequenceEqual("null".AsSpan());
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 删不掉（被占用 / 只读）：保持旧文件，⛔ 不抛给 UI */ }
    }
}
