using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「全部资产」的批量重命名入口：**多选 + 规则对话框**（⛔ 不做逐个弹输入框）。
/// <para>
/// 与详情卡「↗ 应用到文件」的分工：那边一次改一个（手输名字），这里按规则批量改；落盘通路完全相同
/// （<c>FileOperationService.Rename</c>：日志先行 + 库里路径同步 + 同名冲突策略）。
/// </para>
/// </summary>
public partial class AssetAllViewModel
{
    /// <summary>批量重命名弹层的 VM（规则 / 预览 / 落盘）。</summary>
    public BatchRenameViewModel BatchRename { get; } = new();

    /// <summary>批量重命名的启用条件：≥2 个选中（只改一个走详情卡的「↗ 应用到文件」）。</summary>
    public bool CanBatchRename => SelectedRows.Count >= 2;

    [RelayCommand]
    private void BatchRenameSelected()
    {
        var rows = BatchRows();
        if (rows.Count < 2)
        {
            StatusText = "批量重命名要选中至少 2 个资产（只改一个请用详情卡的「↗ 应用到文件」）。";
            return;
        }

        var targets = new List<BatchRenameTarget>();
        var skipped = 0;
        foreach (var row in rows)
        {
            var loc = PrimaryLocationOf(row);
            if (loc is null || !File.Exists(loc.Path)) { skipped++; continue; }
            targets.Add(new BatchRenameTarget(row, loc));
        }

        if (targets.Count == 0)
        {
            StatusText = "选中的资产都没有可用的文件位置（没登记位置，或文件已不在磁盘上），没法改名。";
            return;
        }

        if (targets.Count < 2)
        {
            StatusText = "只有 1 个资产能改名（其余没登记位置 / 文件已不在）：请用详情卡的「↗ 应用到文件」。";
            return;
        }

        BatchRename.Attach(_fileOps, _opLog);
        BatchRename.Open(targets, skipped, "批量重命名（全部资产）", message =>
        {
            StatusText = message;
            Reload();
        });
    }
}
