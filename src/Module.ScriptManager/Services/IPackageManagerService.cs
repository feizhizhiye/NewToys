using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 包管理器命令行封装（当前后端为 winget，未来可扩展为 apt 等）。
/// <para>
/// 定位：本工程是**带 GUI 的脚本管理器**（见规范 1.1），所以这里的做法是
/// 「**调用系统已有命令行工具 + 把它的输出解析成模型**」，而不是去 P/Invoke 平台原生包管理 API。
/// 好处：行为与用户在终端里敲 <c>winget</c> 完全一致、不需要管理员权限、升级 winget 本体即升级能力。
/// </para>
/// <para>
/// 线程：所有方法都是真异步（`Process` 的流读取），**不阻塞 UI 线程**；调用方在 UI 线程 await 即可。
/// 非 Windows 平台 <see cref="IsSupported"/> 为 false，各方法返回失败消息而不抛异常。
/// </para>
/// </summary>
public interface IPackageManagerService
{
    /// <summary>当前平台是否支持（仅 Windows / winget）。</summary>
    bool IsSupported { get; }

    /// <summary>探测后端是否可用（<c>winget --version</c>）。</summary>
    Task<(bool Ok, string Message)> ProbeAsync(CancellationToken ct = default);

    /// <summary>列出所有源（<c>winget source list</c>）。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageSourceInfo> Items)> ListSourcesAsync(CancellationToken ct = default);

    /// <summary>
    /// 导出当前源的**完整定义**（<c>winget source export</c>，JSON 行）：名字 / 地址 / 类型 / 是否显式。
    /// <para>🔴 用途：winget 1.29 **没有 <c>source enable|disable</c>**，停用只能是 <c>source remove</c>；
    /// 先把定义记下来，开关拨回来时才能按原样 <c>source add</c> 回去（否则地址未知 = 加不回来）。</para>
    /// </summary>
    Task<(bool Ok, string Message, IReadOnlyDictionary<string, PackageSourceDefinition> Items)> ExportSourceDefinitionsAsync(CancellationToken ct = default);

    // ==================== 源的增删（v1.33：直接改系统 winget，与它共用一套数据） ====================
    //
    // ⚠️ 以下都写**系统级** winget 配置（等价于用户在终端里敲 winget source …）⇒ **必须管理员权限**（弹 UAC）。
    //    删除 / 重置属于破坏性操作 → 内部走二次确认钩子（与卸载同理；钩子没接上一律返回 false）。

    /// <summary>添加一个源（<c>winget source add --name … --arg …</c>）。<paramref name="explicitSource"/> = 加 <c>--explicit</c>。</summary>
    Task<(bool Ok, string Message)> AddSourceAsync(string name, string argument, string? type = null, bool explicitSource = false, CancellationToken ct = default);

    /// <summary>删除一个源（<c>winget source remove</c>）—— 内部二次确认。</summary>
    Task<(bool Ok, string Message)> RemoveSourceAsync(string name, CancellationToken ct = default);

    /// <summary>把系统的源恢复为默认（<c>winget source reset --force</c>）—— 内部二次确认。</summary>
    Task<(bool Ok, string Message)> ResetSourcesAsync(CancellationToken ct = default);

    /// <summary>
    /// 源的**启用 / 停用**（v3.79 改写）：winget 1.29 实测**没有 <c>source enable|disable</c>**，
    /// 所以开 = <c>source add</c>（按 <paramref name="definition"/> 的原地址 / 类型 / 显式标记加回）、
    /// 关 = <c>source remove</c>（内部二次确认）。两向都要管理员权限。
    /// <para>🔴 商店源 <c>msstore</c> 不可切换（用户定调）⇒ 直接返回失败说明原因。</para>
    /// </summary>
    Task<(bool Ok, string Message)> SetSourceEnabledAsync(string name, bool enable, PackageSourceDefinition? definition = null, CancellationToken ct = default);

    /// <summary>已安装的包（<c>winget list</c>）。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListInstalledAsync(CancellationToken ct = default);

    /// <summary>可升级的包（<c>winget upgrade</c>）。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListUpgradesAsync(CancellationToken ct = default);

    /// <summary>搜索（<c>winget search &lt;q&gt; [--source S]</c>）。<paramref name="source"/> 为空表示不限定源。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> SearchAsync(string query, string? source, CancellationToken ct = default);

    /// <summary>被 pin 的包（<c>winget pin list</c>）。返回项的 <c>Id</c> / <c>PinnedVersion</c> 有效。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListPinnedAsync(CancellationToken ct = default);

    /// <summary>
    /// 包详情（<c>winget show</c>）：长描述 / 发布者 / 主页 / 协议。
    /// <para>🔴 <c>source</c> 必传（v3.78）：商店包（<c>msstore</c>）在社区源里查不到 ⇒
    /// 不带 <c>--source</c> 时详情直接读不出来。</para>
    /// </summary>
    Task<(bool Ok, string Message, PackageInfo? Detail)> ShowAsync(string id, string? source = null, CancellationToken ct = default);

    Task<(bool Ok, string Message)> InstallAsync(string id, string? source, bool silent, CancellationToken ct = default);

    Task<(bool Ok, string Message)> UpgradeAsync(string id, bool silent, CancellationToken ct = default);

    Task<(bool Ok, string Message)> UninstallAsync(string id, CancellationToken ct = default);

    /// <summary>pin 住包 = 暂停更新（<c>winget pin add</c>），pin 到当前已安装版本。</summary>
    Task<(bool Ok, string Message)> PinAsync(string id, CancellationToken ct = default);

    /// <summary>取消 pin（<c>winget pin remove</c>）。</summary>
    Task<(bool Ok, string Message)> UnpinAsync(string id, CancellationToken ct = default);
}
