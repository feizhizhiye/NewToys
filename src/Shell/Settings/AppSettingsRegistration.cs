using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.Settings;

/// <summary>应用级设置的菜单注册：原 Module.Settings 的框架职责收归 Shell —— 它本就是框架组件（只是名字里带「Module」），撤销该程序集后由这里注册。（WHY: SHL-029）</summary>
internal static class AppSettingsRegistration
{
    /// <summary>不渲染宿主分组键：<c>IsHome=true</c> ⇒ 左树 / 首页计数 / 功能清单页都跳过它。</summary>
    private const string HiddenGroupKey = "system";

    public static void RegisterMenu(IServiceProvider sp)
    {
        var menu = sp.GetRequiredService<IMenuService>();

        // 原 `RegisterModule("settings", "设置", …)` **已删除** —— 左树不再有「设置」一级菜单项。
        //        应用级设置页的六个节 + 功能清单页改挂不渲染宿主分组（Key 全部保留，⛔ 一律不改归属语义）。
        menu.RegisterModule(HiddenGroupKey, "系统", "IconSettings", 999, isHome: true);

        // ⚠️ Key 全部是 v3.62 之前发布过的**节锚点**（§5.1 Key 一经发布⛔不改；首页跳转 / 收藏白名单 /
        //    托盘「打开设置」按 Key 寻址）⇒ 只改标题与内容，不改 Key。Order 只作宿主组内排序。
        menu.RegisterFunction<Views.SettingsView>(HiddenGroupKey, "settings-general", "启动与退出", "⚙", 0,
            "开机启动 / 托盘 / 关闭按钮行为", SettingsCategory.General);
        // 原「通用」节（一页塞三组设置）**拆成三个节**（用户要求"拆开、删除通用"）——
        // `settings-general`（启动与退出，Key ⛔ 不变，首页收藏卡 / 收藏白名单仍按它寻址）+
        // `settings-theme`（主题外观，即 v3.40 合并前的页 Key）。
        menu.RegisterFunction<Views.SettingsView>(HiddenGroupKey, "settings-theme", "主题外观", "🎨", 1,
            "明暗模式与强调色", SettingsCategory.Theme);
        // 「路径配置」+「配置」合并为一张**设置总表**（分类列区分行类型）；
        // 它是应用级设置页的「运行环境」节（配置档 + 外部程序路径），标题保持「路径与配置」。
        menu.RegisterFunction<Views.SettingsView>(HiddenGroupKey, "settings-path", "路径与配置", "📁", 2,
            "运行环境路径 + 多套配置档（分类列区分，可新增 / 切换 / 重命名 / 删除）", SettingsCategory.Environment);
        // 「启动配置」→「一键按钮」：首页三个一键按钮的**全部**配置都在这页（名称 / 程序 / 参数 / 网址）。
        menu.RegisterFunction<Views.SettingsView>(HiddenGroupKey, "settings-launch", "一键按钮", "🚀", 3,
            "首页一键按钮的程序与网址", SettingsCategory.Launch);
        menu.RegisterFunction<Views.SettingsView>(HiddenGroupKey, "settings-dns", "加密 DNS", "🛡", 4,
            "加密 DNS 与网络配置", SettingsCategory.Network);
        menu.RegisterFunction<Views.SettingsView>(HiddenGroupKey, "settings-data", "数据管理", "💾", 6,
            "备份 / 导入 config 配置", SettingsCategory.Data);

        // 🔴 v3.67（用户 2026-10-18 拍板）：原「更新」+「关于」**合并**成「版本与更新」节
        //    （两者都围绕版本号）⇒ 8 节变 7 节。Key 保留 `settings-update`（原菜单 Key，⛔ 不改）；
        //    `settings-about` 退成**内部别名**（SettingsViewModel.OpenSection 里映射，⛔ 不再注册）。
        menu.RegisterFunction<Views.SettingsView>(HiddenGroupKey, "settings-update", "版本与更新", "🔄", 8,
            "版本号 / 检查更新 / 组件版本 / 重载模块", SettingsCategory.Update);

        // 功能清单不再是独立页：⛔ 不注册 `settings-all` —— 它是首页收藏卡尾部「显示更多」就地展开的
        // 内嵌视图（复用 AllFunctionsView / AllFunctionsViewModel，见 HomeViewModel），没有独立标签页。
    }
}
