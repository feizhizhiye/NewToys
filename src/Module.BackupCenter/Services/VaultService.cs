using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NewToys.BackupCenter.Services;

/// <summary>主密码密码库：包密码与投递凭据的唯一保存处（SQLite backup.db 的 vault_meta / vault_items 表）。（WHY: BC-014）</summary>
public sealed class VaultService
{
    private const int DefaultIterations = 300_000;
    private const int MaxFailures = 5;
    private const string LegacyMigration = "vault-json";

    private const string MetaSalt = "vault.salt";
    private const string MetaIterations = "vault.iterations";
    private const string MetaVerifier = "vault.verifier";

    private static readonly byte[] VerifierPlain = Encoding.UTF8.GetBytes("newtoys-backup-vault");

    /// <summary>旧 <c>vault.json</c> 的结构（只用于一次性导入）。</summary>
    private sealed class VaultFile
    {
        public int Version { get; set; } = 1;
        public string Salt { get; set; } = "";
        public int Iterations { get; set; } = DefaultIterations;
        public string Verifier { get; set; } = "";
        public Dictionary<string, string> Entries { get; set; } = new();
    }

    private readonly BackupDb _db;
    private readonly BackupSettingsService _paths;

    private string _salt = "";
    private int _iterations = DefaultIterations;
    private string _verifier = "";
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

    private byte[]? _key;
    private int _failures;

    public VaultService(BackupDb db, BackupSettingsService paths)
    {
        _db = db;
        _paths = paths;
        Load();
    }

    /// <summary>密码库是否已经建过（= 用户设过主密码）。</summary>
    public bool IsInitialized => _salt.Length > 0 && _verifier.Length > 0;

    /// <summary>本次会话是否已解锁（内存里有派生密钥）。</summary>
    public bool IsUnlocked => _key != null;

    /// <summary>失败次数已用尽 ⇒ 本会话不再接受解锁尝试。</summary>
    public bool IsLockedOut => _failures >= MaxFailures;

    public int RemainingAttempts => Math.Max(0, MaxFailures - _failures);

    /// <summary>库文件全路径（界面显示用）。</summary>
    public string FilePath => _db.DatabasePath;

    /// <summary>条目数（界面显示用）。</summary>
    public int EntryCount => _entries.Count;

    /// <summary>首次设置主密码（已存在 ⇒ 返回 false，改用 <see cref="ChangeMaster"/>）。</summary>
    public bool Initialize(string master)
    {
        if (IsInitialized || string.IsNullOrEmpty(master)) return false;
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Derive(master, salt, DefaultIterations);
        _salt = Convert.ToBase64String(salt);
        _iterations = DefaultIterations;
        _verifier = Seal(key, VerifierPlain);
        _entries.Clear();
        _key = key;
        _failures = 0;
        Save();
        return true;
    }

    /// <summary>解锁（失败累计到 5 次即锁库）。</summary>
    public bool Unlock(string master)
    {
        if (!IsInitialized || IsLockedOut || string.IsNullOrEmpty(master)) return false;
        var key = Derive(master, Convert.FromBase64String(_salt), _iterations);
        var plain = Open(key, _verifier);
        if (plain == null || !plain.AsSpan().SequenceEqual(VerifierPlain))
        {
            _failures++;
            _key = null;
            return false;
        }
        _key = key;
        _failures = 0;
        return true;
    }

    /// <summary>锁库（清掉内存里的密钥；已保存的条目不受影响）。</summary>
    public void Lock() => _key = null;

    /// <summary>改主密码：用旧主密码解出全部条目，再用新主密码重新封装。</summary>
    public bool ChangeMaster(string oldMaster, string newMaster)
    {
        if (!IsInitialized || string.IsNullOrEmpty(newMaster)) return false;
        if (!Unlock(oldMaster)) return false;

        var plain = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (id, blob) in _entries)
        {
            var raw = Open(_key!, blob);
            if (raw != null) plain[id] = raw;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Derive(newMaster, salt, DefaultIterations);
        _salt = Convert.ToBase64String(salt);
        _iterations = DefaultIterations;
        _verifier = Seal(key, VerifierPlain);
        _entries.Clear();
        foreach (var (id, raw) in plain) _entries[id] = Seal(key, raw);

        _key = key;
        _failures = 0;
        Save();
        return true;
    }

