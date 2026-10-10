using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NewToys.Shared.Services;

public class LaunchRunner
{
    public void OpenBrowser(string? exePath, string? argsTemplate, IEnumerable<string> urls)
    {
        if (urls == null) return;
        foreach (var raw in urls)
        {
            var url = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(url) || url.StartsWith("#")) continue;
            try
            {
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = CombineArgs(argsTemplate, url),
                        UseShellExecute = false
                    });
                }
                else
                {
                    OpenUrlDefault(url);
                }
            }
            catch { }
        }
    }

    public void LaunchPrograms(IEnumerable<(string exe, string args)> progs)
    {
        if (progs == null) return;
        foreach (var p in progs)
        {
            var exe = (p.exe ?? "").Trim();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = p.args ?? "",
                    UseShellExecute = false
                });
            }
            catch { }
        }
    }

    public static void OpenUrlDefault(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
        }
    }

    public static string CombineArgs(string? template, string url)
    {
        if (string.IsNullOrWhiteSpace(template)) return Quote(url);
        return template.Contains("{url}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{url}", url, StringComparison.OrdinalIgnoreCase)
            : (template + " " + Quote(url)).Trim();
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
}