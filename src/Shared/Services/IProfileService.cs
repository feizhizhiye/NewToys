using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>
/// 「多配置」（v3.5）：一个配置 = 一套**独立的 `config` + `data` + `logs`**（像浏览器的多用户）。
/// <para>
/// ⚠️ **切换配置 = 重启应用**：配置在启动时就被大量单例服务读进内存了，热切换改动面大、且极易
/// 留下"一半新配置一半旧配置"的状态。所以本接口只负责**写「当前配置」指针 + 重启进程**。
/// </para>
/// <para>
/// v3.26 起支持**多实例**（需求 §3.5）：可同时开多个配置的窗口，各跑各的数据目录（目录级天然隔离）。
/// 语义随之调整：<c>active-profile.txt</c> 只写两处——首次迁移与 <see cref="SetAsDefault"/>；
/// <see cref="SwitchTo"/> 不再写指针，切换 = <see cref="RestartApp(string)"/> 显式传参重启。
/// </para>
/// <para>数据落 <c>&lt;应用根&gt;\profiles\&lt;配置名&gt;\</c>，全局默认指针在 <c>&lt;应用根&gt;\active-profile.txt</c>。</para>
/// </summary>
public interface IProfileService
{
    /// <summary>当前生效的配置名（本实例的；可能是 --profile 启动参数指定的）。</summary>
    string ActiveProfile { get; }

    /// <summary>全部配置名（已排序）。</summary>
    IReadOnlyList<string> Profiles { get; }

    /// <summary>某个配置的数据目录（展示用）。</summary>
    string GetDirectory(string name);

    /// <summary>配置名是否合法（含**防路径穿越**校验）。</summary>
    bool IsValidName(string? name);

    /// <summary>
    /// 校验切换目标（v3.26 起**不再写指针**：切换 = 重启到目标配置，见 <see cref="RestartApp(string)"/>；
    /// 写全局默认请用 <see cref="SetAsDefault"/>）。
    /// </summary>
    void SwitchTo(string name);

    /// <summary>把 <paramref name="name"/> 设为**全局默认**（写 active-profile.txt；影响以后无参启动的实例）。</summary>
    void SetAsDefault(string name);

    /// <summary>新建一个空配置（仅建目录，内容由各服务首次读写时自行播种）。</summary>
    void Create(string name);

    /// <summary>重命名配置；若改的正是全局默认指针指向的配置，指针一并更新。</summary>
    void Rename(string oldName, string newName);

    /// <summary>删除配置（⛔ 不允许删当前配置；**调用方必须先二次确认**）。</summary>
    void Delete(string name);

    /// <summary>以指定配置起一个新实例并退出自己（切换配置用；新实例起不来时不退出）。</summary>
    void RestartApp(string profile);

    /// <summary>以指定配置**新开一个实例**（不退出当前窗口；同一配置已被单例互斥挡住时会提示）。</summary>
    void LaunchNewInstance(string profile);
}
