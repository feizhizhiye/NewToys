using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.Settings;

/// <summary>
/// 应用级设置的菜单注册（NT-062）：原 <c>Module.Settings</c> 的框架职责收归 Shell ——
/// 它本就是框架组件（只是名字里带「Module」），撤销该程序集后由这里注册。
/// <para>
/// v3.66 / NT-062 细节调整（用户 2026-10-16 反馈）：**「设置」一级菜单项已删除** ——
/// 应用级设置是**一个整页**（六个节），模块设置各自归到**所属模块的一级菜单下**（见各模块 <c>Initialize</c>）。
/// </para>
/// <para>
/// 🔴 应用级设置页的入口 = 左树底部**版本行的设置齿轮** + `Ctrl+,` + 首页「一键操作」的 `⚙`
/// （跳 `settings-launch`，归一成页 Key `settings`）。
/// </para>
/// <para>
/// 但五个**节锚点 Key** + `settings-all` 仍必须注册：<c>ShellViewModel.OpenTab</c> 靠
/// <c>IMenuService.FindItem</c> 解析（首页收藏卡 / 默认收藏种子 <c>settings-dns</c> / <c>settings-general</c> /
/// 功能清单页），⛔ 不注册这些入口会静默失效。故把它们挂在一个 <c>IsHome</c> 的**不渲染宿主分组**上：
/// 左树（<c>BuildTree</c>）、首页模块计数、功能清单页都按 <c>!IsHome</c> 过滤 ⇒ 它不出现在任何菜单里。
/// </para>
/// </summary>
internal static class AppSettingsRegistration
{
    /// <summary>不渲染宿主分组键：<c>IsHome=true</c> ⇒ 左树 / 首页计数 / 功能清单页都跳过它。</summary>
    private const string HiddenGroupKey = "system";

    public static void RegisterMenu(IServiceProvider sp)
    {
        var menu = sp.GetRequiredService<IMenuService>();

        // v3.66：原 `RegisterModule("settings", "设置", …)` **已删除** —— 左树不再有「设置」一级菜单项。
        //        应用级设置页的六个节 + 功能清单页改挂不渲染宿主分组（Key 全部保留，⛔ 一律不改归属语义）。
        menu.RegisterModule(HiddenGroupKey, "系统", "IconSettings", 999, isHome: true);

        // ⚠️ Key 全部是 v3.62 之前发布过的**节锚点**（§5.1 Key 一经发布⛔不改；首页跳转 / 收藏白名单 /
        //    托盘「打开设置」按 Key 寻址）⇒ 只改标题与内容，不改 Key。Order 只作宿主组内排序。
        menu.RegisterFunction(HiddenGroupKey, "settings-general", "启动与退出", "⚙", "SettingsView", 0,
            "开机启动 / 托盘 / 关闭按钮行为", SettingsCategory.General);
        // v3.66 第五轮：原「通用」节（一页塞三组设置）**拆成三个节**（用户要求"拆开、删除通用"）——
        // `settings-general`（启动与退出，Key ⛔ 不变，首页收藏卡 / 收藏白名单仍按它寻址）+
        // 下面两个新节锚点（`settings-update` / `settings-theme`，后者即 v3.40 合并前的页 Key）。
        menu.RegisterFunction(HiddenGroupKey, "settings-update", "更新", "🔄", "SettingsView", 1,
            "版本文件地址与手动检查更新", SettingsCategory.Update);
        menu.RegisterFunction(HiddenGroupKey, "settings-theme", "主题外观", "🎨", "SettingsView", 1,
            "明暗模式与强调色", SettingsCategory.Theme);
        // v3.53 / NT-056：「路径配置」+「配置」合并为一张**设置总表**（分类列区分行类型）；
        // v3.62 起它是应用级设置页的「运行环境」节（配置档 + 外部程序路径），标题保持「路径与配置」。
        menu.RegisterFunction(HiddenGroupKey, "settings-path", "路径与配置", "📁", "SettingsView", 2,
            "运行环境路径 + 多套配置档（分类列区分，可新增 / 切换 / 重命名 / 删除）", SettingsCategory.Environment);
        // 「启动配置」→「一键按钮」：首页三个一键按钮的**全部**配置都在这页（名称 / 程序 / 参数 / 网址）。
        menu.RegisterFunction(HiddenGroupKey, "settings-launch", "一键按钮", "🚀", "SettingsView", 3,
            "首页一键按钮的程序与网址", SettingsCategory.Launch);
        menu.RegisterFunction(HiddenGroupKey, "settings-dns", "加密 DNS", "🛡", "SettingsView", 4,
            "加密 DNS 与网络配置", SettingsCategory.Network);
        menu.RegisterFunction(HiddenGroupKey, "settings-data", "数据管理", "💾", "SettingsView", 6,
            "备份 / 导入 config 配置", SettingsCategory.Data);

        // 「关于」节（版本 / 功能清单入口 / 重载模块）**无节锚点 Key**（需求 §4.9.2 表格 = "—（页内可点）"）
        // ⇒ ⛔ 不在此注册，只从设置页的节里进入。

        // 功能清单（Key `settings-all` ⛔ 不改）：v3.65 起是**独立页**（⛔ 不在设置内），
        // v3.66 起**同时内嵌在首页**（收藏卡尾部「显示更多」展开）；独立页仍保留 —— 标题栏 `＋` 与
        // 「关于」节的按钮都按 Key 寻址到它（当"选择器"用，不切走当前页）。
        menu.RegisterFunction(HiddenGroupKey, "settings-all", "功能清单", "📋", "AllFunctionsView", 7,
            "所有模块与功能总览（收藏卡是它的常用子集）");
    }
}
