using Microsoft.Extensions.DependencyInjection;

namespace NewToys.Shared.Models;

public interface IModule
{
    void RegisterServices(IServiceCollection services);

    /// <summary>启动期初始化：必须在首帧之前完成 —— 只做菜单注册与轻量的服务接线 （左树 / 首页功能清单 / 收藏跳转都依赖它，晚了会看到左树先空后跳）。（WHY: SH-010）</summary>
    void Initialize(IServiceProvider services);

    /// <summary>首帧之后（Window.Show() 之后、DispatcherPriority.Background 队列）才执行的重活： 数据库打开与清理、配置读取、种子播种、扫描器接线、过期数据清理入队等。（WHY: SH-011）</summary>
    void InitializeDeferred(IServiceProvider services) { }
}
