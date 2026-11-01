using System.Linq;
using AngleSharp.Html.Parser;
using NewToys.Reading.Services;
using Xunit;

namespace NewToys.Tests;

/// <summary>
/// 书源规则求值器（<see cref="RuleEvaluator"/>）的单元测试。
/// <para>
/// 🔴 规则写错在运行期**不报错**，只表现为「这个字段空 / 这一条源跑不通」—— 所以每种语法元件都要独立断言。
/// 用例覆盖：选择器四写法 / 六种属性 / 两种索引 / 两种前缀 / <c>||</c> <c>&amp;&amp;</c> <c>##</c> /
/// <c>{{}}</c> / <c>@put</c> <c>@get</c> / JSON 路径 / 三条真实书源规则片段。
/// </para>
/// </summary>
public class RuleEvaluatorTests
{
    private const string DetailUrl = "https://example.com/book/1.html";

    private static RuleContext Ctx(string html, string baseUrl = DetailUrl)
    {
        var document = new HtmlParser().ParseDocument(html);
        return new RuleContext().WithDocument(document.DocumentElement, baseUrl, html);
    }

    private static RuleContext JsonCtx(string json)
        => new RuleContext().WithDocument(null, DetailUrl, json);

    private static readonly RuleEvaluator Rules = new();

    // ==================== 选择器四写法 ====================

    [Fact]
    public void ClassSelector_ReturnsNode()
    {
        var value = Rules.Evaluate("class.title", Ctx("<div class=\"title\">A</div>"));

        Assert.Equal(RuleValueKind.Nodes, value.Kind);
        Assert.Single(value.Nodes);
    }

    [Fact]
    public void TagSelector_ReturnsNode()
    {
        var value = Rules.Evaluate("tag.li", Ctx("<ul><li>1</li><li>2</li></ul>"));

        Assert.Equal(RuleValueKind.Nodes, value.Kind);
        Assert.Equal(2, value.Nodes.Count);
    }

    [Fact]
    public void IdSelector_ReturnsNode()
    {
        var value = Rules.Evaluate("id.main@text", Ctx("<div id=\"main\">正文</div>"));

        Assert.Equal("正文", value.FirstText);
    }

    [Fact]
    public void DotClassSelector_ReturnsNode()
    {
        var value = Rules.Evaluate(".item@text", Ctx("<span class=\"item\">x</span>"));

        Assert.Equal("x", value.FirstText);
    }

    [Fact]
    public void BareTagSelector_ReturnsNode()
    {
        var value = Rules.Evaluate("h1@text", Ctx("<h1>标题</h1>"));

        Assert.Equal("标题", value.FirstText);
    }

    // ==================== 属性抽取 ====================

    [Fact]
    public void TextAttribute_JoinsChildText()
    {
        var value = Rules.Evaluate("class.x@text", Ctx("<div class=\"x\">A<span>B</span></div>"));

        Assert.Equal("AB", value.FirstText);
    }

    [Fact]
    public void OwnTextAttribute_ExcludesChildText()
    {
        var value = Rules.Evaluate("class.x@ownText", Ctx("<div class=\"x\">A<span>B</span></div>"));

        Assert.Equal("A", value.FirstText);
    }

    [Fact]
    public void HtmlAttribute_ReturnsOuterHtml()
    {
        var value = Rules.Evaluate("class.x@html", Ctx("<div class=\"x\">A</div>"));

        Assert.Contains("<div", value.FirstText);
        Assert.Contains("A", value.FirstText);
    }

    [Fact]
    public void HrefAttribute_ResolvesAbsoluteUrl()
    {
        var value = Rules.Evaluate("class.l@href", Ctx("<a class=\"l\" href=\"/b/2.html\">x</a>"));

        Assert.Equal("https://example.com/b/2.html", value.FirstText);
    }

    [Fact]
    public void SrcAttribute_ResolvesAbsoluteUrl()
    {
        var value = Rules.Evaluate("class.c@src", Ctx("<img class=\"c\" src=\"/img/1.jpg\"/>"));

        Assert.Equal("https://example.com/img/1.jpg", value.FirstText);
    }

    [Fact]
    public void ContentAttribute_KeepsParagraphBreaks()
    {
        var value = Rules.Evaluate("class.c@content", Ctx("<div class=\"c\"><p>第一段</p><p>第二段</p></div>"));

        Assert.Contains("第一段", value.FirstText);
        Assert.Contains("第二段", value.FirstText);
        Assert.Contains("\n", value.FirstText);
    }

    // ==================== 索引 ====================

