using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>ILaunchConfigService 实现：已并入 app.json， 本类退化为 IAppConfigService 的薄转接层 —— 目的是保住调用方签名不变 （GeneralViewModel / HomeViewModel / LaunchConfigViewModel / ShellView 全都不用改）。（WHY: CR-032）</summary>
public class LaunchConfigService : ILaunchConfigService
{
    private readonly IAppConfigService _app;

    public LaunchConfigService(IAppConfigService app) => _app = app;

    public LaunchConfig Config => _app.Launch;

    /// <summary>落盘位置（排障 / 设置页展示用）：就是 <c>app.json</c>。</summary>
    public string FilePath => _app.ConfigFilePath;

    public LaunchConfig Load()
    {
        _app.Load();
        return _app.Launch;
    }

    /// <summary>
    /// 保存：整块替换 <c>Launch</c> 节点后立即落盘。
    /// <para>⚠️ 失败不抛 —— <c>app.json</c> 的写入失败由 <see cref="AppConfigService.Save"/> 内部吞掉并写调试日志，
    /// 这是既有行为（改动它会牵动所有应用级设置的错误反馈路径）。</para>
    /// </summary>
    public (bool Ok, string Message) Save(LaunchConfig config)
    {
        _app.Launch = config;
        return (true, "已保存");
    }
}
