using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NewToys.Core.Services;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 剪切 / 转码两页的<b>任务清单</b>（单例，跨页面 / 跨会话保留）。
/// <para>
/// Why：这两页的本质就是「先攒一批任务，再批量跑」（用户可能扫完目录挑 20 个文件、一个一个调完参数再跑），
/// 页面 VM 是**瞬态**的（每次导航都重建，见规范 6.5），任务若挂在 VM 上，切走一次就全没了。
/// </para>
/// <para>
/// 落 <c>&lt;应用根&gt;\config\video-tasks.json</c> —— 在 <c>config\</c> 下会被
/// 「设置 → 数据管理」的备份 / 导入一起覆盖。
/// </para>
/// </summary>
public class VideoTaskStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConfigStoreService _cfg;

    private const string FileName = "video-tasks.json";

    public ObservableCollection<CutTask> CutTasks { get; } = new();
    public ObservableCollection<TranscodeTask> TranscodeTasks { get; } = new();

    // ==================== 跨页面交接（不落盘） ====================

    /// <summary>「视频列表」页多选后要送过来的文件（转码页进来时取走）。</summary>
    private readonly List<string> _pendingTranscode = new();

    /// <summary>「视频列表」页多选后要送过来的文件（剪切页进来时取走）。</summary>
    private readonly List<string> _pendingCut = new();

    public VideoTaskStore(ConfigStoreService cfg)
    {
        _cfg = cfg;
        Load();
    }

    public string FilePath => Path.Combine(_cfg.ConfigDir, FileName);

    // ==================== 交接 ====================

    public void EnqueueForTranscode(IEnumerable<string> paths) => _pendingTranscode.AddRange(paths);
    public void EnqueueForCut(IEnumerable<string> paths) => _pendingCut.AddRange(paths);

    public int PendingTranscodeCount => _pendingTranscode.Count;
    public int PendingCutCount => _pendingCut.Count;

    /// <summary>取走待处理的转码文件（取一次就清空，避免重复建任务）。</summary>
    public List<string> TakePendingTranscode()
    {
        var list = _pendingTranscode.ToList();
        _pendingTranscode.Clear();
        return list;
    }

    /// <summary>取走待处理的剪切文件。</summary>
    public List<string> TakePendingCut()
    {
        var list = _pendingCut.ToList();
        _pendingCut.Clear();
        return list;
    }

    // ==================== 增删改 ====================

    public void AddCut(CutTask task) { CutTasks.Add(task); Save(); }
    public void AddTranscode(TranscodeTask task) { TranscodeTasks.Add(task); Save(); }

    public void RemoveCut(CutTask task) { CutTasks.Remove(task); Save(); }
    public void RemoveTranscode(TranscodeTask task) { TranscodeTasks.Remove(task); Save(); }

    /// <summary>清掉已经跑完 / 失败 / 取消的任务（保留还没跑的）。</summary>
    public int ClearFinishedCut()
    {
        var done = CutTasks.Where(t => TaskStatus.IsFinished(t.Status)).ToList();
        foreach (var t in done) CutTasks.Remove(t);
        if (done.Count > 0) Save();
        return done.Count;
    }

    public int ClearFinishedTranscode()
    {
        var done = TranscodeTasks.Where(t => TaskStatus.IsFinished(t.Status)).ToList();
        foreach (var t in done) TranscodeTasks.Remove(t);
        if (done.Count > 0) Save();
        return done.Count;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            var data = new TaskFile
            {
                Cut = CutTasks.ToList(),
                Transcode = TranscodeTasks.ToList()
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, JsonOpts));
        }
        catch { /* 存不下不打断 UI：任务照样能跑，只是关掉软件后不保留 */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var data = JsonSerializer.Deserialize<TaskFile>(File.ReadAllText(FilePath));
            if (data == null) return;

            // 磁盘上可能还留着"运行中"的状态（上次是被强杀的）→ 如实标成失败，别假装还在跑
            foreach (var t in data.Cut ?? new List<CutTask>())
            {
                if (t.Status == TaskStatus.Running) { t.Status = TaskStatus.Failed; t.Message = "上次没有跑完（程序退出）"; }
                CutTasks.Add(t);
            }
            foreach (var t in data.Transcode ?? new List<TranscodeTask>())
            {
                if (t.Status == TaskStatus.Running) { t.Status = TaskStatus.Failed; t.Message = "上次没有跑完（程序退出）"; }
                TranscodeTasks.Add(t);
            }
        }
        catch
        {
            // 文件损坏：空手启动即可，下次保存会重写
        }
    }

    private sealed class TaskFile
    {
        public List<CutTask>? Cut { get; set; }
        public List<TranscodeTask>? Transcode { get; set; }
    }
}
