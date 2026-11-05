using System;
using System.IO;

namespace NewToys.Core.Services;

/// <summary>配置文件写盘工具：原子写 + 默认值不落盘。（WHY: CR-005）</summary>
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
