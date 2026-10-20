using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.WebTools.Models;

/// <summary>
/// 规则编辑器里的一行命令（NT-084 批 C）：<c>(字段名, 表达式)</c> + 上次运行的命中信息。
/// <para>一个规则集 = 它的 <c>RULES</c> 列表；一条命令 = 这里的一行。</para>
/// </summary>
public partial class RuleCommand : ObservableObject
{
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _expr = "";
    [ObservableProperty] private string _hitText = "—";
    [ObservableProperty] private string _preview = "";

    /// <summary>命中条数（NT-086 批 G：运行记录 / 导出用；出错 = 0）。</summary>
    [ObservableProperty] private int _count;

    /// <summary>
    /// Python 侧的错误 / 异常文本（NT-086 批 C：**错误必须回显**，⛔ 禁止静默显示「0 条」）。
    /// </summary>
    [ObservableProperty] private string _error = "";

    public bool HasError => Error.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>表达式类型标签（由前缀推断，只用于列表展示）。</summary>
    public string TypeLabel
    {
        get
        {
            var e = (Expr ?? "").Trim();
            if (e.StartsWith("xpath:", System.StringComparison.OrdinalIgnoreCase)) return "xpath";
            if (e.StartsWith("json:", System.StringComparison.OrdinalIgnoreCase)) return "json";
            if (e.StartsWith("regex:", System.StringComparison.OrdinalIgnoreCase)) return "regex";
            if (e.StartsWith("text:", System.StringComparison.OrdinalIgnoreCase)) return "text";
            if (e.StartsWith("link:", System.StringComparison.OrdinalIgnoreCase)) return "link";
            if (e.StartsWith("url:", System.StringComparison.OrdinalIgnoreCase)) return "url";
            if (e.StartsWith("=", System.StringComparison.Ordinal)) return "常量";
            if (e.StartsWith("css:", System.StringComparison.OrdinalIgnoreCase)) return "css";
            return "默认规则";
        }
    }

    partial void OnExprChanged(string value) => OnPropertyChanged(nameof(TypeLabel));
}
