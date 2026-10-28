using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>7z 探测结果。</summary>
public sealed record SevenZipStatus(bool Found, string Path, string Version, string Message);

/// <summary>
/// 找 7z 可执行文件并读版本号。
/// <para>
/// 顺序：用户显式指定（配了但文件不在 ⇒ 直接判不可用，⛔ 不偷偷换别的）→ 常见安装位 → <c>PATH</c> 里的 <c>7z</c>。
/// 只起一次进程读版本文本（<c>7z</c> 不带参数打印横幅），结果缓存到 <see cref="Reset"/> 或下次显式探测。
/// </para>
/// </summary>
public sealed class SevenZipLocator
{
    private static readonly Regex VersionRe = new(@"7-Zip\s+([0-9][0-9.]*)", RegexOptions.Compiled);

    private readonly IProcessRunner _runner;
    private readonly BackupSettingsService _settings;
    private SevenZipStatus? _cache;

    public SevenZipLocator(IProcessRunner runner, BackupSettingsService settings)
    {
        _runner = runner;
        _settings = settings;
    }

    /// <summary>丢掉缓存（改了设置 / 用户点了重新探测）。</summary>
    public void Reset() => _cache = null;

    /// <summary>探测（<paramref name="force"/> = 忽略缓存）。</summary>
    public async Task<SevenZipStatus> LocateAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && _cache != null) return _cache;

        var configured = _settings.ConfiguredSevenZip;
        if (configured.Length > 0)
        {
            _cache = File.Exists(configured)
                ? await ProbeAsync(configured, ct).ConfigureAwait(true)
                : new SevenZipStatus(false, configured, "", "设置里指定的 7z 路径不存在，请重新指定或清空走自动探测。");
            return _cache;
        }

        foreach (var candidate in CommonPaths())
        {
            if (!File.Exists(candidate)) continue;
            var status = await ProbeAsync(candidate, ct).ConfigureAwait(true);
            if (status.Found) { _cache = status; return _cache; }
        }

        var onPath = await ProbeAsync("7z", ct).ConfigureAwait(true);
        _cache = onPath.Found
            ? onPath
            : new SevenZipStatus(false, "", "", "没找到 7z：请安装 7-Zip，或在本页「归档引擎」里手动指定 7z.exe 路径。");
        return _cache;
    }

    private static IEnumerable<string> CommonPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFiles))
            yield return Path.Combine(programFiles, "7-Zip", "7z.exe");
        if (!string.IsNullOrEmpty(programFilesX86))
            yield return Path.Combine(programFilesX86, "7-Zip", "7z.exe");
    }

    private async Task<SevenZipStatus> ProbeAsync(string file, CancellationToken ct)
    {
        try
        {
            var res = await _runner.RunAsync(new ProcessRunRequest
            {
                FileName = file,
                Arguments = "",
                StrictOutput = false
            }, TimeSpan.FromSeconds(15), ct).ConfigureAwait(true);

            if (!res.Started)
                return new SevenZipStatus(false, file, "", res.Error ?? "无法启动：" + file);

            var text = res.StdOut + "\n" + res.StdErr;
            var m = VersionRe.Match(text);
            if (!m.Success)
                return new SevenZipStatus(false, file, "", "这个文件不是 7-Zip（读不到版本横幅）。");

            return new SevenZipStatus(true, file, m.Groups[1].Value, "7-Zip " + m.Groups[1].Value);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new SevenZipStatus(false, file, "", ex.Message);
        }
    }
}
