using NewToys.Shared.Services;
using Xunit;

namespace NewToys.Tests;

/// <summary>ViewNaming 的回归测试：页面解析与静态检查共用这套约定。</summary>
public class ViewNamingTests
{
    [Theory]
    [InlineData("AssetAllView", "AssetAllViewModel")]
    [InlineData("ShellView", "ShellViewModel")]
    [InlineData("SettingsPage", "SettingsPageViewModel")]   // 没有 View 后缀 ⇒ 直接追加
    [InlineData("View", "ViewModel")]                       // 只有一个 View ⇒ 剥成空 + ViewModel
    public void ViewModelName_FollowsConvention(string viewName, string expected)
        => Assert.Equal(expected, ViewNaming.ViewModelName(viewName));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ViewModelName_EmptyInput_ReturnsBareViewModel(string? viewName)
        => Assert.Equal("ViewModel", ViewNaming.ViewModelName(viewName!));

    [Theory]
    [InlineData("NewToys.Module.VideoTools.Views", "NewToys.Module.VideoTools.ViewModels")]
    [InlineData("NewToys.Shell.Views", "NewToys.Shell.ViewModels")]
    public void ViewModelNamespace_ViewsSuffix_ReplacesLastSegment(string ns, string expected)
        => Assert.Equal(expected, ViewNaming.ViewModelNamespace(ns));

    [Theory]
    [InlineData("NewToys.Module.VideoTools.Controls")]  // ⛔ 不是 .Views ⇒ 交回调用方兜底
    [InlineData("")]
    [InlineData(null)]
    public void ViewModelNamespace_NonViewsNamespace_ReturnsNull(string? ns)
        => Assert.Null(ViewNaming.ViewModelNamespace(ns));
}
