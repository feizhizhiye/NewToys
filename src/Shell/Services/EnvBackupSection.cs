using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Shell.Services;

/// <summary>本机环境节：%LOCALAPPDATA%\NewToys\env.json —— 解释器 / ffmpeg / 播放器 / 更新地址 / 加密 DNS。（WHY: SHL-024）</summary>
public sealed class EnvBackupSection : IBackupSection
{
    private const string EntryName = "env.json";

    private readonly EnvConfigService _env;

    public EnvBackupSection(EnvConfigService env) => _env = env;

    public string Id => "env";

    public string Title => "本机环境（绝对路径）";

    public string Description => "env.json：ffmpeg / 播放器 / Python / PowerShell 路径、更新地址、加密 DNS";

    public BackupScope Scope => BackupScope.Env;

    public int Order => 10;

    public bool DefaultSelected => false;

    public bool IsMachineBound => true;

    public int SchemaVersion => 1;

    public long EstimateBytes()
    {
        try { return File.Exists(_env.FilePath) ? new FileInfo(_env.FilePath).Length : 0; }
        catch { return 0; }
    }

    public void ExportTo(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        if (File.Exists(_env.FilePath))
            BackupPaths.AddFile(zip, EntryName, _env.FilePath);
    }

    public (int Ok, int Skipped) RestoreFrom(ZipArchiveEntry entry)
    {
        using var inner = new ZipArchive(entry.Open(), ZipArchiveMode.Read);
        var item = inner.GetEntry(EntryName)
                   ?? inner.Entries.FirstOrDefault(e => !e.FullName.EndsWith("/", StringComparison.Ordinal));
        if (item == null) return (0, 1);

        string text;
        using (var reader = new StreamReader(item.Open()))
            text = reader.ReadToEnd();

        // 坏 JSON 不落盘：宁可报错也不要让下次启动的 EnvConfigService 走「解析失败 → 归档」那条路
        try { _ = JsonNode.Parse(text); }
        catch (Exception ex) { throw new InvalidDataException("env.json 内容不是合法 JSON：" + ex.Message, ex); }

        // 覆盖前归档当前文件（⛔ 不删；保留最近一份就够，故按 .bak-时间戳 落盘）
        try
        {
            if (File.Exists(_env.FilePath))
                File.Copy(_env.FilePath, $"{_env.FilePath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][备份] env.json 归档失败（继续恢复）：" + ex.Message);
        }

        AtomicJsonFile.Write(_env.FilePath, text);
        return (1, 0);
    }
}