    [Fact]
    public void ForwardIndex_PicksFirst()
    {
        var value = Rules.Evaluate("class.i.0@text", Ctx("<p class=\"i\">一</p><p class=\"i\">二</p>"));

        Assert.Equal("一", value.FirstText);
    }

    [Fact]
    public void ReverseIndex_PicksLast()
    {
        var value = Rules.Evaluate("class.i!0@text", Ctx("<p class=\"i\">一</p><p class=\"i\">二</p>"));

        Assert.Equal("二", value.FirstText);
    }

    // ==================== 前缀 ====================

    [Fact]
    public void CssPrefix_UsesRawSelector()
    {
        var value = Rules.Evaluate("@css:.wrap > .inner@text", Ctx("<div class=\"wrap\"><p class=\"inner\">深</p></div>"));

        Assert.Equal("深", value.FirstText);
    }

    [Fact]
    public void JsonPrefix_ExtractsScalar()
    {
        var value = Rules.Evaluate("@json:$.data.name", JsonCtx("{\"data\":{\"name\":\"剑来\"}}"));

        Assert.Equal("剑来", value.FirstText);
    }

    [Fact]
    public void JsonPath_ExpandsArrayToItems()
    {
        var value = Rules.Evaluate("@json:$.data", JsonCtx("{\"data\":[{\"n\":1},{\"n\":2}]}"));

        Assert.Equal(RuleValueKind.Json, value.Kind);
        Assert.Equal(2, value.JsonItems.Count);
    }

    [Fact]
    public void JsonItemField_EvaluatesInsideItem()
    {
        var document = System.Text.Json.JsonDocument.Parse("{\"name\":\"雪中悍刀行\",\"author\":\"烽火\"}");
        var root = document.RootElement.Clone();
        var ctx = new RuleContext().WithDocument(null, DetailUrl, "").ForJson(root);

        Assert.Equal("雪中悍刀行", Rules.EvaluateText("$.name", ctx));
    }

    [Fact]
    public void UnknownPrefix_InvalidatesOnlyThisField()
    {
        var ctx = Ctx("<div class=\"x\">值</div>");
        var value = Rules.Evaluate("@XPath://div/text()", ctx);

        Assert.Equal(RuleValueKind.Empty, value.Kind);
        Assert.NotEmpty(ctx.Errors);
    }

    [Fact]
    public void JsRule_ReturnsEmptyAndReports()
    {
        var ctx = Ctx("<div class=\"x\">值</div>");
        var value = Rules.Evaluate("@js:result.split('|')[0]", ctx);

        Assert.Equal(RuleValueKind.Empty, value.Kind);
        Assert.NotEmpty(ctx.Errors);
    }

    // ==================== 组合子 ====================

    [Fact]
    public void OrOperator_TakesFirstNonEmpty()
    {
        var value = Rules.Evaluate("class.miss||class.hit@text", Ctx("<p class=\"hit\">中</p>"));

        Assert.Equal("中", value.FirstText);
    }

    [Fact]
    public void OrOperator_ReturnsEmptyWhenAllMiss()
    {
        var value = Rules.Evaluate("class.a||class.b", Ctx("<p class=\"c\">x</p>"));

        Assert.Equal(RuleValueKind.Empty, value.Kind);
    }

    [Fact]
    public void AndOperator_UsesNodesAsRoot()
    {
        var html = "<div class=\"list\"><span class=\"t\">甲</span></div>"
                 + "<span class=\"t\">乙</span>";

        var value = Rules.Evaluate("class.list&&class.t@text", Ctx(html));

        Assert.Equal("甲", value.FirstText);
    }

    [Fact]
    public void ReplaceOperator_SupportsBackreference()
    {
        var value = Rules.Evaluate("class.n@text##(\\d+)##[$1]", Ctx("<p class=\"n\">第 12 章</p>"));

        Assert.Equal("第 [12] 章", value.FirstText);
    }

    [Fact]
    public void StepsChain_NarrowsScope()
    {
        var html = "<div class=\"box\"><ul><li><a href=\"/c/1.html\">点我</a></li></ul></div>";

        var value = Rules.Evaluate("class.box@tag.li@tag.a@text", Ctx(html));

        Assert.Equal("点我", value.FirstText);
    }

    // ==================== 插值 / 字典 ====================

    [Fact]
    public void Interpolate_ReplacesKeyVariable()
    {
        var ctx = Ctx("<div/>");
        ctx.Vars["key"] = "abc";

        Assert.Equal("https://example.com/s?q=abc", RuleSyntax.Interpolate("https://example.com/s?q={{key}}", ctx, out _));
    }

