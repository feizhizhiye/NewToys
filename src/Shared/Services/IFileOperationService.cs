namespace NewToys.Shared.Services;

/// <summary>目标已存在时的处理策略（需求 §6.6「同名冲突有策略」）。</summary>
public enum FileConflictPolicy
{
    /// <summary>追加序号：<c>名字 (1).ext</c>、<c>名字 (2).ext</c>…</summary>
    AppendNumber,

    /// <summary>追加源路径短哈希后 8 位（稳定、可重复）。</summary>
    ShortHash,

    /// <summary>跳过（不动磁盘）。</summary>
    Skip,

    /// <summary>覆盖（危险：目标文件直接没了）。</summary>
    Overwrite
}

/// <summary>物理文件操作结果。<see cref="Path"/> = 动作完成后的目标路径（失败时 <c>null</c>）。</summary>
public readonly record struct FileOpResult(bool Ok, string Message, string? Path)
{
    public static FileOpResult Fail(string message) => new(false, message, null);

    public static FileOpResult Success(string message, string? path) => new(true, message, path);
}

/// <summary>跨模块共用的文件系统动作（复制 / 移动 / 改名 / 删除）。（WHY: SH-039）</summary>
public interface IFileOperationService
{
    /// <summary>当前平台是否支持「移入回收站」（目前只有 Windows）。</summary>
    bool IsRecycleBinSupported { get; }

    /// <summary>同目录内改名（只改文件名）。<c>Path</c> = 新全路径。</summary>
    FileOpResult Rename(string fullPath, string newName, FileConflictPolicy policy = FileConflictPolicy.AppendNumber);

    /// <summary>移动到另一个目录（目标目录必须已存在）。<c>Path</c> = 新全路径。</summary>
    FileOpResult Move(string sourcePath, string targetDir, FileConflictPolicy policy = FileConflictPolicy.AppendNumber);

    /// <summary>复制到另一个目录（原文件留在原地）。<c>Path</c> = 新全路径。</summary>
    FileOpResult Copy(string sourcePath, string targetDir, FileConflictPolicy policy = FileConflictPolicy.AppendNumber);

    /// <summary>送进系统回收站（可还原）。文件本来就不在时按成功处理（幂等）。</summary>
    FileOpResult DeleteToRecycle(string fullPath);

    /// <summary>永久删除（不进回收站）。调用方必须已经做过二次确认。</summary>
    FileOpResult PermanentDelete(string fullPath);

    /// <summary>确保目录存在（已存在时什么都不做）。返回该目录全路径。</summary>
    string EnsureDirectory(string dir);

    /// <summary>路径是否落在根目录内（带分隔符比较 + 忽略大小写，⛔ 不用裸字符串前缀）。</summary>
    bool IsInside(string? root, string? path);
}
