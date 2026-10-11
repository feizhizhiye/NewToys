using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>
/// 「多配置」（v3.5）：一个配置 = 一套**独立的 `config` + `data` + `logs`**（像浏览器的多用户）。
/// <para>
/// ⚠️ **切换配置 = 重启应用**：配置在启动时就被大量单例服务读进内存了，热切换改动面大、且极易
/// 留下"一半新配置一半旧配置"的状态。所以本接口只负责**写「当前配置」指针 + 重启进程**。
/// </para>
/// <para>数据落 <c>&lt;应用根&gt;\profiles\&lt;配置名&gt;\</c>，指针在 <c>&lt;应用根&gt;\active-profile.txt</c>。</para>
/// </summary>
public interface IProfileService
{
    /// <summary>当前生效的配置名。</summary>
    string ActiveProfile { get; }

    /// <summary>全部配置名（已排序）。</summary>
    IReadOnlyList<string> Profiles { get; }

    /// <summary>某个配置的数据目录（展示用）。</summary>
    string GetDirectory(string name);

    /// <summary>配置名是否合法（含**防路径穿越**校验）。</summary>
    bool IsValidName(string? name);

    /// <summary>把「当前配置」指向 <paramref name="name"/>（**只写指针文件，不重启**）。</summary>
    void SwitchTo(string name);

    /// <summary>新建一个空配置（仅建目录，内容由各服务首次读写时自行播种）。</summary>
    void Create(string name);

    /// <summary>重命名配置；若改的正是当前配置，指针一并更新。</summary>
    void Rename(string oldName, string newName);

    /// <summary>删除配置（⛔ 不允许删当前配置；**调用方必须先二次确认**）。</summary>
    void Delete(string name);

    /// <summary>重启当前进程（切换配置后调用）。</summary>
    void RestartApp();
}
