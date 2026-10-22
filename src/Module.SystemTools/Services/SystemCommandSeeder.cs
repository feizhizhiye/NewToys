using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewToys.SystemTools.Models;

namespace NewToys.SystemTools.Services;

/// <summary>
/// 一条命令库预设（NT-097）：<see cref="SystemCommand"/> 的播种用投影（Id 固定 ⇒ 幂等）。
/// </summary>
internal sealed record PresetCommand(
    string Id, string Title, string Category, string Description, string Command, bool NeedAdmin = false);

/// <summary>
/// 命令库<b>随包预设</b>的播种器（NT-097 / 需求 §6.7 v3.90）：Windows 常用命令（设置面板 / 控制面板 /
/// 管理工具）+ 快捷设置（原 <c>system-quick</c> 十项，已补全缺 <c>/d</c> 取值的 reg 命令）+ winget 命令。
/// <para>
/// 🔴 <b>只播种一次</b>：<c>config\system\commands.seeded</c> 标记存在 ⇒ 整体跳过 —— 用户删掉的预设
/// <b>不会复活</b>（与 <c>ExampleSeeder</c> 同一口径）。按 <see cref="SystemCommand.Id"/> <b>只加不改</b>：
/// 用户改过标题 / 命令的预设不被覆盖。
/// </para>
/// <para>
/// 🔴 全部预设 <c>Shell = "cmd"</c>：<c>reg</c> / <c>netsh</c> / <c>winget</c> / <c>start</c> / <c>.cpl</c> / <c>.msc</c>
/// 在 <c>cmd /c</c> 下行为最稳（pwsh 会把逗号当数组运算符、对引号转义更敏感）。
/// 需要管理员的项 <see cref="SystemCommand.NeedAdmin"/> = true ⇒ App ⛔ 不代跑，只复制命令供用户在提权终端自跑。
/// </para>
/// </summary>
public static class SystemCommandSeeder
{
    /// <summary>播种完成标记（内容 = 程序集版本）。存在 ⇒ 本次及以后都不再播种。</summary>
    public const string MarkerFileName = ".seeded";

    /// <summary>
    /// 需要时播种一次（幂等）。
    /// </summary>
    /// <returns>状态文案（<c>""</c> = 什么都没做 / 已播种过）。</returns>
    public static string Seed(ISystemCommandStore store)
    {
        if (store is null) return "";

        var dir = Path.GetDirectoryName(store.FilePath);
        if (string.IsNullOrEmpty(dir)) return "";
        var marker = Path.Combine(dir, MarkerFileName);
        if (File.Exists(marker)) return "";   // 🔴 只播种一次：删掉的预设不复活

        try
        {
            var existing = store.Items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            var added = 0;
            foreach (var p in Presets)
            {
                if (!existing.Add(p.Id)) continue;   // 已有（用户留着的 / 本次刚加的）⇒ 跳过，⛔ 不覆盖
                store.Insert(new SystemCommand
                {
                    Id = p.Id,
                    Title = p.Title,
                    Category = p.Category,
                    Description = p.Description,
                    Command = p.Command,
                    Shell = "cmd",
                    NeedAdmin = p.NeedAdmin,
                    RunHidden = true
                });
                added++;
            }

            File.WriteAllText(marker, AssemblyVersion);
            return added > 0 ? $"已播种 {added} 条预设命令（首次运行的一次性礼物，删了不会再回来）。" : "";
        }
        catch (Exception ex)
        {
            // 播种失败 ⇒ 不写标记，下次启动再试；⛔ 不拦启动。
            return "⚠ 命令库预设播种失败：" + ex.Message;
        }
    }

    private static string AssemblyVersion
        => typeof(SystemCommandSeeder).Assembly.GetName().Version?.ToString() ?? "0";

    // ==================== 预设清单（四类 + winget） ====================

