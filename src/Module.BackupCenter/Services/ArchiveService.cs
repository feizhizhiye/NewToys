using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewToys.BackupCenter.Models;
using NewToys.Shared.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>归档结果（<paramref name="Parts"/> 仅分卷时有值，按 <c>.001 .002 …</c> 升序）。</summary>
public sealed record ArchiveOutcome(bool Ok, bool Canceled, string Message, int SkippedFiles, List<string>? Parts = null);

/// <summary>还原结果。</summary>
public sealed record RestoreOutcome(bool Ok, bool Canceled, int Restored, int Skipped, string Message);

/// <summary>
/// 7z 封装：建包（AES-256 + 头部加密）与还原（先解到暂存目录 → 校验条目名 → 按冲突策略落盘）。
/// <para>
/// 密码只能走命令行 <c>-p</c>（7z 不支持 stdin / 密码文件）⇒ 同机其他用户可看到进程命令行；
/// 缓解手段 = 包级独立密码 + 密码库加密 + 界面与日志⛔ 不回显命令原文。
/// </para>
/// </summary>
public sealed class ArchiveService
{
    /// <summary>大包压缩/解压的上限（超时即杀进程树，防挂死）。</summary>
    private static readonly TimeSpan LongRun = TimeSpan.FromHours(6);

    private readonly IProcessRunner _runner;
    private readonly BackupSettingsService _settings;

    public ArchiveService(IProcessRunner runner, BackupSettingsService settings)
    {
        _runner = runner;
        _settings = settings;
    }

    /// <summary>收集源目录里实际存在的条目（源全不存在 ⇒ 空集合）。</summary>
    public static IReadOnlyList<string> CollectExisting(IEnumerable<string> sources)
        => sources.Where(s => !string.IsNullOrWhiteSpace(s) && (Directory.Exists(s) || File.Exists(s))).ToList();

    /// <summary>
    /// 建一个加密归档。<paramref name="phase"/> 收到的是**阶段文案**（7z 不流式回报百分比）。
    /// <para>
    /// <paramref name="volumeBytes"/> &gt; 0 时 7z 按该大小**分卷**（产出 <c>包名.7z.001/.002…</c>）；
    /// 🔴 分卷只能在压缩时决定（<c>-v</c>），事后无法切分 —— 邮件投递必须走这里。
    /// </para>
    /// </summary>
    public async Task<ArchiveOutcome> CreateAsync(
        string sevenZip, BackupPlan plan, string archiveFile, string password,
        IProgress<string>? phase, CancellationToken ct, long volumeBytes = 0)
    {
        if (string.IsNullOrWhiteSpace(sevenZip))
            return new ArchiveOutcome(false, false, "没有可用的 7z。", 0);

        var sources = CollectExisting(plan.Sources);
        if (sources.Count == 0)
            return new ArchiveOutcome(false, false, "源目录都不存在（或没指定）：" + string.Join("；", plan.Sources), 0);

        var dir = Path.GetDirectoryName(archiveFile);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(archiveFile)) TryDelete(archiveFile);
        foreach (var stale in ListVolumes(archiveFile)) TryDelete(stale);

        phase?.Report(volumeBytes > 0 ? "压缩 + 加密 + 分卷中…" : "压缩 + 加密中…");

        var args = new List<string>
        {
            "a", "-t7z", "-mhe=on", "-mx=5", "-y", "-p" + password, archiveFile
        };
        if (volumeBytes > 0) args.Add("-v" + Math.Max(1, volumeBytes / (1024 * 1024)) + "m");
        args.AddRange(sources);
        foreach (var pattern in SplitExcludes(plan.Excludes)) args.Add("-xr!" + pattern);

        var res = await _runner.RunAsync(new ProcessRunRequest
        {
            FileName = sevenZip,
            ArgumentList = args,
            StrictOutput = false
        }, LongRun, ct).ConfigureAwait(true);

        if (res.Canceled)
        {
            TryDelete(archiveFile);
            return new ArchiveOutcome(false, true, "已取消（半成品包已删除）。", 0);
        }
        if (!res.Started)
        {
            TryDelete(archiveFile);
            return new ArchiveOutcome(false, false, "启动 7z 失败：" + (res.Error ?? sevenZip), 0);
        }
        if (res.TimedOut)
        {
            TryDelete(archiveFile);
            return new ArchiveOutcome(false, false, "7z 超时被杀（文件太多或磁盘太慢）。", 0);
        }