    /// <summary>取一个条目的明文（未解锁 / 不存在 ⇒ null）。</summary>
    public string? Get(string id)
    {
        if (_key == null || string.IsNullOrEmpty(id)) return null;
        if (!_entries.TryGetValue(id, out var blob)) return null;
        var plain = Open(_key, blob);
        return plain == null ? null : Encoding.UTF8.GetString(plain);
    }

    /// <summary>写入 / 覆盖一个条目（未解锁 ⇒ false）。</summary>
    public bool Set(string id, string value)
    {
        if (_key == null || string.IsNullOrEmpty(id)) return false;
        _entries[id] = Seal(_key, Encoding.UTF8.GetBytes(value));
        Save();
        return true;
    }

    /// <summary>删除一个条目（未解锁 / 不存在 ⇒ false）。</summary>
    public bool Remove(string id)
    {
        if (_key == null || string.IsNullOrEmpty(id)) return false;
        if (!_entries.Remove(id)) return false;
        Save();
        return true;
    }

    /// <summary>密码库是否已经建过（页面提示用；不读条目内容）。</summary>
    public bool FileExists => IsInitialized;

    /// <summary>某个标识在库里是否已有条目（不需要解锁：只查密文索引）。</summary>
    public bool HasEntry(string id) => !string.IsNullOrEmpty(id) && _entries.ContainsKey(id);

    private void Load()
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            _salt = ReadMeta(conn, MetaSalt) ?? "";
            _verifier = ReadMeta(conn, MetaVerifier) ?? "";
            _iterations = int.TryParse(ReadMeta(conn, MetaIterations), out var it) && it > 0 ? it : DefaultIterations;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, blob FROM vault_items;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) _entries[reader.GetString(0)] = reader.GetString(1);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 密码库读取失败：" + ex.Message);
        }
        MigrateLegacyJson();
    }

    private static string? ReadMeta(SqliteConnection conn, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM vault_meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private void Save()
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            WriteMeta(conn, tx, MetaSalt, _salt);
            WriteMeta(conn, tx, MetaIterations, _iterations.ToString());
            WriteMeta(conn, tx, MetaVerifier, _verifier);

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM vault_items;";
                del.ExecuteNonQuery();
            }
            foreach (var (id, blob) in _entries)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO vault_items (id, blob) VALUES ($id, $blob);";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$blob", blob);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 密码库写入失败：" + ex.Message);
        }
    }

    private static void WriteMeta(SqliteConnection conn, SqliteTransaction tx, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO vault_meta (key, value) VALUES ($k, $v) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>首次启动把旧 <c>vault.json</c> 导进库（导完标记，⛔ 旧文件不删）。</summary>
    private void MigrateLegacyJson()
    {
        if (!_db.IsReady || _db.IsMigrated(LegacyMigration)) return;
        try
        {
            if (File.Exists(_paths.VaultFile))
            {
                var legacy = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(_paths.VaultFile));
                if (legacy is { Salt.Length: > 0, Verifier.Length: > 0 })
                {
                    _salt = legacy.Salt;
                    _iterations = legacy.Iterations > 0 ? legacy.Iterations : DefaultIterations;
                    _verifier = legacy.Verifier;
                    _entries.Clear();
                    foreach (var (id, blob) in legacy.Entries) _entries[id] = blob;
                    Save();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] vault.json 迁移失败（按未设置主密码处理）：" + ex.Message);
        }
        _db.MarkMigrated(LegacyMigration);
    }

    private static byte[] Derive(string master, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(master, salt, iterations, HashAlgorithmName.SHA256, 32);

    private static string Seal(byte[] key, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var all = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, all, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, all, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, all, nonce.Length + cipher.Length, tag.Length);
        return Convert.ToBase64String(all);
    }

    private static byte[]? Open(byte[] key, string blob)
    {
        try
        {
            var raw = Convert.FromBase64String(blob);
            if (raw.Length < 28) return null;
            var nonce = raw.AsSpan(0, 12).ToArray();
            var cipher = raw.AsSpan(12, raw.Length - 28).ToArray();
            var tag = raw.AsSpan(raw.Length - 16, 16).ToArray();
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }
        catch { return null; }
    }
}
