using System.Collections.Generic;
using System.Text.RegularExpressions;
using NewToys.SystemTools.Services;
using Xunit;

namespace NewToys.Tests;

/// <summary>
/// 包管理器**输出解析**的回归测试（分层后的纯函数）。
/// <para>
/// 这些函数是「外部命令输出 → 行模型」的唯一通道，改一行正则就会静默改变全页数据；
/// 以往只能靠打开软件肉眼看列表，这里固化成断言。
/// </para>
/// <para>
/// ⛔ 本文件只测**纯函数**：不碰 Avalonia、不碰文件系统、不 new VM。
/// </para>
/// </summary>
public class ParserTests
{
    // ==================== TsvRows：机读输出（dpkg-query / snap / flatpak） ====================

    [Fact]
    public void ParseTsv_WithHeader_UsesHeaderAsKeys_AndTrimsCells()
    {
        const string raw = "Name\tVersion\tStatus\nfoo\t1.2.3\tinstall ok installed\n";
        var rows = TsvRows.ParseTsv(raw, hasHeader: true);

        Assert.Single(rows);
        Assert.Equal("foo", rows[0]["Name"]);
        Assert.Equal("1.2.3", rows[0]["Version"]);
        Assert.Equal("install ok installed", rows[0]["Status"]);
    }

    [Fact]
    public void ParseTsv_NoHeader_UsesGivenColumnNames()
    {
        const string raw = "foo\t1.0\nbar\t2.0\n";
        var rows = TsvRows.ParseTsv(raw, hasHeader: false, columnNames: new[] { "Package", "Version" });

        Assert.Equal(2, rows.Count);
        Assert.Equal("bar", rows[1]["Package"]);
        Assert.Equal("2.0", rows[1]["Version"]);
    }

    [Fact]
    public void ParseTsv_ExtraColumns_GetFallbackNames()
    {
        // 实际列数 > 给定列名数 ⇒ 多出的列叫 col3 / col4，⛔ 不许丢数据
        const string raw = "a\tb\tc\td\n";
        var rows = TsvRows.ParseTsv(raw, hasHeader: false, columnNames: new[] { "L1", "L2" });

        Assert.Equal("c", rows[0]["col3"]);
        Assert.Equal("d", rows[0]["col4"]);
    }

