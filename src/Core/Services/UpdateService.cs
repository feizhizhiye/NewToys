using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NewToys.Core.Services;

/// <summary>
/// 「启动时检查更新」的<b>真实实现</b>（v1.35）。
/// <para>
/// 做法极简、可验证：GET 一个**纯文本版本地址**（<c>IAppConfigService.UpdateUrl</c>，例如
/// 一个 raw 文件或对象存储里的 <c>version.txt</c>），取回文本里第一个形如 <c>1.20261010.00.10</c>
/// 的版本号，与本程序版本比较；比本程序新 → 回报「有新版本」。
/// </para>
/// <para>
/// ⛔ 诚实边界：
/// <list type="bullet">
///   <item>没配地址 → 直接跳过并如实说「未配置更新地址」，<b>不假装检查过</b>；</item>
///   <item>本程序<b>不会自己下载 / 覆盖自己</b> —— 只提示有新版本，更新仍由用户手动完成；</item>
///   <item>任何网络异常都吞掉并返回失败原因，绝不阻塞启动（超时 8 秒）。</item>
/// </list>
/// </para>
/// </summary>
public class UpdateService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>上一次检查的失败原因（成功或跳过时为空）。</summary>
    public string LastError { get; private set; } = "";

    /// <summary>
    /// 当前程序版本（取 <c>AssemblyInformationalVersion</c>，即编译时自动生成的 <c>1.yyyyMMdd.HH.mm0</c>）。
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            try
            {
                var asm = Assembly.GetEntryAssembly();
                var info = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                {
                    // 形如 "1.20261010.00.10+abcdef" —— 去掉 + 后面的元数据
                    var plus = info.IndexOf('+');
                    return (plus > 0 ? info[..plus] : info).Trim();
                }
                return asm?.GetName().Version?.ToString() ?? "0.0.0.0";
            }
            catch { return "0.0.0.0"; }
        }
    }

    /// <summary>
    /// 检查一次更新。
    /// </summary>
    /// <param name="url">纯文本版本地址；空 = 未配置（跳过）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>(是否有新版本, 远端版本号, 说明)。</returns>
    public async Task<(bool HasUpdate, string RemoteVersion, string Message)> CheckAsync(
        string? url, CancellationToken ct = default)
    {
        LastError = "";
        var current = CurrentVersion;

        if (string.IsNullOrWhiteSpace(url))
            return (false, "", $"未配置更新地址（当前版本 {current}）—— 可在「设置 → 通用」里填一个版本文件地址后生效。");

        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            var text = await http.GetStringAsync(url, ct).ConfigureAwait(false);

            var remote = ExtractVersion(text);
            if (remote == null)
                return (false, "", "更新地址返回的内容里没找到版本号（期望形如 1.20261010.00.10）。");

            var cmp = Compare(remote, current);
            return cmp > 0
                ? (true, remote, $"发现新版本 {remote}（当前 {current}）—— 本程序不会自动替换自己，请手动更新。")
                : (false, remote, cmp == 0
                    ? $"已是最新版本（{current}）。"
                    : $"线上版本 {remote} 低于当前 {current}，无需更新。");
        }
        catch (OperationCanceledException)
        {
            return (false, "", "检查更新已取消。");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return (false, "", "检查更新失败：" + ex.Message);
        }
    }

    /// <summary>从一段文本里抠出第一个 <c>a.b.c.d</c> 形式的版本号。</summary>
    private static string? ExtractVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 逐字符扫，找出「数字 + 至少两组 .数字」的最长片段（宽容：允许末尾少几段）
        var best = "";
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsDigit(text[i])) { i++; continue; }

            var start = i;
            var parts = 0;
            while (i < text.Length)
            {
                if (char.IsDigit(text[i])) { i++; continue; }
                if (text[i] == '.') { parts++; i++; continue; }
                break;
            }
            if (parts >= 1)
            {
                var cand = text[start..i].TrimEnd('.');
                if (cand.Length > best.Length) best = cand;
            }
        }
        return best.Length > 0 ? best : null;
    }

    /// <summary>版本号比较（逐段按数字比；非数字段按文本比）。返回 &gt;0 表示 a 更新。</summary>
    public static int Compare(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        var n = Math.Max(pa.Length, pb.Length);

        for (var i = 0; i < n; i++)
        {
            var sa = i < pa.Length ? pa[i] : "0";
            var sb = i < pb.Length ? pb[i] : "0";

            if (long.TryParse(sa, NumberStyles.Integer, CultureInfo.InvariantCulture, out var na)
                && long.TryParse(sb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nb))
            {
                if (na != nb) return na > nb ? 1 : -1;
            }
            else
            {
                var c = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c > 0 ? 1 : -1;
            }
        }
        return 0;
    }
}
