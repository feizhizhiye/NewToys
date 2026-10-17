using System;
using System.IO;
using System.Linq;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// <b>可采集判定</b>（NT-068 §三.5）：一条条目能不能当采集器用。
/// </summary>
/// <remarks>
/// <para>
/// 判定是<b>派生</b>的（每次加载 / 扫描重算），但结果落盘 ——
/// <see cref="ScriptItem.CollectableSource"/> 记来源：<c>auto</c> = 本类算的，<c>user</c> = 用户手动覆盖过。
/// 用户覆盖优先，⛔ 不被自动规则推翻。
/// </para>
/// <para>
/// ⚠️ 黑名单按<b>子串</b>匹配（卡面口径「含 start / explorer / …」）。
/// 副作用是 <c>Restart-*</c> 这类也命中 —— 这符合"宁可误判为不可采集"的取向：
/// 用户真要采集，可在集合成员选择器里手动打开（<c>collectableSource=user</c>）。
/// </para>
/// </remarks>
public static class CollectableEvaluator
{
    /// <summary>有副作用 / 需要交互的命令关键字（命中即不可采集）。</summary>
    public static readonly string[] SideEffectKeywords =
    {
        "start", "explorer", "control", "ms-settings:", "shutdown", "pause",
    };

    /// <summary>可用于采集的脚本扩展名。</summary>
    public static readonly string[] ScriptExtensions = { ".py", ".ps1", ".bat", ".cmd" };

    /// <summary>把脚本文件字段（相对脚本目录或绝对路径）解析成磁盘全路径；空则返回空串。</summary>
    public static string ResolveScriptPath(string? target, string scriptsDirectory)
    {
        var file = (target ?? "").Trim();
        if (file.Length == 0) return "";
        var expanded = Environment.ExpandEnvironmentVariables(file);
        return Path.IsPathRooted(expanded) ? expanded : Path.Combine(scriptsDirectory ?? "", expanded);
    }

    /// <summary>
    /// 判定一条条目是否可采集。
    /// </summary>
    /// <param name="item">条目。</param>
    /// <param name="scriptsDirectory">脚本目录（<see cref="ScriptItem.Target"/> 为相对路径时的基准）。</param>
    /// <returns>(可采集, 不可采集原因)；原因仅在不可采集时非空。</returns>
    public static (bool Ok, string Reason) Evaluate(ScriptItem item, string scriptsDirectory)
    {
        if (item == null) return (false, "无条目");

        // 用户手动排除优先（⛔ 不被自动规则翻回来）。
        if (ScriptCollectableSource.Normalize(item.CollectableSource) == ScriptCollectableSource.User && !item.Collectable)
            return (false, string.IsNullOrWhiteSpace(item.IneligibleReason) ? "手动排除" : item.IneligibleReason);

        var target = (item.Target ?? "").Trim();

        // 软件包不是采集器。
        if (item.Role == ScriptItemRole.Package) return (false, "软件包条目");

        // ---- 脚本文件型：扩展名 + 文件存在 ----
        var looksLikeFile = ScriptExtensions.Any(e => target.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        if (item.Role is ScriptItemRole.Script or ScriptItemRole.Collector || looksLikeFile)
        {
            if (target.Length == 0) return (false, "未指定脚本文件");
            if (!looksLikeFile) return (false, "不是可采集的脚本类型（.py / .ps1 / .bat / .cmd）");

            var path = ResolveScriptPath(target, scriptsDirectory);
            if (!File.Exists(path)) return (false, "文件缺失");

            // 脚本自身也可能"含 start" —— 但脚本内容不扫（成本高、误判多），只扫命令行。
            return (true, "");
        }

        // ---- 命令行型：capture + 黑名单 ----
        if (!item.Capture) return (false, "未开启「捕获输出」");
        if (target.Length == 0) return (false, "命令为空");

        var hit = SideEffectKeywords.FirstOrDefault(k => target.Contains(k, StringComparison.OrdinalIgnoreCase));
        if (hit != null) return (false, "有副作用或需交互");

        return (true, "");
    }

    /// <summary>按判定结果就地更新条目（⛔ 不动用户覆盖过的行）。</summary>
    /// <returns>是否有字段变化（需要回写磁盘）。</returns>
    public static bool Apply(ScriptItem item, string scriptsDirectory)
    {
        if (item == null) return false;
        if (ScriptCollectableSource.Normalize(item.CollectableSource) == ScriptCollectableSource.User) return false;

        var (ok, reason) = Evaluate(item, scriptsDirectory);
        var changed = item.Collectable != ok || item.IneligibleReason != reason;
        item.Collectable = ok;
        item.IneligibleReason = ok ? "" : reason;
        item.CollectableSource = ScriptCollectableSource.Auto;
        return changed;
    }
}
