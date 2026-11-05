using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NewToys.Core.Services;
using NewToys.Shell.ViewModels;
using NewToys.Shared.Services;

namespace NewToys.Shell.Views;

/// <summary>「设置 → 数据」节的 code-behind。（WHY: SHL-052）</summary>
public partial class DataView : UserControl
{
    /// <summary>勾选了数据节且合计超过这个体积 ⇒ 导出前先确认（卡内拍板「过百 MB 先问一句」）。</summary>
    private const long LargeBackupBytes = 100L * 1024 * 1024;

    public DataView() { InitializeComponent(); }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DataViewModel vm)
        {
            vm.RefreshFileListCommand.Execute(null);
            LoadSections();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // 先摘再挂：页面切来切去会重复触发 OnLoaded，⛔ 否则一次点击跑三遍导出
        var btnExport = this.FindControl<Button>("BtnExport");
        var btnRestore = this.FindControl<Button>("BtnRestore");
        var btnLegacy = this.FindControl<Button>("BtnLegacy");
        var btnRefresh = this.FindControl<Button>("BtnRefresh");

        if (btnExport != null) { btnExport.Click -= OnExport; btnExport.Click += OnExport; }
        if (btnRestore != null) { btnRestore.Click -= OnRestore; btnRestore.Click += OnRestore; }
        if (btnLegacy != null) { btnLegacy.Click -= OnLegacyImport; btnLegacy.Click += OnLegacyImport; }
        if (btnRefresh != null) { btnRefresh.Click -= OnRefresh; btnRefresh.Click += OnRefresh; }

        RefreshList();
        LoadSections();
    }

    // ==================== 节清单 ====================

    /// <summary>
    /// 从服务重建节清单（勾选初值 = 各节的 <c>DefaultSelected</c>；已存在的同 ID 行**保留用户勾选**）。
    /// <para>⚠️ 本方法只动节清单，⛔ 不碰状态栏 / 配置文件列表（否则恢复完成后结果会被冲掉）。</para>
    /// </summary>
    private async void LoadSections()
    {
        try
        {
            var backup = AppServices.Get<BackupService>();
            if (backup == null || DataContext is not DataViewModel vm) return;

            var kept = vm.Sections.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                                  .ToDictionary(g => g.Key, g => g.First().IsSelected, StringComparer.OrdinalIgnoreCase);

            vm.Sections.Clear();
            var rows = new List<BackupSectionRow>();
            foreach (var section in backup.Sections)
            {
                var row = new BackupSectionRow
                {
                    Id = section.Id,
                    Title = section.Title,
                    Description = section.Description,
                    ScopeText = ScopeLabel(section.Scope),
                    IsMachineBound = section.IsMachineBound,
                    IsData = section.Scope == BackupScope.Data,
                    IsSelected = kept.TryGetValue(section.Id, out var was) ? was : section.DefaultSelected,
                };
                row.SelectionChanged += (_, _) => vm.RebuildSelectionSummary();
                vm.Sections.Add(row);
                rows.Add(row);
            }

            vm.RebuildSelectionSummary();

            // ⚠️ 大小估算要扫目录（缩略图可能几万个文件）⇒ 后台算完再贴，⛔ 不在 UI 线程递归
            var sizes = await Task.Run(() =>
            {
                var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var section in backup.Sections)
                {
                    try { map[section.Id] = section.EstimateBytes(); }
                    catch { map[section.Id] = 0; }
                }
                return map;
            });

            foreach (var row in rows)
                if (sizes.TryGetValue(row.Id, out var bytes)) row.SizeBytes = bytes;

            vm.RebuildSelectionSummary();
        }
        catch (Exception ex)
        {
            if (DataContext is DataViewModel vm) vm.StatusText = $"⚠ 读取备份节失败：{ex.Message}";
        }
    }

    private static string ScopeLabel(BackupScope scope) => scope switch
    {
        BackupScope.Env => "本机",
        BackupScope.Shell => "设置",
        BackupScope.Module => "模块",
        BackupScope.Data => "数据",
        _ => "",
    };

    private void RefreshList()
    {
        try
        {
            var cfgStore = AppServices.Get<ConfigStoreService>();
            if (cfgStore == null || DataContext is not DataViewModel vm) return;
            vm.ConfigFiles.Clear();
            if (!Directory.Exists(cfgStore.ConfigDir)) return;

            // 模块设置已归位 config\modules\ ⇒ 递归列举（备份 / 导入本来就是递归，列表要和它一致）
            foreach (var f in Directory.GetFiles(cfgStore.ConfigDir, "*.json", SearchOption.AllDirectories)
                                         .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}.", StringComparison.Ordinal))
                                         .OrderBy(x => x))
            {
                var fi = new FileInfo(f);
                var rel = Path.GetRelativePath(cfgStore.ConfigDir, f);
                vm.ConfigFiles.Add(new ConfigFileDisplay { Name = rel, SizeBytes = fi.Length, LastWrite = fi.LastWriteTime });
            }

            // 总库建表 / 迁移失败时在这里给红字提示（状态栏是页面级契约，
            // 而 schema 跑在任何页面出现之前 ⇒ 由本页承担"看得见"的责任）
            var schema = AppServices.Get<LibrarySchemaService>();
            vm.StatusText = schema != null && schema.Failed.Count > 0
                ? schema.Summary
                : $"已列出 {vm.ConfigFiles.Count} 个配置文件";
        }
        catch (Exception ex)
        {
            if (DataContext is DataViewModel vm) vm.StatusText = $"重列配置失败：{ex.Message}";
        }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => LoadSections();

    // ==================== 导出 ====================

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as DataViewModel;
        try
        {
            var backup = AppServices.Get<BackupService>();
            if (backup == null || vm == null) return;
            if (vm.IsBusy) { vm.StatusText = "⚠ 上一个操作还没结束，稍等。"; return; }

            var ids = vm.Sections.Where(r => r.IsSelected).Select(r => r.Id).ToList();
            if (ids.Count == 0)
            {
                vm.StatusText = "⚠ 没有勾选任何节 —— 勾上「应用与模块设置」再导出。";
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出备份",
                SuggestedFileName = $"newtoys-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                DefaultExtension = "zip",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { new FilePickerFileType("NewToys 备份包") { Patterns = new[] { "*.zip" } } }
            });
            if (file == null) return;

            // 大体积确认（卡内拍板：数据节过百 MB 先问一句；⛔ 不做进度条 / 取消）
            var picked = vm.Sections.Where(r => r.IsSelected).ToList();
            var totalBytes = picked.Sum(r => r.SizeBytes);
            if (picked.Any(r => r.IsData) && totalBytes > LargeBackupBytes)
            {
                var dataTitles = string.Join("、", picked.Where(r => r.IsData).Select(r => r.Title));
                if (!await ConfirmAsync("导出数据备份",
                        $"选中的节合计约 {DataViewModel.FormatSize(totalBytes)}（数据节：{dataTitles}）。\n\n" +
                        "数据节是完整拷贝（数据库走 VACUUM INTO 一致快照），不会压缩，可能要几十秒。是否继续？",
                        "开始导出"))
                {
                    vm.StatusText = "已取消导出。";
                    return;
                }
            }

            var path = file.Path.LocalPath;
            vm.IsBusy = true;
            vm.StatusText = "正在导出备份…";
            try
            {
                // ⛔ 别在 UI 线程上压缩：数据节（批次 B）可能上百 MB
                var result = await Task.Run(() => backup.Export(path, ids));
                vm.LastBackupPath = result.Path;
                vm.StatusText = $"备份完成：{result.SectionIds.Count} 节 · {DataViewModel.FormatSize(result.Bytes)} → {result.Path}";
            }
            finally
            {
                vm.IsBusy = false;
            }
        }
        catch (Exception ex)
        {
            if (vm != null) vm.StatusText = $"⚠ 备份失败：{ex.Message}";
        }
    }

    // ==================== 恢复 ====================

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as DataViewModel;
        try
        {
            var backup = AppServices.Get<BackupService>();
            if (backup == null || vm == null) return;
            if (vm.IsBusy) { vm.StatusText = "⚠ 上一个操作还没结束，稍等。"; return; }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择备份包",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("NewToys 备份包") { Patterns = new[] { "*.zip" } },
                    new FilePickerFileType("旧格式 JSON 备份") { Patterns = new[] { "*.json" } },
                }
            });
            if (files.Count == 0) return;

            var path = files[0].Path.LocalPath;

            // 旧格式（v1 单 JSON）—— 只读兼容，⛔ 不走 manifest
            if (BackupService.LooksLikeLegacyJson(path))
            {
                await ImportLegacyAsync(path);
                return;
            }

            var manifest = backup.ReadManifest(path);
            if (manifest == null)
            {
                await ShowMessageAsync("无法恢复", "这个文件里没有 manifest.json —— 不是 NewToys 的备份包（旧格式请用「导入旧格式」）。");
                return;
            }

            var (canRestore, notes) = backup.Validate(manifest);
            if (!canRestore)
            {
                await ShowMessageAsync("无法恢复", string.Join(Environment.NewLine, notes));
                return;
            }

            var sectionList = string.Join("、", manifest.Sections.Select(s => s.Title));
            var message = new List<string>
            {
                $"备份时间：{manifest.CreatedAt:yyyy-MM-dd HH:mm:ss}",
                $"包含节：{sectionList}",
            };
            message.AddRange(notes);
            message.Add("");
            message.Add("恢复会把当前 config 下的同名设置文件覆盖（覆盖前自动归档到 .bak-时间戳），数据节会写成新副本。是否继续？");

            if (!await ConfirmAsync("确认恢复备份", string.Join(Environment.NewLine, message), "确认恢复"))
            {
                vm.StatusText = "已取消恢复。";
                return;
            }

            vm.IsBusy = true;
            vm.StatusText = "正在恢复…";
            BackupRestoreResult result;
            try
            {
                result = await Task.Run(() => backup.Restore(path, manifest));
            }
            finally
            {
                vm.IsBusy = false;
            }

            foreach (var note in result.Notes)
                System.Diagnostics.Debug.WriteLine("[NewToys][备份] " + note);

            vm.StatusText = $"恢复完成：{result.Ok} 节成功" +
                            (result.Skipped > 0 ? $" / {result.Skipped} 节跳过（详见下）" : "") +
                            " · 建议重启应用生效。";

            if (result.Notes.Count > 0)
                await ShowMessageAsync("恢复结果", string.Join(Environment.NewLine, result.Notes));

            LoadSections();
        }
        catch (Exception ex)
        {
            if (vm != null) vm.StatusText = $"⚠ 恢复失败：{ex.Message}";
        }
    }

    // ==================== 旧格式（v1 单 JSON） ====================

    private async void OnLegacyImport(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as DataViewModel;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择旧格式备份（json）",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("旧格式 JSON 备份") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0) return;
            await ImportLegacyAsync(files[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            if (vm != null) vm.StatusText = $"⚠ 导入失败：{ex.Message}";
        }
    }

    private async Task ImportLegacyAsync(string path)
    {
        var vm = DataContext as DataViewModel;
        var backup = AppServices.Get<BackupService>();
        if (backup == null || vm == null) return;
        if (vm.IsBusy) { vm.StatusText = "⚠ 上一个操作还没结束，稍等。"; return; }

        if (!await ConfirmAsync("导入旧格式备份",
                "旧格式备份只含 config 目录的 json（不含数据库 / 缩略图 / 脚本文件）。\n" +
                "导入会覆盖同名配置，导入前会自动把当前配置归档到 config\\.bak-时间戳\\。是否继续？",
                "确认导入"))
        {
            vm.StatusText = "已取消导入。";
            return;
        }

        var (ok, skipped, bakDir) = await Task.Run(() => backup.ImportLegacy(path));
        vm.StatusText = $"导入完成：{ok} 个文件" + (skipped > 0 ? $" / {skipped} 个跳过" : "") +
                        (bakDir != null ? $"，旧配置已归档到 {bakDir}" : "") + "。建议重启应用。";
        LoadSections();
    }

    // ==================== 对话框（统一走全局弹出卡片 DialogCardHost） ====================

    private Task ShowMessageAsync(string title, string message)
        => DialogAsync(title, message, "知道了", showCancel: false);

    private Task<bool> ConfirmAsync(string title, string message, string okText)
        => DialogAsync(title, message, okText, showCancel: true);

    /// <summary>
    /// 弹一张全局弹出卡片（⛔ 不再自造 Window：原独立窗口标题栏是系统装饰，明暗主题下与主窗口割裂）。
    /// <para>拿不到宿主（页面未挂载）时按「用户取消」处理。</para>
    /// </summary>
    private async Task<bool> DialogAsync(string title, string message, string okText, bool showCancel)
    {
        var host = (TopLevel.GetTopLevel(this) as ShellView)?.DialogCards;
        if (host == null) return false;

        if (!showCancel)
        {
            await host.NotifyAsync(title, message);
            return true;   // 只有「知道了」的提示框 ⇒ 关闭即视为确认（保留原语义）
        }
        return await host.MessageAsync(title, message, okText, showCancel: true);
    }
}
