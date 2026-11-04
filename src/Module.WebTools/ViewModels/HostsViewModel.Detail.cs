using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// hosts 工作台的**详情卡部分**（条目编辑器）：选中任一侧的一行 → 改 IP / 主机名 / 注释 / 启用，
/// 或新增 / 删除条目。侧别（系统 hosts 草稿 / 拉取方案）决定改动落到 <c>_draftText</c> 还是 <c>HostsProfile.Entries</c>。
/// </summary>
public partial class HostsViewModel
{
    /// <summary>重投影时抑制选中联动（否则会互相清空、编辑目标乱跳）。</summary>
    private bool _suppressSelectionSync;

    /// <summary>当前编辑的是左侧（系统草稿）还是右侧（方案）。</summary>
    private bool _editIsSystem;

    [ObservableProperty] private string _editIp = "";
    [ObservableProperty] private string _editHost = "";
    [ObservableProperty] private string _editComment = "";
    [ObservableProperty] private bool _editEnabled = true;
    [ObservableProperty] private string _editTargetText = "";
    [ObservableProperty] private string _editDiffText = "";

    public bool HasSelection => SelectedSystemRow != null || SelectedProfileRow != null;

    public bool HasNoSystemRows => IsListMode && SystemRows.Count == 0;

    public bool HasNoProfileRows => ProfileRows.Count == 0;

    /// <summary>右栏方案有条目且左栏不是备份快照 ⇒ 允许「应用到左侧」。</summary>
    public bool CanApplyToTarget => !TargetIsBackup && SelectedProfile is { Entries.Count: > 0 };

    /// <summary>备份快照只读；方案侧恒可编辑。</summary>
    public bool EditIsEditable => _editIsSystem ? !TargetIsBackup : true;

    public bool EditIsSystemSide => _editIsSystem;

    partial void OnSelectedSystemRowChanged(HostsLine? value)
    {
        if (_suppressSelectionSync) return;
        if (value is null)
        {
            if (SelectedProfileRow is null) ClearEditBuffer();
            return;
        }

        if (SelectedProfileRow != null)
        {
            _suppressSelectionSync = true;
            SelectedProfileRow = null;
            _suppressSelectionSync = false;
        }
        LoadEditBuffer(value, true);
    }

    partial void OnSelectedProfileRowChanged(HostsLine? value)
    {
        if (_suppressSelectionSync) return;
        if (value is null)
        {
            if (SelectedSystemRow is null) ClearEditBuffer();
            return;
        }

        if (SelectedSystemRow != null)
        {
            _suppressSelectionSync = true;
            SelectedSystemRow = null;
            _suppressSelectionSync = false;
        }
        LoadEditBuffer(value, false);
    }

