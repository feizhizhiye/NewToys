using System;
using System.Linq;
using NewToys.Core.Services;
using Xunit;

namespace NewToys.Tests.Fixtures.Views
{
    /// <summary>测试用页面（<c>Views</c> 命名空间 + <c>View</c> 后缀，命中约定快路径）。</summary>
    public class SamplePageView { }
}

namespace NewToys.Tests.Fixtures.ViewModels
{
    /// <summary>测试用 VM（<c>ViewModels</c> 命名空间与 View 同程序集）。</summary>
    public class SamplePageViewModel { }
}

namespace NewToys.Tests.Fixtures.Other
{
    /// <summary>故意重名：验证「同名取全部候选」而不是只留一个。</summary>
    public class SamplePageView { }
}

namespace NewToys.Tests
{
    /// <summary>
    /// <see cref="PageCatalog"/> 的回归测试。
    /// <para>
    /// 它替掉了两处「每次导航全量扫已加载程序集」的实现（`NavigationService` / `ShellViewModel`），
    /// 一旦索引语义走样，表现是「某些页面点开是空白 / 点开是别的页面」，编译期与运行期都不报错。
    /// 因此这里钉住三条：按简单类名命中、同名返回全部候选、<c>Invalidate</c> 后可重建。
    /// </para>
    /// </summary>
    public class PageCatalogTests
    {
        [Fact]
        public void FindByName_HitsTypeInSameAssembly()
        {
            var catalog = new PageCatalog();

            var found = catalog.FindByName(nameof(NewToys.Tests.Fixtures.Views.SamplePageView));

            Assert.Contains(found, t => t.Assembly == typeof(PageCatalogTests).Assembly);
        }

        [Fact]
        public void FindByName_SameNameInTwoNamespaces_ReturnsBoth()
        {
            // 🔴 同名不是错误（C-05 只拦「同模块两颗同名 View」），索引必须保留全部，
            //    由调用方按 `Control` 子类等条件筛首个 —— 少留一个就会把某页判成「找不到视图类型」。
            var catalog = new PageCatalog();

            var found = catalog.FindByName("SamplePageView");

            Assert.True(found.Count(t => t.Namespace == "NewToys.Tests.Fixtures.Views") == 1);
            Assert.True(found.Count(t => t.Namespace == "NewToys.Tests.Fixtures.Other") == 1);
        }

        [Theory]
        [InlineData("")]
        [InlineData("NoSuchTypeNameAtAll")]
        public void FindByName_EmptyOrUnknown_ReturnsEmpty(string name)
            => Assert.Empty(new PageCatalog().FindByName(name));

        [Fact]
        public void FindByName_NullName_ReturnsEmpty()
            => Assert.Empty(new PageCatalog().FindByName(null!));

        [Fact]
        public void ViewModelCandidates_PrefersConventionNamespace()
        {
            var catalog = new PageCatalog();

            var candidates = catalog.ViewModelCandidates(typeof(NewToys.Tests.Fixtures.Views.SamplePageView));

            Assert.NotEmpty(candidates);
            Assert.Equal(typeof(NewToys.Tests.Fixtures.ViewModels.SamplePageViewModel), candidates[0]);
        }

        [Fact]
        public void ViewModelCandidates_UnknownView_ReturnsEmpty()
        {
            var catalog = new PageCatalog();

            // `Other.SamplePageView` 不在 `.Views` 命名空间 ⇒ 快路径失效，按类名兜底也找不到 SamplePageViewModel 之外的候选。
            var candidates = catalog.ViewModelCandidates(typeof(NewToys.Tests.Fixtures.Other.SamplePageView));

            // 兜底路径仍能按类名命中约定 VM（这是「View 命名空间不合规」的历史页面仍可用的原因）
            Assert.Contains(typeof(NewToys.Tests.Fixtures.ViewModels.SamplePageViewModel), candidates);
        }

        [Fact]
        public void Invalidate_RebuildsAndStillFinds()
        {
            var catalog = new PageCatalog();
            _ = catalog.FindByName("SamplePageView");   // 建索引

            catalog.Invalidate();

            Assert.Contains(catalog.FindByName("SamplePageView"),
                            t => t.Namespace == "NewToys.Tests.Fixtures.Views");
        }

        [Fact]
        public void ViewModelCandidates_NullView_ReturnsEmpty()
            => Assert.Empty(new PageCatalog().ViewModelCandidates(null!));
    }
}
