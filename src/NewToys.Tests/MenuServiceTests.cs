using System.Linq;
using NewToys.Core.Services;
using NewToys.Shared.Models;
using Xunit;

namespace NewToys.Tests.Fixtures.Menu
{
    /// <summary>注册用页面替身：菜单项只取类型简单名，无需真是一个控件。</summary>
    public class AlphaView { }
    public class BetaView { }
    public class GammaView { }
}

namespace NewToys.Tests
{
    /// <summary>MenuService 的回归测试。（WHY: TS-004）</summary>
    public class MenuServiceTests
    {
        private const string Group = "test-group";

        [Fact]
        public void RegisterFunction_Generic_UsesSimpleTypeName()
        {
            var menu = new MenuService();
            menu.RegisterModule(Group, "测试组", "IconTest", 10);
            menu.RegisterFunction<Fixtures.Menu.AlphaView>(Group, "alpha", "甲", "▤", 1, "简介");

            var item = menu.FindItem("alpha");
            Assert.NotNull(item);
            Assert.Equal("AlphaView", item!.ViewTypeName);
            Assert.Equal("甲", item.Title);
            Assert.Equal("简介", item.Desc);
        }

        [Fact]
        public void RegisterFunction_OrdersItemsByOrder()
        {
            var menu = new MenuService();
            menu.RegisterModule(Group, "测试组", "IconTest", 10);
            menu.RegisterFunction<Fixtures.Menu.GammaView>(Group, "gamma", "丙", "▤", 3);
            menu.RegisterFunction<Fixtures.Menu.AlphaView>(Group, "alpha", "甲", "▤", 1);
            menu.RegisterFunction<Fixtures.Menu.BetaView>(Group, "beta", "乙", "▤", 2);

            var keys = menu.FindGroup(Group)!.Items.Select(i => i.Key).ToArray();
            Assert.Equal(new[] { "alpha", "beta", "gamma" }, keys);
        }

        [Fact]
        public void RegisterFunction_SameKey_UpdatesInsteadOfDuplicating()
        {
            var menu = new MenuService();
            menu.RegisterModule(Group, "测试组", "IconTest", 10);
            menu.RegisterFunction<Fixtures.Menu.AlphaView>(Group, "alpha", "甲", "▤", 1, "旧简介", SettingsCategory.Theme);
            menu.RegisterFunction<Fixtures.Menu.BetaView>(Group, "alpha", "甲改", "▥", 1);

            var items = menu.FindGroup(Group)!.Items;
            Assert.Single(items);
            Assert.Equal("甲改", items[0].Title);
            Assert.Equal("BetaView", items[0].ViewTypeName);
            // 空简介 / 默认分类视为「没给」⇒ 保留旧值（局部更新语义）
            Assert.Equal("旧简介", items[0].Desc);
            Assert.Equal(SettingsCategory.Theme, items[0].Category);
        }

        [Fact]
        public void RegisterFunction_BeforeGroupExists_IsPendingThenFlushed()
        {
            var menu = new MenuService();
            menu.RegisterFunction<Fixtures.Menu.AlphaView>("late-group", "alpha", "甲", "▤", 1, "跨组注册");
            Assert.Equal(1, menu.PendingCount);
            Assert.Null(menu.FindItem("alpha"));

            menu.RegisterModule("late-group", "迟到组", "IconTest", 30);
            Assert.Equal(0, menu.PendingCount);
            Assert.Equal("AlphaView", menu.FindItem("alpha")!.ViewTypeName);
        }
    }
}
