using System;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 「运行结果」页 ④ 运行记录节里的一行= 一次 run 的摘要。
/// <para>🔴 行右键挂在行对象上（ContextMenu 在弹出树，取不到页面 VM 的命令）⇒ 三项命令自带。</para>
/// </summary>
public sealed class RunHistoryRow
{
    public RunHistoryRow(RunRecord rec)
    {
        At = rec.At;
        Url = rec.Url;
        Total = rec.Total;
        Dir = rec.Dir;
        Columns = rec.Fields.Count;
        Errors = rec.Fields.Count(f => f.Error.Length > 0);
    }

    /// <summary>运行时刻。</summary>
    public DateTime At { get; }

    /// <summary>`MM-dd HH:mm:ss` 短串（列表里不占宽度）。</summary>
    public string AtText => At.ToString("MM-dd HH:mm:ss");

    /// <summary>起始网址（双击行 = 打开这次运行的目录）。</summary>
    public string Url { get; }

    /// <summary>条目数（url 字段命中优先）。</summary>
    public int Total { get; }

    /// <summary>字段（列）数。</summary>
    public int Columns { get; }

    /// <summary>出错字段数（&gt;0 ⇒ 行尾显示 ⚠）。</summary>
    public int Errors { get; }

    /// <summary>落盘目录（打开 / 日志用）。</summary>
    public string Dir { get; }

    /// <summary>日志文件名固定 <c>diag.txt</c>（<c>RunRecordService</c> 写的）。</summary>
    public string LogPath => Path.Combine(Dir, "diag.txt");

    /// <summary>`3 列 · 12 条 · ⚠1`（错误数为 0 时不带 ⚠）。</summary>
    public string Summary => Errors > 0
        ? $"{Columns} 列 · {Total} 条 · ⚠{Errors}"
        : $"{Columns} 列 · {Total} 条";

    /// <summary>📂 打开目录（挂行）。</summary>
    public ICommand? OpenDirCommand { get; set; }

    /// <summary>📄 打开日志（挂行）。</summary>
    public ICommand? OpenLogCommand { get; set; }

    /// <summary>📋 复制诊断（挂行）。</summary>
    public ICommand? CopyDiagCommand { get; set; }
}
