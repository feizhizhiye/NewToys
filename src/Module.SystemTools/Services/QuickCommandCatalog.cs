using System.Collections.Generic;

namespace NewToys.SystemTools.Services;

/// <summary>
/// 一条**快捷命令**（NT-091 `system-quick`）：本机常见设置 / 开关的一键入口。
/// <para>
/// 🔴 **无提权机制**（需求 §2.3.7③）：需要管理员的项 <see cref="NeedsAdmin"/> = true ⇒
/// 一律降级为「生成命令 + 📋 复制」供用户自己跑，⛔ 不假装调用成功。
/// </para>
/// </summary>
public sealed class QuickCommandItem
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>可直接执行的命令（cmd 语法）；空 = 这一项只能打开设置页。</summary>
    public string Command { get; init; } = "";

    /// <summary>设置页 / 控制面板入口（<c>ms-settings:</c> / <c>*.cpl</c>）；空 = 没有入口。</summary>
    public string Uri { get; init; } = "";

    /// <summary>需要管理员的命令（⛔ 不执行，只生成给用户自跑）。</summary>
    public string AdminCommand { get; init; } = "";

    public bool NeedsAdmin => AdminCommand.Length > 0;

    /// <summary>按钮上显示的目标摘要（悬浮看得到）。</summary>
    public string TargetText => NeedsAdmin ? AdminCommand : (Command.Length > 0 ? Command : Uri);
}

/// <summary>
/// 十项快捷命令清单（NT-091）：电源计划 / 主题明暗 / 休眠 / 网卡启禁 / 磁盘清理 /
/// UAC / 开发者模式 / 远程桌面 / 代理开关 / 时间与区域。
/// <para>⛔ 不写注册表 / 不调 netsh 提权 —— 需要管理员的项只给命令。</para>
/// </summary>
public static class QuickCommandCatalog
{
    public static IReadOnlyList<QuickCommandItem> All { get; } = new[]
    {
        new QuickCommandItem
        {
            Title = "电源计划",
            Description = "打开电源选项（控制面板）；切换计划 / 改睡眠时间",
            Command = "control powercfg.cpl,,3",
            Uri = "ms-settings:powersleep"
        },
        new QuickCommandItem
        {
            Title = "主题明暗",
            Description = "切换应用浅色 / 深色（改 HKCU，无需管理员）",
            Command = "reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize\" /v AppsUseLightTheme /t REG_DWORD /f"
        },
        new QuickCommandItem
        {
            Title = "休眠开关",
            Description = "开启 / 关闭休眠（要管理员）",
            AdminCommand = "powercfg /hibernate on"
        },
        new QuickCommandItem
        {
            Title = "网卡启禁",
            Description = "启用 / 禁用网卡（要管理员）",
            AdminCommand = "netsh interface set interface \"以太网\" admin=disable"
        },
        new QuickCommandItem
        {
            Title = "磁盘清理",
            Description = "启动磁盘清理工具",
            Command = "cleanmgr"
        },
        new QuickCommandItem
        {
            Title = "UAC",
            Description = "用户账户控制设置（改 HKLM，要管理员）",
            Uri = "ms-settings:privacy-security",
            AdminCommand = "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v EnableLUA /t REG_DWORD /f"
        },
        new QuickCommandItem
        {
            Title = "开发者模式",
            Description = "打开「开发者选项」设置页",
            Uri = "ms-settings:developers"
        },
        new QuickCommandItem
        {
            Title = "远程桌面",
            Description = "打开远程桌面设置（开关本身要管理员）",
            Uri = "ms-settings:remotedesktop",
            AdminCommand = "reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server\" /v fDenyTSConnections /t REG_DWORD /f"
        },
        new QuickCommandItem
        {
            Title = "代理开关",
            Description = "打开代理设置页（脚本改代理要管理员）",
            Uri = "ms-settings:network-proxy",
            AdminCommand = "netsh winhttp set proxy 127.0.0.1:8080"
        },
        new QuickCommandItem
        {
            Title = "时间与区域",
            Description = "打开日期 / 时间设置，或控制面板的时钟",
            Command = "timedate.cpl",
            Uri = "ms-settings:dateandtime"
        }
    };
}