        var skipped = CountSkipped(res.StdOut);
        var parts = ListVolumes(archiveFile);
        switch (res.ExitCode)
        {
            case 0:
                return new ArchiveOutcome(true, false, "完成。", 0, parts);
            case 1:
                return new ArchiveOutcome(true, false,
                    skipped > 0 ? $"完成（跳过 {skipped} 个被占用/无权限的文件）。" : "完成（7z 报了警告，通常是个别文件被跳过）。",
                    skipped, parts);
            default:
                foreach (var part in parts) TryDelete(part);
                TryDelete(archiveFile);
                return new ArchiveOutcome(false, false, ExplainExit(res.ExitCode, res.StdErr), skipped);
        }
    }

    /// <summary>
    /// 校验一个包（含分卷，传 <c>.001</c> 即可）能否完整解开。
    /// <para>邮件投递前必做：分卷缺一封或压缩半截时，先在本地发现，⛔ 不发到邮箱再等还原失败。</para>
    /// </summary>
    public async Task<(bool Ok, string Message)> TestAsync(
        string sevenZip, string archiveFile, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sevenZip))
            return (false, "没有可用的 7z。");
        if (!File.Exists(archiveFile))
            return (false, "包不存在：" + archiveFile);

        var res = await _runner.RunAsync(new ProcessRunRequest
        {
            FileName = sevenZip,
            ArgumentList = new List<string> { "t", archiveFile, "-y", "-p" + password },
            StrictOutput = false
        }, LongRun, ct).ConfigureAwait(true);

        if (res.Canceled) return (false, "已取消。");
        if (!res.Started) return (false, "启动 7z 失败：" + (res.Error ?? sevenZip));
        if (res.TimedOut) return (false, "7z 校验超时。");
        if (res.ExitCode == 0 || res.ExitCode == 1) return (true, "校验通过。");
        return (false, ExplainExit(res.ExitCode, res.StdErr));
    }

    /// <summary>
    /// 枚举一个包的分卷文件（按序号升序）。无分卷 ⇒ 返回包本体（存在时）；都不存在 ⇒ 空表。
    /// <para>7z 分卷命名 = <c>包名.7z.001</c>，所以按「包名 + .数字」匹配。</para>
    /// </summary>
    public static List<string> ListVolumes(string archiveFile)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(archiveFile)) return result;

        var dir = Path.GetDirectoryName(archiveFile);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            if (File.Exists(archiveFile)) result.Add(archiveFile);
            return result;
        }

        var baseName = Path.GetFileName(archiveFile);
        try
        {
            result.AddRange(Directory.EnumerateFiles(dir, baseName + ".*")
                .Where(p => Path.GetExtension(p).Length == 4 && int.TryParse(Path.GetExtension(p)[1..], out _))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 枚举分卷失败：" + ex.Message);
        }

        if (result.Count == 0 && File.Exists(archiveFile)) result.Add(archiveFile);
        return result;
    }

    /// <summary>
    /// 还原一个归档到 <paramref name="targetDir"/>：
    /// 先解到独立暂存目录，再逐条目校验（拒绝 <c>..</c> / 绝对路径 / 盘符 / 符号链接），最后按策略搬运。
    /// 失败时回滚本次已放进去的文件，不留半截。
    /// </summary>
    public async Task<RestoreOutcome> RestoreAsync(
        string sevenZip, string archiveFile, string password, string targetDir,
        RestoreConflictPolicy policy, IProgress<string>? phase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sevenZip))
            return new RestoreOutcome(false, false, 0, 0, "没有可用的 7z。");
        if (!File.Exists(archiveFile))
            return new RestoreOutcome(false, false, 0, 0, "归档不存在：" + archiveFile);
        if (string.IsNullOrWhiteSpace(targetDir))
            return new RestoreOutcome(false, false, 0, 0, "没指定还原到哪个目录。");

        Directory.CreateDirectory(targetDir);

        var staging = Path.Combine(_settings.StagingRoot, "restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(staging);

        try
        {
            phase?.Report("解包到暂存目录…");
            var res = await _runner.RunAsync(new ProcessRunRequest
            {
                FileName = sevenZip,
                ArgumentList = new List<string> { "x", archiveFile, "-o" + staging, "-y", "-p" + password },
                StrictOutput = false
            }, LongRun, ct).ConfigureAwait(true);

            if (res.Canceled) return new RestoreOutcome(false, true, 0, 0, "已取消（暂存目录已清理）。");
            if (!res.Started) return new RestoreOutcome(false, false, 0, 0, "启动 7z 失败：" + (res.Error ?? sevenZip));
            if (res.TimedOut) return new RestoreOutcome(false, false, 0, 0, "7z 超时被杀。");
            if (res.ExitCode != 0 && res.ExitCode != 1)
                return new RestoreOutcome(false, false, 0, 0, ExplainExit(res.ExitCode, res.StdErr));

            phase?.Report("校验条目名…");
            var files = EnumerateSafeFiles(staging);
            if (files.Error != null)
                return new RestoreOutcome(false, false, 0, 0, "拒绝还原：" + files.Error);

            phase?.Report("搬运到目标目录…");
            var restored = 0;
            var skipped = 0;
            var created = new List<string>();
            try
            {
                foreach (var (full, rel) in files.Items)
                {
                    ct.ThrowIfCancellationRequested();

                    var dest = Path.GetFullPath(Path.Combine(targetDir, rel));
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    if (File.Exists(dest))
                    {
                        if (policy == RestoreConflictPolicy.Skip) { skipped++; continue; }
                        if (policy == RestoreConflictPolicy.Rename) dest = UniquePath(dest);
                    }

                    File.Copy(full, dest, overwrite: policy == RestoreConflictPolicy.Overwrite);
                    created.Add(dest);
                    restored++;
                }
            }
            catch (OperationCanceledException)
            {
                Rollback(created);
                return new RestoreOutcome(false, true, 0, 0, "已取消（本次已放进去的文件已回滚）。");
            }
            catch (Exception ex)
            {
                Rollback(created);
                return new RestoreOutcome(false, false, 0, 0, "搬运失败（已回滚本次放入的文件）：" + ex.Message);
            }

            return new RestoreOutcome(true, false, restored, skipped,
                skipped > 0 ? $"还原完成：{restored} 个文件，跳过 {skipped} 个同名文件。" : $"还原完成：{restored} 个文件。");
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>列出暂存目录里的文件，并做路径穿越 / 链接校验（任一条不合格 ⇒ 整包拒收）。</summary>
    private static (List<(string Full, string Rel)> Items, string? Error) EnumerateSafeFiles(string staging)
    {
        var list = new List<(string, string)>();
        try
        {
            var root = Path.GetFullPath(staging);
            foreach (var full in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(full);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return (list, "包含符号链接：" + info.Name);

                var rel = Path.GetRelativePath(root, full);
                if (Path.IsPathRooted(rel) || rel.Contains(':') || rel.StartsWith("..", StringComparison.Ordinal))
                    return (list, "条目名非法：" + rel);

                var abs = Path.GetFullPath(Path.Combine(root, rel));
                if (!IsUnder(root, abs))
                    return (list, "条目越出暂存目录：" + rel);

                list.Add((abs, rel));
            }
        }
        catch (Exception ex) { return (list, "读取暂存目录失败：" + ex.Message); }
        return (list, null);
    }

    private static bool IsUnder(string root, string full)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        var t = Path.GetFullPath(full);
        return t.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, name + "-" + Guid.NewGuid().ToString("N")[..6] + ext);
    }

    private static void Rollback(List<string> created)
    {
        foreach (var f in created) TryDelete(f);
    }

    private static IEnumerable<string> SplitExcludes(string excludes)
        => (excludes ?? "")
            .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);

    private static int CountSkipped(string stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return 0;
        var n = 0;
        foreach (var line in stdout.Split('\n'))
            if (line.Contains("Can not open", StringComparison.OrdinalIgnoreCase)
                || line.Contains("cannot open", StringComparison.OrdinalIgnoreCase)) n++;
        return n;
    }

    /// <summary>退出码 → 人话（2 与 7 要区分：后者多半是参数 / 路径问题）。</summary>
    public static string ExplainExit(int code, string stderr)
    {
        var detail = string.IsNullOrWhiteSpace(stderr) ? "" : "：" + stderr.Trim();
        return code switch
        {
            2 => "7z 报致命错误（多半是中途中止 / 磁盘满 / 文件被独占）" + detail,
            7 => "7z 命令行参数错误（检查源目录 / 输出路径里是否有异常字符）" + detail,
            8 => "内存不足，7z 无法继续" + detail,
            255 => "用户中断（7z 被主动终止）" + detail,
            _ => "7z 退出码 " + code + detail
        };
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 半成品删不掉就留给用户手工清 */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* 暂存目录删不掉不影响结果 */ }
    }
}
