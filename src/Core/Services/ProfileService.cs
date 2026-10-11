using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IProfileService"/> 的实现：包一层 <see cref="ConfigStoreService"/> 的配置目录 API，
/// 切换时只写 `active-profile.txt`，然后重启进程。
/// <para>本类在 <c>Core</c>，⛔ 不引用 Avalonia —— 重启走 <see cref="Process"/> + <see cref="Environment.Exit(int)"/>。</para>
/// </summary>
public class ProfileService : IProfileService
{
    private readonly ConfigStoreService _cfg;

    public ProfileService(ConfigStoreService cfg) => _cfg = cfg;

    public string ActiveProfile => _cfg.ActiveProfileName;

    public IReadOnlyList<string> Profiles => _cfg.ListProfiles();

    public string GetDirectory(string name) => _cfg.GetProfileDirectory(name);

    public bool IsValidName(string? name) => ConfigStoreService.IsValidProfileName(name);

    public void SwitchTo(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("非法的配置名", nameof(name));
        if (!_cfg.ListProfiles().Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"配置不存在：{name}");

        _cfg.SetActiveProfile(name);
    }

    public void Create(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("非法的配置名", nameof(name));
        if (_cfg.ListProfiles().Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"配置已存在：{name}");

        Directory.CreateDirectory(_cfg.GetProfileDirectory(name));
    }

    public void Rename(string oldName, string newName)
    {
        if (!IsValidName(newName)) throw new ArgumentException("非法的配置名", nameof(newName));
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;
        if (_cfg.ListProfiles().Any(p => string.Equals(p, newName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"配置已存在：{newName}");

        var src = _cfg.GetProfileDirectory(oldName);
        if (!Directory.Exists(src)) throw new InvalidOperationException($"配置不存在：{oldName}");

        Directory.Move(src, _cfg.GetProfileDirectory(newName));

        // 改的正好是当前配置 → 指针跟着改（否则指针指向一个已不存在的目录）
        if (string.Equals(oldName, _cfg.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
            _cfg.SetActiveProfile(newName);
    }

    public void Delete(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("非法的配置名", nameof(name));
        if (string.Equals(name, _cfg.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("不能删除当前正在使用的配置。");

        var dir = _cfg.GetProfileDirectory(name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// 重启：先起一个新实例，成功后再退出自己。
    /// ⚠️ 新实例**起不来时不退出** —— 宁可留在当前窗口，也不能让用户"点一下就没窗口了"。
    /// </summary>
    public void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch
        {
            return;
        }

        Environment.Exit(0);
    }
}
