using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// HTTP 测试 页 ViewModel（v3.19 / NT-001 实装）：选方法 + 填 URL / 请求头 / 请求体 → 发送 → 看 状态码 / 耗时 / 响应头 / 响应体。
/// <para>栈锁定：用 <see cref="System.Net.Http.HttpClient"/>（⛔ 不引第三方 HTTP 库）。</para>
/// </summary>
public partial class WebHttpViewModel : ObservableObject, NewToys.Shared.Services.IStatusProvider
{
    private readonly HttpTestService _svc = new();

    [ObservableProperty] private string _title = "HTTP 测试";
    [ObservableProperty] private string _description = "发送 HTTP 请求并查看响应";

    /// <summary>可选的请求方法。</summary>
    public IReadOnlyList<string> Methods { get; } = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD" };

    [ObservableProperty] private string _method = "GET";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _headersText = "";
    [ObservableProperty] private string _bodyText = "";

    /// <summary>响应：状态（含码 / 短语 / ✅⚠）、耗时、响应头、响应体。</summary>
    [ObservableProperty] private string _responseStatus = "";
    [ObservableProperty] private string _elapsedText = "";
    [ObservableProperty] private string _responseHeaders = "";
    [ObservableProperty] private string _responseBody = "";

    public bool HasResponse => ResponseStatus.Length > 0;

    [ObservableProperty] private string _statusText = "就绪";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    [ObservableProperty] private bool _isBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));
    public bool IsNotBusy => !IsBusy;

    partial void OnResponseStatusChanged(string value) => OnPropertyChanged(nameof(HasResponse));

    [RelayCommand]
    private async Task SendAsync()
    {
        var url = (Url ?? "").Trim();
        if (url.Length == 0) { StatusText = "先填 URL（http:// 或 https:// 开头）。"; return; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) { StatusText = "URL 格式不对（要 http:// 或 https:// 开头）。"; return; }

        try
        {
            IsBusy = true;
            StatusText = $"正在发送 {Method} {url} ……";
            var r = await _svc.SendAsync(Method, url, HeadersText, BodyText);
            ElapsedText = $"{r.ElapsedMs} ms";

            if (!r.Ok)
            {
                ResponseStatus = "❌ 请求失败";
                ResponseHeaders = "";
                ResponseBody = r.Error ?? "";
                StatusText = "请求失败：" + (r.Error ?? "");
                return;
            }

            var mark = r.StatusCode is >= 200 and < 300 ? "✅" : "⚠";
            ResponseStatus = $"{mark} {r.StatusCode} {r.ReasonPhrase}".Trim();
            ResponseHeaders = r.ResponseHeaders;
            ResponseBody = r.ResponseBody;
            StatusText = $"完成 · {r.StatusCode} · {r.ElapsedMs} ms · 响应 {r.ResponseBody.Length} 字符";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Clear()
    {
        Url = "";
        HeadersText = "";
        BodyText = "";
        ResponseStatus = "";
        ElapsedText = "";
        ResponseHeaders = "";
        ResponseBody = "";
        StatusText = "已清空。";
    }
}
