using Microsoft.Extensions.DependencyInjection;

namespace NewToys.Shared.Models;

public interface IModule
{
    void RegisterServices(IServiceCollection services);
    void Initialize(IServiceProvider services);
}