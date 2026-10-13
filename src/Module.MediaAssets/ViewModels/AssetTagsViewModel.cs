using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>「标签管理」页（NT-014 骨架占位；列表页后续统一升级为 <c>ListPageViewModelBase</c>）。</summary>
public partial class AssetTagsViewModel : ObservableObject, IStatusProvider
{
    /// <summary>Shell 底部状态栏文本（由 Shell 底栏统一显示；setter 保持 public，页面可直接赋值）。</summary>
    [ObservableProperty] private string _statusText = "标签管理 · 骨架占位（后续待办落地）";

    public event EventHandler? StatusChanged;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
}
