using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.ScriptManager.Models;

/// <summary>详情卡「分类 chips」的一项（功能标签：设置 / 脚本 / 定时任务）。（WHY: SM-003）</summary>
public partial class CategoryPick : ObservableObject
{
    public CategoryPick(string name) => Name = name;

    /// <summary>标签文案（= 写进 <c>QuickAction.Category</c> 的值）。</summary>
    public string Name { get; }

    [ObservableProperty] private bool _isSelected;
}
