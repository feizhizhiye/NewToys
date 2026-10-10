using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IQuickActionService"/> 实现：落到 <c>&lt;应用根&gt;\config\quickactions.json</c>。
/// <para>
/// 首次运行（文件不存在）灌入一组**内置的常用「系统设置 + 脚本」种子**，开箱即用；之后用户改动直接覆盖该文件。
/// 落点选在 <c>config\</c>：「设置 → 数据管理」的备份 / 导入（<see cref="ConfigStoreService"/>）天然覆盖它。
/// </para>
/// <para>
/// 删除的二次确认通过静态钩子 <see cref="ConfirmHandler"/> 外抛 —— <c>Core</c> 不引用 Avalonia，
/// 由宿主（<c>App.axaml.cs</c>）接到模态框上，与 <see cref="PackageManagerService"/> 同款做法。
/// </para>
/// </summary>
public class QuickActionService : IQuickActionService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ConfigStoreService _cfg;
    private readonly List<QuickAction> _items;

    /// <summary>删除前的二次确认钩子（宿主注入；为 null 时直接删除）。</summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    public QuickActionService(ConfigStoreService cfg)
    {
        _cfg = cfg;
        var (items, isNew) = Load();
        _items = items;
        if (isNew) Persist();   // 首次运行：把种子落盘，让文件与界面一致
    }

    public IReadOnlyList<QuickAction> Items => _items;
    public IReadOnlyList<QuickAction> Favorites => _items.Where(i => i.IsFavorite).ToList();
    public string FilePath => Path.Combine(_cfg.ConfigDir, "quickactions.json");

    public event Action? Changed;

    public QuickAction Add()
    {
        var item = new QuickAction
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "新的设置项",
            Category = "未分类",
            Description = "",
            Command = "",
            NeedAdmin = false,
            IsFavorite = false
        };
        _items.Add(item);
        Persist();
        return item;
    }

    public void Save() => Persist();

    public async Task<bool> RemoveAsync(QuickAction item)
    {
        if (item == null || !_items.Contains(item)) return false;
        if (ConfirmHandler != null)
        {
            var ok = await ConfirmHandler($"确定删除「{item.Title}」吗？此操作不可撤销。").ConfigureAwait(false);
            if (!ok) return false;
        }
        _items.Remove(item);
        Persist();
        return true;
    }

    public void ToggleFavorite(QuickAction item)
    {
        if (item == null) return;
        item.IsFavorite = !item.IsFavorite;
        Persist();
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items, JsonOpts));
        }
        catch { /* 保存失败不打断 UI；下一次改动会再试 */ }
        Changed?.Invoke();
    }

    private (List<QuickAction> items, bool isNew) Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<QuickAction>>(File.ReadAllText(FilePath));
                if (list != null)
                {
                    foreach (var i in list)
                        if (string.IsNullOrEmpty(i.Id)) i.Id = Guid.NewGuid().ToString("N");   // 老文件兜底
                    return (list, false);
                }
            }
        }
        catch { /* 配置损坏 → 回落到默认种子，不阻断启动 */ }
        return (BuildSeed(), true);
    }

    /// <summary>内置种子：常用「系统设置项」+「脚本 / 打开入口」。命令仅作展示与编辑，本轮不执行。</summary>
    private static List<QuickAction> BuildSeed()
    {
        static QuickAction A(string title, string category, string desc, string cmd, bool admin = false) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Category = category,
            Description = desc,
            Command = cmd,
            NeedAdmin = admin,
            IsFavorite = false
        };

        return new List<QuickAction>
        {
            // ---------- 系统设置（注册表 / 电源等） ----------
            A("显示文件扩展名", "系统设置", "在资源管理器里显示已知文件类型的扩展名",
              @"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /t REG_DWORD /d 0 /f"),
            A("隐藏文件扩展名", "系统设置", "恢复为不显示已知文件的扩展名",
              @"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /t REG_DWORD /d 1 /f"),
            A("显示隐藏文件", "系统设置", "资源管理器里显示隐藏的文件与文件夹",
              @"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v Hidden /t REG_DWORD /d 1 /f"),
            A("任务栏时钟显示秒", "系统设置", "在任务栏时钟里显示秒数",
              @"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ShowSecondsInSystemClock /t REG_DWORD /d 1 /f"),
            A("启用休眠", "系统设置", "开启系统休眠（会生成 hiberfil.sys）",
              "powercfg /hibernate on", admin: true),
            A("关闭休眠", "系统设置", "关闭系统休眠并删除休眠文件（释放磁盘空间）",
              "powercfg /hibernate off", admin: true),
            A("切换到高性能电源计划", "系统设置", "把当前电源计划切到「高性能」",
              "powercfg /setactive SCHEME_MIN"),
            A("切换到平衡电源计划", "系统设置", "把当前电源计划切回「平衡」",
              "powercfg /setactive SCHEME_BALANCED"),

            // ---------- 网络 ----------
            A("刷新 DNS 缓存", "网络", "清空本机 DNS 解析缓存（域名解析异常时常用）",
              "ipconfig /flushdns"),
            A("释放并重新获取 IP", "网络", "ipconfig /release 后再 /renew，重连 DHCP",
              "ipconfig /release & ipconfig /renew", admin: true),
            A("查看网络配置", "网络", "显示全部网卡的 IP / 网关 / DNS 明细",
              "ipconfig /all"),

            // ---------- 维护 ----------
            A("重启资源管理器", "维护", "结束并重启 explorer.exe（任务栏 / 桌面卡死时用）",
              "taskkill /f /im explorer.exe & start explorer.exe"),
            A("清理用户临时文件", "维护", "清空当前用户的 %TEMP% 目录",
              @"del /q /f ""%TEMP%\*"" & rd /s /q ""%TEMP%"""),
            A("系统文件检查", "维护", "sfc /scannow，扫描并修复系统文件（较慢）",
              "sfc /scannow", admin: true),
            A("检查磁盘（C:）", "维护", "chkdsk C: /scan，在线扫描磁盘错误",
              "chkdsk C: /scan", admin: true),

            // ---------- 打开（控制面板 / 管理工具入口） ----------
            A("打开控制面板", "打开", "启动经典控制面板",
              "control"),
            A("打开系统属性", "打开", "系统属性（计算机名 / 高级 / 环境变量）",
              "sysdm.cpl"),
            A("打开程序和功能", "打开", "卸载或更改已安装的程序",
              "appwiz.cpl"),
            A("打开设备管理器", "打开", "查看与管理硬件设备",
              "devmgmt.msc"),
            A("打开服务", "打开", "查看与管理 Windows 服务",
              "services.msc"),
            A("打开磁盘清理", "打开", "清理系统盘临时文件与旧更新",
              "cleanmgr"),
            A("打开任务管理器", "打开", "启动任务管理器",
              "taskmgr"),
        };
    }
}
