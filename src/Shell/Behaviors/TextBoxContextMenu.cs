using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace NewToys.Shell.Behaviors;

/// <summary>给 TextBox 统一挂上「剪切 / 复制 / 粘贴 / 全选」右键菜单（：需求 §4.10.4）。（WHY: SHL-009）</summary>
public class TextBoxMenu
{
    /// <summary>挂载开关：在样式里设 True 即给该 TextBox 加右键编辑菜单。</summary>
    public static readonly AttachedProperty<bool> AttachProperty =
        AvaloniaProperty.RegisterAttached<TextBoxMenu, TextBox, bool>("Attach");

    public static void SetAttach(TextBox obj, bool value) => obj.SetValue(AttachProperty, value);
    public static bool GetAttach(TextBox obj) => obj.GetValue(AttachProperty);

    static TextBoxMenu()
    {
        // 属性在 TextBox 上被置为 true 时挂菜单（幂等：已有菜单不重复挂）。
        AttachProperty.Changed.AddClassHandler<TextBox>((tb, e) =>
        {
            if (e.GetNewValue<bool>()) EnsureMenu(tb);
        });
    }

    private static void EnsureMenu(TextBox tb)
    {
        if (tb.ContextMenu != null) return;

        var cut = new MenuItem { Header = "剪切" };
        cut.Click += (_, _) => DoCut(tb);
        var copy = new MenuItem { Header = "复制" };
        copy.Click += (_, _) => DoCopy(tb);
        var paste = new MenuItem { Header = "粘贴" };
        paste.Click += async (_, _) => await DoPasteAsync(tb);
        var selectAll = new MenuItem { Header = "全选" };
        selectAll.Click += (_, _) => tb.SelectAll();

        var menu = new ContextMenu();
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(selectAll);
        tb.ContextMenu = menu;
    }

    private static IClipboard? ClipboardOf(Control c) => TopLevel.GetTopLevel(c)?.Clipboard;

    private static (int Start, int End) Selection(TextBox tb)
    {
        var s = Math.Min(tb.SelectionStart, tb.SelectionEnd);
        var e = Math.Max(tb.SelectionStart, tb.SelectionEnd);
        return (s, e);
    }

    private static async void DoCopy(TextBox tb)
    {
        var sel = tb.SelectedText;
        if (string.IsNullOrEmpty(sel)) return;
        var clip = ClipboardOf(tb);
        if (clip != null) await clip.SetTextAsync(sel);
    }

    private static async void DoCut(TextBox tb)
    {
        var sel = tb.SelectedText;
        if (string.IsNullOrEmpty(sel)) return;
        var clip = ClipboardOf(tb);
        if (clip != null) await clip.SetTextAsync(sel);
        DeleteSelection(tb);
    }

    private static async Task DoPasteAsync(TextBox tb)
    {
        var clip = ClipboardOf(tb);
        var text = clip == null ? null : await clip.TryGetTextAsync();
        if (string.IsNullOrEmpty(text)) return;

        var t = tb.Text ?? "";
        var (s, e) = Selection(tb);
        tb.Text = t.Substring(0, s) + text + t.Substring(e);
        tb.CaretIndex = s + text.Length;
        tb.SelectionStart = tb.CaretIndex;
        tb.SelectionEnd = tb.CaretIndex;
    }

    private static void DeleteSelection(TextBox tb)
    {
        var (s, e) = Selection(tb);
        if (e <= s) return;
        var t = tb.Text ?? "";
        tb.Text = t.Substring(0, s) + t.Substring(e);
        tb.CaretIndex = s;
        tb.SelectionStart = tb.CaretIndex;
        tb.SelectionEnd = tb.CaretIndex;
    }
}
