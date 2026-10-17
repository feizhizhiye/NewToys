namespace NewToys.Shared.Services;

/// <summary>
/// 模块热加载契约（NT-052，方案 A 2026-10-15 用户拍板）。
/// <para>
/// 背景：「🔄 重载模块」原在标题栏（第三个按钮），用户判定非常用 ⇒ 移入**功能清单页**「增加功能」操作区。
/// 设置页 VM 与 <c>BuildTree()</c>（Shell 私有）之间不宜直接耦合
/// ⇒「Shared/Core 服务做事 + 事件通知 Shell 收尾」是唯一干净解（事件模式先例：<see cref="INavigationService.Navigated"/>）。
/// </para>
/// <para>
/// 语义（规范 §3.7）：重扫 <c>&lt;程序目录&gt;\Modules\</c> 把**新**模块装进来；只做「新增」，移除模块要重启；
/// 单个 DLL 失败被隔离；已构建的 DI 容器无法追加服务 ⇒ 热加载模块的 RegisterServices 跳过，优先页面型模块。
/// </para>
/// </summary>
public interface IModuleReloadService
{
    /// <summary>执行一次热加载，返回 (新增, 跳过, 失败) 计数；完成后触发 <see cref="Reloaded"/>。</summary>
    (int Added, int Skipped, int Failed) HotReload();

    /// <summary>
    /// 热加载完成（无论计数）时触发，参数 = 计数元组 —— Shell 订阅它重建左树 + 写底栏消息
    /// （「新增 x / 跳过 y / 失败 z」文案要计数 ⇒ 事件带负载；AC 草图的裸 <c>Action?</c> 承载不了）。
    /// </summary>
    event Action<(int Added, int Skipped, int Failed)>? Reloaded;
}
