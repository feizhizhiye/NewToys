using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 扫描候选的<b>界面行</b>（NT-068 §三.6）：在 <see cref="ScriptCandidate"/> 之上加勾选状态。
/// <para>三态文案：新文件（默认勾选）/ 已入库（灰，不可勾）/ 不可采集（灰，不可勾 + 原因）。</para>
/// </summary>
public partial class ScanCandidateItem : ObservableObject
{
    public ScanCandidateItem(ScriptCandidate candidate)
    {
        FullPath = candidate.FullPath;
        RelativePath = candidate.RelativePath;
        State = candidate.State;
        Reason = candidate.Reason;
        // 新文件默认勾选；已入库 / 不可采集一律不打勾（灰显不可勾）。
        _isSelected = candidate.Selectable;
    }

    /// <summary>磁盘全路径（入库时交给导入逻辑）。</summary>
    public string FullPath { get; }

    /// <summary>相对脚本目录的路径（界面显示，短）。</summary>
    public string RelativePath { get; }

    /// <summary>三态（见 <see cref="ScriptCandidate"/> 的 State* 常量）。</summary>
    public string State { get; }

    /// <summary>原因 / 说明（已入库、不可采集的具体原因）。</summary>
    public string Reason { get; }

    /// <summary>可勾选 = 只有新文件；其余灰显。</summary>
    public bool Selectable => State == ScriptCandidate.StateNew;

    /// <summary>状态列文案。</summary>
    public string StateText => State switch
    {
        ScriptCandidate.StateExisting => "已入库",
        ScriptCandidate.StateIneligible => string.IsNullOrEmpty(Reason) ? "不可采集" : Reason,
        _ => "新文件",
    };

    [ObservableProperty] private bool _isSelected;
}
