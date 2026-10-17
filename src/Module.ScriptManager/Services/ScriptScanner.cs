using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 扫描目录得到的一条<b>候选</b>（NT-068 §三.6）。
/// </summary>
/// <param name="FullPath">磁盘全路径。</param>
/// <param name="RelativePath">相对脚本目录的路径（入库时写进 <see cref="ScriptItem.Target"/>）。</param>
/// <param name="State">三态：<c>new</c> / <c>existing</c> / <c>ineligible</c>。</param>
/// <param name="Reason">不可采集 / 已入库的原因（界面灰字显示）。</param>
public readonly record struct ScriptCandidate(string FullPath, string RelativePath, string State, string Reason)
{
    public const string StateNew = "new";
    public const string StateExisting = "existing";
    public const string StateIneligible = "ineligible";

    /// <summary>默认勾选：只有新文件默认打勾，其余灰显不可勾。</summary>
    public bool Selectable => State == StateNew;
}

/// <summary>
/// <b>手动扫描</b>（NT-068 §三.6）：把脚本目录里的脚本列成候选，⛔ 不自动入库。
/// </summary>
/// <remarks>
/// 用户拍板「用户自己扫描」⇒ 扫描只产出候选列表，入库动作由界面上的「➕ 加入库」触发。
/// </remarks>
public static class ScriptScanner
{
    /// <summary>扫描目录里的脚本文件。</summary>
    /// <param name="directory">脚本目录。</param>
    /// <param name="includeSubFolders">是否含子目录。</param>
    /// <param name="existing">已入库条目（按 <see cref="ScriptItem.Target"/> 判重）。</param>
    public static IReadOnlyList<ScriptCandidate> Scan(string directory, bool includeSubFolders, IEnumerable<ScriptItem> existing)
    {
        var list = new List<ScriptCandidate>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return list;

        // 已入库的相对 / 绝对路径集合（统一归一成小写全路径比较）。
        var known = new HashSet<string>(
            (existing ?? Enumerable.Empty<ScriptItem>())
                .Select(i => (i.Target ?? "").Trim())
                .Where(t => t.Length > 0)
                .Select(t => Normalize(CollectableEvaluator.ResolveScriptPath(t, directory))),
            StringComparer.OrdinalIgnoreCase);

        var option = includeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", option)
                             .Where(f => CollectableEvaluator.ScriptExtensions.Any(
                                 e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                             .ToList();
        }
        catch
        {
            return list;
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(directory, file);
            var full = Normalize(file);

            if (known.Contains(full))
            {
                list.Add(new ScriptCandidate(file, relative, ScriptCandidate.StateExisting, "已入库"));
                continue;
            }

            var (ok, reason) = Inspect(file);
            list.Add(ok
                ? new ScriptCandidate(file, relative, ScriptCandidate.StateNew, "新文件")
                : new ScriptCandidate(file, relative, ScriptCandidate.StateIneligible, reason));
        }

        return list;
    }

    /// <summary>文件级可采集判定（扫描阶段只能看文件本身，不扫脚本内容）。</summary>
    private static (bool Ok, string Reason) Inspect(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists) return (false, "文件不存在");
            if (info.Length == 0) return (false, "空文件");
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, "读取失败：" + ex.Message);
        }
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
