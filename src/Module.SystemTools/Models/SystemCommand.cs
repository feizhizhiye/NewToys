using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.SystemTools.Models;

/// <summary>系统工具里的一条 命令条目：执行完只产生副作用、不产出数据的那一类 （打开设置面板 / 开关注册表 / 跑一条命令行命令）。（WHY: ST-004）</summary>
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

    /// <summary>收藏翻转 ⇒ 通知列表里的 ★ 标记（行模板直接绑 <see cref="FavoriteMark"/>）。</summary>
    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteMark));

    /// <summary>列表里的宿主显示名。</summary>
    public string ShellText => Shell switch
    {
        "cmd" => "cmd",
        "pwsh" => "pwsh",
        _ => "自动"
    };
}
