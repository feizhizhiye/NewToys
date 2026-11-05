using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewToys.SystemTools.Models;

namespace NewToys.SystemTools.Services;

/// <summary>包管理器命令行封装（当前后端为 winget，未来可扩展为 apt 等）。（WHY: ST-009）</summary>
public interface IPackageManagerService
{
    /// <summary>当前平台是否支持（仅 Windows / winget）。</summary>
    bool IsSupported { get; }

    /// <summary>探测后端是否可用（<c>winget --version</c>）。</summary>
    Task<(bool Ok, string Message)> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// 列出所有源（<c>winget source list</c>）。
    /// <para>🔴 源的增删 / 切换（<c>source add|remove|reset</c>，全要 UAC）**不在本服务**：
    /// 它们是「命令库」里的命令条目（用户按 <c>winget source …</c> 自己加，设置页 ⛔ 不再陈列命令清单）。</para>
    /// </summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageSourceInfo> Items)> ListSourcesAsync(CancellationToken ct = default);

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
