using System.Linq;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using Xunit;

namespace NewToys.Tests;

/// <summary>SysInfoOutputParser 的回归测试：脚本 / 命令的 stdout → 行数组。（WHY: TS-008）</summary>
public class SysInfoOutputParserTests
{
    // ==================== json ====================

    [Fact]
    public void Json_RowsWithAttrs_KeepsKeyOrderAndStatus()
    {
        const string stdout = """
            { "rows": [ { "key": "cpu", "status": "ok", "attrs": { "项目": "CPU", "值": "i7-9700" } } ] }
            """;

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Json, out var rows, out var error);

        Assert.True(ok);
        Assert.Null(error);
        var row = Assert.Single(rows);
        Assert.Equal("cpu", row.RowKey);
        Assert.Equal(SysInfoStatus.Ok, row.Status);
        Assert.Equal(new[] { "项目", "值" }, row.Attrs.Select(a => a.Key).ToArray());
        Assert.Equal("i7-9700", row.Attrs[1].Value);
    }

    [Fact]
    public void Json_LegacyValuesArray_BecomesColumnNames()
    {
        const string stdout = """{ "rows": [ { "values": ["a", "b"] } ] }""";

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Json, out var rows, out _);

        Assert.True(ok);
        var row = Assert.Single(rows);
        Assert.Equal(new[] { "列1", "列2" }, row.Attrs.Select(a => a.Key).ToArray());
        // 没有 key ⇒ 取第一个属性值当行键（不是 "列1"）
        Assert.Equal("a", row.RowKey);
    }

    [Fact]
    public void Json_PropertyNames_AreCaseInsensitive()
    {
        const string stdout = """{ "ROWS": [ { "ATTRS": { "x": "1" }, "STATUS": "warn" } ] }""";

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Json, out var rows, out _);

        Assert.True(ok);
        var row = Assert.Single(rows);
        Assert.Equal(SysInfoStatus.Warn, row.Status);
        Assert.Equal("1", row.Attrs[0].Value);
    }

    [Fact]
    public void Json_RowWithoutAttrsOrValues_IsDropped()
    {
        // ⛔ 不猜字段：既没 attrs 也没 values 的行直接丢弃
        const string stdout = """{ "rows": [ { "key": "x", "status": "ok" } ] }""";

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Json, out var rows, out var error);

        Assert.False(ok);
        Assert.Empty(rows);
        Assert.Contains("没有可用", error!);
    }

    [Fact]
    public void Json_RootArray_IsAccepted()
    {
        const string stdout = """[ { "attrs": { "a": "1" } }, { "attrs": { "a": "2" } } ]""";

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Json, out var rows, out _);

        Assert.True(ok);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1", "2" }, rows.Select(r => r.RowKey).ToArray());
    }

    [Theory]
    [InlineData("{ oops", "JSON")]
    [InlineData("""{ "data": [] }""", "rows")]
    [InlineData("42", "数组")]
    public void Json_BadShapes_ReportReason(string stdout, string expectedFragment)
    {
        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Json, out var rows, out var error);

        Assert.False(ok);
        Assert.Empty(rows);
        Assert.Contains(expectedFragment, error!);
    }

    [Fact]
    public void Json_NullAndNumberAttrs_UseRawText()
    {
        const string stdout = """{ "rows": [ { "attrs": { "n": 42, "nil": null, "b": true } } ] }""";

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Json, out var rows, out _);

        Assert.True(ok);
        var row = Assert.Single(rows);
        Assert.Equal("42", row.Attrs.Single(a => a.Key == "n").Value);
        Assert.Equal("", row.Attrs.Single(a => a.Key == "nil").Value);
        Assert.Equal("true", row.Attrs.Single(a => a.Key == "b").Value);
    }

    // ==================== csv ====================

    [Fact]
    public void Csv_HeaderThenRows_HonoursQuotes()
    {
        const string stdout = "项目,值\n内存,\"8,GB\"\n磁盘,1TB";

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Csv, out var rows, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(2, rows.Count);
        Assert.Equal("8,GB", rows[0].Attrs[1].Value);
        Assert.Equal("#1", rows[0].RowKey);
        Assert.Equal("#2", rows[1].RowKey);
    }

    [Fact]
    public void Csv_EscapedQuote_IsUnescaped()
    {
        const string stdout = "a\n\"say \"\"hi\"\"\"";

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Csv, out var rows, out _);

        Assert.True(ok);
        Assert.Equal("say \"hi\"", Assert.Single(rows).Attrs[0].Value);
    }

    [Fact]
    public void Csv_MissingCells_FilledWithEmptyString()
    {
        const string stdout = "a,b,c\n1";

        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Csv, out var rows, out _);

        Assert.True(ok);
        var row = Assert.Single(rows);
        Assert.Equal(3, row.Attrs.Count);
        Assert.Equal("", row.Attrs[2].Value);
    }

    [Fact]
    public void Csv_HeaderOnly_Fails()
    {
        var ok = SysInfoOutputParser.TryParse("项目,值", ScriptParseMode.Csv, out var rows, out var error);

        Assert.False(ok);
        Assert.Empty(rows);
        Assert.Contains("csv", error!);
    }

    // ==================== line / regex ====================

    [Fact]
    public void Line_SkipsBlankLines_AndNumbersKeys()
    {
        var ok = SysInfoOutputParser.TryParse("a\n\n  b  \r\n", ScriptParseMode.Line, out var rows, out _);

        Assert.True(ok);
        Assert.Equal(2, rows.Count);
        Assert.Equal("#1", rows[0].RowKey);
        Assert.Equal("b", rows[1].Attrs[0].Value);
        Assert.Equal(SysInfoOutputParser.LineAttr, rows[1].Attrs[0].Key);
    }

    [Fact]
    public void Regex_Mode_FallsBackToLine()
    {
        // 本期 regex 未定义 pattern 字段 ⇒ 按 line 处理（见类注释）
        var ok = SysInfoOutputParser.TryParse("x\ny", ScriptParseMode.Regex, out var rows, out _);

        Assert.True(ok);
        Assert.Equal(2, rows.Count);
    }

    // ==================== 空输入 ====================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void EmptyStdout_FailsWithReason(string? stdout)
    {
        var ok = SysInfoOutputParser.TryParse(stdout, ScriptParseMode.Json, out var rows, out var error);

        Assert.False(ok);
        Assert.Empty(rows);
        Assert.Contains("没有任何输出", error!);
    }

    [Fact]
    public void UnknownParseMode_TreatedAsJson()
    {
        const string stdout = """{ "rows": [ { "attrs": { "a": "1" } } ] }""";

        Assert.True(SysInfoOutputParser.TryParse(stdout, "whatever", out var rows, out _));
        Assert.Single(rows);
    }
}
