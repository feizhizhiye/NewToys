using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NewToys.Core.Services;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// 关于节「组件版本」列表的一行：一个<b>已加载</b>程序集的名字 / 版本 / 编译时间。
/// </summary>
public sealed class LoadedComponentItem
{
    public required string Name { get; init; }

    /// <summary>版本串 = 编译时自动生成的 <c>1.yyMMdd.HH.mm0</c>（已去 <c>+sha</c> 元数据）。</summary>
    public required string Version { get; init; }

    /// <summary>从 <see cref="Version"/> 解析出的编译时间，形如 <c>2026-10-30 01:40</c>；解析不出为空。</summary>
    public string BuiltAt { get; init; } = "";

    /// <summary>与外壳版本的差异提示（比外壳新 / 旧）；一致时为空 —— 只对模块项有意义。</summary>
    public string Note { get; init; } = "";
}

/// <summary>「组件版本」的取数器：读当前进程已加载的程序集，不加载任何新程序集、不读磁盘。</summary>
public static class LoadedComponentProbe
{
    public static (IReadOnlyList<LoadedComponentItem> Shell, IReadOnlyList<LoadedComponentItem> Modules) Collect()
    {
        var shellVersion = ShellVersion();
        var shell = new List<LoadedComponentItem>();
        var modules = new List<LoadedComponentItem>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            var name = asm.GetName().Name;
            if (string.IsNullOrEmpty(name)) continue;

            var isModule = name.StartsWith("Module.", StringComparison.Ordinal);
            var isShell = name == "NewToys" || name.StartsWith("NewToys.", StringComparison.Ordinal);
            if (!isModule && !isShell) continue;

            var version = ReadVersion(asm);
            var item = new LoadedComponentItem
            {
                Name = name,
                Version = version,
                BuiltAt = BuildTimeText(version),
                Note = isModule ? DiffNote(version, shellVersion) : "",
            };
            (isModule ? modules : shell).Add(item);
        }

        // 入口程序集置顶，其余按名升序 —— 列表位置稳定，便于每次刷新后对照。
        return (shell.OrderBy(i => i.Name == "NewToys" ? 0 : 1).ThenBy(i => i.Name, StringComparer.Ordinal).ToList(),
                modules.OrderBy(i => i.Name, StringComparer.Ordinal).ToList());
    }

    private static string ShellVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly();
            return asm == null ? "0.0.0.0" : ReadVersion(asm);
        }
        catch { return "0.0.0.0"; }
    }

    private static string ReadVersion(Assembly asm)
    {
        try
        {
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // .NET SDK 默认追加 "+<commit sha>"，展示时去掉
                var plus = info.IndexOf('+');
                return (plus > 0 ? info[..plus] : info).Trim();
            }
            return asm.GetName().Version?.ToString() ?? "0.0.0.0";
        }
        catch { return "0.0.0.0"; }
    }

    /// <summary>从 <c>1.yyMMdd.HH.mm0</c>（兼容历史 <c>1.yyyyMMdd....</c>）解析编译时间；格式不符返回空串。</summary>
    private static string BuildTimeText(string version)
    {
        var p = version.Split('.');
        if (p.Length < 4) return "";

        var date = p[1];
        string year, month, day;
        if (date.Length == 6) { year = "20" + date[..2]; month = date[2..4]; day = date[4..6]; }
        else if (date.Length == 8) { year = date[..4]; month = date[4..6]; day = date[6..8]; }
        else return "";

        if (!int.TryParse(p[2], out var hour) || !int.TryParse(p[3], out var minute)) return "";
        if (minute > 59) minute /= 10;          // 历史 4 段版本末段是 "mm0"，取整到十位

        if (hour is < 0 or > 23 || minute is < 0 or > 59) return "";
        return $"{year}-{month}-{day} {hour:00}:{minute:00}";
    }

    /// <summary>
    /// 与外壳版本比较。日期段先归一成 8 位 —— 老 DLL（<c>1.20261030.…</c>）与新外壳（<c>1.261030.…</c>）
    /// 直接按数字比会把「20261030 &gt; 261030」误判成"较新"。
    /// </summary>
    private static string DiffNote(string version, string shellVersion)
    {
        try
        {
            var cmp = UpdateService.Compare(Normalize(version), Normalize(shellVersion));
            return cmp > 0 ? "⚠ 比外壳新" : cmp < 0 ? "⚠ 比外壳旧" : "";
        }
        catch { return ""; }
    }

    private static string Normalize(string version)
    {
        var p = version.Split('.');
        if (p.Length >= 2 && p[1].Length == 6) p[1] = "20" + p[1];
        return string.Join('.', p);
    }
}
