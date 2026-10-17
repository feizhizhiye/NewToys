using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 详情卡「分类 chips」的一项（功能标签：设置 / 脚本 / 定时任务）。
/// <para>
/// 🔴 与资产库的 <c>PickItem</c> 同构但**故意不共用**：跨模块引用 ⛔（模块禁互引），
/// 本页也不需要色块 / 分组 ⇒ 只留 <see cref="Name"/> + <see cref="IsSelected"/>。
/// </para>
/// </summary>
public partial class CategoryPick : ObservableObject
{
    public CategoryPick(string name) => Name = name;

    /// <summary>标签文案（= 写进 <c>QuickAction.Category</c> 的值）。</summary>
    public string Name { get; }

    [ObservableProperty] private bool _isSelected;
}
