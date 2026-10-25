using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class CollectionViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        var path = await DialogHooks.SaveFileAsync("导出集合", _store.ConfigDir, $"collection-{col.Name}.json", "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(col,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
            File.WriteAllText(path, json);
            StatusText = $"已导出到 {path}";
        }
        catch (Exception ex)
        {
            StatusText = "导出失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 🔍 扫描目录：把脚本目录里的新脚本入库，并**加入当前集合**。
    /// <para>本页的扫描是"补进这个集合"，所以不再走库页那套候选勾选。</para>
    /// </summary>
    [RelayCommand]
    private async Task ScanIntoCollectionAsync()
    {
        if (_store == null || SelectedCollection == null) { StatusText = "先选中一个集合。"; return; }

        // 优先从当前集合的根目录（脚本库）开始扫。
        var colRoot = _store.RootOf(SelectedCollection.Id);
        var dir = await DialogHooks.PickFolderAsync("扫描脚本目录（新脚本会入库并加入当前集合）",
            colRoot.Length > 0 ? colRoot : (_settings?.EffectiveScriptsFolder ?? _svc?.ScriptsDirectory ?? ""));
        if (string.IsNullOrWhiteSpace(dir)) return;

        var candidates = ScriptScanner.Scan(dir, true, _store.Data.Items)
                                     .Where(c => c.State == ScriptCandidate.StateNew)
                                     .ToList();
        if (candidates.Count == 0) { StatusText = "没有新脚本可加入。"; return; }

        var col = _store.FindCollection(SelectedCollection.Id);
        foreach (var c in candidates)
        {
            var item = new ScriptItem
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Role = ScriptItemRole.Script,
                Title = Path.GetFileNameWithoutExtension(c.FullPath),
                Target = c.RelativePath,
                Capture = true,
                Readonly = true,
                RetentionMode = ScriptRetentionMode.Latest,
                Collectable = true,
                CollectableSource = ScriptCollectableSource.Auto,
            };
            item.Collections.Add(SelectedCollection.Id);
            _store.Data.Items.Add(item);
            CollectableEvaluator.Apply(item, dir);
        }

        _store.SyncBuiltinCollections();
        _store.Save();
        _svc?.Reload();

        RefreshCollections();
        LoadMemberTable();
        StatusText = $"已加入 {candidates.Count} 个脚本到「{col?.Name}」（同时入库）。";
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

}
