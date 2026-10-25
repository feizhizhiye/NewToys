using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using NewToys.Shell.Views;
using NewToys.UI.Controls;

namespace NewToys.Shell;

public partial class App : Application
{
    // ==================== 选择器钩子的宿主实现（v3.53） ====================

    /// <summary>把选择器的起始位置定到某个目录（拿不到就交给系统默认位置，⛔ 不抛）。</summary>
    private static async Task<IStorageFolder?> TryStartFolderAsync(IStorageProvider sp, string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir!); }
        catch { return null; }
    }

    private static async Task<string?> PickFolderAsync(Window owner, string title, string? initialDir)
    {
        var sp = owner.StorageProvider;
        if (sp == null) return null;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryStartFolderAsync(sp, initialDir)
        }).ConfigureAwait(false);
        return folders?.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static async Task<IReadOnlyList<string>> PickFilesAsync(
        Window owner, string title, string? initialDir, bool allowMultiple, string pattern)
    {
        var sp = owner.StorageProvider;
        if (sp == null) return Array.Empty<string>();

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            SuggestedStartLocation = await TryStartFolderAsync(sp, initialDir),
            FileTypeFilter = BuildFileTypes(pattern)
        }).ConfigureAwait(false);

        if (files == null || files.Count == 0) return Array.Empty<string>();
        var paths = new List<string>();
        foreach (var f in files)
        {
            var p = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p)) paths.Add(p);
        }
        return paths;
    }

    private static async Task<string?> SaveFileAsync(
        Window owner, string title, string? initialDir, string suggestedName, string pattern)
    {
        var sp = owner.StorageProvider;
        if (sp == null) return null;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = await TryStartFolderAsync(sp, initialDir),
            SuggestedFileName = suggestedName,
            FileTypeChoices = BuildFileTypes(pattern)
        }).ConfigureAwait(false);
        return file?.TryGetLocalPath();
    }

    /// <summary>把通配模式转成选择器的类型过滤（多个模式用 <c>;</c> 分隔；<c>*</c> = 所有文件）。</summary>
    private static FilePickerFileType[] BuildFileTypes(string pattern)
    {
        var patterns = (pattern ?? "").Split(';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (patterns.Length == 0 || (patterns.Length == 1 && patterns[0] == "*"))
            return new[] { new FilePickerFileType("所有文件") { Patterns = new[] { "*" } } };

        return new[]
        {
            new FilePickerFileType(string.Join(" / ", patterns)) { Patterns = patterns },
            new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
        };
    }

    /// <summary>
    /// 通用二次确认对话框（= 页内弹出卡片，见 <see cref="DialogCardHost"/>）。
    /// <para>钩子没接上（主窗口未构造）时返回 false = 当作用户没同意。</para>
    /// </summary>
    private static Task<bool> ConfirmAsync(string message)
        => (_mainWindow as ShellView)?.DialogCards?.ConfirmAsync(message) ?? Task.FromResult(false);

    /// <summary>
    /// 通用单行输入对话框（= 页内弹出卡片；取消返回 null）。
    /// </summary>
    private static Task<string?> InputAsync(string title, string initialValue)
        => (_mainWindow as ShellView)?.DialogCards?.InputAsync(title, initialValue)
           ?? Task.FromResult<string?>(null);

}
