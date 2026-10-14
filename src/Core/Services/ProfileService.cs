using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IProfileService"/> 的实现：包一层 <see cref="ConfigStoreService"/> 的配置目录 API。
/// <para>v3.26 起切换**不写指针**：切换 = 以目标配置起新进程并退出自己（显式传 <c>--profile</c>）；
/// 写全局默认（<c>active-profile.txt</c>）只走 <see cref="SetAsDefault"/> 与首次迁移。</para>
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

    /// <summary>
    /// 校验切换目标（v3.26 起**不再写指针**——切换 = 重启到目标配置，见 <see cref="RestartApp(string)"/>；
    /// 写全局默认请用 <see cref="SetAsDefault"/>）。
    /// </summary>
    public void SwitchTo(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("非法的配置名", nameof(name));
        if (!_cfg.ListProfiles().Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"配置不存在：{name}");
    }

    /// <summary>把 <paramref name="name"/> 设为**全局默认**（写 active-profile.txt；影响以后无参启动的实例）。</summary>
    public void SetAsDefault(string name)
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

    // ==================== 内置配置档（NT-038，需求 §3.5） ====================

    public IReadOnlyList<string> BuiltInProfiles => ConfigStoreService.BuiltInProfiles;

    public void EnsureBuiltInProfiles() => _cfg.EnsureBuiltInProfiles();

    /// <summary>
    /// 把内置档（debug / release）重置为初始态：debug = 复制 default 的 config；release = 清空。
    /// ⛔ 拒绝 default（真实数据）与当前活动档（本实例在用）；具体校验在 <see cref="ConfigStoreService.ResetBuiltInProfile"/>。
    /// </summary>
    public void ResetToInitial(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("非法的配置名", nameof(name));
        if (!_cfg.ListProfiles().Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"配置不存在：{name}");
        _cfg.ResetBuiltInProfile(name);
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

        // 改的正好是**全局默认指针**指向的配置 → 指针跟着改（否则指针指向一个已不存在的目录）
        // ⚠️ 判的是指针文件而不是 ActiveProfileName——本实例可能由 --profile 启动，两者不同（v3.26）
        if (string.Equals(oldName, _cfg.PointerProfileName, StringComparison.OrdinalIgnoreCase))
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
    /// 以指定配置起一个新实例，成功后再退出自己（v3.26：显式传 <c>--profile</c>，
    /// ⛔ 不依赖也不写全局默认指针——多实例下写指针会偷改别的窗口下次无参启动的默认）。
    /// ⚠️ 新实例**起不来时不退出** —— 宁可留在当前窗口，也不能让用户"点一下就没窗口了"。
    /// </summary>
    public void RestartApp(string profile)
    {
        // ⚠️ 新实例起不来时不退出 —— 宁可留在当前窗口，也不能让用户"点一下就没窗口了"
        if (StartWithProfile(profile))
            Environment.Exit(0);
    }

    /// <summary>以指定配置**新开一个实例**（v3.26，需求 §3.5）：不退出当前窗口。</summary>
    public void LaunchNewInstance(string profile)
    {
        if (!IsValidName(profile)) throw new ArgumentException("非法的配置名", nameof(profile));
        StartWithProfile(profile);
    }

    /// <summary>带 <c>--profile</c> 起一个新进程（配置名可能含空格 → 参数加引号）；返回是否发起成功。</summary>
    private static bool StartWithProfile(string profile)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            Process.Start(new ProcessStartInfo(exe, $"--profile \"{profile}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
