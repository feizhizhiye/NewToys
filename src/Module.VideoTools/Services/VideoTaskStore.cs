using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NewToys.Core.Services;
using NewToys.Core.Services.DataMigrations;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 剪切 / 转码两页的<b>任务清单</b>（单例，跨页面 / 跨会话保留）。
/// <para>
/// Why：这两页的本质就是「先攒一批任务，再批量跑」（用户可能扫完目录挑 20 个文件、一个一个调完参数再跑），
/// 页面 VM 是**瞬态**的（每次导航都重建，见规范 6.5），任务若挂在 VM 上，切走一次就全没了。
/// </para>
/// <para>
/// 落本模块库 <c>video.db</c> 的 <c>video_docs</c> 表（文档名 <c>tasks</c>；⛔ 不写 <c>config\video-tasks.json</c>）。
/// 它跟随配置档 —— 换配置就是另一套任务缓存，与视频库本身一致。
/// </para>
/// </summary>
public class VideoTaskStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary><c>video_docs</c> 里的文档名。</summary>
    public const string DocName = "tasks";

    private readonly ConfigStoreService _cfg;
    private readonly VideoDb _db;
    private readonly JsonDocStore _docs;

    public ObservableCollection<CutTask> CutTasks { get; } = new();
    public ObservableCollection<TranscodeTask> TranscodeTasks { get; } = new();

    // ==================== 跨页面交接（不落盘） ====================

    /// <summary>「批量处理」→「视频预览」：要带过去**整条编辑**的任务（同一实例，取走即清空）。</summary>
    private VideoTaskBase? _previewTarget;

    /// <summary>
    /// 批量页「🎬 预览编辑」用：把选中的任务交给预览页。
    /// <para>Why 不用 <see cref="EnqueueForCut"/>：那条通道只传路径，预览页拿不到区间 / 预设 / 裁剪等参数，
    /// 复杂任务就没法「先预览再改」。</para>
    /// </summary>
    public void AttachPreviewTarget(VideoTaskBase task) => _previewTarget = task;

    /// <summary>取走待编辑的任务（取一次就清空）。</summary>
    public VideoTaskBase? TakePreviewTarget()
    {
        var t = _previewTarget;
        _previewTarget = null;
        return t;
    }

    /// <summary>「视频列表」页多选后要送过来的文件（转码页进来时取走）。</summary>
    private readonly List<string> _pendingTranscode = new();

    /// <summary>「视频列表」页多选后要送过来的文件（剪切页进来时取走）。</summary>
    private readonly List<string> _pendingCut = new();

    public VideoTaskStore(ConfigStoreService cfg, VideoDb db)
    {
        _cfg = cfg;
        _db = db;
        _docs = new JsonDocStore(db, "video");
        // 打开软件即恢复上次的队列：任务清单是「工作现场」而不是临时缓存（用户关掉软件后
        // 攒了半天的 20 条任务必须还在）。上次没跑完的「运行中」条目由 LoadFromFile 标成失败。
        // 想清空就点「清除缓存」—— 那条会连库里的文档一起删，重开不会自己长回来。
        LoadFromFile();
    }

    /// <summary>库文件全路径（展示 / 排障用；数据在 <c>video_docs</c> 表）。</summary>
    public string FilePath => _db.DatabasePath;

    /// <summary>旧文件（迁移源；搬完是 <c>video-tasks.json.legacy</c>）。</summary>
    private string LegacyFile => Path.Combine(_cfg.ConfigDir, "video-tasks.json");

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

    /// <summary>
    /// 把一条任务**换成另一条**（详情卡两个分区的勾选框改任务类型时用：剪辑 ⇄ 转码 ⇄ 剪辑+转码）。
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

    /// <summary>清掉页面上的这条队列（两类一起清，**并落盘**）；返回清掉条数。
    /// <para>必须落盘：<see cref="VideoTaskStore"/> 构造时会自动恢复，若只清内存，重开软件旧任务就全回来了
    /// —— 那是「我明明清空了」的另一半。想再要回来用导出的 JSON 走「导入任务」。</para>
    /// </summary>
    public int ClearCache()
    {
        var n = CutTasks.Count + TranscodeTasks.Count;
        CutTasks.Clear();
        TranscodeTasks.Clear();
        Save();                   // 两队都空 ⇒ Save 内部删文档
        return n;
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
            var data = new TaskFile
            {
                Cut = CutTasks.ToList(),
                Transcode = TranscodeTasks.ToList()
            };
            if (data.Cut.Count == 0 && data.Transcode.Count == 0)
            {
                _docs.Delete(DocName);      // 两队都空 ⇒ 删文档（沿用 「空即删」语义）
                return;
            }
            _docs.Save(DocName, data, JsonOpts);
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
    /// 把库里的任务队列**读回**当前队列（追加，⛔ 不清空已有）。旧 <c>video-tasks.json</c> 先搬进表（幂等）。
    /// <para>构造时自动调用一次 —— 队列是「工作现场」，关掉软件再打开必须还在。</para>
    /// </summary>
    /// <returns>读入的条数。</returns>
    public int LoadFromFile()
    {
        var n = 0;
        try
        {
            JsonDocMigration.ImportOnce(_docs, DocName, LegacyFile);
            var data = _docs.Load<TaskFile>(DocName, JsonOpts);
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
