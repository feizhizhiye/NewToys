using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.SystemTools.Models;

/// <summary>
/// 系统工具里的一条 <b>命令条目</b>（NT-091）：执行完<b>只产生副作用、不产出数据</b>的那一类
/// （打开设置面板 / 开关注册表 / 跑一条命令行命令）。
/// <para>
/// 🔴 与 ScriptManager 的条目模型是<b>两套</b>（模块禁互引 ⇒ 各持一份）：本模块条目落
/// <c>config\system\commands.json</c>，⛔ 不共用 script 的存储，也不带集合 / 采集器 / 保留策略那些字段。
/// </para>
/// </summary>
public sealed partial class SystemCommand : ObservableObject
{
    /// <summary>稳定标识（导入 / 去重 / 迁移判重用）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _category = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _command = "";

    /// <summary>命令行宿主：<c>cmd</c> / <c>pwsh</c> / <c>auto</c>（字符串，免跨模块引枚举）。</summary>
    [ObservableProperty] private string _shell = "auto";

    [ObservableProperty] private bool _needAdmin;
    [ObservableProperty] private bool _runHidden = true;
    [ObservableProperty] private bool _isFavorite;

    /// <summary>列表里的收藏标记（★ / 空）。</summary>
    public string FavoriteMark => IsFavorite ? "★" : "";

    /// <summary>列表里的宿主显示名。</summary>
    public string ShellText => Shell switch
    {
        "cmd" => "cmd",
        "pwsh" => "pwsh",
        _ => "自动"
    };
}
