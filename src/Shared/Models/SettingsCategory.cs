namespace NewToys.Shared.Models;

/// <summary>菜单项 / 设置项的显式分类（，需求 §4.9.2 第 4 条）。（WHY: SH-023）</summary>
public enum SettingsCategory
{
    /// <summary>不是设置项（普通功能页）。默认值 —— 非设置项的分组走它。</summary>
    None = 0,

    /// <summary>应用级 · 通用 —— 现为**启动与退出**（v3.66 第五轮「通用」节拆分后只留启动 / 托盘行为）。节锚点 `settings-general`。</summary>
    General = 1,

    /// <summary>应用级 · 更新（版本号 / 版本文件地址 / 立即检查）。节锚点 `settings-update`（v3.66 第五轮新增）。v3.67：「关于」已并入本节。</summary>
    Update = 8,

    /// <summary>应用级 · 主题外观（明暗模式 / 强调色）。节锚点 `settings-theme`（v3.66 第五轮复原）。</summary>
    Theme = 9,

    /// <summary>应用级 · 一键按钮（首页三个一键按钮的配置）。节锚点 `settings-launch`。</summary>
    Launch = 2,

    /// <summary>应用级 · 运行环境（配置档 + 外部程序路径）。节锚点 `settings-path`。</summary>
    Environment = 3,

    /// <summary>应用级 · 网络（加密 DNS）。节锚点 `settings-dns`。</summary>
    Network = 4,

    /// <summary>应用级 · 数据（备份 / 导入 config）。节锚点 `settings-data`。</summary>
    Data = 5,

    /// <summary>应用级 · 关于（版本 / 组件版本 / 重载模块）。v3.67 起**已并入 <see cref="Update"/>**（节锚点 `settings-about` 退成内部别名），枚举值保留以兼容旧数据。</summary>
    About = 6,

    /// <summary>模块级设置（View/VM 归模块、自注册进 `settings` 组，需求 §4.9）。</summary>
    Module = 7
}
