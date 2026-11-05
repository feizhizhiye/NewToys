using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NewToys.WebTools.Services;

/// <summary>平台底座：系统 hosts 位置与 DNS 缓存刷新。（WHY: WT-009）</summary>
public interface IHostsPlatform
{
    /// <summary>本平台是否支持改系统 hosts。</summary>
    bool IsSupported { get; }

    /// <summary>系统 hosts 文件全路径。</summary>
    string SystemHostsPath { get; }

    /// <summary>刷新 DNS 缓存（应用方案后调用，失败不影响写入结果）。</summary>
    Task<(bool Ok, string Msg)> FlushDnsAsync();
}

/// <summary>Windows 实现：<c>%SystemRoot%\System32\drivers\etc\hosts</c> + <c>ipconfig /flushdns</c>。</summary>
public sealed class WindowsHostsPlatform : IHostsPlatform
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public string SystemHostsPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public async Task<(bool Ok, string Msg)> FlushDnsAsync()
    {
        if (!IsSupported) return (false, "当前平台不支持刷新 DNS 缓存");

        try
        {
            using var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p is null) return (false, "没能启动 ipconfig");

            await p.WaitForExitAsync().ConfigureAwait(false);
            return p.ExitCode == 0
                ? (true, "已刷新 DNS 缓存")
                : (false, $"刷新 DNS 缓存失败（ipconfig 退出码 {p.ExitCode}）");
        }
        catch (Exception ex)
        {
            return (false, "刷新 DNS 缓存失败：" + ex.Message);
        }
    }
}
