using NewToys.Shared.Services;
using Xunit;

namespace NewToys.Tests;

/// <summary>
/// <see cref="ViewNaming"/> 的回归测试：页面解析与静态检查共用这套约定。
/// <para>
/// 约定一旦漂移，表现是「页面打得开、整页空白、无异常」，编译期和运行期都不报错，
/// 因此这里把边界（无 View 后缀 / 空串 / 命名空间不合规）逐条钉死。
/// </para>
/// </summary>
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