    [Fact]
    public void ParseTsv_CrlfAndBlankLines_Normalized()
    {
        const string raw = "K\tV\r\n\r\nx\t1\r\n\r\ny\t2\r\n";
        var rows = TsvRows.ParseTsv(raw, hasHeader: true, columnNames: null);

        Assert.Equal(2, rows.Count);
        Assert.Equal("2", rows[1]["y"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\r\n\r\n")]
    public void ParseTsv_EmptyInput_ReturnsEmpty(string? raw)
    {
        Assert.Empty(TsvRows.ParseTsv(raw, hasHeader: true));
    }

    // ==================== FixedWidthTable：列宽表格（winget 本地化输出） ====================

    [Fact]
    public void Clean_StripsAnsi_UnifiesNewlines_RemovesBackspace()
    {
        var raw = "\u001B[32mfoo\u001B[0m\r\nbar\bbaz\rqux";

        Assert.Equal("foo\nbarbaz\nqux", FixedWidthTable.Clean(raw));
    }

    [Fact]
    public void Clean_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal("", FixedWidthTable.Clean(null));
        Assert.Equal("", FixedWidthTable.Clean(""));
    }

    [Theory]
    [InlineData("-----", true)]
    [InlineData("---  ----  --", true)]
    [InlineData("--", false)]        // 少于 3 个字符不算分隔线
    [InlineData("Name  Id", false)]
    public void IsSeparator_OnlyDashAndSpace(string line, bool expected)
    {
        Assert.Equal(expected, FixedWidthTable.IsSeparator(line));
    }

    [Fact]
    public void DisplayWidth_CjkCountsTwoColumns()
    {
        Assert.Equal(3, FixedWidthTable.DisplayWidth("abc"));
        Assert.Equal(4, FixedWidthTable.DisplayWidth("中文"));      // 全角 = 2 列
        Assert.Equal(3, FixedWidthTable.DisplayWidth("a中"));
        Assert.Equal(0, FixedWidthTable.DisplayWidth(""));
    }

    [Fact]
    public void DisplayWidth_SurrogatePair_CountedOnceByCodePoint()
    {
        // 代理对 = 1 个码点：⛔ 不能按 char 数成 2 个字符
        // 「𠀀」U+20000 落在 CJK 扩展 B 宽区间 ⇒ 2 列（验证代理对 + 宽判定同时正确）
        Assert.Equal(2, FixedWidthTable.DisplayWidth("𠀀"));
        // emoji 不在 WideRanges 里 ⇒ 按 1 列算（现状；⛔ 别当成 2 列来断言）
        Assert.Equal(1, FixedWidthTable.DisplayWidth("🌐"));
        Assert.Equal(4, FixedWidthTable.DisplayWidth("🌐a中"));
    }

    [Theory]
    [InlineData("中文abc", 0, 2, "中")]
    [InlineData("中文abc", 2, 4, "文")]
    [InlineData("中文abc", 4, int.MaxValue, "abc")]
    [InlineData("abc", 0, int.MaxValue, "abc")]
    [InlineData("abc", 5, int.MaxValue, "")]
    public void SliceByWidth_SlicesByDisplayColumns(string s, int start, int end, string expected)
    {
        Assert.Equal(expected, FixedWidthTable.SliceByWidth(s, start, end));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("████░░", true)]      // 进度条
    [InlineData("│  ─  ┌", true)]     // 纯框线
    [InlineData("Git", false)]
    public void IsNoiseLine_DetectsProgressAndFrames(string line, bool expected)
    {
        Assert.Equal(expected, FixedWidthTable.IsNoiseLine(line));
    }

    // ==================== PackageOutputParser：winget 启发式 ====================

    [Fact]
    public void ParsePins_Empty_ReturnsEmpty()
    {
        Assert.Empty(PackageOutputParser.ParsePins(null));
        Assert.Empty(PackageOutputParser.ParsePins(""));
    }

    [Theory]
    [InlineData("Foo.Bar 1.2.3\nBaz.Qux 24.08\n")]
    [InlineData("Version\n1.2.3\nFoo.Bar\n")]
    public void ParsePins_NeverReturnsPureVersionNumbersAsId(string raw)
    {
        // 🔴 老 bug 回归：兜底通道曾把 24.08 / 1.2.3 这类版本号当包 Id 塞进列表。
        //    这里断言「性质」而不是具体行数 —— 无论表头认不认得出，纯版本号都不得成为 Id。
        foreach (var (id, _) in PackageOutputParser.ParsePins(raw))
        {
            Assert.False(Regex.IsMatch(id, @"^[0-9]+(\.[0-9]+)+$"), $"纯版本号被当成包 Id：{id}");
        }
    }

    [Fact]
    public void ParsePins_DeduplicatesById()
    {
        const string raw = "Name       Id           Version\n" +
                           "---------------------------------\n" +
                           "Git        Git.Git      2.47.0\n" +
                           "Git again  Git.Git      2.47.0\n";
        var pins = PackageOutputParser.ParsePins(raw);

        var ids = new List<string>();
        foreach (var (id, _) in pins) ids.Add(id);
        Assert.Equal(ids.Count, new HashSet<string>(ids, System.StringComparer.OrdinalIgnoreCase).Count);
    }

    [Fact]
    public void ParsePackageIds_DelegatesToParsePins()
    {
        const string raw = "Foo.Bar 1.2.3\n";
        var ids = PackageOutputParser.ParsePackageIds(raw);

        foreach (var id in ids)
            Assert.False(Regex.IsMatch(id, @"^[0-9]+(\.[0-9]+)+$"), $"纯版本号被当成包 Id：{id}");
    }
}
