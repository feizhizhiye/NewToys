using NewToys.Shared.Models;
namespace NewToys.Shared.Services;
public interface ILaunchConfigService
{
    LaunchConfig Config { get; }
    string FilePath { get; }
    LaunchConfig Load();
    (bool Ok, string Message) Save(LaunchConfig config);
}