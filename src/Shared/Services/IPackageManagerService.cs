using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

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

    /// <summary>已安装的包（<c>winget list</c>）。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListInstalledAsync(CancellationToken ct = default);

    /// <summary>可升级的包（<c>winget upgrade</c>）。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListUpgradesAsync(CancellationToken ct = default);

    /// <summary>搜索（<c>winget search &lt;q&gt; [--source S]</c>）。<paramref name="source"/> 为空表示不限定源。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> SearchAsync(string query, string? source, CancellationToken ct = default);

    /// <summary>被 pin 的包（<c>winget pin list</c>）。返回项的 <c>Id</c> / <c>PinnedVersion</c> 有效。</summary>
    Task<(bool Ok, string Message, IReadOnlyList<PackageInfo> Items)> ListPinnedAsync(CancellationToken ct = default);

    /// <summary>包详情（<c>winget show</c>）：长描述 / 发布者 / 主页 / 协议。</summary>
    Task<(bool Ok, string Message, PackageInfo? Detail)> ShowAsync(string id, CancellationToken ct = default);

    Task<(bool Ok, string Message)> InstallAsync(string id, string? source, bool silent, CancellationToken ct = default);

    Task<(bool Ok, string Message)> UpgradeAsync(string id, bool silent, CancellationToken ct = default);

    Task<(bool Ok, string Message)> UninstallAsync(string id, CancellationToken ct = default);

    /// <summary>pin 住包 = 暂停更新（<c>winget pin add</c>），pin 到当前已安装版本。</summary>
    Task<(bool Ok, string Message)> PinAsync(string id, CancellationToken ct = default);

    /// <summary>取消 pin（<c>winget pin remove</c>）。</summary>
    Task<(bool Ok, string Message)> UnpinAsync(string id, CancellationToken ct = default);
}