    [Fact]
    public void Interpolate_ReplacesPageVariable()
    {
        var ctx = Ctx("<div/>");
        ctx.Vars["page"] = "3";

        Assert.Equal("https://example.com/s?p=3", RuleSyntax.Interpolate("https://example.com/s?p={{page}}", ctx, out _));
    }

    [Fact]
    public void Interpolate_FunctionCallMarksNeedsJs()
    {
        var ctx = Ctx("<div/>");
        RuleSyntax.Interpolate("{{java.base64('x')}}", ctx, out var needsJs);

        Assert.True(needsJs);
    }

    [Fact]
    public void PutGet_TransfersValueBetweenSteps()
    {
        var value = Rules.Evaluate("@put:{t:tag.h1@text}@get:{t}", Ctx("<h1>存下来的</h1>"));

        Assert.Equal("存下来的", value.FirstText);
    }

    // ==================== 真实书源规则片段 ====================

    [Fact]
    public void RealRule_SearchListItemFields()
    {
        const string html = @"
<div class=""result-item"">
  <h3 class=""book-name""><a href=""/book/1.html"">剑来</a></h3>
  <p class=""author"">烽火戏诸侯</p>
  <img class=""cover"" src=""/img/1.jpg""/>
</div>";

        var ctx = Ctx(html);
        var list = Rules.Evaluate("class.result-item", ctx);
        Assert.Single(list.Nodes);

        var item = ctx.ForElement(list.Nodes[0]);
        Assert.Equal("剑来", Rules.EvaluateText("class.book-name@tag.a@text", item));
        Assert.Equal("烽火戏诸侯", Rules.EvaluateText("class.author@text", item));
        Assert.Equal("https://example.com/book/1.html", item.AbsoluteUrl(Rules.EvaluateText("class.book-name@tag.a@href", item)));
        Assert.Equal("https://example.com/img/1.jpg", item.AbsoluteUrl(Rules.EvaluateText("class.cover@src", item)));
    }

    [Fact]
    public void RealRule_TocWithNextPage()
    {
        const string html = @"
<div id=""list""><dl>
  <dd><a href=""/c/1.html"">第一章 起点</a></dd>
  <dd><a href=""/c/2.html"">第二章 出发</a></dd>
</dl></div>
<a class=""next"" href=""/book/1_2.html"">下一页</a>";

        var ctx = Ctx(html);
        var list = Rules.Evaluate("id.list@tag.dd", ctx);
        Assert.Equal(2, list.Nodes.Count);

        var second = ctx.ForElement(list.Nodes[1]);
        Assert.Equal("第二章 出发", Rules.EvaluateText("tag.a@text", second));
        Assert.Equal("https://example.com/c/2.html", ctx.AbsoluteUrl(Rules.EvaluateText("tag.a@href", second)));

        var next = Rules.EvaluateText("class.next@href", ctx);
        Assert.Equal("https://example.com/book/1_2.html", ctx.AbsoluteUrl(next));
    }

    [Fact]
    public void RealRule_ContentSelectorKeepsParagraphs()
    {
        const string html = "<div id=\"content\"><p>开头一句。</p><p>结尾一句。</p></div>";

        var ctx = Ctx(html);
        var value = Rules.EvaluateContent("id.content", ctx);

        Assert.Contains("开头一句", value.FirstText);
        Assert.Contains("结尾一句", value.FirstText);
        Assert.Contains("\n", value.FirstText);
    }

    // ==================== 语法拆解 ====================

    [Fact]
    public void SplitReplace_SeparatesPatternFromBody()
    {
        var (body, pattern, replacement) = RuleSyntax.SplitReplace("class.x@text##\\s+##");

        Assert.Equal("class.x@text", body);
        Assert.Equal("\\s+", pattern);
        Assert.Equal("", replacement);
    }

    [Fact]
    public void SplitSteps_KeepsPutBlockIntact()
    {
        var steps = RuleSyntax.SplitSteps("@put:{k:tag.a@href}@get:{k}");

        Assert.Equal(2, steps.Count);
        Assert.Equal("@put:{k:tag.a@href}", steps[0]);
        Assert.Equal("@get:{k}", steps[1]);
    }

    [Fact]
    public void ToCss_MapsLegadoPrefixes()
    {
        Assert.Equal(".x", RuleSyntax.ToCss("class.x"));
        Assert.Equal("li", RuleSyntax.ToCss("tag.li"));
        Assert.Equal("#m", RuleSyntax.ToCss("id.m"));
        Assert.Equal(".y", RuleSyntax.ToCss(".y"));
    }
}