    private void LoadEditBuffer(HostsLine row, bool isSystem)
    {
        _editIsSystem = isSystem;
        EditIp = row.Ip;
        EditHost = row.Host;
        EditComment = row.Comment;
        EditEnabled = row.Enabled;
        EditDiffText = row.Mark switch
        {
            "＋" => "＋ 只在右栏（拉取的方案）有 —— 应用到左侧后会新增",
            "－" => "－ 只在左栏（系统 hosts）有 —— 应用方案时会被移除",
            "≠" => "≠ 两侧 IP 不同 —— 以右栏为准时会改掉本行",
            _ => "两侧一致"
        };
        EditTargetText = isSystem
            ? (TargetIsBackup ? "备份快照" : "系统 hosts") + $" · 第 {row.No} 行" + (row.InManaged ? " · 受管块" : " · 用户手写行")
            : $"方案「{SelectedProfile?.Name}」· 第 {row.No} 条";

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(EditIsEditable));
        OnPropertyChanged(nameof(EditIsSystemSide));
    }

    private void ClearEditBuffer()
    {
        EditIp = "";
        EditHost = "";
        EditComment = "";
        EditEnabled = true;
        EditTargetText = "";
        EditDiffText = "";
        OnPropertyChanged(nameof(HasSelection));
    }

    // ==================== 条目增删改 ====================

    [RelayCommand]
    private void SaveEntry()
    {
        if (!HasSelection) { StatusText = "先点选一行。"; return; }
        if (!IsValidEntry(out var why)) { StatusText = "⚠ " + why; return; }

        if (_editIsSystem)
        {
            if (TargetIsBackup) { StatusText = "⚠ 备份快照只读。"; return; }

            var row = SelectedSystemRow;
            if (row is null) return;

            var all = HostsService.ParseLines(_draftText);
            var target = all.FirstOrDefault(r => r.No == row.No);
            if (target is null) { StatusText = "⚠ 行号已变化，请重读后再改。"; return; }

            target.IsEntry = true;
            target.Ip = EditIp.Trim();
            target.Host = EditHost.Trim();
            target.Comment = EditComment.Trim();
            target.Enabled = EditEnabled;

            _draftText = HostsService.RenderLines(all);
            IsDirty = true;
            RefreshRows();
            StatusText = $"已改草稿第 {row.No} 行（点「写入系统 hosts」落盘）。";
            return;
        }

        var entry = GetSelectedEntry();
        if (entry is null) return;

        entry.Ip = EditIp.Trim();
        entry.Host = EditHost.Trim();
        entry.Comment = EditComment.Trim();
        entry.Enabled = EditEnabled;
        RefreshRows();
        StatusText = "已改右栏条目（点「保存方案」落库）。";
    }

    [RelayCommand]
    private void AddEntry()
    {
        var ip = string.IsNullOrWhiteSpace(EditIp) ? "127.0.0.1" : EditIp.Trim();
        var host = string.IsNullOrWhiteSpace(EditHost) ? "example.com" : EditHost.Trim();

        if (_editIsSystem || SelectedProfile is null)
        {
            if (TargetIsBackup) { StatusText = "⚠ 备份快照只读。"; return; }

            _draftText = _draftText.TrimEnd('\n') + "\n" + HostsText.RenderEntry(ip, host, EditComment, EditEnabled);
            IsDirty = true;
            RefreshRows();
            StatusText = "已往草稿末尾加一条（点「写入系统 hosts」落盘）。";
            return;
        }

        SelectedProfile.Entries.Add(new HostsEntry { Ip = ip, Host = host, Comment = EditComment.Trim(), Enabled = EditEnabled });
        RefreshRows();
        StatusText = "已往右栏加一条（点「保存方案」落库）。";
    }

    [RelayCommand]
    private async Task DeleteEntryAsync()
    {
        if (!HasSelection) { StatusText = "先点选一行。"; return; }

        if (_editIsSystem)
        {
            if (TargetIsBackup) { StatusText = "⚠ 备份快照只读。"; return; }

            var row = SelectedSystemRow;
            if (row is null) return;

            var ok = await DialogHooks.ConfirmAsync($"从草稿里删除第 {row.No} 行？\n（点「写入系统 hosts」才落盘）");
            if (!ok) return;

            var all = HostsService.ParseLines(_draftText);
            all.RemoveAll(r => r.No == row.No);
            _draftText = HostsService.RenderLines(all);
            IsDirty = true;
            ClearEditBuffer();
            _suppressSelectionSync = true;
            SelectedSystemRow = null;
            _suppressSelectionSync = false;
            RefreshRows();
            StatusText = "已从草稿删除该行。";
            return;
        }

        var entry = GetSelectedEntry();
        if (entry is null || SelectedProfile is null) return;

        var no = SelectedProfileRow?.No ?? 0;
        SelectedProfile.Entries.Remove(entry);
        RefreshRows();
        StatusText = $"已删除右栏第 {no} 条（点「保存方案」落库）。";
    }

    [RelayCommand]
    private async Task CheckEntryAsync()
    {
        if (_check is null) return;

        var entry = GetCheckTarget();
        if (entry is null) { StatusText = "⚠ 只有条目行能检测（空行 / 注释行不能）。"; return; }

        StatusText = $"正在检测 {entry.Host}…";
        var options = new HostsCheckOptions
        {
            TimeoutSeconds = _settings?.HostsCheckTimeoutSeconds ?? 3,
            Concurrency = 1
        };
        await _check.CheckAsync(new List<HostsEntry> { entry }, options, null, default);
        EditDiffText = entry.StatusText;
        RefreshRows();
        StatusText = $"{entry.Host} → {entry.StatusText}";
    }

    // ==================== 取数 ====================

    private bool IsValidEntry(out string why)
    {
        if (string.IsNullOrWhiteSpace(EditHost)) { why = "主机名不能为空。"; return false; }
        if (!System.Net.IPAddress.TryParse(EditIp.Trim(), out _)) { why = "IP 不是合法地址。"; return false; }
        if (EditHost.Any(char.IsWhiteSpace)) { why = "主机名不能含空格。"; return false; }
        why = "";
        return true;
    }

    /// <summary>右栏选中行对应的方案条目（按行号 1 起取）。</summary>
    private HostsEntry? GetSelectedEntry()
    {
        if (SelectedProfile is not { } profile || SelectedProfileRow is not { } row) return null;
        var index = row.No - 1;
        return index >= 0 && index < profile.Entries.Count ? profile.Entries[index] : null;
    }

    /// <summary>检测目标：右栏直接取条目；左栏取当前编辑值（不落草稿）。</summary>
    private HostsEntry? GetCheckTarget()
    {
        if (!_editIsSystem) return GetSelectedEntry();
        if (!IsValidEntry(out _)) return null;

        return new HostsEntry { Ip = EditIp.Trim(), Host = EditHost.Trim(), Comment = EditComment.Trim(), Enabled = true };
    }

    // ==================== 详情卡 ====================

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(HasSelection),
        EmptyText = "在左右列表里点一行，这里改它的 IP / 主机名 / 注释 / 启用",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(EditTargetText)
                    },
                    new DetailField
                    {
                        Label = "IP",
                        ValuePath = nameof(EditIp),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        EnabledWhen = nameof(EditIsEditable),
                        Placeholder = "127.0.0.1"
                    },
                    new DetailField
                    {
                        Label = "主机名",
                        ValuePath = nameof(EditHost),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        EnabledWhen = nameof(EditIsEditable),
                        Placeholder = "example.com"
                    },
                    new DetailField
                    {
                        Label = "注释（写成 # 后面的文字）",
                        ValuePath = nameof(EditComment),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        EnabledWhen = nameof(EditIsEditable)
                    },
                    new DetailField
                    {
                        Label = "启用（取消勾选 = 行首加 # ）",
                        ValuePath = nameof(EditEnabled),
                        Kind = FieldKind.Check,
                        Commit = EditCommitMode.Instant,
                        EnabledWhen = nameof(EditIsEditable)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        ValuePath = nameof(EditDiffText)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Divider
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "检测结果（右栏方案，点工具条「检测」刷新）：",
                        VisibleWhen = nameof(HasCheckReport)
                    },
                    new DetailField
                    {
                        ValuePath = nameof(CheckReportText),
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true,
                        MultiLine = true,
                        Rows = 90,
                        VisibleWhen = nameof(HasCheckReport)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "静态安全检查（不联网，只查一眼能看出的风险；不等于无毒）：",
                        VisibleWhen = nameof(HasSecurity)
                    },
                    new DetailField
                    {
                        ValuePath = nameof(SecurityText),
                        Kind = FieldKind.Text,
                        Selectable = true,
                        MultiLine = true,
                        Rows = 70,
                        VisibleWhen = nameof(HasSecurity)
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "保存条目", IconKey = "IconSave",
                Classes = "primary",
                ToolTip = "把上面的改动写回选中行（左栏 = 草稿，右栏 = 方案）",
                IsVisiblePath = nameof(HasSelection),
                IsEnabledPath = nameof(EditIsEditable),
                Command = SaveEntryCommand
            },
            new DetailAction
            {
                Text = "＋ 新增",
                Classes = "ghost",
                ToolTip = "用上面的 IP / 主机名 / 注释新增一条（左栏追加到草稿末尾，右栏加到方案末尾）",
                Command = AddEntryCommand
            },
            new DetailAction
            {
                Text = "删除", IconKey = "IconTrash",
                Classes = "ghost",
                ToolTip = "删除选中行",
                IsVisiblePath = nameof(HasSelection),
                IsEnabledPath = nameof(EditIsEditable),
                Command = DeleteEntryCommand
            },
            new DetailAction
            {
                Text = "🧪 检测此条",
                Classes = "ghost",
                ToolTip = "只检测选中的这条：DNS 对比 + 端口可达",
                IsVisiblePath = nameof(HasSelection),
                Command = CheckEntryCommand
            },
            new DetailAction
            {
                Text = "导入文件", IconKey = "IconImport",
                Classes = "ghost",
                ToolTip = "把本地 hosts 文本导入右栏当前方案（只读也能导入）",
                Command = ImportFileCommand
            },
            new DetailAction
            {
                Text = "导出方案", IconKey = "IconExport",
                Classes = "ghost",
                ToolTip = "把右栏当前方案导出成一份 hosts 文本",
                IsVisiblePath = nameof(HasProfileSelection),
                Command = ExportCommand
            },
            new DetailAction
            {
                Text = "保存方案", IconKey = "IconSave",
                Classes = "ghost",
                ToolTip = "把右栏条目落库（左栏草稿不受影响）",
                IsVisiblePath = nameof(HasProfileSelection),
                Command = SaveProfileCommand
            },
            new DetailAction
            {
                Text = "删除方案", IconKey = "IconTrash",
                Classes = "ghost",
                ToolTip = "删除右栏这个方案（⛔ 不动系统 hosts）",
                IsVisiblePath = nameof(HasProfileSelection),
                Command = DeleteProfileCommand
            }
        }
    };
}
