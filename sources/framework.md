# NewToys 框架源码（Framework Export）

> **用途**：AI 协作续作用的最小框架快照。新会话开始时上传本文件，AI 即可读取框架层全部代码。
>
> **包含范围**：Shared + Core + Shell + 根目录配置文件
> **不包含**：各业务模块（Module.*）、脚本（scripts/）、生成物（artifacts/）
> **文件格式**：FILE: <相对路径> 段标记，可配合 import-sources.ps1 反向还原

---

## 1. 元信息

| 项 | 值 |
|---|---|
| 导出生成时间 | 2026-10-09 20:23:12 |
| 机器名 | START2019 |
| 仓库根 | C:\Users\zhen\Downloads\NewToys |
| 导出类型 | framework |
| 文件总数 | 43 |

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>NewToys.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Shared/Shared.csproj" />
  </ItemGroup>
</Project>
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/AppConfigService.cs`

```csharp
using System;
using System.IO;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class AppConfigService : IAppConfigService
{
    private class ConfigData
    {
        public bool IsDarkTheme { get; set; } = true;
        public string DefaultVideoFolder { get; set; } = "";
        public string FfmpegPath { get; set; } = "ffmpeg";
        public string PlayerPath { get; set; } = "";
        public int MaxConcurrency { get; set; } = 2;
        public bool GpuAcceleration { get; set; } = false;
        public bool EncryptedDnsEnabled { get; set; } = false;
        public string EncryptedDnsProviderId { get; set; } = "dnspod";
        public string EncryptedDnsCustomUrl { get; set; } = "";
    }

    private ConfigData _data = new();
    private readonly ConfigStoreService _cfg;

    public AppConfigService(ConfigStoreService cfg) { _cfg = cfg; Load(); }

    private string ConfigFile => Path.Combine(_cfg.ConfigDir, "app.json");

    public string ConfigFilePath => ConfigFile;
    public string DataDirectory => _cfg.DataDir;

    public bool IsDarkTheme { get => _data.IsDarkTheme; set { _data.IsDarkTheme = value; Save(); } }
    public string DefaultVideoFolder { get => _data.DefaultVideoFolder; set { _data.DefaultVideoFolder = value; Save(); } }
    public string FfmpegPath { get => _data.FfmpegPath; set { _data.FfmpegPath = value; Save(); } }
    public string PlayerPath { get => _data.PlayerPath; set { _data.PlayerPath = value; Save(); } }
    public int MaxConcurrency { get => _data.MaxConcurrency; set { _data.MaxConcurrency = value; Save(); } }
    public bool GpuAcceleration { get => _data.GpuAcceleration; set { _data.GpuAcceleration = value; Save(); } }
    public bool EncryptedDnsEnabled { get => _data.EncryptedDnsEnabled; set { _data.EncryptedDnsEnabled = value; Save(); } }
    public string EncryptedDnsProviderId { get => _data.EncryptedDnsProviderId; set { _data.EncryptedDnsProviderId = value; Save(); } }
    public string EncryptedDnsCustomUrl { get => _data.EncryptedDnsCustomUrl; set { _data.EncryptedDnsCustomUrl = value; Save(); } }

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                var d = JsonSerializer.Deserialize<ConfigData>(json);
                if (d != null) _data = d;
            }
            if (string.IsNullOrEmpty(_data.DefaultVideoFolder))
            {
                _data.DefaultVideoFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NewToys");
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/AppServices.cs`

```csharp
using System;

namespace NewToys.Core.Services;

/// <summary>
/// 全局服务访问点。
/// 由 Shell 在构建 ServiceProvider 之后调用 Initialize 注入；
/// 供各模块（不能反向引用 Shell）在 code-behind 中按需获取服务实例。
/// </summary>
public static class AppServices
{
    private static IServiceProvider? _provider;

    public static void Initialize(IServiceProvider provider) => _provider = provider;

    public static T? Get<T>() where T : class
        => _provider?.GetService(typeof(T)) as T;
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/ConfigStoreService.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewToys.Core.Services;

public class ConfigFileEntry
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    [JsonIgnore] public string? Content { get; set; }
}

public class ConfigStoreService
{
    public string ConfigDir => EnsureDir();
    public string DataDir
    {
        get
        {
            var d = Path.Combine(HomeDir, "data");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private string HomeDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("NEWTOYS_HOME");
            if (!string.IsNullOrEmpty(env)) return env;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NewToys");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private string EnsureDir()
    {
        var d = Path.Combine(HomeDir, "var", "config");
        Directory.CreateDirectory(d);
        return d;
    }

    public List<ConfigFileEntry> ListConfigFiles()
    {
        var result = new List<ConfigFileEntry>();
        if (!Directory.Exists(ConfigDir)) return result;
        foreach (var f in new DirectoryInfo(ConfigDir).GetFiles("*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(ConfigDir, f.FullName);
            if (rel.StartsWith(".bak")) continue;
            result.Add(new ConfigFileEntry
            {
                FileName = f.Name,
                RelativePath = rel,
                FileSize = f.Length,
                LastWriteTimeUtc = f.LastWriteTimeUtc
            });
        }
        return result;
    }

    public string ExportBackup()
    {
        var files = new List<ConfigFileEntry>();
        foreach (var f in new DirectoryInfo(ConfigDir).GetFiles("*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(ConfigDir, f.FullName);
            if (rel.StartsWith(".bak")) continue;
            files.Add(new ConfigFileEntry
            {
                FileName = f.Name,
                RelativePath = rel,
                FileSize = f.Length,
                LastWriteTimeUtc = f.LastWriteTimeUtc,
                Content = File.ReadAllText(f.FullName)
            });
        }
        var pkg = new { Version = 1, ExportedAt = DateTime.UtcNow, Files = files };
        return JsonSerializer.Serialize(pkg, new JsonSerializerOptions { WriteIndented = true });
    }

    public (int ok, int skipped, string? backupDir) ImportBackup(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Files", out var filesEl)) throw new InvalidDataException("备份文件缺少 Files 节点");

        var bak = Path.Combine(ConfigDir, $".bak-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(bak);
        foreach (var f in new DirectoryInfo(ConfigDir).GetFiles("*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(ConfigDir, f.FullName);
            if (rel.StartsWith(".bak")) continue;
            var dst = Path.Combine(bak, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(f.FullName, dst, true);
        }

        int ok = 0, skipped = 0;
        foreach (var e in filesEl.EnumerateArray())
        {
            var rel = (e.GetProperty("RelativePath").GetString() ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(rel)) { skipped++; continue; }
            var full = Path.GetFullPath(Path.Combine(ConfigDir, rel));
            if (!full.StartsWith(ConfigDir, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; } // 路径穿越防御
            var content = e.GetProperty("Content").GetString() ?? "";
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            ok++;
        }
        return (ok, skipped, bak);
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/EncryptedDnsService.cs`

```csharp
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Core.Services
{
    /// <summary>
    /// 加密 DNS（DoH）实现：用 **JSON 接口**（`?name=x&amp;type=A` + `Accept: application/dns-json`）查 A/AAAA，
    /// 并把它接到 <see cref="SocketsHttpHandler.ConnectCallback"/> 上——于是本软件发出的 HTTP 请求在**建连之前**
    /// 的域名解析走的是加密 DNS（系统 DNS、浏览器都不受影响）。
    ///
    /// 几个关键点：
    /// - 解析 DoH 端点**自身**的域名必须用系统 DNS（bootstrap），否则递归。
    /// - `ConnectCallback` 跑在**线程池线程**上，里面只能做纯后台的事（这里只查 DNS + 连 socket），
    ///   并且一律 `ConfigureAwait(false)`；绝不在这里碰界面。
    /// - 带 TTL 的缓存（上限 5 分钟），`ClearCache()` 供「清除 DNS 缓存」调用。
    /// </summary>
    public class EncryptedDnsService : IEncryptedDnsService
    {
        private const int MaxCacheSeconds = 300;

        private readonly IAppConfigService _config;
        private readonly ConcurrentDictionary<string, (string[] Addresses, DateTime Expires)> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HttpClient _bootstrap;

        public EncryptedDnsService(IAppConfigService config)
        {
            _config = config;

            // bootstrap 客户端：只用来访问 DoH 端点本身（用系统 DNS 解析端点域名），永远不接 DoH 回调
            _bootstrap = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(6) })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _bootstrap.DefaultRequestHeaders.UserAgent.ParseAdd("NewToys/1.0 (+doh)");
        }

        public bool IsEnabled => _config.EncryptedDnsEnabled;

        public string Endpoint
        {
            get
            {
                var provider = DohProviders.Find(_config.EncryptedDnsProviderId);
                var endpoint = provider.Id == "custom" ? _config.EncryptedDnsCustomUrl : provider.Endpoint;
                return (endpoint ?? "").Trim();
            }
        }

        public string Describe()
        {
            if (!IsEnabled) return "未启用（用系统 DNS 解析）";
            if (Endpoint.Length == 0) return "已启用，但没填 DoH 端点（会退化成系统 DNS）";

            var provider = DohProviders.Find(_config.EncryptedDnsProviderId);
            return provider.Id == "custom" ? $"已启用：自定义 {Endpoint}" : $"已启用：{provider.Name}";
        }

        // ==================== 解析 ====================

        public async Task<(bool Ok, string Message, string[] Addresses)> ResolveAsync(string host, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(host)) return (false, "主机名为空", Array.Empty<string>());

            if (!IsEnabled || Endpoint.Length == 0)
            {
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(host.Trim(), ct).ConfigureAwait(false);
                    return (true, $"系统 DNS 解析 {host} 成功（{addresses.Length} 个地址）",
                        addresses.Select(a => a.ToString()).ToArray());
                }
                catch (Exception ex)
                {
                    return (false, "系统 DNS 解析失败：" + ex.Message, Array.Empty<string>());
                }
            }

            try
            {
                var addresses = await QueryAsync(host.Trim(), ct).ConfigureAwait(false);
                return addresses.Length == 0
                    ? (false, $"DoH 没有返回 {host} 的地址", Array.Empty<string>())
                    : (true, $"加密 DNS 解析 {host} 成功（{addresses.Length} 个地址）", addresses);
            }
            catch (Exception ex)
            {
                return (false, "加密 DNS 解析失败：" + ex.Message, Array.Empty<string>());
            }
        }

        /// <summary>查 A + AAAA（JSON 接口）。</summary>
        private async Task<string[]> QueryAsync(string host, CancellationToken ct)
        {
            var results = new List<string>();

            foreach (var type in new[] { "A", "AAAA" })
            {
                var url = $"{Endpoint}{(Endpoint.Contains('?') ? '&' : '?')}name={Uri.EscapeDataString(host)}&type={type}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Accept", "application/dns-json");

                using var response = await _bootstrap.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (type == "A") throw new HttpRequestException($"DoH 端点返回 {(int)response.StatusCode} {response.ReasonPhrase}");
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                results.AddRange(ParseAnswers(json, type == "A" ? 1 : 28));
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>解析 <c>{ "Answer":[{"type":1,"data":"1.2.3.4","TTL":300}] }</c>。</summary>
        public static List<string> ParseAnswers(string json, int wantedType, out int ttlSeconds)
        {
            var values = new List<string>();
            ttlSeconds = MaxCacheSeconds;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("Status", out var status) && status.TryGetInt32(out var statusCode) && statusCode != 0)
            {
                throw new InvalidOperationException($"DoH 返回状态码 {statusCode}");
            }

            if (!root.TryGetProperty("Answer", out var answers) || answers.ValueKind != JsonValueKind.Array) return values;

            foreach (var answer in answers.EnumerateArray())
            {
                if (!answer.TryGetProperty("type", out var typeElement) || !typeElement.TryGetInt32(out var type) || type != wantedType) continue;
                if (!answer.TryGetProperty("data", out var dataElement)) continue;

                var data = dataElement.GetString();
                if (!string.IsNullOrWhiteSpace(data)) values.Add(data.Trim());

                if (answer.TryGetProperty("TTL", out var ttlElement) && ttlElement.TryGetInt32(out var ttl) && ttl > 0)
                {
                    ttlSeconds = Math.Min(ttl, MaxCacheSeconds);
                }
            }

            return values;
        }

        private static List<string> ParseAnswers(string json, int wantedType) => ParseAnswers(json, wantedType, out _);

        private async Task<string[]> ResolveCachedAsync(string host, CancellationToken ct)
        {
            if (_cache.TryGetValue(host, out var cached) && cached.Expires > DateTime.UtcNow) return cached.Addresses;

            var addresses = await QueryAsync(host, ct).ConfigureAwait(false);
            if (addresses.Length > 0)
            {
                _cache[host] = (addresses, DateTime.UtcNow.AddSeconds(MaxCacheSeconds));
            }

            return addresses;
        }

        public void ClearCache() => _cache.Clear();

        // ==================== HTTP 客户端 ====================

        public HttpClient CreateClient(int timeoutSeconds = 15, string? userAgent = null)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(3, timeoutSeconds));

            HttpClient client;
            if (!IsEnabled || Endpoint.Length == 0)
            {
                client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, ConnectTimeout = TimeSpan.FromSeconds(6) })
                {
                    Timeout = timeout
                };
            }
            else
            {
                var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = 5,
                    ConnectTimeout = TimeSpan.FromSeconds(8),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    ConnectCallback = async (context, ct) =>
                    {
                        var host = context.DnsEndPoint.Host;

                        // IPv4/IPv6 字面量直接连，不用解析
                        if (IPAddress.TryParse(host, out var literal))
                        {
                            return await ConnectAsync(literal, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                        }

                        var addresses = await ResolveCachedAsync(host, ct).ConfigureAwait(false);
                        if (addresses.Length == 0) throw new HttpRequestException($"加密 DNS 未能解析 {host}");

                        Exception? last = null;
                        foreach (var text in addresses)
                        {
                            if (!IPAddress.TryParse(text, out var address)) continue;

                            try
                            {
                                return await ConnectAsync(address, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                last = ex;
                            }
                        }

                        throw last ?? new HttpRequestException($"加密 DNS 解析到 {host} 的地址都无法连接");
                    }
                };

                client = new HttpClient(handler) { Timeout = timeout };
            }

            if (!string.IsNullOrWhiteSpace(userAgent)) client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return client;
        }

        private static async Task<Stream> ConnectAsync(IPAddress address, int port, CancellationToken ct)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/LaunchConfigService.cs`

```csharp
using System;
using System.IO;
using System.Text.Json;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class LaunchConfigService : ILaunchConfigService
{
    private readonly ConfigStoreService _cfg;
    private LaunchConfig? _config;

    public LaunchConfig Config => _config ??= Load();
    public string FilePath => Path.Combine(_cfg.ConfigDir, "launch.json");

    public LaunchConfigService(ConfigStoreService cfg) { _cfg = cfg; _config = Load(); }

    public LaunchConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var c = JsonSerializer.Deserialize<LaunchConfig>(json);
                if (c != null) return _config = c;
            }
        }
        catch { }
        return _config = new LaunchConfig();
    }

    public (bool Ok, string Message) Save(LaunchConfig config)
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            _config = config;
            return (true, "已保存");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/MenuService.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class MenuService : IMenuService
{
    private readonly List<MenuGroup> _groups = new();

    public IReadOnlyList<MenuGroup> Groups
    {
        get
        {
            lock (_groups)
            {
                return _groups
                    .OrderBy(g => g.IsBottom ? 1 : 0)
                    .ThenBy(g => g.Order)
                    .ToList()
                    .AsReadOnly();
            }
        }
    }

    public MenuGroup RegisterModule(string key, string title, string icon, int order,
                                     bool isBottom = false, bool isHome = false)
    {
        lock (_groups)
        {
            var existing = _groups.FirstOrDefault(g => g.Key == key);
            if (existing != null)
            {
                existing.Title = title;
                existing.Icon = icon;
                existing.Order = order;
                existing.IsBottom = isBottom;
                existing.IsHome = isHome;
                return existing;
            }
            var g = new MenuGroup
            {
                Key = key,
                Title = title,
                Icon = icon,
                Order = order,
                IsBottom = isBottom,
                IsHome = isHome
            };
            _groups.Add(g);
            return g;
        }
    }

    public void RegisterFunction(string moduleKey, string key, string title, string icon,
                                  string viewName, int order = 0)
    {
        MenuGroup? g;
        lock (_groups)
        {
            g = _groups.FirstOrDefault(x => x.Key == moduleKey);
        }
        if (g == null) throw new InvalidOperationException($"模块 {moduleKey} 未注册");
        var existing = g.Items.FirstOrDefault(i => i.Key == key);
        if (existing != null)
        {
            existing.Title = title;
            existing.Icon = icon;
            existing.ViewTypeName = viewName;
            existing.Order = order;
            existing.GroupKey = moduleKey;
            return;
        }
        g.Items.Add(new MenuItem
        {
            Key = key,
            GroupKey = moduleKey,
            Title = title,
            Icon = icon,
            ViewTypeName = viewName,
            Order = order
        });
        var sorted = g.Items.OrderBy(i => i.Order).ToList();
        g.Items.Clear();
        foreach (var it in sorted) g.Items.Add(it);
    }

    public MenuItem? FindItem(string key)
    {
        foreach (var g in Groups)
        {
            var it = g.Items.FirstOrDefault(i => i.Key == key);
            if (it != null) return it;
        }
        return null;
    }

    public MenuGroup? FindGroup(string key) => Groups.FirstOrDefault(g => g.Key == key);
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/ModuleLoader.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public static class ModuleLoader
{
    public static void LoadAll(IServiceCollection services, IMenuService menu)
    {
        var modules = new List<IModule>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            foreach (var t in asm.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (typeof(IModule).IsAssignableFrom(t))
                {
                    try
                    {
                        var m = (IModule?)Activator.CreateInstance(t);
                        if (m != null) modules.Add(m);
                    }
                    catch { }
                }
            }
        }
        foreach (var m in modules) m.RegisterServices(services);
        // Initialize 在 BuildServiceProvider 之后由 App 调用
        AppDomain.CurrentDomain.SetData("__LoadedModules", modules);
    }

    public static void InitializeAll(IServiceProvider sp)
    {
        if (AppDomain.CurrentDomain.GetData("__LoadedModules") is List<IModule> list)
        {
            foreach (var m in list)
            {
                try { m.Initialize(sp); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Module init failed: {ex.Message}"); }
            }
        }
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/NavigationService.cs`

```csharp
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;
namespace NewToys.Core.Services;
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    public event EventHandler<(string key, object? view, object? param)>? Navigated;
    public string CurrentPageKey { get; private set; } = "";
    public object? CurrentView { get; private set; }
    public NavigationService(IServiceProvider sp, IMenuService menu) { _sp = sp; _menu = menu; }
    public void NavigateTo(string pageKey, object? parameter = null)
    {
        CurrentPageKey = pageKey;
        var item = _menu.FindItem(pageKey);
        object? view = null;
        if (item != null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var viewType = asm.GetTypes().FirstOrDefault(t => t.Name == item.ViewTypeName && !t.IsAbstract && t.IsClass);
                if (viewType != null)
                {
                    try { view = ActivatorUtilities.CreateInstance(_sp, viewType); }
                    catch { view = Activator.CreateInstance(viewType); }
                    break;
                }
            }
        }
        CurrentView = view;
        Navigated?.Invoke(this, (pageKey, view, parameter));
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Core/Services/ThemeService.cs`

```csharp
using System;
using System.IO;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class ThemeService : IThemeService
{
    private readonly ConfigStoreService _cfg;
    private bool _isDark;

    public event Action? ThemeChanged;

    public bool IsDark => _isDark;
    public string ConfigDir => _cfg.ConfigDir;

    public ThemeService(ConfigStoreService cfg)
    {
        _cfg = cfg;
        _isDark = true; // 默认深色
        Load();
    }

    private string ThemeFile => Path.Combine(ConfigDir, "theme.json");

    public void Load()
    {
        try
        {
            if (File.Exists(ThemeFile))
            {
                var json = File.ReadAllText(ThemeFile);
                var d = JsonSerializer.Deserialize<ThemeData>(json);
                _isDark = d?.IsDark ?? true;
            }
        }
        catch { _isDark = true; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ThemeFile, JsonSerializer.Serialize(new ThemeData { IsDark = _isDark }));
        }
        catch { }
    }

    public void Toggle() => SetDark(!_isDark);
    public void SetDark(bool dark)
    {
        if (_isDark == dark) return;
        _isDark = dark;
        Save();
        ThemeChanged?.Invoke();
    }

    private class ThemeData { public bool IsDark { get; set; } = true; }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Input/DelegateCommand.cs`

```csharp
using System;
using System.Windows.Input;

namespace NewToys.Shared.Input;

public class DelegateCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    public DelegateCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public bool CanExecute(object? p) => _canExecute?.Invoke() ?? true;
    public void Execute(object? p) => _execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class DelegateCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;
    public DelegateCommand(Action<T?> execute, Func<T?, bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public bool CanExecute(object? p) => _canExecute?.Invoke((T?)p!) ?? true;
    public void Execute(object? p) => _execute((T?)p!);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Models/IModule.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace NewToys.Shared.Models;

public interface IModule
{
    void RegisterServices(IServiceCollection services);
    void Initialize(IServiceProvider services);
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Models/LaunchConfig.cs`

```csharp
using System.Collections.Generic;

namespace NewToys.Shared.Models;

public class LaunchButtonConfig
{
    public string Name { get; set; } = "";
    public string ProgramPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Urls { get; set; } = "";
    public int IconColor { get; set; } = 0; // 0=蓝绿 1=青绿 2=橙
}

public class LaunchConfig
{
    public bool StartWithSystem { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool CheckUpdateOnStartup { get; set; } = true;
    public LaunchButtonConfig Browser1 { get; set; } = new()
    {
        Name = "浏览器1 批量开网址",
        Urls = "",
        ProgramPath = "",
        Arguments = "",
        IconColor = 0
    };
    public LaunchButtonConfig Browser2 { get; set; } = new()
    {
        Name = "浏览器2 批量开网址",
        Urls = "",
        ProgramPath = "",
        Arguments = "",
        IconColor = 1
    };
    public List<LaunchButtonConfig> Programs { get; set; } = new()
    {
        new LaunchButtonConfig { Name = "批量打开软件", ProgramPath = "", Arguments = "", IconColor = 2 }
    };
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Models/MenuModels.cs`

```csharp
using System.Collections.ObjectModel;

namespace NewToys.Shared.Models;

public class MenuGroup
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "📁";
    public int Order { get; set; } = 100;
    public bool IsBottom { get; set; } = false;
    public bool IsHome { get; set; } = false;
    public ObservableCollection<MenuItem> Items { get; set; } = new();
}

public class MenuItem
{
    public string Key { get; set; } = "";
    public string GroupKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public string ViewTypeName { get; set; } = "";
    public string ViewModelTypeName { get; set; } = "";
    public int Order { get; set; }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Models/ViewModelBase.cs`

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shared.Models;

public class ViewModelBase : ObservableObject
{
    public virtual void OnNavigatedTo(object? parameter = null) { }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Services/IAppConfigService.cs`

```csharp
namespace NewToys.Shared.Services
{
    public interface IAppConfigService
    {
        bool IsDarkTheme { get; set; }
        string DefaultVideoFolder { get; set; }
        string FfmpegPath { get; set; }

        /// <summary>
        /// 外部视频播放器路径（exe 全路径）。留空 = 用系统默认关联程序打开。
        /// 本应用不做内嵌播放（主窗口是分层窗口，托管播放器画面只会黑屏）。
        /// </summary>
        string PlayerPath { get; set; }

        int MaxConcurrency { get; set; }
        bool GpuAcceleration { get; set; }

        /// <summary>配置文件完整路径（<c>%LocalAppData%\NewToys\config.json</c>）。</summary>
        string ConfigFilePath { get; }

        /// <summary>
        /// 应用数据根目录（媒体库 library.json、缩略图、各模块自己的数据都放这里）。
        /// 默认 <c>%LocalAppData%\NewToys\data</c>，可用环境变量 <c>NEWTOYS_DATA_DIR</c> 覆盖（便携版/测试），
        /// 用户目录不可写时兜底到程序目录的 <c>Data\</c>。放在用户目录是为了**跨 Debug/Release、跨清理输出目录都不丢数据**。
        /// </summary>
        string DataDirectory { get; }

        /// <summary>是否启用**加密 DNS（DoH）**——只作用于本软件自己的网络请求，不动系统设置。</summary>
        bool EncryptedDnsEnabled { get; set; }

        /// <summary>DoH 服务商 id（见 <see cref="DohProviders"/>：dnspod / aliyun / cloudflare / google / custom）。</summary>
        string EncryptedDnsProviderId { get; set; }

        /// <summary>自定义 DoH 端点（provider 选 custom 时用）。</summary>
        string EncryptedDnsCustomUrl { get; set; }

        void Save();
        void Load();
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Services/IEncryptedDnsService.cs`

```csharp
using System.Net.Http;

namespace NewToys.Shared.Services
{
    /// <summary>
    /// **加密 DNS（DNS over HTTPS）**：只作用于**本软件自己**发起的网络请求（书签检测、网址爬取等），
    /// 不改系统 DNS、不影响浏览器。未启用时走系统解析。
    ///
    /// 实现用 DoH 的 **JSON 接口**（`Accept: application/dns-json`），所以「自定义」端点必须是支持 JSON 的 DoH 服务。
    /// </summary>
    public interface IEncryptedDnsService
    {
        /// <summary>当前是否启用（来自配置）。</summary>
        bool IsEnabled { get; }

        /// <summary>当前生效的 DoH 端点。</summary>
        string Endpoint { get; }

        /// <summary>解析一个主机名（走 DoH；未启用时走系统解析）。返回 A/AAAA 地址。</summary>
        Task<(bool Ok, string Message, string[] Addresses)> ResolveAsync(string host, CancellationToken ct);

        /// <summary>
        /// 造一个 HTTP 客户端：启用加密 DNS 时，它建立连接前的域名解析走 DoH；未启用时就是普通客户端。
        /// </summary>
        HttpClient CreateClient(int timeoutSeconds = 15, string? userAgent = null);

        /// <summary>清掉本服务自己的 DoH 解析缓存（「清除 DNS 缓存」会一起调）。</summary>
        void ClearCache();

        /// <summary>当前配置的中文描述（界面上显示）。</summary>
        string Describe();
    }

    /// <summary>内置的 DoH 服务商（默认国内的 DNSPod / 阿里）。</summary>
    public static class DohProviders
    {
        public sealed record Provider(string Id, string Name, string Endpoint);

        public static IReadOnlyList<Provider> All { get; } = new List<Provider>
        {
            new("dnspod", "腾讯 DNSPod（doh.pub）", "https://doh.pub/dns-query"),
            new("aliyun", "阿里云公共 DNS（dns.alidns.com）", "https://dns.alidns.com/resolve"),
            new("cloudflare", "Cloudflare（cloudflare-dns.com）", "https://cloudflare-dns.com/dns-query"),
            new("google", "Google（dns.google）", "https://dns.google/resolve"),
            new("custom", "自定义（下面填 URL）", "")
        };

        public static Provider Find(string? id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Services/ILaunchConfigService.cs`

```csharp
using NewToys.Shared.Models;
namespace NewToys.Shared.Services;
public interface ILaunchConfigService
{
    LaunchConfig Config { get; }
    string FilePath { get; }
    LaunchConfig Load();
    (bool Ok, string Message) Save(LaunchConfig config);
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Services/IMenuService.cs`

```csharp
using System.Collections.Generic;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

public interface IMenuService
{
    IReadOnlyList<MenuGroup> Groups { get; }
    MenuGroup RegisterModule(string key, string title, string icon, int order, bool isBottom = false, bool isHome = false);
    void RegisterFunction(string moduleKey, string key, string title, string icon, string viewName, int order = 0);
    MenuItem? FindItem(string key);
    MenuGroup? FindGroup(string key);
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Services/INavigationService.cs`

```csharp
namespace NewToys.Shared.Services;

public interface INavigationService
{
    void NavigateTo(string pageKey, object? parameter = null);
    string CurrentPageKey { get; }
    object? CurrentView { get; }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Services/IThemeService.cs`

```csharp
namespace NewToys.Shared.Services;

public interface IThemeService
{
    bool IsDark { get; }
    void Toggle();
    void SetDark(bool dark);
    event Action? ThemeChanged;
    string ConfigDir { get; }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Services/LaunchRunner.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NewToys.Shared.Services;

public class LaunchRunner
{
    public void OpenBrowser(string? exePath, string? argsTemplate, IEnumerable<string> urls)
    {
        if (urls == null) return;
        foreach (var raw in urls)
        {
            var url = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(url) || url.StartsWith("#")) continue;
            try
            {
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = CombineArgs(argsTemplate, url),
                        UseShellExecute = false
                    });
                }
                else
                {
                    OpenUrlDefault(url);
                }
            }
            catch { }
        }
    }

    public void LaunchPrograms(IEnumerable<(string exe, string args)> progs)
    {
        if (progs == null) return;
        foreach (var p in progs)
        {
            var exe = (p.exe ?? "").Trim();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = p.args ?? "",
                    UseShellExecute = false
                });
            }
            catch { }
        }
    }

    public static void OpenUrlDefault(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
        }
    }

    public static string CombineArgs(string? template, string url)
    {
        if (string.IsNullOrWhiteSpace(template)) return Quote(url);
        return template.Contains("{url}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{url}", url, StringComparison.OrdinalIgnoreCase)
            : (template + " " + Quote(url)).Trim();
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shared/Shared.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>NewToys.Shared</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
</Project>
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/App.axaml`

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Shell.App">
    <Application.Styles>
        <FluentTheme/>
        <Style Selector="Button.nav-btn">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource SideIconOff}"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="0"/>
        </Style>
        <Style Selector="Button.nav-btn:pointerover">
            <Setter Property="Background" Value="{DynamicResource SideActiveBg}"/>
            <Setter Property="Foreground" Value="{DynamicResource SideIconOn}"/>
        </Style>
        <Style Selector="Button.win-btn">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource TitleBarFg}"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="0"/>
        </Style>
        <Style Selector="Button.win-btn:pointerover">
            <Setter Property="Background" Value="{DynamicResource WinBtnHover}"/>
        </Style>
        <Style Selector="Button.win-btn.close:pointerover">
            <Setter Property="Background" Value="{DynamicResource WinCloseHover}"/>
        </Style>
        <Style Selector="Button.win-btn.close:pointerover Path">
            <Setter Property="Stroke" Value="{DynamicResource WinCloseHoverText}"/>
        </Style>
        <Style Selector="Button.primary">
            <Setter Property="Background" Value="{DynamicResource Accent}"/>
            <Setter Property="Foreground" Value="{DynamicResource AccentText}"/>
            <Setter Property="CornerRadius" Value="6"/>
            <Setter Property="Padding" Value="16,8"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="BorderThickness" Value="0"/>
        </Style>
        <Style Selector="Button.primary:pointerover">
            <Setter Property="Background" Value="{DynamicResource AccentHover}"/>
        </Style>
        <Style Selector="Button.ghost">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Text1}"/>
            <Setter Property="CornerRadius" Value="6"/>
            <Setter Property="Padding" Value="12,7"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="BorderBrush" Value="{DynamicResource Border}"/>
        </Style>
        <Style Selector="Button.ghost:pointerover">
            <Setter Property="Background" Value="{DynamicResource HoverBg}"/>
        </Style>
        <Style Selector="Border.card">
            <Setter Property="Background" Value="{DynamicResource CardBg}"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="Padding" Value="18"/>
        </Style>
    </Application.Styles>
</Application>
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/App.axaml`

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Shell.App">
    <Application.Styles>
        <FluentTheme/>
        <Style Selector="Button.nav-btn">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource SideIconOff}"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="0"/>
        </Style>
        <Style Selector="Button.nav-btn:pointerover">
            <Setter Property="Background" Value="{DynamicResource SideActiveBg}"/>
            <Setter Property="Foreground" Value="{DynamicResource SideIconOn}"/>
        </Style>
        <Style Selector="Button.win-btn">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource TitleBarFg}"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="0"/>
        </Style>
        <Style Selector="Button.win-btn:pointerover">
            <Setter Property="Background" Value="{DynamicResource WinBtnHover}"/>
        </Style>
        <Style Selector="Button.win-btn.close:pointerover">
            <Setter Property="Background" Value="{DynamicResource WinCloseHover}"/>
        </Style>
        <Style Selector="Button.win-btn.close:pointerover Path">
            <Setter Property="Stroke" Value="{DynamicResource WinCloseHoverText}"/>
        </Style>
        <Style Selector="Button.primary">
            <Setter Property="Background" Value="{DynamicResource Accent}"/>
            <Setter Property="Foreground" Value="{DynamicResource AccentText}"/>
            <Setter Property="CornerRadius" Value="6"/>
            <Setter Property="Padding" Value="16,8"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="BorderThickness" Value="0"/>
        </Style>
        <Style Selector="Button.primary:pointerover">
            <Setter Property="Background" Value="{DynamicResource AccentHover}"/>
        </Style>
        <Style Selector="Button.ghost">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Text1}"/>
            <Setter Property="CornerRadius" Value="6"/>
            <Setter Property="Padding" Value="12,7"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="BorderBrush" Value="{DynamicResource Border}"/>
        </Style>
        <Style Selector="Button.ghost:pointerover">
            <Setter Property="Background" Value="{DynamicResource HoverBg}"/>
        </Style>
        <Style Selector="Border.card">
            <Setter Property="Background" Value="{DynamicResource CardBg}"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="Padding" Value="18"/>
        </Style>
    </Application.Styles>
</Application>
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/App.axaml.cs`

```csharp
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using NewToys.Shell.Views;

namespace NewToys.Shell;

public class App : Application
{
    private ServiceProvider? _sp;
    private static App? _current;

    public static T? GetService<T>() where T : class
        => _current?._sp?.GetService<T>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var services = new ServiceCollection();

            // Core services
            services.AddSingleton<ConfigStoreService>();
            services.AddSingleton<IAppConfigService, AppConfigService>();
            services.AddSingleton<ILaunchConfigService, LaunchConfigService>();
            services.AddSingleton<IMenuService, MenuService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ShellViewModel>();
            services.AddTransient<HomeView>();
            services.AddTransient<HomeViewModel>();

            // Register module VMs/Views via ModuleLoader
            // 先显式加载同目录下的 Module.*.dll（Release 模式下 Shell 未 ProjectReference 模块，需要文件加载）
            LoadModuleAssemblies();
            Core.Services.ModuleLoader.LoadAll(services, null!);

            _sp = services.BuildServiceProvider();
            _current = this;
            AppServices.Initialize(_sp);
            Core.Services.ModuleLoader.InitializeAll(_sp);

            var theme = _sp.GetRequiredService<IThemeService>();
            ApplyTheme(theme.IsDark);
            theme.ThemeChanged += () => ApplyTheme(theme.IsDark);

            var win = new ShellView { DataContext = _sp.GetRequiredService<ShellViewModel>() };
            win.Show();
        }
        catch (Exception ex)
        {
            var w = new Window
            {
                Width = 720, Height = 460,
                Title = "启动失败",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = ex.ToString(),
                        Margin = new Thickness(20),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontFamily = "Consolas"
                    }
                }
            };
            w.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void LoadModuleAssemblies()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var dll in System.IO.Directory.GetFiles(baseDir, "Module.*.dll"))
            {
                try { System.Reflection.Assembly.LoadFrom(dll); }
                catch { /* ignore */ }
            }
        }
        catch { }
    }

    private void ApplyTheme(bool dark)
    {
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var d = new ResourceDictionary();
        void S(string k, string c) => d[k] = new Avalonia.Media.SolidColorBrush(Color.Parse(c));
        if (dark)
        {
            S("AppBg","#191919"); S("TitleBarBg","#1F1F1F"); S("TitleBarFg","#E5E5E5");
            S("WinBtnHover","#333333"); S("WinCloseHover","#E81123"); S("WinCloseHoverText","#FFFFFF");
            S("SidebarBg","#202020"); S("SideIconOff","#9A9A9A"); S("SideIconOn","#FFFFFF"); S("SideActiveBg","#2E2E2E"); S("SideDivider","#2E2E2E");
            S("PanelBg","#262626"); S("PanelHeaderBg","#2B2B2B"); S("SubSelBg","#2F4A38"); S("SubSelText","#7EE0A8");
            S("ContentBg","#191919"); S("ContentHeaderBg","#1F1F1F"); S("StatusBg","#1F1F1F");
            S("CardBg","#262626"); S("HoverBg","#333333"); S("Border","#333333");
            S("Text1","#E5E5E5"); S("Text2","#9E9E9E"); S("MenuHover","#3A3A3A");
            S("Accent","#07C160"); S("AccentHover","#06AD56"); S("AccentText","#FFFFFF");
            S("InputBg","#1F1F1F"); S("InputBorder","#3D3D3D"); S("InputHoverBorder","#07C160");
        }
        else
        {
            S("AppBg","#FFFFFF"); S("TitleBarBg","#FFFFFF"); S("TitleBarFg","#1F1F1F");
            S("WinBtnHover","#E5E5E5"); S("WinCloseHover","#E81123"); S("WinCloseHoverText","#FFFFFF");
            S("SidebarBg","#2E2E2E"); S("SideIconOff","#B7B7B7"); S("SideIconOn","#FFFFFF"); S("SideActiveBg","#444444"); S("SideDivider","#3F3F3F");
            S("PanelBg","#F3F3F3"); S("PanelHeaderBg","#F7F7F7"); S("SubSelBg","#D9EDDF"); S("SubSelText","#07C160");
            S("ContentBg","#FFFFFF"); S("ContentHeaderBg","#FFFFFF"); S("StatusBg","#FAFBFC");
            S("CardBg","#F7F8FA"); S("HoverBg","#EFEFEF"); S("Border","#E6E8EC");
            S("Text1","#1F1F1F"); S("Text2","#8A8A8F"); S("MenuHover","#E9E9E9");
            S("Accent","#07C160"); S("AccentHover","#06AD56"); S("AccentText","#FFFFFF");
            S("InputBg","#FFFFFF"); S("InputBorder","#D1D5DB"); S("InputHoverBorder","#07C160");
        }
        if (Resources != null)
        {
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(d);
        }
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/App.axaml.cs`

```csharp
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using NewToys.Shell.Views;

namespace NewToys.Shell;

public class App : Application
{
    private ServiceProvider? _sp;
    private static App? _current;

    public static T? GetService<T>() where T : class
        => _current?._sp?.GetService<T>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var services = new ServiceCollection();

            // Core services
            services.AddSingleton<ConfigStoreService>();
            services.AddSingleton<IAppConfigService, AppConfigService>();
            services.AddSingleton<ILaunchConfigService, LaunchConfigService>();
            services.AddSingleton<IMenuService, MenuService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ShellViewModel>();
            services.AddTransient<HomeView>();
            services.AddTransient<HomeViewModel>();

            // Register module VMs/Views via ModuleLoader
            // 先显式加载同目录下的 Module.*.dll（Release 模式下 Shell 未 ProjectReference 模块，需要文件加载）
            LoadModuleAssemblies();
            Core.Services.ModuleLoader.LoadAll(services, null!);

            _sp = services.BuildServiceProvider();
            _current = this;
            AppServices.Initialize(_sp);
            Core.Services.ModuleLoader.InitializeAll(_sp);

            var theme = _sp.GetRequiredService<IThemeService>();
            ApplyTheme(theme.IsDark);
            theme.ThemeChanged += () => ApplyTheme(theme.IsDark);

            var win = new ShellView { DataContext = _sp.GetRequiredService<ShellViewModel>() };
            win.Show();
        }
        catch (Exception ex)
        {
            var w = new Window
            {
                Width = 720, Height = 460,
                Title = "启动失败",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = ex.ToString(),
                        Margin = new Thickness(20),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontFamily = "Consolas"
                    }
                }
            };
            w.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void LoadModuleAssemblies()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var dll in System.IO.Directory.GetFiles(baseDir, "Module.*.dll"))
            {
                try { System.Reflection.Assembly.LoadFrom(dll); }
                catch { /* ignore */ }
            }
        }
        catch { }
    }

    private void ApplyTheme(bool dark)
    {
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var d = new ResourceDictionary();
        void S(string k, string c) => d[k] = new Avalonia.Media.SolidColorBrush(Color.Parse(c));
        if (dark)
        {
            S("AppBg","#191919"); S("TitleBarBg","#1F1F1F"); S("TitleBarFg","#E5E5E5");
            S("WinBtnHover","#333333"); S("WinCloseHover","#E81123"); S("WinCloseHoverText","#FFFFFF");
            S("SidebarBg","#202020"); S("SideIconOff","#9A9A9A"); S("SideIconOn","#FFFFFF"); S("SideActiveBg","#2E2E2E"); S("SideDivider","#2E2E2E");
            S("PanelBg","#262626"); S("PanelHeaderBg","#2B2B2B"); S("SubSelBg","#2F4A38"); S("SubSelText","#7EE0A8");
            S("ContentBg","#191919"); S("ContentHeaderBg","#1F1F1F"); S("StatusBg","#1F1F1F");
            S("CardBg","#262626"); S("HoverBg","#333333"); S("Border","#333333");
            S("Text1","#E5E5E5"); S("Text2","#9E9E9E"); S("MenuHover","#3A3A3A");
            S("Accent","#07C160"); S("AccentHover","#06AD56"); S("AccentText","#FFFFFF");
            S("InputBg","#1F1F1F"); S("InputBorder","#3D3D3D"); S("InputHoverBorder","#07C160");
        }
        else
        {
            S("AppBg","#FFFFFF"); S("TitleBarBg","#FFFFFF"); S("TitleBarFg","#1F1F1F");
            S("WinBtnHover","#E5E5E5"); S("WinCloseHover","#E81123"); S("WinCloseHoverText","#FFFFFF");
            S("SidebarBg","#2E2E2E"); S("SideIconOff","#B7B7B7"); S("SideIconOn","#FFFFFF"); S("SideActiveBg","#444444"); S("SideDivider","#3F3F3F");
            S("PanelBg","#F3F3F3"); S("PanelHeaderBg","#F7F7F7"); S("SubSelBg","#D9EDDF"); S("SubSelText","#07C160");
            S("ContentBg","#FFFFFF"); S("ContentHeaderBg","#FFFFFF"); S("StatusBg","#FAFBFC");
            S("CardBg","#F7F8FA"); S("HoverBg","#EFEFEF"); S("Border","#E6E8EC");
            S("Text1","#1F1F1F"); S("Text2","#8A8A8F"); S("MenuHover","#E9E9E9");
            S("Accent","#07C160"); S("AccentHover","#06AD56"); S("AccentText","#FFFFFF");
            S("InputBg","#FFFFFF"); S("InputBorder","#D1D5DB"); S("InputHoverBorder","#07C160");
        }
        if (Resources != null)
        {
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(d);
        }
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/app.manifest`

```text
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="NewToys.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
      <supportedOS Id="{1f676c76-80e1-4239-95bb-83d0f6d0da78}" />
      <supportedOS Id="{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}" />
      <supportedOS Id="{35138b9a-5d96-4fbd-8e2d-a2440225f93a}" />
      <supportedOS Id="{e2011457-1546-43c5-a5fe-008deee3d3f0}" />
    </application>
  </compatibility>
</assembly>
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Converters/BoolInversionConverter.cs`

```csharp
using Avalonia.Data.Converters;
using System.Globalization;

namespace NewToys.Shell.Converters;

public class BoolInversionConverter : IValueConverter
{
    public static readonly BoolInversionConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Program.cs`

```csharp
using Avalonia;
using System;

namespace NewToys.Shell;

class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Shell.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>NewToys.Shell</RootNamespace>
    <AssemblyName>NewToys</AssemblyName>
    <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" />
    <PackageReference Include="Avalonia.Desktop" />
    <PackageReference Include="Avalonia.Themes.Fluent" />
    <PackageReference Include="Avalonia.Fonts.Inter" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Shared/Shared.csproj" />
    <ProjectReference Include="../Core/Core.csproj" />
    <ProjectReference Include="../Module.PcTools/Module.PcTools.csproj" />
    <ProjectReference Include="../Module.WebTools/Module.WebTools.csproj" />
    <ProjectReference Include="../Module.VideoTools/Module.VideoTools.csproj" />
    <ProjectReference Include="../Module.Settings/Module.Settings.csproj" />
  </ItemGroup>
</Project>
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/ViewModels/HomeViewModel.cs`

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

public class QuickBtn
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "🌐";
    public string Color { get; set; } = "#07C160";
    public ICommand RunCommand { get; set; } = null!;
    public ICommand SettingsCommand { get; set; } = null!;
    public string SettingsKey { get; set; } = "";
}

public class FavCard
{
    public string Title { get; set; } = "";
    public string Desc { get; set; } = "";
    public string Key { get; set; } = "";
    public string Icon { get; set; } = "";
}

public partial class HomeViewModel : ViewModelBase
{
    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly LaunchRunner _runner;
    private readonly ILaunchConfigService _launch;

    [ObservableProperty] private string _version = "v0.2.0";

    public ObservableCollection<FavCard> Favorites { get; } = new();
    public ObservableCollection<QuickBtn> QuickButtons { get; } = new();

    public HomeViewModel(IServiceProvider sp, IMenuService menu, LaunchRunner runner, ILaunchConfigService launch)
    {
        _sp = sp; _menu = menu; _runner = runner; _launch = launch;
        NavigateCommand = new RelayCommand<string?>(k =>
        {
            if (string.IsNullOrEmpty(k)) return;
            var shell = sp.GetRequiredService<ShellViewModel>();
            var item = menu.FindItem(k);
            if (item == null) return;
            shell.SelectGroupCommand.Execute(item.GroupKey);
            shell.SelectItemCommand.Execute(k);
        });
        BuildContent();
    }

    public ICommand NavigateCommand { get; }

    private void BuildContent()
    {
        Favorites.Clear();
        Favorites.Add(new FavCard { Title = "电脑信息", Desc = "硬件、系统、网络概览", Key = "pc-info", Icon = "💻" });
        Favorites.Add(new FavCard { Title = "设置与脚本", Desc = "我的快捷指令与自定义操作", Key = "pc-shortcuts", Icon = "▤" });
        Favorites.Add(new FavCard { Title = "网页抓取", Desc = "批量抓取网页链接", Key = "web-scrape", Icon = "🕷" });
        Favorites.Add(new FavCard { Title = "视频库", Desc = "本地视频管理与预览", Key = "video-list", Icon = "📁" });
        Favorites.Add(new FavCard { Title = "加密 DNS", Desc = "加密 DNS 与网络配置", Key = "settings-dns", Icon = "🛡" });
        Favorites.Add(new FavCard { Title = "通用设置", Desc = "通用、主题、启动项", Key = "settings-general", Icon = "⚙" });

        QuickButtons.Clear();
        var cfg = _launch.Config;
        QuickButtons.Add(new QuickBtn
        {
            Title = cfg.Browser1.Name,
            Subtitle = "自定义网址列表",
            Icon = "🌐",
            Color = "#2E9FFF",
            RunCommand = new RelayCommand(() => RunBrowser(cfg.Browser1)),
            SettingsCommand = NavigateCommand,
            SettingsKey = "settings-path"
        });
        QuickButtons.Add(new QuickBtn
        {
            Title = cfg.Browser2.Name,
            Subtitle = "工作/学习独立配置",
            Icon = "🌐",
            Color = "#07C160",
            RunCommand = new RelayCommand(() => RunBrowser(cfg.Browser2)),
            SettingsCommand = NavigateCommand,
            SettingsKey = "settings-path"
        });
        QuickButtons.Add(new QuickBtn
        {
            Title = "批量打开软件",
            Subtitle = "常用软件一键启动",
            Icon = "📦",
            Color = "#FF8C00",
            RunCommand = new RelayCommand(RunPrograms),
            SettingsCommand = NavigateCommand,
            SettingsKey = "settings-launch"
        });
    }

    private ICommand NavigateCommandFor(string key) => NavigateCommand; // 保留兼容

    private void RunBrowser(LaunchButtonConfig b)
    {
        var urls = (b.Urls ?? "").Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries);
        _runner.OpenBrowser(b.ProgramPath, b.Arguments, urls);
    }

    private void RunPrograms()
    {
        var list = new List<(string,string)>();
        foreach (var p in _launch.Config.Programs)
        {
            if (!string.IsNullOrWhiteSpace(p.ProgramPath) && File.Exists(p.ProgramPath))
                list.Add((p.ProgramPath, p.Arguments ?? ""));
        }
        _runner.LaunchPrograms(list.ToArray());
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/ViewModels/ShellViewModel.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using MenuItem = NewToys.Shared.Models.MenuItem;
using NewToys.Shared.Services;
using NewToys.Shell.Views;

namespace NewToys.Shell.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly IThemeService _theme;
    private readonly INavigationService _nav;

    [ObservableProperty] private bool _isHomePage = true;
    [ObservableProperty] private MenuGroup? _currentGroup;
    [ObservableProperty] private MenuItem? _currentItem;
    [ObservableProperty] private object? _currentContent;
    [ObservableProperty] private string _statusText = "NewToys · 已就绪";

    public IReadOnlyList<MenuGroup> Groups => _menu.Groups;

    public ShellViewModel(IServiceProvider sp, IMenuService menu, IThemeService theme, INavigationService nav)
    {
        _sp = sp; _menu = menu; _theme = theme; _nav = nav;
        SelectGroupCommand = new RelayCommand<string?>(k => { if (!string.IsNullOrEmpty(k)) SelectGroup(k); });
        SelectItemCommand = new RelayCommand<string?>(k => { if (!string.IsNullOrEmpty(k)) SelectItem(k); });
        GoHomeCommand = new RelayCommand(() =>
        {
            IsHomePage = true;
            CurrentContent = _sp.GetRequiredService<HomeView>();
            if (CurrentContent is Control c && c.DataContext == null)
                c.DataContext = _sp.GetRequiredService<HomeViewModel>();
        });
        GoHomeCommand.Execute(null);
    }

    public ICommand SelectGroupCommand { get; }
    public ICommand SelectItemCommand { get; }
    public ICommand GoHomeCommand { get; }

    private void SelectGroup(string key)
    {
        var g = _menu.FindGroup(key);
        if (g == null) return;
        IsHomePage = false;
        CurrentGroup = g;
        // 默认选第一项
        var first = g.Items.FirstOrDefault();
        if (first != null) SelectItem(first.Key);
        else CurrentContent = null;
    }

    private void SelectItem(string key)
    {
        var it = _menu.FindItem(key);
        if (it == null) return;
        CurrentItem = it;
        CurrentGroup = _menu.FindGroup(it.GroupKey);
        IsHomePage = false;
        // DI 解析 View
        var view = ResolveView(it);
        CurrentContent = view;
    }

    private Control? ResolveView(MenuItem it)
    {
        // 按约定：View 类名 = ViewTypeName，在对应模块 Views/ 命名空间下
        Type? viewType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            var t = asm.GetTypes().FirstOrDefault(x =>
                x.Name == it.ViewTypeName
                && !x.IsAbstract && x.IsClass
                && typeof(Control).IsAssignableFrom(x));
            if (t != null) { viewType = t; break; }
        }
        if (viewType == null) return BuildErrorView($"找不到视图类型：{it.ViewTypeName}");

        Control? view = null;
        try { view = ActivatorUtilities.CreateInstance(_sp, viewType) as Control; }
        catch (Exception ex) { return BuildErrorView($"视图 {it.ViewTypeName} 创建失败：{ex.Message}"); }
        if (view == null) return BuildErrorView($"视图 {it.ViewTypeName} 实例化返回 null");

        // DataContext 设为同名 VM：XxxView -> XxxViewModel，在 NewToys.<Module>.ViewModels / NewToys.Module.<Module>.ViewModels 下查找
        var vmName = it.ViewTypeName.Replace("View", "ViewModel");
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            var vmType = asm.GetTypes().FirstOrDefault(x =>
                x.Name == vmName && !x.IsAbstract && x.IsClass);
            if (vmType == null) continue;
            try
            {
                var vm = ActivatorUtilities.GetServiceOrCreateInstance(_sp, vmType);
                view.DataContext = vm;
                break;
            }
            catch (Exception ex) { return BuildErrorView($"VM {vmName} 创建失败：{ex.Message}"); }
        }
        return view;
    }

    private Control BuildErrorView(string msg)
    {
        var tb = new TextBlock
        {
            Text = msg,
            Foreground = Avalonia.Media.Brushes.Red,
            Margin = new Thickness(24),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontFamily = "Consolas"
        };
        return new ScrollViewer { Content = tb };
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Views/HomeView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="NewToys.Shell.Views.HomeView">
  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Hidden">
    <StackPanel Margin="36,28,36,28" Spacing="24">
      <!-- 欢迎卡 -->
      <Border Classes="card" Padding="28">
        <StackPanel>
          <TextBlock Text="欢迎使用 NewToys" FontSize="28" FontWeight="Bold" Foreground="{DynamicResource Text1}"/>
          <TextBlock Text="个人高强度使用工具集 · Avalonia 12 + CommunityToolkit.Mvvm + .NET 10" Foreground="{DynamicResource Text2}" FontSize="14"/>
          <TextBlock Text="这里是你的常用功能区，点击收藏卡可直达对应二级页面；底部三个一键按钮右上角的 ⚙ 是各自对应的快捷设置。"
                     Foreground="{DynamicResource Text2}" FontSize="13" TextWrapping="Wrap" Margin="0,4,0,0"/>
        </StackPanel>
      </Border>

      <!-- 常用功能 -->
      <StackPanel>
        <StackPanel Orientation="Horizontal">
          <TextBlock Text="★" FontSize="18" Foreground="#FFB300" VerticalAlignment="Center"/>
          <TextBlock Text="常用功能  " FontSize="16" FontWeight="SemiBold" Foreground="{DynamicResource Text1}" VerticalAlignment="Center"/>
          <TextBlock Text="点击卡片快速进入对应二级页面 · 后续支持右键固定/取消" Foreground="{DynamicResource Text2}" VerticalAlignment="Center"/>
        </StackPanel>
        <ItemsControl ItemsSource="{Binding Favorites}">
          <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
              <UniformGrid Rows="2" Columns="3"/>
            </ItemsPanelTemplate>
          </ItemsControl.ItemsPanel>
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Button Margin="6" HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" Padding="0"
                      Classes="ghost" Command="{Binding $parent[UserControl].DataContext.NavigateCommand}"
                      CommandParameter="{Binding Key}">
                <Border Classes="card" Margin="-1" Padding="20,18" HorizontalAlignment="Stretch">
                  <StackPanel>
                    <TextBlock Text="{Binding Title}" FontSize="16" FontWeight="SemiBold" Foreground="{DynamicResource Text1}"/>
                    <TextBlock Text="{Binding Desc}" FontSize="12" Foreground="{DynamicResource Text2}" TextWrapping="Wrap"/>
                  </StackPanel>
                </Border>
              </Button>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </StackPanel>

      <!-- 一键操作 -->
      <StackPanel>
        <StackPanel Orientation="Horizontal">
          <TextBlock Text="⚡" FontSize="18" VerticalAlignment="Center"/>
          <TextBlock Text="一键操作" FontSize="16" FontWeight="SemiBold" Foreground="{DynamicResource Text1}" VerticalAlignment="Center"/>
        </StackPanel>
        <Grid ColumnDefinitions="*,*,*">
          <ItemsControl ItemsSource="{Binding QuickButtons}">
            <ItemsControl.ItemsPanel>
              <ItemsPanelTemplate>
                <UniformGrid Rows="1" Columns="3"/>
              </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
              <DataTemplate>
                <Grid Margin="6">
                  <Button Grid.Column="0" HorizontalAlignment="Stretch" Height="76" Padding="20,0"
                          HorizontalContentAlignment="Left" Background="{Binding Color}"
                          CornerRadius="10" BorderThickness="0"
                          Command="{Binding RunCommand}">
                    <StackPanel Orientation="Horizontal">
                      <TextBlock Text="{Binding Icon}" FontSize="26" VerticalAlignment="Center" Foreground="White"/>
                      <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" Foreground="White"/>
                        <TextBlock Text="{Binding Subtitle}" FontSize="12" Foreground="#B0FFFFFF"/>
                      </StackPanel>
                    </StackPanel>
                  </Button>
                  <Button HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,8,8,0"
                          Width="28" Height="28" CornerRadius="14" Background="#33FFFFFF" BorderThickness="0"
                          Command="{Binding SettingsCommand}"
                          CommandParameter="{Binding SettingsKey}"
                          ToolTip.Tip="快捷设置">
                    <TextBlock Text="⚙" FontSize="14" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Button>
                </Grid>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </Grid>
      </StackPanel>

      <!-- 状态栏 -->
      <Border Height="36" Margin="0,8,0,0">
        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
          <Ellipse Width="8" Height="8" Fill="#07C160"/>
          <TextBlock Text="NewToys 工具箱 · 三栏仿微信布局 · 跨平台 (Windows / macOS / Linux)"
                     Foreground="{DynamicResource Text2}" FontSize="12"/>
        </StackPanel>
      </Border>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Views/HomeView.axaml.cs`

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Shell.Views;
public partial class HomeView : UserControl
{
    public HomeView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Views/HomeView.axaml.cs`

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace NewToys.Shell.Views;
public partial class HomeView : UserControl
{
    public HomeView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Views/ShellView.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="NewToys.Shell.Views.ShellView"
        xmlns:vm="clr-namespace:NewToys.Shell.ViewModels"
        Title="NewToys" Width="1180" Height="760" MinWidth="900" MinHeight="600"
        WindowState="Normal"
        ExtendClientAreaToDecorationsHint="False"
        Background="{DynamicResource AppBg}">
  <Grid RowDefinitions="40,*" ColumnDefinitions="60,210,*">
    <!-- 标题栏 -->
    <Grid Grid.Row="0" Grid.ColumnSpan="3" Background="{DynamicResource TitleBarBg}" ColumnDefinitions="*,Auto">
      <TextBlock Text="NewToys" VerticalAlignment="Center" Margin="14,0,0,0"
                 Foreground="{DynamicResource TitleBarFg}" FontSize="13" FontWeight="SemiBold"/>
      <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="0">
        <Button x:Name="BtnMin" Classes="win-btn" Width="46" Height="40">
          <Path Data="M0,5 L10,5" StrokeThickness="1.5" Stroke="{DynamicResource TitleBarFg}" StrokeLineCap="Round" VerticalAlignment="Center" HorizontalAlignment="Center"/>
        </Button>
        <Button x:Name="BtnMax" Classes="win-btn" Width="46" Height="40">
          <Path x:Name="MaxIcon" Data="M0,0 L10,0 L10,10 L0,10 Z" StrokeThickness="1.5" Stroke="{DynamicResource TitleBarFg}" Fill="Transparent" VerticalAlignment="Center" HorizontalAlignment="Center"/>
        </Button>
        <Button x:Name="BtnClose" Classes="win-btn,close" Width="46" Height="40">
          <Path Data="M0,0 L10,10 M10,0 L0,10" StrokeThickness="1.5" Stroke="{DynamicResource TitleBarFg}" StrokeLineCap="Round" VerticalAlignment="Center" HorizontalAlignment="Center"/>
        </Button>
      </StackPanel>
    </Grid>

    <!-- 侧栏（第1列）-->
    <Grid Grid.Row="1" Grid.Column="0" Background="{DynamicResource SidebarBg}">
      <Grid.RowDefinitions>
        <RowDefinition Height="*"/>
        <RowDefinition Height="Auto"/>
      </Grid.RowDefinitions>
      <ScrollViewer Grid.Row="0" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Hidden">
        <StackPanel x:Name="NavPanel" Orientation="Vertical" Spacing="2" Margin="0,12,0,8"/>
      </ScrollViewer>
      <Grid Grid.Row="1" Height="1" Background="{DynamicResource SideDivider}" Margin="0,0,0,0"/>
      <StackPanel Grid.Row="1" Margin="0,4,0,8" HorizontalAlignment="Center">
        <Button x:Name="BtnSettings" Classes="nav-btn" Tag="settings"
                Width="48" Height="48" HorizontalAlignment="Center" VerticalAlignment="Bottom"
                Margin="0,4,0,0">
          <TextBlock Text="⚙" FontSize="22" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Button>
      </StackPanel>
    </Grid>

    <!-- 二级菜单面板（第2列）-->
    <Grid x:Name="SubPanel" Grid.Row="1" Grid.Column="1" Background="{DynamicResource PanelBg}" IsVisible="False">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
      </Grid.RowDefinitions>
      <Grid Grid.Row="0" Height="56" Background="{DynamicResource PanelHeaderBg}">
        <TextBlock x:Name="PanelTitle" Text="" VerticalAlignment="Center" Margin="20,0,0,0"
                   Foreground="{DynamicResource Text1}" FontSize="15" FontWeight="SemiBold"/>
      </Grid>
      <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
        <StackPanel x:Name="SubMenu" Orientation="Vertical" Margin="6,6"/>
      </ScrollViewer>
    </Grid>

    <!-- 内容区（第3列；首页模式占第2+3列）-->
    <ContentControl x:Name="ContentArea" Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2"
                    Background="{DynamicResource ContentBg}" Content="{Binding CurrentContent}"/>
  </Grid>
</Window>
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Views/ShellView.axaml.cs`

```csharp
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Data.Core.Plugins;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using MenuItem = NewToys.Shared.Models.MenuItem;

namespace NewToys.Shell.Views;

public partial class ShellView : Window
{
    private ShellViewModel? _vm;

    public ShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BtnMin.Click += (_, _) => WindowState = WindowState.Minimized;
        BtnMax.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        BtnClose.Click += (_, _) => Close();
        UpdateMaxIcon();

        this.PointerPressed += (s, ev) =>
        {
            if (ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (ev.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    return;
                }
                BeginMoveDrag(ev);
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        _vm = vm;
        vm.PropertyChanged += (s, a) =>
        {
            if (a.PropertyName == nameof(ShellViewModel.CurrentGroup)) { UpdateSelection(); UpdateSubMenu(); }
            if (a.PropertyName == nameof(ShellViewModel.CurrentItem)) UpdateSubSelection();
            if (a.PropertyName == nameof(ShellViewModel.IsHomePage)) { UpdateLayout(); }
        };
        BuildNavButtons();
        UpdateSelection();
        UpdateLayout();
    }

    private void UpdateMaxIcon()
    {
        MaxIcon.Data = WindowState == WindowState.Maximized
            ? PathGeometry.Parse("M1,2 L9,2 L9,8 L1,8 Z")
            : PathGeometry.Parse("M0,0 L10,0 L10,10 L0,10 Z");
    }

    private void BuildNavButtons()
    {
        if (_vm == null) return;
        NavPanel.Children.Clear();
        foreach (var g in _vm.Groups.Where(x => !x.IsBottom))
        {
            NavPanel.Children.Add(MakeNavButton(g.Icon, g.Key));
        }
        BtnSettings.Click += (s, e) => _vm.SelectGroupCommand.Execute("settings");
    }

    private Button MakeNavButton(string icon, string key)
    {
        var btn = new Button
        {
            Tag = key,
            Width = 48, Height = 48,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Classes = { "nav-btn" },
            Content = new TextBlock { Text = icon, FontSize = 22, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
        };
        btn.Click += (s, e) => _vm?.SelectGroupCommand.Execute(key);
        return btn;
    }

    private IBrush? Res(string key) => (IBrush?)this.FindResource(key);

    private void UpdateSelection()
    {
        if (_vm == null) return;
        var cur = _vm.CurrentGroup;
        foreach (var b in NavPanel.Children.OfType<Button>())
            ApplyNavState(b, (string?)b.Tag == cur?.Key);
        ApplyNavState(BtnSettings, cur?.Key == "settings");
    }

    private void ApplyNavState(Button b, bool selected)
    {
        b.Background = selected ? Res("SideActiveBg") : Brushes.Transparent;
        if (b.Content is TextBlock tb) tb.Foreground = selected ? Res("SideIconOn") : Res("SideIconOff");
    }

    private new void UpdateLayout()
    {
        if (_vm == null) return;
        if (_vm.IsHomePage)
        {
            SubPanel.IsVisible = false;
            Grid.SetColumn(ContentArea, 1);
            Grid.SetColumnSpan(ContentArea, 2);
        }
        else
        {
            SubPanel.IsVisible = true;
            Grid.SetColumn(ContentArea, 2);
            Grid.SetColumnSpan(ContentArea, 1);
            UpdateSubMenu();
        }
    }

    private void UpdateSubMenu()
    {
        if (_vm == null || _vm.CurrentGroup == null) return;
        PanelTitle.Text = _vm.CurrentGroup.Title;
        SubMenu.Children.Clear();
        foreach (var it in _vm.CurrentGroup.Items)
        {
            var isSel = _vm.CurrentItem?.Key == it.Key;
            var row = new Border
            {
                Height = 40,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(8, 2),
                Padding = new Thickness(12, 0),
                Background = isSel ? Res("SubSelBg") : Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = it.Key,
                Child = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 10,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = it.Icon, FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = isSel ? Res("SubSelText") : Res("Text1") },
                        new TextBlock { Text = it.Title, FontSize = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = isSel ? Res("SubSelText") : Res("Text1") }
                    }
                }
            };
            row.PointerPressed += (s, ev) =>
            {
                _vm?.SelectItemCommand.Execute((string?)((Border)s!).Tag);
            };
            SubMenu.Children.Add(row);
        }
    }

    private void UpdateSubSelection()
    {
        if (_vm?.CurrentGroup == null) return;
        foreach (var child in SubMenu.Children.OfType<Border>())
        {
            var isSel = (string?)child.Tag == _vm.CurrentItem?.Key;
            child.Background = isSel ? Res("SubSelBg") : Brushes.Transparent;
            if (child.Child is StackPanel sp)
                foreach (var tb in sp.Children.OfType<TextBlock>())
                    tb.Foreground = isSel ? Res("SubSelText") : Res("Text1");
        }
    }
}
```

### `FILE: /Users/zhen/Downloads/NewToys/src/Shell/Views/ShellView.axaml.cs`

```csharp
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Data.Core.Plugins;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using MenuItem = NewToys.Shared.Models.MenuItem;

namespace NewToys.Shell.Views;

public partial class ShellView : Window
{
    private ShellViewModel? _vm;

    public ShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BtnMin.Click += (_, _) => WindowState = WindowState.Minimized;
        BtnMax.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        BtnClose.Click += (_, _) => Close();
        UpdateMaxIcon();

        this.PointerPressed += (s, ev) =>
        {
            if (ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (ev.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    return;
                }
                BeginMoveDrag(ev);
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        _vm = vm;
        vm.PropertyChanged += (s, a) =>
        {
            if (a.PropertyName == nameof(ShellViewModel.CurrentGroup)) { UpdateSelection(); UpdateSubMenu(); }
            if (a.PropertyName == nameof(ShellViewModel.CurrentItem)) UpdateSubSelection();
            if (a.PropertyName == nameof(ShellViewModel.IsHomePage)) { UpdateLayout(); }
        };
        BuildNavButtons();
        UpdateSelection();
        UpdateLayout();
    }

    private void UpdateMaxIcon()
    {
        MaxIcon.Data = WindowState == WindowState.Maximized
            ? PathGeometry.Parse("M1,2 L9,2 L9,8 L1,8 Z")
            : PathGeometry.Parse("M0,0 L10,0 L10,10 L0,10 Z");
    }

    private void BuildNavButtons()
    {
        if (_vm == null) return;
        NavPanel.Children.Clear();
        foreach (var g in _vm.Groups.Where(x => !x.IsBottom))
        {
            NavPanel.Children.Add(MakeNavButton(g.Icon, g.Key));
        }
        BtnSettings.Click += (s, e) => _vm.SelectGroupCommand.Execute("settings");
    }

    private Button MakeNavButton(string icon, string key)
    {
        var btn = new Button
        {
            Tag = key,
            Width = 48, Height = 48,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Classes = { "nav-btn" },
            Content = new TextBlock { Text = icon, FontSize = 22, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
        };
        btn.Click += (s, e) => _vm?.SelectGroupCommand.Execute(key);
        return btn;
    }

    private IBrush? Res(string key) => (IBrush?)this.FindResource(key);

    private void UpdateSelection()
    {
        if (_vm == null) return;
        var cur = _vm.CurrentGroup;
        foreach (var b in NavPanel.Children.OfType<Button>())
            ApplyNavState(b, (string?)b.Tag == cur?.Key);
        ApplyNavState(BtnSettings, cur?.Key == "settings");
    }

    private void ApplyNavState(Button b, bool selected)
    {
        b.Background = selected ? Res("SideActiveBg") : Brushes.Transparent;
        if (b.Content is TextBlock tb) tb.Foreground = selected ? Res("SideIconOn") : Res("SideIconOff");
    }

    private new void UpdateLayout()
    {
        if (_vm == null) return;
        if (_vm.IsHomePage)
        {
            SubPanel.IsVisible = false;
            Grid.SetColumn(ContentArea, 1);
            Grid.SetColumnSpan(ContentArea, 2);
        }
        else
        {
            SubPanel.IsVisible = true;
            Grid.SetColumn(ContentArea, 2);
            Grid.SetColumnSpan(ContentArea, 1);
            UpdateSubMenu();
        }
    }

    private void UpdateSubMenu()
    {
        if (_vm == null || _vm.CurrentGroup == null) return;
        PanelTitle.Text = _vm.CurrentGroup.Title;
        SubMenu.Children.Clear();
        foreach (var it in _vm.CurrentGroup.Items)
        {
            var isSel = _vm.CurrentItem?.Key == it.Key;
            var row = new Border
            {
                Height = 40,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(8, 2),
                Padding = new Thickness(12, 0),
                Background = isSel ? Res("SubSelBg") : Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = it.Key,
                Child = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 10,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = it.Icon, FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = isSel ? Res("SubSelText") : Res("Text1") },
                        new TextBlock { Text = it.Title, FontSize = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = isSel ? Res("SubSelText") : Res("Text1") }
                    }
                }
            };
            row.PointerPressed += (s, ev) =>
            {
                _vm?.SelectItemCommand.Execute((string?)((Border)s!).Tag);
            };
            SubMenu.Children.Add(row);
        }
    }

    private void UpdateSubSelection()
    {
        if (_vm?.CurrentGroup == null) return;
        foreach (var child in SubMenu.Children.OfType<Border>())
        {
            var isSel = (string?)child.Tag == _vm.CurrentItem?.Key;
            child.Background = isSel ? Res("SubSelBg") : Brushes.Transparent;
            if (child.Child is StackPanel sp)
                foreach (var tb in sp.Children.OfType<TextBlock>())
                    tb.Foreground = isSel ? Res("SubSelText") : Res("Text1");
        }
    }
}
```

### `FILE: Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <!-- 仓库根 = 本文件所在目录（NewToys\），所有构建产物统一落在 NewToys\artifacts 内 -->
    <RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot>
    <BaseOutputPath>$(RepoRoot)artifacts\bin\$(MSBuildProjectName)\</BaseOutputPath>
    <BaseIntermediateOutputPath>$(RepoRoot)artifacts\obj\$(MSBuildProjectName)\</BaseIntermediateOutputPath>
    <AppendTargetFrameworkToOutputPath>true</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  </PropertyGroup>
</Project>
```

### `FILE: Directory.Packages.props`

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <!-- Avalonia 最新稳定版 -->
    <PackageVersion Include="Avalonia" Version="12.1.3" />
    <PackageVersion Include="Avalonia.Desktop" Version="12.1.3" />
    <PackageVersion Include="Avalonia.Themes.Fluent" Version="12.1.3" />
    <PackageVersion Include="Avalonia.Fonts.Inter" Version="12.1.3" />
    <!-- MVVM 工具包 -->
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <!-- 微软扩展 -->
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Console" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Options" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.0" />
  </ItemGroup>
</Project>
```

### `FILE: global.json`

```json
{
  "sdk": {
    "version": "10.0.0",
    "rollForward": "latestMinor",
    "allowPrerelease": false
  }
}
```

### `FILE: NewToys.slnx`

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/Shared/Shared.csproj" />
    <Project Path="src/Core/Core.csproj" />
    <Project Path="src/Shell/Shell.csproj" />
  </Folder>
  <Folder Name="/src/Modules/">
    <Project Path="src/Module.PcTools/Module.PcTools.csproj" />
    <Project Path="src/Module.WebTools/Module.WebTools.csproj" />
    <Project Path="src/Module.VideoTools/Module.VideoTools.csproj" />
    <Project Path="src/Module.Settings/Module.Settings.csproj" />
  </Folder>
</Solution>
```

