using NewToys.Shared.Services;
using Xunit;

namespace NewToys.Tests;

/// <summary>备份恢复的路径防御回归测试（BackupPaths.IsSafeRelativePath）。（WHY: TS-001）</summary>
public class BackupPathsTests
{
    // ==================== 正常路径 ====================

    [Theory]
    [InlineData("a.ps1")]
    [InlineData("scripts/a.ps1")]
    [InlineData("scripts/sub/deep.json")]
    [InlineData("a\\b\\c.txt")]
    public void Safe_NormalRelativePaths(string rel)
        => Assert.True(BackupPaths.IsSafeRelativePath(rel));

    // ==================== 穿越 / 绝对路径 ====================

    [Theory]
    [InlineData("../evil.ps1")]          // 开头就是 ..
    [InlineData("scripts/../../evil.ps1")] // 中段穿越
    [InlineData("scripts/..")]            // 结尾 ..
    [InlineData("./a.ps1")]               // . 段同样拒绝（统一抹平）
    [InlineData("/etc/passwd")]           // 根相对
    [InlineData("\\\\server\\share\\a")]  // UNC
    [InlineData("C:/Windows/a.dll")]      // 盘符（正斜杠）
    [InlineData("C:\\Windows\\a.dll")]    // 盘符（反斜杠）
    public void Unsafe_TraversalOrAbsolute(string rel)
        => Assert.False(BackupPaths.IsSafeRelativePath(rel));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unsafe_EmptyOrNull(string? rel)
        => Assert.False(BackupPaths.IsSafeRelativePath(rel));

    // ==================== 归档 / 种子产物 ====================

    [Theory]
    [InlineData("a.seeded")]              // 导出时写的「已播种」标记
    [InlineData("scripts/x.seeded")]
    [InlineData("a.legacy")]              // 旧版残留
    [InlineData(".bak/a.ps1")]            // 备份目录
    [InlineData("a/.hidden.ps1")]         // 隐藏文件
    public void Unsafe_ArchiveArtifacts(string rel)
        => Assert.False(BackupPaths.IsSafeRelativePath(rel));

    [Theory]
    [InlineData("a//b")]
    [InlineData("scripts//a.ps1")]
    public void Unsafe_MalformedSeparators(string rel)
        => Assert.False(BackupPaths.IsSafeRelativePath(rel));

    /// <summary>顺序契约：穿越判定必须先于「跳过后缀」，否则 <c>../x.seeded</c> 会被当成普通归档产物放过。</summary>
    [Fact]
    public void Traversal_Wins_OverArchiveSuffix()
        => Assert.False(BackupPaths.IsSafeRelativePath("../x.seeded"));

    // ==================== 名字互转 ====================

    [Fact]
    public void ToEntryName_NormalizesToForwardSlash()
    {
        Assert.Equal("a/b/c.txt", BackupPaths.ToEntryName("a\\b\\c.txt"));
        Assert.Equal("a/b", BackupPaths.ToEntryName("/a/b"));
    }

    [Fact]
    public void ToEntryName_ThenToLocalPath_RoundTrips()
    {
        const string rel = "scripts\\sub\\a.ps1";
        var entry = BackupPaths.ToEntryName(rel);
        Assert.Equal("scripts/sub/a.ps1", entry);
        Assert.Equal(rel.Replace('/', System.IO.Path.DirectorySeparatorChar), BackupPaths.ToLocalPath(entry));
    }
}
