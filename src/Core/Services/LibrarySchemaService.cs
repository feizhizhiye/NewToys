using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// 模块自有库建表的**唯一入口**：容器里所有 <see cref="ILibrarySchemaContributor"/> 按 <c>ModuleKey</c>
/// 排序依次跑 schema，汇总每个模块的结果（版本记账 / 升级前快照由各库自己的 <c>ApplySchema</c> 完成）。
/// <para>
/// 调用时机：`ModuleLoader.InitializeAllDeferred` 的第一步（首帧之后、各模块重活之前）——
/// 这样任何页面的首次查询都能看到完整的表。
/// </para>
/// <para>
/// 语义：单模块失败只记日志、⛔ 不阻断其它模块；库里版本高于代码版本（= 用旧版软件打开新版库）
/// **不回写版本号**，只记 <see cref="AheadModules"/>，由调用方决定是否提示。
/// </para>
/// </summary>
public sealed class LibrarySchemaService
{
    private readonly ConfigStoreService _cfg;
    private readonly IReadOnlyList<ILibrarySchemaContributor> _contributors;

    public LibrarySchemaService(
        ConfigStoreService cfg,
        IEnumerable<ILibrarySchemaContributor> contributors)
    {
        _cfg = cfg;
        _contributors = contributors
            .OrderBy(c => c.ModuleKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>建表成功的模块键（诊断用）。</summary>
    public List<string> Applied { get; } = new();

    /// <summary>建表失败的模块键 + 原因（诊断用）。</summary>
    public List<string> Failed { get; } = new();

    /// <summary>库里 schema 版本高于本软件的模块键（旧软件打开新库）。</summary>
    public List<string> AheadModules { get; } = new();

    /// <summary>失败日志全路径（本次没有失败 ⇒ 空串）。</summary>
    public string FailureLogPath { get; private set; } = "";

    /// <summary>给界面用的一句话摘要（没有需要提示的事 ⇒ 空串）。</summary>
    public string Summary { get; private set; } = "";

    /// <summary>
    /// 跑全部贡献者。返回 (已变更数, 失败数, 版本超前数)。可重复调用（各实现幂等）。
    /// </summary>
    public (int Applied, int Failed, int Ahead) EnsureAll()
    {
        Applied.Clear();
        Failed.Clear();
        AheadModules.Clear();
        FailureLogPath = "";
        Summary = "";

        var details = new List<string>();

        foreach (var c in _contributors)
        {
            SchemaOutcome outcome;
            try
            {
                outcome = c.EnsureSchema();
            }
            catch (Exception ex)        // 契约要求实现自己兜，这里只做最后一道保险
            {
                outcome = SchemaOutcome.Failed;
                details.Add($"{c.ModuleKey} 抛异常：{ex.Message}");
            }

            switch (outcome)
            {
                case SchemaOutcome.Applied:
                    Applied.Add(c.ModuleKey);
                    break;

                case SchemaOutcome.UpToDate:
                    Applied.Add(c.ModuleKey);
                    break;

                case SchemaOutcome.Ahead:
                    AheadModules.Add(c.ModuleKey);
                    details.Add($"{c.ModuleKey}：库版本 {c.SchemaVersion} 已被更高版本写过（旧版软件打开新版库？）");
                    System.Diagnostics.Debug.WriteLine("[NewToys][schema] " + details[^1]);
                    break;

                default:
                    Failed.Add(c.ModuleKey);
                    details.Add($"{c.ModuleKey} 建表 / 迁移失败（库键 {c.DatabaseKey}）");
                    break;
            }
        }

        if (Failed.Count > 0 || AheadModules.Count > 0)
        {
            FailureLogPath = WriteLog(details);
            Summary = Failed.Count > 0
                ? $"⚠ 模块库建表 / 迁移失败：{string.Join(", ", Failed)}（详见 {FailureLogPath}）"
                : $"⚠ 模块库版本高于本软件：{string.Join(", ", AheadModules)}";
        }

        return (Applied.Count, Failed.Count, AheadModules.Count);
    }

    /// <summary>写 <c>logs\migrate-&lt;时间戳&gt;.log</c>。写失败只吞掉（不因为日志写不了而影响启动）。</summary>
    private string WriteLog(List<string> details)
    {
        try
        {
            Directory.CreateDirectory(_cfg.LogsDirectory);
            var path = Path.Combine(_cfg.LogsDirectory, $"migrate-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var sb = new StringBuilder();
            sb.AppendLine($"# NewToys 模块库 schema 日志 · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"store：{_cfg.StoreDir}");
            sb.AppendLine($"变更：{string.Join(", ", Applied)}");
            sb.AppendLine($"失败：{string.Join(", ", Failed)}");
            sb.AppendLine($"超前：{string.Join(", ", AheadModules)}");
            sb.AppendLine();
            foreach (var d in details) sb.AppendLine("- " + d);
            File.WriteAllText(path, sb.ToString());
            return path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][schema] 迁移日志写入失败：" + ex.Message);
            return "";
        }
    }
}
