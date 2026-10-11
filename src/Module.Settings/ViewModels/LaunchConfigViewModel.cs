using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 「一键按钮」页 —— 首页「⚡ 一键操作」三个按钮的**全部设置都在这一页**。
/// <para>
/// 三个按钮分别是：浏览器 1 批量开网址、浏览器 2 批量开网址、批量打开软件。
/// 首页上不再给每个按钮挂 ⚙（v1.19 起改成一个「⚙ 一键按钮设置」入口），
/// 因为"三个按钮的配置本来就是一件事，拆到三处只会互相打架"。
/// </para>
/// <para>
/// <b>本页保留「💾 保存」按钮</b>：多字段 + 多行自由文本属"复杂输入"（规范 3.7），
/// 边打边落盘既无意义也可能写进半截路径。「🔄 还原默认」只把默认值**填进输入框**，仍需保存才落盘。
/// </para>
/// <para>
/// ⚠️ 保存时**在原对象上改**（<c>_launch.Config</c>），不新建 <see cref="LaunchConfig"/> ——
/// 否则会把「通用设置」页维护的四个开关（开机启动 / 最小化 / 关闭到托盘 / 更新检查）冲掉。
/// </para>
/// </summary>
public partial class LaunchConfigViewModel : ObservableObject, IStatusProvider
{
    private readonly ILaunchConfigService? _launch;

    // 按钮 1（浏览器 1）
    [ObservableProperty] private string _btn1Name = "";
    [ObservableProperty] private string _btn1Path = "";
    [ObservableProperty] private string _btn1Args = "";
    [ObservableProperty] private string _btn1Urls = "";

    // 按钮 2（浏览器 2）
    [ObservableProperty] private string _btn2Name = "";
    [ObservableProperty] private string _btn2Path = "";
    [ObservableProperty] private string _btn2Args = "";
    [ObservableProperty] private string _btn2Urls = "";

    // 按钮 3（批量打开软件）
    [ObservableProperty] private string _btn3Name = "";
    [ObservableProperty] private string _programsText = "";

    [ObservableProperty] private string _statusText = "改完点右上角「保存」写入 launch.json。";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public LaunchConfigViewModel() { }

    public LaunchConfigViewModel(ILaunchConfigService launch)
    {
        _launch = launch;
        var c = launch.Config;

        Btn1Name = c.Browser1.Name;
        Btn1Path = c.Browser1.ProgramPath;
        Btn1Args = c.Browser1.Arguments;
        Btn1Urls = c.Browser1.Urls;

        Btn2Name = c.Browser2.Name;
        Btn2Path = c.Browser2.ProgramPath;
        Btn2Args = c.Browser2.Arguments;
        Btn2Urls = c.Browser2.Urls;

        Btn3Name = c.Programs.FirstOrDefault()?.Name ?? "批量打开软件";
        ProgramsText = string.Join(Environment.NewLine, c.Programs.Select(FormatProgram));
    }

    [RelayCommand]
    private void Save()
    {
        if (_launch == null) { StatusText = "启动配置服务未就绪。"; return; }
        try
        {
            // 取现有对象再改：StartWithSystem / StartMinimized / CheckUpdateOnStartup / CloseToTray
            // 由「通用设置」页维护，这里**不能重建 LaunchConfig**，否则会把那些开关冲掉。
            var c = _launch.Config;

            c.Browser1.Name = NameOr(Btn1Name, "浏览器1 批量开网址");
            c.Browser1.ProgramPath = Btn1Path.Trim();
            c.Browser1.Arguments = Btn1Args.Trim();
            c.Browser1.Urls = Btn1Urls.Trim();

            c.Browser2.Name = NameOr(Btn2Name, "浏览器2 批量开网址");
            c.Browser2.ProgramPath = Btn2Path.Trim();
            c.Browser2.Arguments = Btn2Args.Trim();
            c.Browser2.Urls = Btn2Urls.Trim();

            var parsed = ParsePrograms(ProgramsText);
            // 保留原有按钮的配色（IconColor）：能按名字对上的复用
            for (var i = 0; i < parsed.Count; i++)
            {
                var old = c.Programs.FirstOrDefault(p => string.Equals(p.Name, parsed[i].Name, StringComparison.Ordinal));
                if (old != null) parsed[i].IconColor = old.IconColor;
            }
            if (parsed.Count == 0)
                parsed.Add(new LaunchButtonConfig { IconColor = 2 });          // 至少留一个，按钮才存在
            parsed[0].Name = NameOr(Btn3Name, parsed[0].Name.Length > 0 ? parsed[0].Name : "批量打开软件");
            c.Programs = parsed;

            var (ok, msg) = _launch.Save(c);
            StatusText = ok
                ? $"已保存 · {DateTime.Now:HH:mm:ss}（浏览器各 1 组网址、{parsed.Count} 个批量启动项；首页点 🏠 即可看到新按钮）"
                : $"保存失败：{msg}";
        }
        catch (Exception ex)
        {
            StatusText = "保存失败：" + ex.Message;
        }
    }

    /// <summary>把默认值填进输入框（**不落盘**）。</summary>
    [RelayCommand]
    private void RestoreDefaults()
    {
        Btn1Name = "浏览器1 批量开网址";
        Btn1Path = "";
        Btn1Args = "";
        Btn1Urls = "# 一行一个网址，# 开头为注释行\r\nhttps://www.baidu.com\r\nhttps://www.bilibili.com";

        Btn2Name = "浏览器2 批量开网址";
        Btn2Path = "";
        Btn2Args = "";
        Btn2Urls = "";

        Btn3Name = "批量打开软件";
        ProgramsText = "记事本 = notepad.exe\r\n计算器 = \"C:/Windows/System32/calc.exe\"";

        StatusText = "已填入默认示例，点「保存」才会生效。";
    }

    private static string NameOr(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    // ==================== 文本 ↔ 模型 ====================

    /// <summary>把"显示名 = 程序路径 参数"逐行解析成启动项。空行与 <c>#</c> 注释行忽略。</summary>
    private static List<LaunchButtonConfig> ParsePrograms(string text)
    {
        var list = new List<LaunchButtonConfig>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

            var name = "";
            var rest = line;

            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                name = line[..eq].Trim();
                rest = line[(eq + 1)..].Trim();
            }

            // 路径含空格时用引号包住，其余情况按第一个空格切开「路径 / 参数」
            string path, args;
            if (rest.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = rest.IndexOf('"', 1);
                if (end > 0) { path = rest[1..end]; args = rest[(end + 1)..].Trim(); }
                else { path = rest.Trim('"'); args = ""; }
            }
            else
            {
                var sp = rest.IndexOf(' ');
                if (sp > 0) { path = rest[..sp]; args = rest[(sp + 1)..].Trim(); }
                else { path = rest; args = ""; }
            }

            if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
            if (name.Length == 0 && path.Length == 0) continue;

            list.Add(new LaunchButtonConfig { Name = name, ProgramPath = path, Arguments = args });
        }
        return list;
    }

    /// <summary>启动项 → 一行文本（路径含空格自动加引号）。</summary>
    private static string FormatProgram(LaunchButtonConfig p)
    {
        var path = p.ProgramPath.Contains(' ') ? $"\"{p.ProgramPath}\"" : p.ProgramPath;
        var body = string.IsNullOrEmpty(p.Arguments) ? path : $"{path} {p.Arguments}";
        return $"{p.Name} = {body}";
    }
}
