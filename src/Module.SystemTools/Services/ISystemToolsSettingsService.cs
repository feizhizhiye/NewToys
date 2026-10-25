namespace NewToys.SystemTools.Services;

/// <summary>
/// 系统工具设置：本模块配置的**唯一写入点**，经 Core 的 <c>IOptionsStore</c> 落
/// <c>config\system.json</c>（分区名 <c>system</c>）。
/// </summary>
public interface ISystemToolsSettingsService
{
    /// <summary>分区名（<c>IOptionsStore</c> 的 module 参数）。</summary>
    string Section { get; }

    /// <summary>单条命令的超时（秒；钳位 5~300）。</summary>
    int CommandTimeoutSeconds { get; set; }

    /// <summary>设置文件全路径（页面显示用）。</summary>
    string FilePath { get; }
}
