using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using Avalonia.Controls;      // ← TryFindResource 是这里的扩展方法（挂在 IResourceHost 上）
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Details;

namespace NewToys.UI.Controls;

// ============================================================================
// · L1 渲染层：把「描述符」翻译成「可绑定行」
// 需求 §4.5.1 / 规范 §3.6「详情卡声明式渲染」。
//
// 🔴 为什么不让 XAML 直接绑描述符里的字符串路径？
//    因为 `{Binding <字符串>}` 在 XAML 里**写不出来**（路径必须是字面量）。
//    所以这里在 C# 侧把 `ValuePath` / `VisibleWhen` / `OptionsPath` … 解析成真实值，
//    行对象只暴露**强类型属性**给模板 → 模板里零字符串路径，改属性名时编译器至少管住行这一侧，
//    描述符那一侧由 `skill/scripts/check-detail-paths.ps1` 兜底。
//
// 🔴 刷新契约（需求 §4.5.1）：
//    属性级改动 → 靠页面 VM 的 INotifyPropertyChanged；本层**订阅源 VM 一次**，
//    任何属性变化就整体重解析（卡片规模极小，够用且不会漏）。
//    动作级改动 → 页面 VM 自己回刷；描述符不参与。
// ============================================================================

/// <summary>绑定路径解析（<c>"A.B"</c> 多级 + <c>"!"</c> 取反）。⛔ 一切失败都当「取不到」处理，不抛。</summary>
internal static class DetailPath
{
    /// <summary>路径是否带取反前缀 <c>!</c>。</summary>
    public static bool IsNegated(string? path) => path is { Length: > 0 } && path[0] == '!';

    /// <summary>剥掉取反前缀。</summary>
    public static string Strip(string? path)
        => path is null ? "" : IsNegated(path) ? path[1..] : path;

    /// <summary>按路径取值；任一段缺失 → null。</summary>
    public static object? Resolve(object? root, string? path)
    {
        var p = Strip(path);
        if (root is null || p.Length == 0) return null;

        var cur = root;
        foreach (var seg in p.Split('.'))
        {
            if (cur is null) return null;
            var pi = cur.GetType().GetProperty(seg, BindingFlags.Public | BindingFlags.Instance);
            if (pi is null) return null;
            cur = pi.GetValue(cur);
        }
        return cur;
    }

    /// <summary>按路径取布尔值（带取反前缀）。</summary>
    public static bool ResolveBool(object? root, string? path)
    {
        var b = Resolve(root, path) is true;
        return IsNegated(path) ? !b : b;
    }

    /// <summary>按路径写值（末段必须有 setter）；类型不符时做一次转换。</summary>
    public static void Write(object? root, string? path, object? value)
    {
        var p = Strip(path);
        if (root is null || p.Length == 0) return;

        var segs = p.Split('.');
        var cur = root;
        for (var i = 0; i < segs.Length - 1; i++)
        {
            if (cur is null) return;
            var mid = cur.GetType().GetProperty(segs[i], BindingFlags.Public | BindingFlags.Instance);
            if (mid is null) return;
            cur = mid.GetValue(cur);
        }
        if (cur is null) return;

        var pi = cur.GetType().GetProperty(segs[^1], BindingFlags.Public | BindingFlags.Instance);
        if (pi is null || !pi.CanWrite) return;

        try { pi.SetValue(cur, Coerce(value, pi.PropertyType)); }
        catch (Exception) { /* ⛔ 路径 / 类型不对就静默跳过：详情卡不该因为一处声明写错就崩 UI */ }
    }

    /// <summary>把值掰成目标类型（Nullable 拆包 / 枚举 / 字符串化）。</summary>
    private static object? Coerce(object? value, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is null) return null;
        if (t.IsInstanceOfType(value)) return value;
        if (t == typeof(string)) return value.ToString();
        if (t.IsEnum) return value is string s ? Enum.Parse(t, s, true) : Enum.ToObject(t, value);
        return System.Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
    }

    /// <summary>宽松比较（跨 long/int/decimal 的 Id 也能配上）。</summary>
    public static bool LooseEquals(object? a, object? b) => string.Equals(a?.ToString(), b?.ToString(), StringComparison.Ordinal);
}

/// <summary>字段行基类：承载「可见 / 可用」这类通用联动，子类只管自己的值。</summary>
public abstract class DetailFieldRow : ObservableObject
{
    protected DetailFieldRow(DetailField field, object? source)
    {
        Field = field;
        Source = source;
    }

    /// <summary>原始声明（文案 / 修饰位这类静态信息直接透给模板）。</summary>
    public DetailField Field { get; }

    /// <summary>卡片 DataContext（= 页面 VM），所有路径都相对它解析。</summary>
    protected object? Source { get; }

    /// <summary>提交语义（子类据此决定「什么时候把值写回 VM」）。</summary>
    protected EditCommitMode CommitMode => Field.Commit;

    public string? Label => Field.Label;
    public bool HasLabel => !string.IsNullOrWhiteSpace(Field.Label);
    public string? Hint => Field.Hint;
    public bool HasHint => !string.IsNullOrWhiteSpace(Field.Hint);

    private bool _isVisible = true;
    private bool _isEnabled = true;

    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }

    /// <summary>重解析「可见 / 可用」+ 子类的值。源 VM 任一属性变化都会调到。</summary>
    public void Refresh()
    {
        IsVisible = Field.VisibleWhen is null || DetailPath.ResolveBool(Source, Field.VisibleWhen);
        IsEnabled = Field.EnabledWhen is null || DetailPath.ResolveBool(Source, Field.EnabledWhen);
        RefreshCore();
    }

    /// <summary>子类重写：回读自己的值。</summary>
    protected virtual void RefreshCore() { }
}
