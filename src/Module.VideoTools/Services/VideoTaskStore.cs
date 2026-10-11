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

    /// <summary>
    /// v3.15：「视频预览」页「📸 取当前帧」抽出来的帧（<b>源文件路径 + 缩略图文件名</b>），
    /// 由「视频收藏」页取走建卡（需求 §6.4.2 / §6.4.9）。⛔ 不落盘（只是页面间的一次性交接）。
    /// </summary>
    private readonly List<(string Path, string Thumb)> _pendingFrames = new();

    public VideoTaskStore(ConfigStoreService cfg)
    {
        _cfg = cfg;
        // v3.16.1：⛔ 构造时**不自动载入** video-tasks.json —— 那个 json 只是"缓存 / 备份"，
        // 批处理列表在程序刚打开时就是空白的（需求 §6.4.4）；要恢复上一次的任务显式点「📥 导入任务」。
        // ⛔ 偷偷自动恢复会让用户莫名其妙："我明明清空了，怎么一开机又有一堆旧任务"。
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

    /// <summary>v3.15：登记一张抽出来的帧（「📸 取当前帧」用）。</summary>
    public void EnqueueFrame(string sourcePath, string thumbFileName) => _pendingFrames.Add((sourcePath, thumbFileName));

    /// <summary>v3.15：取走待建卡的帧（取一次就清空）。收藏页在 <c>OnAttachedToVisualTree</c> 里调。</summary>
    public List<(string Path, string Thumb)> TakePendingFrames()
    {
        var list = _pendingFrames.ToList();
        _pendingFrames.Clear();
        return list;
    }

    // ==================== 增删改 ====================

    public void AddCut(CutTask task) { CutTasks.Add(task); Save(); }
    public void AddTranscode(TranscodeTask task) { TranscodeTasks.Add(task); Save(); }

    public void RemoveCut(CutTask task) { CutTasks.Remove(task); Save(); }
    public void RemoveTranscode(TranscodeTask task) { TranscodeTasks.Remove(task); Save(); }

    /// <summary>
    /// v3.16：把一条任务**换成另一条**（详情卡两个分区的勾选框改任务类型时用：剪辑 ⇄ 转码 ⇄ 剪辑+转码）。
    /// <para>保住队列里的位置：删掉旧的、把新的插到**同一个下标**（两个集合分开存，跨类型时退化为加到末尾）。</para>
    /// </summary>
    public void Replace(VideoTaskBase old, VideoTaskBase fresh)
    {
        if (old is CutTask oc && CutTasks.Contains(oc))
        {
            var i = CutTasks.IndexOf(oc);
            CutTasks.RemoveAt(i);
            if (fresh is CutTask nc) CutTasks.Insert(Math.Min(i, CutTasks.Count), nc);
            else TranscodeTasks.Add((TranscodeTask)fresh);
        }
        else if (old is TranscodeTask ot && TranscodeTasks.Contains(ot))
        {
            var i = TranscodeTasks.IndexOf(ot);
            TranscodeTasks.RemoveAt(i);
            if (fresh is TranscodeTask nt) TranscodeTasks.Insert(Math.Min(i, TranscodeTasks.Count), nt);
            else CutTasks.Add((CutTask)fresh);
        }
        else return;
        Save();
    }

    /// <summary>v3.16.1 §6.4.11：清掉**页面上的这条缓存**（两类一起清，⛔ **不落盘**）；返回清掉条数。
    /// <para>磁盘上的 <c>video-tasks.json</c> 原样留着 —— 那是缓存 / 备份，不是"工作现场"；
    /// 想再要回来点「📥 导入任务」读它（或 <see cref="LoadFromFile"/> 直接读本应用的那份）。</para>
    /// </summary>
    public int ClearCache()
    {
        var n = CutTasks.Count + TranscodeTasks.Count;
        if (n == 0) return 0;
        CutTasks.Clear();
        TranscodeTasks.Clear();
        return n;                 // ⛔ 故意不 Save()：清的是页面缓存，不是那份备份
    }

    /// <summary>清掉已经跑完 / 失败 / 取消的任务（保留还没跑的）。</summary>
    public int ClearFinishedCut()
    {
        var done = CutTasks.Where(t => VideoTaskStatus.IsFinished(t.Status)).ToList();
        foreach (var t in done) CutTasks.Remove(t);
        if (done.Count > 0) Save();
        return done.Count;
    }

    public int ClearFinishedTranscode()
    {
        var done = TranscodeTasks.Where(t => VideoTaskStatus.IsFinished(t.Status)).ToList();
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

    // ==================== 导出 / 导入（批量页「转出命令文件用其他脚本批处理」用） ====================

    /// <summary>把当前队列（剪辑 + 转码两类）序列化成 JSON 文件（复用内部 <see cref="TaskFile"/> 结构）。</summary>
    public bool ExportToFile(string path)
    {
        try
        {
            var data = new TaskFile
            {
                Cut = CutTasks.ToList(),
                Transcode = TranscodeTasks.ToList()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOpts));
            return true;
        }
        catch { return false; }
    }

    /// <summary>从导出的 JSON 里**追加**任务进当前队列（⛔ 不覆盖已有）；返回新增条数。</summary>
    public int ImportFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            var data = JsonSerializer.Deserialize<TaskFile>(File.ReadAllText(path));
            if (data == null) return 0;

            var added = 0;
            foreach (var t in data.Cut ?? new List<CutTask>())
            {
                if (t == null || string.IsNullOrWhiteSpace(t.SourcePath)) continue;
                CutTasks.Add(t);
                added++;
            }
            foreach (var t in data.Transcode ?? new List<TranscodeTask>())
            {
                if (t == null || string.IsNullOrWhiteSpace(t.SourcePath)) continue;
                TranscodeTasks.Add(t);
                added++;
            }
            if (added > 0) Save();
            return added;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 从 <c>video-tasks.json</c> 把任务**读回**当前队列（追加，⛔ 不清空已有）。
    /// <para>v3.16.1：⛔ **构造时不再自动调它** —— 那个 json 只是缓存，批处理页刚打开时列表是空的；
    /// 想恢复上一次的任务，由用户显式点「📥 导入任务」（走 <see cref="ImportFromFile"/>，那个是挑任意 json 文件）。</para>
    /// </summary>
    /// <returns>读入的条数。</returns>
    public int LoadFromFile()
    {
        var n = 0;
        try
        {
            if (!File.Exists(FilePath)) return 0;
            var data = JsonSerializer.Deserialize<TaskFile>(File.ReadAllText(FilePath));
            if (data == null) return 0;

            // 磁盘上可能还留着"运行中"的状态（上次是被强杀的）→ 如实标成失败，别假装还在跑
            foreach (var t in data.Cut ?? new List<CutTask>())
            {
                if (t.Status == VideoTaskStatus.Running) { t.Status = VideoTaskStatus.Failed; t.Message = "上次没有跑完（程序退出）"; }
                CutTasks.Add(t);
                n++;
            }
            foreach (var t in data.Transcode ?? new List<TranscodeTask>())
            {
                if (t.Status == VideoTaskStatus.Running) { t.Status = VideoTaskStatus.Failed; t.Message = "上次没有跑完（程序退出）"; }
                TranscodeTasks.Add(t);
                n++;
            }
        }
        catch
        {
            // 文件损坏：读不出来就算了，下次保存会重写
        }
        return n;
    }

    private sealed class TaskFile
    {
        public List<CutTask>? Cut { get; set; }
        public List<TranscodeTask>? Transcode { get; set; }
    }
}
