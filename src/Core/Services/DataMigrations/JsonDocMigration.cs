using System;
using System.IO;

namespace NewToys.Core.Services.DataMigrations;

/// <summary>
/// <b>旧 JSON 文件 → 文档表</b> 的一次性搬运（NT-076 第二批）。
/// <list type="bullet">
///   <item>只在「目标文档还不存在 且 源文件存在」时搬（幂等：重复启动不重复搬）；</item>
///   <item>搬成功后把源文件改名 <c>.json.legacy</c>（⛔ 不删 —— 用户数据永远留一份可手工考回的原文）；</item>
///   <item>目标文档已有 ⇒ 直接改名留档（说明上次已搬过）；</item>
///   <item>解析不了 / 读到一半失败 ⇒ **不动源文件**，下次启动重试。</item>
/// </list>
/// <para>
/// ⚠️ 「源文件不存在」完全正常（用户从没用过那页），静默跳过。
/// </para>
/// </summary>
public static class JsonDocMigration
{
    /// <summary>搬运一份文件。返回是否发生了变化（搬了 / 留档了）。</summary>
    /// <param name="store">目标文档表。</param>
    /// <param name="name">文档名（如 <c>bookmarks</c>）。</param>
    /// <param name="legacyFile">旧 JSON 全路径。</param>
    public static bool ImportOnce(JsonDocStore store, string name, string legacyFile)
    {
        if (!store.IsAvailable) return false;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(legacyFile)) return false;

        try
        {
            if (store.Has(name))
            {
                // 表里已有 ⇒ 老文件只留档（⛔ 不覆盖表，用户数据优先）
                return Archive(legacyFile);
            }

            if (!File.Exists(legacyFile)) return false;

            var raw = File.ReadAllText(legacyFile);
            if (string.IsNullOrWhiteSpace(raw))
            {
                // 空文件 = 没数据，直接留档（留一份空文件没意义，但也不删）
                return Archive(legacyFile);
            }

            store.SaveRaw(name, raw);
            var archived = Archive(legacyFile);
            System.Diagnostics.Debug.WriteLine(
                $"[NewToys][migrate] {Path.GetFileName(legacyFile)} → {store.TableName}/{name}（{raw.Length} 字符）");
            return archived;
        }
        catch (Exception ex)
        {
            // 失败：源文件原样留着，下次启动重试
            System.Diagnostics.Debug.WriteLine(
                $"[NewToys][migrate] {Path.GetFileName(legacyFile)} 搬进 {store.TableName} 失败（下次启动重试）：{ex.Message}");
            return false;
        }
    }

    /// <summary>源文件改名 <c>.legacy</c>（⛔ 不删）。返回是否真的改了名。</summary>
    public static bool Archive(string legacyFile)
    {
        try
        {
            if (!File.Exists(legacyFile)) return false;
            File.Move(legacyFile, legacyFile + ".legacy", overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NewToys][migrate] 留档改名失败（{legacyFile}）：{ex.Message}");
            return false;
        }
    }
}
