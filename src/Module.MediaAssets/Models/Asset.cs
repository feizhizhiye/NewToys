using System;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 资产主表一行（`assets`，需求 §6.6 数据模型）：**一个资产可有多份文件位置**（见 <see cref="FileLocation"/>），
/// 因此文件名 / 大小 / 路径这些**文件属性不在本表**，只在位置表。
/// </summary>
public class Asset
{
    public long Id { get; set; }

    /// <summary>类型，取 <see cref="AssetTypes"/>。</summary>
    public string Type { get; set; } = AssetTypes.Other;

    /// <summary>扫描时由文件名推出的标题（只读语义，用户改的是 <see cref="DisplayName"/>）。</summary>
    public string? Title { get; set; }

    /// <summary>用户在库里改的显示名（⛔ 不改磁盘文件名，物理改名是显式操作）。</summary>
    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    /// <summary>状态，取 <see cref="AssetStatuses"/>。</summary>
    public string Status { get; set; } = AssetStatuses.Active;

    /// <summary>评分 0~5。</summary>
    public int Rating { get; set; }

    /// <summary>类型专属元数据（JSON 文本；不预拆扩展表，需求 §6.6）。</summary>
    public string MetadataJson { get; set; } = "{}";

    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    /// <summary>界面显示用名称：优先显示名，其次标题，最后回退「资产 #id」。</summary>
    public string EffectiveName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName!
        : !string.IsNullOrWhiteSpace(Title) ? Title!
        : $"资产 #{Id}";
}