    private static readonly PresetCommand[] Presets =
    {
        // ---- 系统设置（ms-settings: 入口） ----
        new("st-set-main",        "设置主页",        "系统设置", "打开 Windows 设置主页", "start ms-settings:"),
        new("st-set-display",     "显示设置",        "系统设置", "分辨率 / 缩放 / 显示方向", "start ms-settings:display"),
        new("st-set-sound",       "声音设置",        "系统设置", "输出 / 输入设备与音量", "start ms-settings:sound"),
        new("st-set-notify",      "通知设置",        "系统设置", "应用通知 / 勿扰", "start ms-settings:notifications"),
        new("st-set-power",       "电源和睡眠",      "系统设置", "睡眠时间 / 电源按钮行为", "start ms-settings:powersleep"),
        new("st-set-storage",     "存储设置",        "系统设置", "空间感知 / 清理建议", "start ms-settings:storage"),
        new("st-set-bluetooth",   "蓝牙和设备",      "系统设置", "蓝牙 / 打印机 / 鼠标", "start ms-settings:bluetooth"),
        new("st-set-network",     "网络和 Internet", "系统设置", "Wi-Fi / 拨号 / 高级网络设置", "start ms-settings:network"),
        new("st-set-personal",    "个性化（背景/颜色）", "系统设置", "桌面背景 / 主题色 / 任务栏", "start ms-settings:personalization"),
        new("st-set-apps",        "应用设置",        "系统设置", "已安装应用 / 默认应用 / 启动项", "start ms-settings:apps"),
        new("st-set-accounts",    "账户设置",        "系统设置", "账户 / 登录选项 / 家庭", "start ms-settings:accounts"),
        new("st-set-datetime",    "时间和语言",      "系统设置", "日期时间 / 时区 / 语言", "start ms-settings:dateandtime"),
        new("st-set-privacy",     "隐私和安全性",    "系统设置", "权限 / Windows 安全中心", "start ms-settings:privacy"),
        new("st-set-update",      "Windows 更新",    "系统设置", "更新历史 / 暂停更新", "start ms-settings:windowsupdate"),
        new("st-set-developers",  "开发者选项",      "系统设置", "开发人员模式 / 设备发现", "start ms-settings:developers"),
        new("st-set-gaming",      "游戏设置",        "系统设置", "游戏栏 / 捕获 / 游戏模式", "start ms-settings:gaming"),
        new("st-set-about",       "关于本机",        "系统设置", "规格 / 系统版本 / 重命名", "start ms-settings:about"),

        // ---- 控制面板（.cpl） ----
        new("st-cpl-control",     "控制面板",        "控制面板", "经典控制面板主页", "control"),
        new("st-cpl-programs",    "程序和功能",      "控制面板", "卸载或更改已装程序", "appwiz.cpl"),
        new("st-cpl-power",       "电源选项",        "控制面板", "电源计划 / 高级电源设置", "control powercfg.cpl,,3"),
        new("st-cpl-system",      "系统属性",        "控制面板", "计算机名 / 硬件 / 高级", "sysdm.cpl"),
        new("st-cpl-env",         "环境变量",        "控制面板", "用户 / 系统环境变量编辑框", "rundll32 sysdm.cpl,EditEnvironmentVariables"),
        new("st-cpl-datetime",    "日期和时间",      "控制面板", "时钟 / 时区 / 附加时钟", "timedate.cpl"),
        new("st-cpl-sound",       "声音（旧版面板）", "控制面板", "播放 / 录制 / 声音 选项卡", "mmsys.cpl"),
        new("st-cpl-mouse",       "鼠标属性",        "控制面板", "按键 / 指针 / 滚轮", "main.cpl"),
        new("st-cpl-network",     "网络连接",        "控制面板", "适配器列表（启停 / 属性）", "ncpa.cpl"),
        new("st-cpl-internet",    "Internet 选项",   "控制面板", "连接 / 安全 / 高级", "inetcpl.cpl"),

        // ---- 管理工具（.msc 与系统程序） ----
        new("st-msc-devmgr",      "设备管理器",      "管理工具", "查看 / 更新 / 回滚驱动", "devmgmt.msc"),
        new("st-msc-diskmgmt",    "磁盘管理",        "管理工具", "分区 / 盘符 / 初始化磁盘", "diskmgmt.msc"),
        new("st-msc-services",    "服务",            "管理工具", "启动类型 / 服务状态 / 登录身份", "services.msc"),
        new("st-msc-tasks",       "任务计划程序",    "管理工具", "计划任务 / 触发器 / 历史", "taskschd.msc"),
        new("st-msc-eventvwr",    "事件查看器",      "管理工具", "系统 / 应用日志与筛选", "eventvwr.msc"),
        new("st-msc-perfmon",     "性能监视器",      "管理工具", "实时计数器 / 数据收集器", "perfmon.msc"),
        new("st-msc-resmon",      "资源监视器",      "管理工具", "CPU / 内存 / 磁盘 / 网络占用", "resmon"),
        new("st-msc-firewall",    "高级安全防火墙",  "管理工具", "入站 / 出站规则", "wf.msc"),
        new("st-msc-msinfo",      "系统信息",        "管理工具", "硬件 / 组件 / 环境详细清单", "msinfo32"),
        new("st-tool-taskmgr",    "任务管理器",      "管理工具", "进程 / 性能 / 启动 / 用户", "taskmgr"),
        new("st-tool-cleanmgr",   "磁盘清理",        "管理工具", "清理临时文件 / 系统文件", "cleanmgr"),
        new("st-tool-notepad",    "记事本",          "管理工具", "纯文本编辑器", "notepad"),
        new("st-tool-calc",       "计算器",          "管理工具", "标准 / 科学 / 程序员模式", "calc"),

        // ---- 快捷设置（原 system-quick 十项 + 常用开关） ----
        new("st-quick-dark",      "切换深色模式",    "快捷设置", "改 HKCU，无需管理员；0 = 深色 / 1 = 浅色，改完即时生效",
            "reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize\" /v AppsUseLightTheme /t REG_DWORD /d 0 /f"),
        new("st-quick-fileext",   "显示文件扩展名",  "快捷设置", "0 = 显示 / 1 = 隐藏；改完刷新资源管理器即生效",
            "reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\" /v HideFileExt /t REG_DWORD /d 0 /f"),
        new("st-quick-dns",       "刷新 DNS 缓存",   "快捷设置", "清掉本机 DNS 解析缓存（换 hosts / 换 DNS 后用）", "ipconfig /flushdns"),
        new("st-quick-hibernate", "开启休眠",        "快捷设置", "powercfg /hibernate on（要管理员）", "powercfg /hibernate on", NeedAdmin: true),
        new("st-quick-nic",       "禁用网卡「以太网」", "快捷设置", "适配器名按本机实际改（ncpa.cpl 里看）；要管理员",
            "netsh interface set interface \"以太网\" admin=disable", NeedAdmin: true),
        new("st-quick-uac",       "关闭 UAC",        "快捷设置", "EnableLUA：0 = 关闭 / 1 = 开启；改完需重启（要管理员）",
            "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v EnableLUA /t REG_DWORD /d 0 /f", NeedAdmin: true),
        new("st-quick-rdp",       "开启远程桌面",    "快捷设置", "fDenyTSConnections：0 = 允许 / 1 = 禁止；要管理员",
            "reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server\" /v fDenyTSConnections /t REG_DWORD /d 0 /f", NeedAdmin: true),
        new("st-quick-proxy",     "设置系统代理",    "快捷设置", "netsh winhttp set proxy 127.0.0.1:8080（地址端口按实际改；要管理员）",
            "netsh winhttp set proxy 127.0.0.1:8080", NeedAdmin: true),
        new("st-quick-netreset",  "重置网络配置",    "快捷设置", "netsh int ip reset（TCP/IP 栈复位；要管理员）", "netsh int ip reset", NeedAdmin: true),
        new("st-quick-sfc",       "系统文件检查",    "快捷设置", "sfc /scannow 扫描并修复受损系统文件（要管理员，耗时数分钟）", "sfc /scannow", NeedAdmin: true),

        // ---- winget（源与包） ----
        new("st-winget-srclist",  "查看软件源",      "winget",  "winget source list：当前源与是否显式", "winget source list"),
        new("st-winget-srcreset", "恢复默认软件源",  "winget",  "winget source reset：回到微软默认源（要管理员）", "winget source reset", NeedAdmin: true),
        new("st-winget-list",     "已安装列表",      "winget",  "winget list：本机已装包（含版本 / 源）", "winget list"),
        new("st-winget-upgrade",  "可升级列表",      "winget",  "winget upgrade：有新版可升的包", "winget upgrade"),
        new("st-winget-upall",    "升级全部可升级包", "winget", "winget upgrade --all：一次性升所有（已 pin 的会跳过）", "winget upgrade --all"),
        new("st-winget-pinlist",  "查看锁定列表",    "winget",  "winget pin list：被 pin（暂停更新）的包", "winget pin list"),
        new("st-winget-export",   "导出已装清单",    "winget",  "winget export：存成 json（默认落当前目录，可在命令里改 -o 路径）",
            "winget export -o winget-export.json"),
        new("st-winget-import",   "按清单安装",      "winget",  "winget import -i winget-export.json：按导出清单批量装回", "winget import -i winget-export.json"),
    };
}
