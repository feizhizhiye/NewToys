using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NewToys.Core.Services;
using NewToys.Settings.ViewModels;

namespace NewToys.Settings.Views;

public partial class DataView : UserControl
{
    public DataView() { InitializeComponent(); }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DataViewModel vm) vm.RefreshFileListCommand.Execute(null);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // 找按钮并挂事件（通过名字）
        var btnBackup = this.FindControl<Button>("BtnBackup");
        var btnImport = this.FindControl<Button>("BtnImport");
        var btnRefresh = this.FindControl<Button>("BtnRefresh");
        if (btnBackup != null) btnBackup.Click += OnBackup;
        if (btnImport != null) btnImport.Click += OnImport;
        if (btnRefresh != null) btnRefresh.Click += (_, _) => RefreshList();
        RefreshList();
    }

    private void RefreshList()
    {
        try
        {
            var cfgStore = AppServices.Get<ConfigStoreService>();
            if (cfgStore == null || DataContext is not DataViewModel vm) return;
            vm.ConfigFiles.Clear();
            if (!Directory.Exists(cfgStore.ConfigDir)) return;
            foreach (var f in Directory.GetFiles(cfgStore.ConfigDir, "*.json").OrderBy(x => x))
            {
                var fi = new FileInfo(f);
                vm.ConfigFiles.Add(new ConfigFileDisplay { Name = fi.Name, SizeBytes = fi.Length, LastWrite = fi.LastWriteTime });
            }
            vm.StatusText = $"已列出 {vm.ConfigFiles.Count} 个配置文件";
        }
        catch (Exception ex)
        {
            if (DataContext is DataViewModel vm) vm.StatusText = $"刷新失败：{ex.Message}";
        }
    }

    private async void OnBackup(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cfgStore = AppServices.Get<ConfigStoreService>();
            if (cfgStore == null || DataContext is not DataViewModel vm) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "备份配置",
                SuggestedFileName = $"newtoys-config-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON 备份") { Patterns = new[] { "*.json" } } }
            });
            if (file == null) return;

            var entries = new List<object>();
            foreach (var f in Directory.GetFiles(cfgStore.ConfigDir, "*.json"))
            {
                entries.Add(new
                {
                    path = Path.GetFileName(f),
                    content = File.ReadAllText(f)
                });
            }
            var json = JsonSerializer.Serialize(new { version = 1, exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), files = entries },
                new JsonSerializerOptions { WriteIndented = true });
            await using var stream = await file.OpenWriteAsync();
            await using var sw = new StreamWriter(stream);
            await sw.WriteAsync(json);

            vm.LastBackupPath = file.Path.LocalPath;
            vm.StatusText = $"备份成功：{file.Path.LocalPath}";
            RefreshList();
        }
        catch (Exception ex)
        {
            if (DataContext is DataViewModel vm) vm.StatusText = $"备份失败：{ex.Message}";
        }
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cfgStore = AppServices.Get<ConfigStoreService>();
            if (cfgStore == null || DataContext is not DataViewModel vm) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择备份文件",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON 备份") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0) return;
            var pick = files[0];

            // 二次确认
            // 颜色取**主题资源**（FindResource 到头回退），不能写死深色 —— 浅色主题下会变成一块黑框。
            IBrush Panel() => (IBrush?)this.FindResource("PanelBrush") ?? new SolidColorBrush(Color.Parse("#262626"));
            IBrush Text() => (IBrush?)this.FindResource("PrimaryTextBrush") ?? new SolidColorBrush(Color.Parse("#EFEFEF"));
            var dialog = new Window
            {
                Width = 460, Height = 200,
                Title = "确认导入",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = Panel(),
                Content = new Border
                {
                    Padding = new Thickness(20),
                    Background = Panel(),
                    Child = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "导入将覆盖当前配置文件，导入前会自动备份现有配置到 .bak-*/ 目录。是否继续？",
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = Text()
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Spacing = 8,
                                Children =
                                {
                                    new Button { Content = "取消", Width = 80, Classes = { "ghost" } },
                                    new Button { Content = "确认导入", Width = 90, Classes = { "primary" } }
                                }
                            }
                        }
                    }
                }
            };
            var canceled = true;
            if (dialog.Content is Border b && b.Child is StackPanel sp2 && sp2.Children[1] is StackPanel btns)
            {
                ((Button)btns.Children[0]).Click += (_, _) => { dialog.Close(); };
                ((Button)btns.Children[1]).Click += (_, _) => { canceled = false; dialog.Close(); };
            }
            await dialog.ShowDialog(topLevel as Window ?? throw new InvalidOperationException("no owner window"));
            if (canceled) { vm.StatusText = "已取消导入。"; return; }

            // 执行导入
            string json;
            await using (var stream = await pick.OpenReadAsync())
            using (var sr = new StreamReader(stream))
                json = await sr.ReadToEndAsync();

            var bakDir = Path.Combine(cfgStore.ConfigDir, $".bak-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(bakDir);
            foreach (var f in Directory.GetFiles(cfgStore.ConfigDir, "*.json"))
            {
                File.Copy(f, Path.Combine(bakDir, Path.GetFileName(f)), overwrite: true);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int imported = 0;
            if (root.TryGetProperty("files", out var arr))
            {
                foreach (var entry in arr.EnumerateArray())
                {
                    var rel = entry.GetProperty("path").GetString()!;
                    var content = entry.GetProperty("content").GetString() ?? "";
                    // 防路径穿越
                    var target = Path.GetFullPath(Path.Combine(cfgStore.ConfigDir, rel));
                    if (!target.StartsWith(Path.GetFullPath(cfgStore.ConfigDir) + Path.DirectorySeparatorChar))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, content);
                    imported++;
                }
            }

            vm.StatusText = $"导入完成：{imported} 个文件，旧配置已备份到 {bakDir}。建议重启应用。";
            RefreshList();
        }
        catch (Exception ex)
        {
            if (DataContext is DataViewModel vm) vm.StatusText = $"导入失败：{ex.Message}";
        }
    }
}
