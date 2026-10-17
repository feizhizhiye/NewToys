using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IColumnLayoutService"/> 的实现：把各列表页的列显隐偏好落到
/// <c>config\columnlayout.json</c>（形如 <c>{ "script-packages": { "Name": true, "Id": false } }</c>）。
/// <para>
/// ⚠️ 这里**不**走规范 3.9 的「模块只读设置」约束 —— 列显隐是**纯 UI 偏好**（不是应用设置），
/// 由各列表页自发读写，不属于「设置模块才写」的范畴。
/// </para>
/// </summary>
public class ColumnLayoutService : IColumnLayoutService
{
    private readonly ConfigStoreService _store;
    private const string FileName = "columnlayout.json";

    public ColumnLayoutService(ConfigStoreService store) => _store = store;

    private string FilePath => Path.Combine(_store.ConfigDir, FileName);

    private Dictionary<string, Dictionary<string, bool>> ReadAll()
    {
        if (!File.Exists(FilePath)) return new Dictionary<string, Dictionary<string, bool>>();
        try
        {
            var text = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, bool>>>(text)
                   ?? new Dictionary<string, Dictionary<string, bool>>();
        }
        catch
        {
            // 文件损坏：宁可丢掉显隐偏好，也不要让列表页崩（下次会从默认重建）
            return new Dictionary<string, Dictionary<string, bool>>();
        }
    }

    private void WriteAll(Dictionary<string, Dictionary<string, bool>> all)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写盘失败（权限 / 磁盘）不致命：显隐偏好丢了而已，列表照样能看
        }
    }

    public Dictionary<string, bool> LoadPage(string pageKey)
    {
        var all = ReadAll();
        return all.TryGetValue(pageKey, out var page) ? new Dictionary<string, bool>(page) : new Dictionary<string, bool>();
    }

    public void SavePage(string pageKey, Dictionary<string, bool> visibility)
    {
        var all = ReadAll();
        all[pageKey] = visibility;
        WriteAll(all);
    }

    // ==================== 页面级开关（详情栏开合等） ====================
    //
    // 复用同一份文件、同一个「页面 → 键 → bool」结构，靠**保留前缀**把开关与列键区分开：
    // 好处是 ① 文件格式不变（老文件照读）、② 不必新增配置文件、③ 开关照样进「数据管理」的备份。
    // 列键永远取不到这个前缀（列键是 Title/Name/Id… 这类业务名）。

    /// <summary>页面级开关在文件里的前缀（列键不会以此开头 → 不会撞键）。</summary>
    private const string FlagPrefix = "__";

    public bool LoadFlag(string pageKey, string name, bool defaultValue = false)
    {
        var page = LoadPage(pageKey);
        return page.TryGetValue(FlagPrefix + name, out var v) ? v : defaultValue;
    }

    public void SaveFlag(string pageKey, string name, bool value)
    {
        var page = LoadPage(pageKey);          // 先取整页（含列显隐），只改这一个键再整体写回
        page[FlagPrefix + name] = value;
        SavePage(pageKey, page);
    }
}
