namespace NewToys.Shared.Services;

/// <summary>模块热加载契约。（WHY: SH-046）</summary>
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
