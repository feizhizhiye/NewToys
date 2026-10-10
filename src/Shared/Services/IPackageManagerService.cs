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

    // ==================== 源的增删（v1.33：直接改系统 winget，与它共用一套数据） ====================
    //
    // ⚠️ 这三项会写**系统级**的 winget 配置（等价于用户在终端里敲 winget source …），
    //    是"改到整台机器"的操作。删除 / 重置属于破坏性操作 → 内部走 ConfirmHandler 二次确认
    //    （与卸载同理；没接上钩子时一律返回 false，宁可什么都不做）。

    /// <summary>添加一个源（<c>winget source add --name … --arg …</c>）。</summary>
    Task<(bool Ok, string Message)> AddSourceAsync(string name, string argument, string? type = null, CancellationToken ct = default);

    /// <summary>删除一个源（<c>winget source remove</c>）—— 内部二次确认。</summary>
    Task<(bool Ok, string Message)> RemoveSourceAsync(string name, CancellationToken ct = default);

    /// <summary>把系统的源恢复为默认（<c>winget source reset --force</c>）—— 内部二次确认。</summary>
    Task<(bool Ok, string Message)> ResetSourcesAsync(CancellationToken ct = default);

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
