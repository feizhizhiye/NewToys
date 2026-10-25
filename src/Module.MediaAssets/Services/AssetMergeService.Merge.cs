using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

public partial class AssetMergeService
{

    /// <summary>
    /// 把源库合并进主库（一个事务；源库只读）。
    /// ⚠️ 必须在没有活动事务的连接上先 ATTACH —— 本方法自开连接，⛔ 别在事务里调。
    /// </summary>
    public MergeResult Merge(string dbPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!IsAvailable) return new MergeResult { Ok = false, Message = "主资产库不可用（数据库打不开）。" };
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            return new MergeResult { Ok = false, Message = "文件不存在。" };
        if (string.Equals(Path.GetFullPath(dbPath), Path.GetFullPath(_assets.DatabasePath), StringComparison.OrdinalIgnoreCase))
            return new MergeResult { Ok = false, Message = "这就是当前主库自己——选另一个 .db。" };

        var jobId = 0L;
        SqliteConnection? conn = null;
        try
        {
            jobId = _assets.CreateScanJob(null, "merge");
            conn = _db.Db.Open();

            // ① ATTACH（⛔ 事务外）
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "ATTACH DATABASE $p AS src;";
                cmd.Parameters.AddWithValue("$p", Path.GetFullPath(dbPath));
                cmd.ExecuteNonQuery();
            }

            var have = TableSet(conn, "src");
            var missing = ExpectedTables.Where(t => !have.Contains(t)).ToList();
            if (missing.Count > 0)
                progress?.Report($"源库缺 {missing.Count} 张表（{string.Join("、", missing)}）——按老库兼容处理，缺的跳过。");

            // ② 事务
            using var tx = conn.BeginTransaction();

            // storages：按 root_path 去重（已存在 → 用现有的 id）
            var storageMap = MergeStorages(conn, tx, have, progress);

            // assets：一律新建 + 建 src_id → new_id 映射
            var assetMap = MergeAssets(conn, tx, have, progress, ct, out var newAssets);

            // file_locations：asset_id 重映射 + 去重（storage+relative_path / hash+size）
            var (newLocations, skipped, locatedAssetIds) = MergeLocations(conn, tx, have, storageMap, assetMap, progress, ct);

            // tags / categories / collections：按名字去重
            var tagMap = MergeByName(conn, tx, have, "tags", "namespace", "name", progress);
            var catMap = MergeByName(conn, tx, have, "categories", null, "name", progress);
            var colMap = MergeByName(conn, tx, have, "collections", null, "name", progress);
            FixCategoryParents(conn, tx, have, catMap, progress);

            // 链接表：两端 id 都换新的，主键冲突忽略
            var linked = MergeLinkTable(conn, tx, have, "asset_tags", "tag_id", tagMap, assetMap, progress)
                       + MergeLinkTable(conn, tx, have, "asset_categories", "category_id", catMap, assetMap, progress)
                       + MergeLinkTable(conn, tx, have, "collection_assets", "collection_id", colMap, assetMap, progress);

            // relations：两端重映射（同向同类型已存在 → 跳过）
            var rels = MergeRelations(conn, tx, have, assetMap, progress);

            // ② 修正：本次并入、却没有任何文件位置落到主库（重复并入同一库 / 与现有库共享文件）的资产是孤儿，
            //    连同其标签 / 分类 / 集合挂接 / 关联一并清除，避免主库堆积零位置资产、计数虚高。
            SweepOrphanAssets(conn, tx, assetMap, locatedAssetIds, out var sweptAssets, progress);
            newAssets = locatedAssetIds.Count;

            tx.Commit();

            // ③ DETACH（⛔ 事务外）。用 TryDetach：分离失败不该把"已提交"报成失败。
            TryDetach(conn);

            var job = new ScanJob
            {
                Id = jobId,
                StorageId = null,
                Mode = "merge",
                Status = ScanStatuses.Success,
                TotalFiles = newAssets,
                AddedFiles = newAssets,
                UpdatedFiles = newLocations,
                MissingFiles = skipped,
                StartedAt = AssetService.Now(),
                FinishedAt = AssetService.Now()
            };
            _assets.UpdateScanJob(job);

            progress?.Report($"完成：新增资产 {newAssets} / 新增位置 {newLocations} / 跳过 {skipped} / 清除孤儿 {sweptAssets} / 组织挂接 {linked} / 关联 {rels}");
            return new MergeResult
            {
                Ok = true,
                Message = $"合并完成：新增资产 {newAssets} / 新增位置 {newLocations} / 跳过重复位置 {skipped} / 清除孤儿资产 {sweptAssets} / 组织挂接 {linked} / 关联 {rels}（源库未改动）。",
                NewAssets = newAssets,
                NewLocations = newLocations,
                Skipped = skipped,
                SkippedAssets = sweptAssets
            };
        }
        catch (Exception ex)
        {
            // 事务没提交 → 自动回滚；DETACH 失败也无所谓（连接随即释放）
            TryDetach(conn);
            if (jobId > 0)
                _assets.UpdateScanJob(new ScanJob
                {
                    Id = jobId, Mode = "merge", Status = ScanStatuses.Failed,
                    TotalFiles = 0, ErrorCount = 1, StartedAt = AssetService.Now(), FinishedAt = AssetService.Now()
                });
            return new MergeResult { Ok = false, Message = "合并失败（已回滚，主库未被写坏）：" + ex.Message };
        }
        finally
        {
            conn?.Dispose();
        }
    }

    /// <summary>
    /// 💿 另存库文件（另存为）：把主库**在线备份**成一个新 .db。
    /// <para>🔴 收编：实现统一走 <see cref="NewToys.Core.Services.SqliteDatabase.VacuumIntoFile"/> ——
    /// 与备份数据节（`data.db`）是**同一个** `VACUUM INTO` 实现，⛔ 不再这里单独写一份 SQL。</para>
    /// </summary>
    public (bool Ok, string Message) SaveAs(string targetPath)
    {
        if (!IsAvailable) return (false, "主资产库不可用（数据库打不开）。");
        if (string.IsNullOrWhiteSpace(targetPath)) return (false, "未指定目标文件。");

        if (!_db.Db.VacuumIntoFile(targetPath, out var error))
            return (false, "保存失败：" + error + "（目标已存在？换个文件名或确认覆盖）");

        var size = new FileInfo(Path.GetFullPath(targetPath)).Length;
        return (true, $"已保存到：{Path.GetFullPath(targetPath)}（{AssetRow.FormatSize(size)}）");
    }

    // ==================== 内部：各表合并 ====================

}
