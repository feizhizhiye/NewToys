using Microsoft.Extensions.DependencyInjection;

namespace NewToys.Shared.Models;

public interface IModule
{
    void RegisterServices(IServiceCollection services);

    /// <summary>
    /// 启动期初始化：**必须在首帧之前完成** —— 只做菜单注册与轻量的服务接线
    /// （左树 / 首页功能清单 / 收藏跳转都依赖它，晚了会看到左树先空后跳）。
    /// <para>
    /// 🔴 NT-070（启动提速）：<b>重活不许写这里</b> —— 建库/清库、读配置文件、
    /// 种子播种（Seeder）、扫描磁盘、批量更新任务入队，一律放 <see cref="InitializeDeferred"/>。
    /// </para>
    /// </summary>
    void Initialize(IServiceProvider services);

    /// <summary>
    /// 首帧之后（<c>Window.Show()</c> 之后、<c>DispatcherPriority.Background</c> 队列）才执行的重活：
    /// 数据库打开与清理、配置读取、种子播种、扫描器接线、过期数据清理入队等。
    /// <para>
    /// 默认空实现 —— 菜单注册本身很轻的模块无需覆盖。
    /// </para>
    /// <para>
    /// ⚠️ 契约：<b>实现内部必须自己 try/catch 兜住</b>（数据层不可用不许拦启动）；
    /// 用户在延迟窗口内点进本模块的页面时，页面要能按"空数据"渲染（不可依赖这里已跑完）。
    /// </para>
    /// </summary>
    void InitializeDeferred(IServiceProvider services) { }
}
